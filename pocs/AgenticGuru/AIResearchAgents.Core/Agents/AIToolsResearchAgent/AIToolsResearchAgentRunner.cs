using System.ClientModel;
using System.Diagnostics;
using System.Text;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Core;
using Azure.Identity;
using OpenAI.Responses;

namespace AIResearchAgents.Core.Agents.AIToolsResearchAgent;

/// <summary>
/// Executes research queries using an Azure Foundry Bing Grounded agent.
/// Returns in-memory results only — no file I/O. Consumers (CLI, MCP server)
/// decide how to persist or display the output.
/// </summary>
/// <remarks>
/// <para>
/// The runner accepts a <see cref="TokenCredential"/> so each consumer can choose
/// the credential strategy appropriate for its hosting environment:
/// </para>
/// <code>
/// // CLI (local dev — fast, no credential chain probing)
/// var runner = AIToolsResearchAgentRunner.CreateFromEnvironment(
///     customTopic, new AzureCliCredential());
///
/// // MCP Server / ASP.NET Core (production — Managed Identity)
/// services.AddSingleton&lt;TokenCredential&gt;(new ManagedIdentityCredential());
/// // ... resolve from DI and pass it:
/// var runner = AIToolsResearchAgentRunner.CreateFromEnvironment(
///     topic, credential);  // credential injected via DI
///
/// // Portable default (probes environment, then Managed Identity, then CLI, etc.)
/// var runner = AIToolsResearchAgentRunner.CreateFromEnvironment(customTopic);
/// // ↑ uses DefaultAzureCredential when no credential is supplied
/// </code>
/// </remarks>
public sealed class AIToolsResearchAgentRunner
{
    private readonly AIToolsResearchAgentConfig _config;
    private readonly AIProjectClient _projectClient;

    public AIToolsResearchAgentRunner(AIToolsResearchAgentConfig config, TokenCredential credential)
    {
        _config = config;
        _projectClient = new AIProjectClient(new Uri(config.ProjectEndpoint), credential);
    }

    /// <summary>
    /// Creates a runner by loading configuration from environment variables.
    /// Optionally overrides the research topic and credential.
    /// </summary>
    /// <param name="customTopic">
    /// When provided, overrides <see cref="AIToolsResearchAgentConfig.DefaultResearchTopic"/>.
    /// </param>
    /// <param name="credential">
    /// The <see cref="TokenCredential"/> to authenticate with Azure.
    /// When <c>null</c>, defaults to <see cref="DefaultAzureCredential"/>
    /// (works in local dev, CI, and Azure-hosted environments).
    /// Pass <see cref="AzureCliCredential"/> for fast local dev,
    /// or <see cref="ManagedIdentityCredential"/> for production web servers.
    /// </param>
    public static AIToolsResearchAgentRunner CreateFromEnvironment(
        string? customTopic = null,
        TokenCredential? credential = null)
    {
        var config = AIToolsResearchAgentConfig.FromEnvironment();
        if (customTopic is not null)
        {
            config = config with { ResearchTopic = customTopic };
        }
        return new AIToolsResearchAgentRunner(config, credential ?? new DefaultAzureCredential());
    }

    /// <summary>
    /// Runs a research query: gets-or-creates the Foundry agent, sends the research topic,
    /// and returns the result as in-memory data with a formatted output string.
    /// </summary>
    public async Task<ResearchResult> RunAsync(
        bool forceNewVersion = false,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            AgentVersion agent;
            if (forceNewVersion)
            {
                agent = await CreateNewAgentVersionAsync(cancellationToken);
            }
            else
            {
                try
                {
                    var agentRecord = (await _projectClient.Agents.GetAgentAsync(
                        _config.AgentName, cancellationToken)).Value;
                    agent = agentRecord.Versions.Latest;
                }
                catch (ClientResultException ex) when (ex.Status == 404)
                {
                    agent = await CreateNewAgentVersionAsync(cancellationToken);
                }
            }

            var responsesClient = _projectClient.OpenAI.GetProjectResponsesClientForAgent(agent);

            #pragma warning disable OPENAI001
            var userMessage = ResponseItem.CreateUserMessageItem(
                [ResponseContentPart.CreateInputTextPart(_config.ResearchTopic)]);

            var response = await responsesClient.CreateResponseAsync(
                [userMessage], cancellationToken: cancellationToken);

            var summary = ExtractAnnotatedText(response.Value);
            stopwatch.Stop();
            var elapsed = stopwatch.Elapsed.TotalSeconds;

            return new ResearchResult
            {
                IsSuccess = true,
                Summary = summary,
                ElapsedSeconds = elapsed,
                FormattedOutput = FormatOutput(summary, elapsed)
            };
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            return new ResearchResult
            {
                IsSuccess = false,
                ErrorMessage = "Operation was cancelled.",
                ElapsedSeconds = stopwatch.Elapsed.TotalSeconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ResearchResult
            {
                IsSuccess = false,
                ErrorMessage = $"Error: {ex.Message}",
                ElapsedSeconds = stopwatch.Elapsed.TotalSeconds
            };
        }
    }

