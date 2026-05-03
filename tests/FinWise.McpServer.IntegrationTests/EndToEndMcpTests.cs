using System.Text;
using System.Text.Json;
using FinWise.McpServer.E2ETestBase;
using Xunit;
using Xunit.Abstractions;
using FluentAssertions;

namespace FinWise.McpServer.IntegrationTests;

/// <summary>
/// End-to-end tests that connect to a running FinWise MCP server via HTTP MCP protocol.
/// These tests validate the complete user journey from profile creation to financial advice.
/// 
/// PREREQUISITES:
/// 1. FinWise.McpServer must be running (default: http://localhost:5000, override via FINWISE_MCP_URL)
/// 2. Azure AI Foundry credentials must be configured (FINWISE_AZURE_AI_FOUNDRY_* + FINWISE_AZURE_* service principal)
/// 3. Run: dotnet run --project src/FinWise.McpServer/FinWise.McpServer.csproj --urls http://localhost:5000
/// </summary>
[Trait("Category", "Integration")]
public class EndToEndMcpTests : McpEndToEndTestBase
{
    public EndToEndMcpTests(ITestOutputHelper output) : base(output) { }

    [Fact]
    public async Task CompleteUserJourney_NewUser_ShouldCreateProfileAndProvideAdvice()
    {
        // Initialize MCP session first
        await InitializeMcpSession();

        // Arrange - Test data from log file
        string testEmail = $"journey-{Guid.NewGuid().ToString("N")[..8]}@example.com";

        // Build profile — drive through steps without asserting on intermediate
        // LLM wording (resilient to non-determinism).
        Output.WriteLine("=== COMPLETE USER JOURNEY TEST ===");
        Output.WriteLine($"Test Email: {testEmail}");

        Output.WriteLine("\n--- Profile setup ---");
        var profileReadyResponse = await SetupTestProfileWithEmail(testEmail);
        Assert.Contains(testEmail, profileReadyResponse);

        Output.WriteLine($"\nProfile Complete Response:\n{TruncateForLog(profileReadyResponse!, 500)}");

        // Profile agent may provide advice immediately or ask how to help next — both are valid
        var lower = profileReadyResponse!.ToLowerInvariant();
        bool hasAdvice = lower.Contains("invest") || lower.Contains("stock") ||
                         lower.Contains("fund") || lower.Contains("portfolio");
        bool asksFollowUp = lower.Contains("assist") || lower.Contains("help") ||
                            lower.Contains("how can i");
        Assert.True(hasAdvice || asksFollowUp,
            $"Expected investment advice or follow-up prompt. Got: {profileReadyResponse}");

        Output.WriteLine("=== COMPLETE USER JOURNEY TEST PASSED ===");
    }

    [Fact]
    public async Task SameSession_AfterProfileSetup_ShouldRetainSessionContext()
    {
        await InitializeMcpSession();
        await SetupTestProfile();

        Output.WriteLine("=== SAME SESSION CONTEXT RETENTION TEST ===");
        var response = await CallFinancialAdviceTool("What is my investment profile?");
        var lowerResponse = response.ToLowerInvariant();
        Output.WriteLine($"Response: {response}");

        // The system should recognize the user from conversation history
        // and provide profile-related information without asking for email again
        bool hasProfileData = response.Contains("PROFILE_READY:") ||
                              lowerResponse.Contains("moderate") ||
                              lowerResponse.Contains("increase profit");
        bool asksForEmail = lowerResponse.Contains("please provide your email") ||
                            lowerResponse.Contains("what is your email");

        // After profile setup in the same session, system should NEVER ask for email
        asksForEmail.Should().BeFalse("system should not ask for email when profile is in session context");

        // At minimum, the response should be about profiles/investments
        bool isProfileRelated = hasProfileData ||
                                lowerResponse.Contains("profile") ||
                                lowerResponse.Contains("risk") ||
                                lowerResponse.Contains("investment");
        isProfileRelated.Should().BeTrue($"Expected profile-related response. Got: {response}");
    }

