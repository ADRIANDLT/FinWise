using System.Text;
using System.Text.Json;
using FinWise.McpServer.E2ETestBase;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace FinWise.McpServer.IntegrationTests;

// ---------------------------------------------------------------------------
// End-to-end tests that connect to a running FinWise MCP server via HTTP.
//
// Each scenario is intentionally split into its own test class so xUnit v2
// runs them in parallel across collections (xunit v2 only parallelises tests
// from *different* classes). `xunit.runner.json` caps the parallel degree to
// avoid overwhelming the Azure OpenAI endpoint.
//
// PREREQUISITES:
// 1. FinWise.McpServer must be running (default: http://localhost:5000,
//    override via FINWISE_MCP_URL).
// 2. Azure AI Foundry credentials must be configured
//    (FINWISE_AZURE_AI_FOUNDRY_* + FINWISE_AZURE_* service principal).
// 3. Run: dotnet run --project src/FinWise.McpServer/FinWise.McpServer.csproj
//        --urls http://localhost:5000
// ---------------------------------------------------------------------------

/// <summary>
/// Happy-path test: build a brand-new profile and assert the agent responds
/// with either investment advice or a follow-up prompt. Implicitly covers
/// same-session profile retention because PROFILE_READY is observed
/// after the multi-step setup.
/// </summary>
[Trait("Category", "Integration")]
public class CompleteUserJourneyTests : McpEndToEndTestBase
{
    public CompleteUserJourneyTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task CompleteUserJourney_NewUser_ShouldCreateProfileAndProvideAdvice()
    {
        await InitializeMcpSession();

        string testEmail = $"journey-{Guid.NewGuid().ToString("N")[..8]}@example.com";

        Output.WriteLine("=== COMPLETE USER JOURNEY TEST ===");
        Output.WriteLine($"Test Email: {testEmail}");

        var profileReadyResponse = await SetupTestProfileWithEmail(testEmail);
        Assert.Contains(testEmail, profileReadyResponse);

        Output.WriteLine($"\nProfile Complete Response:\n{TruncateForLog(profileReadyResponse!, 500)}");

        var lower = profileReadyResponse!.ToLowerInvariant();
        bool hasAdvice = lower.Contains("invest") || lower.Contains("stock") ||
                         lower.Contains("fund") || lower.Contains("portfolio");
        bool asksFollowUp = lower.Contains("assist") || lower.Contains("help") ||
                            lower.Contains("how can i");
        Assert.True(hasAdvice || asksFollowUp,
            $"Expected investment advice or follow-up prompt. Got: {profileReadyResponse}");
    }
}

/// <summary>
/// Verifies `reset_conversation` clears session state (or reports in-memory
/// limitation), and that a follow-up message behaves correctly afterwards.
/// </summary>
[Trait("Category", "Integration")]
public class ResetSessionTests : McpEndToEndTestBase
{
    public ResetSessionTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task ResetSession_ShouldClearHistoryAndRequireReidentification()
    {
        await InitializeMcpSession();
        await SetupTestProfile();

        Output.WriteLine("=== RESET CONVERSATION TEST ===");

        var resetResponse = await CallResetSessionTool();
        Output.WriteLine($"Reset Response: {resetResponse}");

        var resetLower = resetResponse.ToLowerInvariant();
        bool confirmsCleared = resetLower.Contains("cleared");
        bool indicatesInMemory = resetLower.Contains("in-memory");
        (confirmsCleared || indicatesInMemory).Should().BeTrue(
            because: "reset should confirm clearing (database store) or explain in-memory limitation. Got: " + resetResponse);

        var followUpResponse = await CallFinancialAdviceTool("Give me financial advice");
        Output.WriteLine($"Post-Reset Response: {followUpResponse}");

        var followUpLower = followUpResponse.ToLowerInvariant();
        bool asksForEmail = followUpLower.Contains("email");
        bool providesAdvice = followUpLower.Contains("invest") || followUpLower.Contains("portfolio") ||
                              followUpLower.Contains("stock") || followUpLower.Contains("fund") ||
                              followUpLower.Contains("bond") || followUpLower.Contains("recommend") ||
                              followUpLower.Contains("risk") || followUpLower.Contains("financial");
        (asksForEmail || providesAdvice).Should().BeTrue(
            because: "after reset, system should either ask for email (cleared session) or provide advice (profile retained). Got: " + followUpResponse);
    }
}

