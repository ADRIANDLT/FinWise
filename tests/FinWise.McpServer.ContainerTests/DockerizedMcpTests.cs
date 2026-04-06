using FinWise.McpServer.E2ETestBase;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace FinWise.McpServer.ContainerTests;

/// <summary>
/// Re-exercises existing E2E MCP protocol scenarios against the Dockerized server.
/// Proves the app works identically inside the container.
/// </summary>
public class DockerizedMcpTests : McpEndToEndTestBase
{
    public DockerizedMcpTests(ITestOutputHelper output) : base(output) { }

    private async Task EnsureContainerRunning()
    {
        var reachable = await ContainerHealthCheck.IsServerReachableAsync(McpBaseUrl, TimeSpan.FromSeconds(5));
        Skip.IfNot(reachable, $"FinWise container not running at {McpBaseUrl}");
    }

    [SkippableFact]
    public async Task Container_McpInitialize_ShouldReturnSessionId()
    {
        await EnsureContainerRunning();

        var sessionId = await InitializeMcpSession();

        sessionId.Should().NotBeNullOrEmpty("MCP initialize should return a session ID");
        Output.WriteLine($"Container MCP Session: {sessionId}");
    }

    [SkippableFact]
    public async Task Container_ToolDiscovery_ShouldExposeTools()
    {
        await EnsureContainerRunning();
        await InitializeMcpSession();

        Output.WriteLine("=== CONTAINER TOOL DISCOVERY TEST ===");

        var listRequest = new
        {
            jsonrpc = "2.0",
            id = Guid.NewGuid().ToString(),
            method = "tools/list",
            @params = new { }
        };

        var json = System.Text.Json.JsonSerializer.Serialize(listRequest, new System.Text.Json.JsonSerializerOptions
        {
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
        });

        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        content.Headers.Add("MCP-Session-Id", SessionId);

        var response = await HttpClient.PostAsync($"{McpBaseUrl}/mcp", content);
        var responseContent = await response.Content.ReadAsStringAsync();

        response.IsSuccessStatusCode.Should().BeTrue($"tools/list failed: {responseContent}");

        string jsonContent = responseContent;
        if (responseContent.StartsWith("event:"))
        {
            var dataLine = responseContent.Split('\n').FirstOrDefault(l => l.StartsWith("data:"));
            dataLine.Should().NotBeNull("SSE response missing data: line");
            jsonContent = dataLine!.Substring(5).Trim();
        }

        var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonContent);
        var tools = jsonDoc.RootElement.GetProperty("result").GetProperty("tools");

        var toolNames = tools.EnumerateArray()
            .Select(t => t.GetProperty("name").GetString())
            .OrderBy(n => n)
            .ToList();

        Output.WriteLine($"Container tools: {string.Join(", ", toolNames)}");

        toolNames.Should().HaveCount(2);
        toolNames.Should().Contain("run_finwise_workflow");
        toolNames.Should().Contain("reset_conversation");
    }

    [SkippableFact]
    public async Task Container_FinancialAdvice_ShouldAskForEmail()
    {
        await EnsureContainerRunning();
        await InitializeMcpSession();

        var response = await CallFinancialAdviceTool("Give me financial advice");

        response.ToLowerInvariant().Should().Contain("email",
            because: "new session should ask for email");
        Output.WriteLine($"Container response: {TruncateForLog(response)}");
    }

    [SkippableFact]
    public async Task Container_ResetConversation_ShouldClear()
    {
        await EnsureContainerRunning();
        await InitializeMcpSession();
        await SetupTestProfile();

        var resetResponse = await CallResetSessionTool();

        resetResponse.ToLowerInvariant().Should().Contain("cleared",
            because: "reset should confirm conversation was cleared");
        Output.WriteLine($"Container reset: {TruncateForLog(resetResponse)}");

        var followUp = await CallFinancialAdviceTool("Give me financial advice");

        followUp.ToLowerInvariant().Should().Contain("email",
            because: "after reset, should ask for email again");
    }
}