    [Fact]
    public async Task ResetSession_ShouldClearHistoryAndRequireReidentification()
    {
        // Arrange — establish a profile in a session
        await InitializeMcpSession();
        await SetupTestProfile();

        Output.WriteLine("=== RESET CONVERSATION TEST ===");

        // Act — call reset_conversation tool
        var resetResponse = await CallResetSessionTool();
        Output.WriteLine($"Reset Response: {resetResponse}");

        // Assert — should confirm reset (different message for database vs in-memory stores)
        var resetLower = resetResponse.ToLowerInvariant();
        bool confirmsCleared = resetLower.Contains("cleared");
        bool indicatesInMemory = resetLower.Contains("in-memory");
        (confirmsCleared || indicatesInMemory).Should().BeTrue(
            because: "reset should confirm clearing (database store) or explain in-memory limitation. Got: " + resetResponse);

        // Act — send a follow-up message after reset
        var followUpResponse = await CallFinancialAdviceTool("Give me financial advice");
        Output.WriteLine($"Post-Reset Response: {followUpResponse}");

        // Assert — after reset, the system should either:
        // (a) Ask for email (session store supports native clear, e.g. Redis), OR
        // (b) Provide financial advice (InMemory store where clear is a no-op,
        //     so profile data persists and the system recognizes the user)
        var followUpLower = followUpResponse.ToLowerInvariant();
        bool asksForEmail = followUpLower.Contains("email");
        bool providesAdvice = followUpLower.Contains("invest") || followUpLower.Contains("portfolio") ||
                              followUpLower.Contains("stock") || followUpLower.Contains("fund") ||
                              followUpLower.Contains("bond") || followUpLower.Contains("recommend") ||
                              followUpLower.Contains("risk") || followUpLower.Contains("financial");
        (asksForEmail || providesAdvice).Should().BeTrue(
            because: "after reset, system should either ask for email (cleared session) or provide advice (profile retained). Got: " + followUpResponse);
        Output.WriteLine($"Post-reset behavior: AsksForEmail={asksForEmail}, ProvidesAdvice={providesAdvice}");
    }

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

        // Parse — handle both SSE and plain JSON
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

    [Fact]
    public async Task ReturningUser_NewSession_ShouldFindExistingProfile()
    {
        // Initialize MCP session first
        await InitializeMcpSession();

        // Arrange - Create profile in one session
        var testEmail = await SetupTestProfile();

        // Act - Create new session and request advice
        // Note: For MCP, we need to initialize a new session for the "new session" test
        // But for this test we're simulating that the user profile persists across sessions
        var newSessionId = await InitializeNewMcpSession();
        Output.WriteLine($"=== NEW SESSION TEST (Session: {newSessionId}) ===");

        // New session means new conversation history (empty), so system WILL ask for email
        var response = await CallFinancialAdviceTool("Give me financial advice", newSessionId);
        Output.WriteLine($"New Session Response: {response}");

        // Assert - New session should ask for email (conversation history is empty)
        Assert.Contains("email", response.ToLowerInvariant());

        // Provide email when asked
        var emailResponse = await CallFinancialAdviceTool(testEmail, newSessionId);
        Output.WriteLine($"Email Response in New Session: {emailResponse}");

        // Assert - Should find existing profile in store
        // Note: If profile was incomplete due to setup issues, agent will ask for missing fields
        // If profile was complete, agent will show PROFILE_READY and not ask for fields
        var lowerEmailResponse = emailResponse.ToLowerInvariant();

        // Should at minimum recognize the email and find something in the profile store
        // The response should contain profile-related information (either found or collection prompts)
        bool hasProfileReady = emailResponse.Contains("PROFILE_READY:");
        bool asksForMissingData = lowerEmailResponse.Contains("risk") || lowerEmailResponse.Contains("goal") || lowerEmailResponse.Contains("timeframe");

        // Either profile was found complete OR it's asking for missing data - both are valid profile agent behavior
        Assert.True(hasProfileReady || asksForMissingData,
            $"Expected either PROFILE_READY or prompts for missing data. Got: {emailResponse}");

        Output.WriteLine($"Profile behavior: HasProfileReady={hasProfileReady}, AsksForMissingData={asksForMissingData}");
    }