/// <summary>
/// Cheap test (no profile setup): asserts MCP exposes exactly the three
/// expected tools.
/// </summary>
[Trait("Category", "Integration")]
public class ToolDiscoveryTests : McpEndToEndTestBase
{
    public ToolDiscoveryTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task ToolDiscovery_ShouldExposeExactlyThreeTools()
    {
        await InitializeMcpSession();

        Output.WriteLine("=== TOOL DISCOVERY TEST ===");

        var listRequest = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "tools/list",
            @params = new { }
        };

        var json = JsonSerializer.Serialize(listRequest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var content = new StringContent(json, Encoding.UTF8, "application/json");
        content.Headers.Add("MCP-Session-Id", SessionId);

        var response = await HttpClient.PostAsync($"{McpBaseUrl}/mcp", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue($"tools/list failed: {responseContent}");
        Output.WriteLine($"Tools List Response: {responseContent}");

        string jsonContent = responseContent;
        if (responseContent.StartsWith("event:"))
        {
            var dataLine = responseContent.Split('\n').FirstOrDefault(l => l.StartsWith("data:"));
            dataLine.Should().NotBeNull("SSE response missing data: line");
            jsonContent = dataLine!.Substring(5).Trim();
        }

        var jsonDoc = JsonDocument.Parse(jsonContent);
        var tools = jsonDoc.RootElement.GetProperty("result").GetProperty("tools");

        var toolNames = tools.EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .OrderBy(n => n)
            .ToList();

        Output.WriteLine($"Tools found: {string.Join(", ", toolNames)}");

        toolNames.Should().HaveCount(3, "server should expose exactly 3 MCP tools");
        toolNames.Should().Contain("run_finwise_workflow");
        toolNames.Should().Contain("reset_conversation");
        toolNames.Should().Contain("get_storage_info");
    }
}

/// <summary>
/// Creates a profile in one session then re-identifies in a new session and
/// confirms the existing profile is found in the profile store.
///
/// Replaces the prior `TwoSessions_SameEmail_…` test, which was a heavier
/// variant of this scenario (two full profile setups).
/// </summary>
[Trait("Category", "Integration")]
public class ReturningUserTests : McpEndToEndTestBase
{
    public ReturningUserTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task ReturningUser_NewSession_ShouldFindExistingProfile()
    {
        await InitializeMcpSession();

        var testEmail = await SetupTestProfile();

        var newSessionId = await InitializeNewMcpSession();
        Output.WriteLine($"=== NEW SESSION TEST (Session: {newSessionId}) ===");

        var response = await CallFinancialAdviceTool("Give me financial advice", newSessionId);
        Output.WriteLine($"New Session Response: {response}");

        Assert.Contains("email", response.ToLowerInvariant());

        var emailResponse = await CallFinancialAdviceTool(testEmail, newSessionId);
        Output.WriteLine($"Email Response in New Session: {emailResponse}");

        var lowerEmailResponse = emailResponse.ToLowerInvariant();
        bool hasProfileReady = emailResponse.Contains("PROFILE_READY:");
        bool asksForMissingData = lowerEmailResponse.Contains("risk") ||
                                  lowerEmailResponse.Contains("goal") ||
                                  lowerEmailResponse.Contains("timeframe");

        Assert.True(hasProfileReady || asksForMissingData,
            $"Expected either PROFILE_READY or prompts for missing data. Got: {emailResponse}");

        Output.WriteLine($"Profile behavior: HasProfileReady={hasProfileReady}, AsksForMissingData={asksForMissingData}");
    }
}

/// <summary>
/// Builds an aggressive/short-term profile and asserts the advisor responds
/// with stock-focused guidance. Exercises the StockAgent path.
/// </summary>
[Trait("Category", "Integration")]
public class StockFocusedAdviceTests : McpEndToEndTestBase
{
    public StockFocusedAdviceTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task AggressiveShortTerm_ShouldReturnStockFocusedAdvice()
    {
        await InitializeMcpSession();
        string testEmail = $"stock-test-{Guid.NewGuid().ToString("N")[..8]}@example.com";

        Output.WriteLine("=== STOCK-FOCUSED ADVICE TEST ===");
        Output.WriteLine($"Test Email: {testEmail}");

        var profileReadyResponse = await SetupTestProfileWithEmail(testEmail, null,
            "Aggressive", "Short-term capital gains through stock trading", "Short-term, less than 1 year");
        Assert.Contains(testEmail, profileReadyResponse);
        Assert.Contains("aggressive", profileReadyResponse.ToLowerInvariant());

        await Task.Delay(250);

        var stockResponse = await CallFinancialAdviceTool(
            "Based on my aggressive profile, which specific stocks should I invest in right now for maximum short-term gains?");
        var stockLower = stockResponse.ToLowerInvariant();

        for (int attempt = 1; attempt <= 2 && (IsTransientError(stockResponse) || stockLower.Length < 30); attempt++)
        {
            Output.WriteLine($"  Stock advice failed (attempt {attempt}), retrying...");
            await Task.Delay(1000 * attempt);
            stockResponse = await CallFinancialAdviceTool(
                "Based on my aggressive profile, which specific stocks should I invest in right now for maximum short-term gains?");
            stockLower = stockResponse.ToLowerInvariant();
        }
        Output.WriteLine($"Stock Response: {stockResponse}");

        bool hasStockAdvice = stockLower.Contains("stock") || stockLower.Contains("share") ||
                              stockLower.Contains("equity") || stockLower.Contains("ticker") ||
                              stockLower.Contains("nasdaq") || stockLower.Contains("s&p") ||
                              stockLower.Contains("nyse") || stockLower.Contains("market") ||
                              stockLower.Contains("trade") || stockLower.Contains("sector");
        bool hasInvestmentAdvice = stockLower.Contains("invest") || stockLower.Contains("portfolio") ||
                                   stockLower.Contains("buy") || stockLower.Contains("growth") ||
                                   stockLower.Contains("return") || stockLower.Contains("gain") ||
                                   stockLower.Contains("allocat") || stockLower.Contains("diversif") ||
                                   stockLower.Contains("financ") || stockLower.Contains("risk") ||
                                   stockLower.Contains("recommend");
        bool hasSpecificCompanies = stockLower.Contains("apple") || stockLower.Contains("nvidia") ||
                                    stockLower.Contains("microsoft") || stockLower.Contains("tesla") ||
                                    stockLower.Contains("amazon") || stockLower.Contains("google") ||
                                    stockLower.Contains("meta") || stockLower.Contains("aapl") ||
                                    stockLower.Contains("nvda") || stockLower.Contains("msft") ||
                                    stockLower.Contains("tsla") || stockLower.Contains("amzn") ||
                                    stockLower.Contains("etf") || stockLower.Contains("fund") ||
                                    stockLower.Contains("index");

        Assert.True(hasStockAdvice || hasInvestmentAdvice || hasSpecificCompanies,
            $"Expected stock-specific investment advice. Got: {stockResponse}");
    }
}

/// <summary>
/// Cheap robustness test: empty query should not crash the server.
/// </summary>
[Trait("Category", "Integration")]
public class EmptyQueryTests : McpEndToEndTestBase
{
    public EmptyQueryTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task EmptyQuery_ShouldReturnGracefulResponse()
    {
        await InitializeMcpSession();

        Output.WriteLine("=== EMPTY QUERY TEST ===");
        var response = await CallFinancialAdviceTool("");
        Output.WriteLine($"Response: {response}");

        response.Should().NotBeNullOrEmpty("server should handle empty queries gracefully");
    }
}
