using System.Diagnostics;
using AIResearchAgents.Core;
using AIResearchAgents.Core.Agents.AIToolsResearchAgent;
using Azure.Identity;

// ---------------------------------------------------------------------------
// 1. Parse CLI arguments (flags + custom research topic)
// ---------------------------------------------------------------------------
var forceNewVersion = args.Contains("--new-agent-version-in-foundry", StringComparer.OrdinalIgnoreCase);
var stripLinks = args.Contains("--no-links", StringComparer.OrdinalIgnoreCase);
var remainingArgs = args.Where(a =>
    !a.Equals("--new-agent-version-in-foundry", StringComparison.OrdinalIgnoreCase) &&
    !a.Equals("--no-links", StringComparison.OrdinalIgnoreCase)).ToArray();
var customTopic = remainingArgs is { Length: > 0 }
    ? string.Join(" ", remainingArgs).Trim()
    : null;
if (string.IsNullOrWhiteSpace(customTopic)) customTopic = null;

// ---------------------------------------------------------------------------
// 2. Create runner from environment (AzureCliCredential for fast local dev)
// ---------------------------------------------------------------------------
AIToolsResearchAgentRunner runner;
try
{
    runner = AIToolsResearchAgentRunner.CreateFromEnvironment(customTopic, new AzureCliCredential());
}
catch (InvalidOperationException ex)
{
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 1;
}

// ---------------------------------------------------------------------------
// 3. Run research with spinner
// ---------------------------------------------------------------------------
using var appCts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; appCts.Cancel(); };

if (forceNewVersion)
{
    Console.WriteLine("Creating new agent version...");
}

var stopwatch = Stopwatch.StartNew();
using var spinnerCts = new CancellationTokenSource();
var spinnerTask = Task.Run(() => RunSpinner(stopwatch, spinnerCts.Token));

var result = await runner.RunAsync(forceNewVersion, appCts.Token);

spinnerCts.Cancel();
try { await spinnerTask; } catch (OperationCanceledException) { }
Console.Write("\r" + new string(' ', 80) + "\r");

// ---------------------------------------------------------------------------
// 4. Display result
// ---------------------------------------------------------------------------
if (!result.IsSuccess)
{
    Console.Error.WriteLine($"ERROR: {result.ErrorMessage}");
    return 1;
}

// Optionally strip citation links for cleaner output (both console and file)
var outputToSave = stripLinks && result.FormattedOutput is not null
    ? ResearchResult.StripMarkdownLinks(result.FormattedOutput)
    : result.FormattedOutput;

// Save summary to file (file I/O is a CLI concern, not the core library's)
var summaryFilePath = ResolveSummaryFilePath();
File.WriteAllText(summaryFilePath, outputToSave);

Console.WriteLine($"✓ Research completed in {result.ElapsedSeconds:F1}s");
Console.WriteLine();
Console.Write(outputToSave);
Console.WriteLine($"📄 Full summary saved to: {summaryFilePath}");
Console.WriteLine();

if (!Console.IsInputRedirected)
{
    Console.Write("Press any key to exit...");
    Console.ReadKey(intercept: true);
    Console.WriteLine();
}
return 0;

// ===========================================================================
// Local functions (CLI UI only)
// ===========================================================================

static void RunSpinner(Stopwatch stopwatch, CancellationToken cancellationToken)
{
    var spinnerFrames = new[] { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
    int frameIndex = 0;

    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var spinner = spinnerFrames[frameIndex % spinnerFrames.Length];
            var elapsed = stopwatch.Elapsed.TotalSeconds;
            Console.Write($"\r{spinner} Researching agentic trends... {elapsed:F1}s");
            frameIndex++;
            cancellationToken.WaitHandle.WaitOne(TimeSpan.FromMilliseconds(500));
        }
    }
    catch (OperationCanceledException)
    {
        Console.Write("\r");
    }
}

static string ResolveSummaryFilePath()
{
    var today = DateTime.UtcNow;
    var dateStamp = today.ToString("yyyy-MM-dd");
    var basePath = $"research_summary_{dateStamp}.md";

    if (!File.Exists(basePath))
        return basePath;

    var timeStamp = today.ToString("HHmm");
    return $"research_summary_{dateStamp}_{timeStamp}.md";
}