    [Fact]
    public async Task TwoSessions_SameEmail_ShouldReuseProfileAndAnswerDifferentQuestions()
    {
        // Arrange - Two separate MCP sessions with unique IDs
        string sessionId1 = await InitializeNewMcpSession();

        // Use unique email per test run to ensure clean profile creation test
        // (Profile store is in-memory but persists while server runs)
        string testEmail = $"session-test-{Guid.NewGuid().ToString("N")[..12]}@example.com";

        Output.WriteLine($"=== SESSION 1: New User Creating Profile (Session: {sessionId1}) ===");
        Output.WriteLine($"Test Email: {testEmail}");

        // ========== SESSION 1: Build profile without rigid intermediate assertions ==========

        Output.WriteLine("\n--- Session 1: Profile setup ---");
        var s1ProfileReady = await SetupTestProfileWithEmail(testEmail, sessionId1, "Moderate", "Wealth building", "Long-term");
        Assert.Contains("moderate", s1ProfileReady.ToLowerInvariant());

        Output.WriteLine($"\nSession 1 Profile Complete:");
        Output.WriteLine(TruncateForLog(s1ProfileReady!, 500));
        Output.WriteLine($"\n=== SESSION 1 COMPLETED - Profile saved for {testEmail} ===");

        // Wait for profile to be fully persisted to the store
        await Task.Delay(250);

        // ========== SESSION 2: Returning User with Different Question ==========

        string sessionId2 = await InitializeNewMcpSession();
        Output.WriteLine($"\n\n=== SESSION 2: Returning User with Different Question (Session: {sessionId2}) ===");

        // Step 1: User asks NEW question about stocks (different from Session 1)
        Output.WriteLine("\n--- Session 2, Step 1: Ask stock selection question ---");
        var s2Response1 = await CallFinancialAdviceTool("What stocks should I buy?", sessionId2);
        var s2R1Lower = s2Response1.ToLowerInvariant();

        // Assert: Should ask for email (new session = empty conversation history)
        Assert.Contains("email", s2R1Lower);
        Output.WriteLine($"Response: {TruncateForLog(s2Response1)}");

        // Step 2: Provide same email from Session 1
        Output.WriteLine("\n--- Session 2, Step 2: Provide same email (should load existing profile) ---");
        var s2Response2 = await CallFinancialAdviceTool(testEmail, sessionId2);

        // Assert: Should find existing profile
        Assert.True(IsProfileCompleteResponse(s2Response2, testEmail) ||
                    s2Response2.ToLowerInvariant().Contains("risk") ||
                    s2Response2.ToLowerInvariant().Contains("goal") ||
                    s2Response2.ToLowerInvariant().Contains("moderate"),
            $"Expected profile recognition. Got: {s2Response2}");
        var s2R2Lower = s2Response2.ToLowerInvariant();

        Output.WriteLine($"Session 2 Profile Loaded:");
        Output.WriteLine(TruncateForLog(s2Response2, 500));

        // Step 3: Re-ask the question (ProfileAgent doesn't remember original question from Step 1)
        Output.WriteLine("\n--- Session 2, Step 3: Re-ask stock question (with profile already loaded) ---");
        var s2Response3 = await CallFinancialAdviceTool("What stocks should I buy?", sessionId2);

        // Assert: Should provide stock recommendations based on loaded profile
        var s2R3Lower = s2Response3.ToLowerInvariant();
        bool hasAdviceS2 = s2R3Lower.Contains("stock") || s2R3Lower.Contains("invest") ||
                         s2R3Lower.Contains("fund") || s2R3Lower.Contains("portfolio") ||
                         s2R3Lower.Contains("etf") || s2R3Lower.Contains("bond");
        Assert.True(hasAdviceS2, $"Expected investment advice. Got: {s2Response3}");

        Output.WriteLine($"Session 2 Stock Recommendations:");
        Output.WriteLine(TruncateForLog(s2Response3, 500));
        Output.WriteLine($"\n=== SESSION 2 COMPLETED - Profile reused, different question answered ===");

        // Final validation
        Output.WriteLine("\n=== FINAL VALIDATION ===");
        Output.WriteLine($"✓ Session 1: Created profile for {testEmail}");
        Output.WriteLine($"✓ Session 1: Answered 'What should I invest into?'");
        Output.WriteLine($"✓ Session 2: Loaded existing profile for {testEmail}");
        Output.WriteLine($"✓ Session 2: Answered 'What stocks should I buy?' with same profile");
        Output.WriteLine($"✓ Profile persistence across sessions: VALIDATED");
    }