    private async Task<AgentVersion> CreateNewAgentVersionAsync(CancellationToken cancellationToken)
    {
        var bingConnection = await _projectClient.Connections.GetConnectionAsync(
            _config.BingConnectionName);

        var bingTool = new BingGroundingAgentTool(
            new BingGroundingSearchToolOptions(
                searchConfigurations: [new BingGroundingSearchConfiguration(
                    projectConnectionId: bingConnection.Value.Id)]
            )
        );

        var agentDefinition = new PromptAgentDefinition(model: _config.ModelDeploymentName)
        {
            Instructions = _config.AgentInstructions,
            Tools = { bingTool }
        };

        var agentVersion = (await _projectClient.Agents.CreateAgentVersionAsync(
            agentName: _config.AgentName,
            options: new AgentVersionCreationOptions(agentDefinition),
            cancellationToken: cancellationToken)).Value;
        return agentVersion;
    }

    /// <summary>
    /// Extracts text from the response, replacing citation markers with real markdown
    /// links from Bing Grounded Search annotations provided by the Azure Foundry agent.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Only the <b>last</b> <see cref="MessageResponseItem"/> in <c>OutputItems</c> is
    /// processed. When Bing Grounded Search is involved, the collection contains an
    /// intermediate message (before search results), tool-call items, and then the final
    /// complete message. Using only the last message avoids duplicating content that
    /// appears in both the intermediate and final messages.
    /// </para>
    /// <para>
    /// Bing Grounded Search returns real URLs as <see cref="UriCitationMessageAnnotation"/>
    /// on each <see cref="ResponseContentPart"/>. The raw text from <c>GetOutputText()</c>
    /// contains citation markers (or hallucinated URLs) at the positions indicated by
    /// <c>StartIndex</c>/<c>EndIndex</c>. This method replaces those markers with clickable
    /// <c>[Title](Uri)</c> markdown links.
    /// </para>
    /// </remarks>
    private static string ExtractAnnotatedText(ResponseResult response)
    {
        // Use only the last MessageResponseItem — it is the final, complete response
        // after all tool calls (e.g., Bing Grounded Search) have been resolved.
        // Earlier MessageResponseItems contain partial/intermediate content that would
        // otherwise be duplicated if concatenated with the final message.
        var messageItem = response.OutputItems
            .OfType<MessageResponseItem>()
            .LastOrDefault();

        if (messageItem is null)
            return response.GetOutputText();

        var sb = new StringBuilder();

        foreach (var part in messageItem.Content)
        {
            // Only process output text parts (skip input text, refusals, etc.)
            if (part.Kind != ResponseContentPartKind.OutputText)
                continue;

            var text = part.Text;
            if (text is null)
                continue;

            var annotations = part.OutputTextAnnotations;
            if (annotations is null || annotations.Count == 0)
            {
                sb.Append(text);
                continue;
            }

            // Process annotations in reverse order (highest StartIndex first)
            // so that replacements at higher positions don't shift lower indices.
            foreach (var annotation in annotations
                .OfType<UriCitationMessageAnnotation>()
                .OrderByDescending(a => a.StartIndex))
            {
                var title = annotation.Title ?? "Source";
                var uri = annotation.Uri?.AbsoluteUri ?? "";
                var link = $"[{title}]({uri})";

                // EndIndex is exclusive (standard OpenAI Responses API convention)
                var endIndex = Math.Min(annotation.EndIndex, text.Length);
                var startIndex = Math.Min(annotation.StartIndex, endIndex);

                text = string.Concat(
                    text.AsSpan(0, startIndex),
                    link,
                    text.AsSpan(endIndex));
            }

            sb.Append(text);
        }

        // Fall back to plain text if the last message had no processable content
        return sb.Length > 0 ? sb.ToString() : response.GetOutputText();
    }

    private static string FormatOutput(string? summary, double elapsedSeconds)
    {
        var currentDate = DateTime.UtcNow;
        var sb = new StringBuilder();

        // Markdown document header (used for both display and file persistence)
        sb.AppendLine("# 🤖 Agentic Dev Tools — Weekly Trends Digest");
        sb.AppendLine();
        sb.AppendLine("**Generated by**: Agentic Trends Guru Agent (Powered by Azure Foundry Agent Bing Grounded Search)");
        sb.AppendLine();
        sb.AppendLine($"📅 Week of {currentDate:MMMM dd, yyyy}  |  ⏱️ Generated in {elapsedSeconds:F1} seconds");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        sb.AppendLine(summary ?? "No research summary available.");
        
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();
        sb.AppendLine("*💡 Powered by [Agentic Trends Guru](https://github.com/cesardl/Agentic-Trends-Guru) — Azure Foundry Agent with Bing Grounded Search*");
        
        return sb.ToString();
    }
}