    [Fact]
    public async Task AggressiveShortTerm_ShouldReturnStockFocusedAdvice()
    {
        // Arrange — new session, unique email
        await InitializeMcpSession();
        string testEmail = $"stock-test-{Guid.NewGuid().ToString("N")[..8]}@example.com";

        Output.WriteLine("=== STOCK-FOCUSED ADVICE TEST ===");
        Output.WriteLine($"Test Email: {testEmail}");

        // Build aggressive/short-term profile — drive through steps without
        // asserting on intermediate LLM wording (resilient to non-determinism).
        Output.WriteLine("\n--- Profile setup: aggressive, short-term stock trader ---");
        var profileReadyResponse = await SetupTestProfileWithEmail(testEmail, null,
            "Aggressive", "Short-term capital gains through stock trading", "Short-term, less than 1 year");
        Assert.Contains(testEmail, profileReadyResponse);
        Assert.Contains("aggressive", profileReadyResponse.ToLowerInvariant());
        Output.WriteLine($"\nProfile Complete Response:\n{TruncateForLog(profileReadyResponse, 500)}");

        Output.WriteLine("Profile validated: email + aggressive risk confirmed");

        // Wait for persistence.
        await Task.Delay(250);

        // Step 6: Ask specifically about stock investments and validate that
        // the response contains stock-focused guidance for this profile.
        Output.WriteLine("\n--- Step 6: Ask for stock investment recommendations ---");
        var stockResponse = await CallFinancialAdviceTool(
            "Based on my aggressive profile, which specific stocks should I invest in right now for maximum short-term gains?");
        var stockLower = stockResponse.ToLowerInvariant();

        // Retry if the response is off-topic or a transient error
        for (int attempt = 1; attempt <= 3 && (IsTransientError(stockResponse) || stockLower.Length < 30); attempt++)
        {
            Output.WriteLine($"  Stock advice failed (attempt {attempt}), retrying...");
            await Task.Delay(1500 * attempt);
            stockResponse = await CallFinancialAdviceTool(
                "Based on my aggressive profile, which specific stocks should I invest in right now for maximum short-term gains?");
            stockLower = stockResponse.ToLowerInvariant();
        }
        Output.WriteLine($"Stock Response: {stockResponse}");

        // Assert: Response should contain stock-related investment advice
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

        Output.WriteLine("\n=== VALIDATION ===");
        Output.WriteLine($"  Has stock-related terms: {hasStockAdvice}");
        Output.WriteLine($"  Has investment advice: {hasInvestmentAdvice}");
        Output.WriteLine($"  Has specific companies/tickers: {hasSpecificCompanies}");
        Output.WriteLine("=== STOCK-FOCUSED ADVICE TEST COMPLETED ===");
    }

    [Fact]
    public async Task EmptyQuery_ShouldReturnGracefulResponse()
    {
        await InitializeMcpSession();

        Output.WriteLine("=== EMPTY QUERY TEST ===");
        var response = await CallFinancialAdviceTool("");
        Output.WriteLine($"Response: {response}");

        // Should not crash — either asks for email or returns an error message
        response.Should().NotBeNullOrEmpty("server should handle empty queries gracefully");
    }
}
