using FinWise.McpServer.E2ETestBase;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace FinWise.McpServer.ContainerTests;

/// <summary>
/// Container smoke test. Proves the Dockerized FinWise MCP server boots,
/// accepts an MCP `initialize` handshake and exposes the expected tools.
///
/// The end-to-end functional scenarios live in
/// <c>FinWise.McpServer.IntegrationTests</c> and already run against the
/// same containerized server in CI — duplicating them here previously
/// doubled the E2E wall time without adding coverage, so this project now
/// keeps only the smoke checks that depend on the container itself.
/// </summary>
[Trait("Category", "Container")]
public class DockerizedMcpTests : McpEndToEndTestBase
{
    public DockerizedMcpTests(ITestOutputHelper output) : base(output) { }

    private async Task EnsureContainerRunning()
    {
        var reachable = await ContainerHealthCheck.IsServerReachableAsync(McpBaseUrl, TimeSpan.FromSeconds(5));
        Skip.IfNot(reachable, $"FinWise container not running at {McpBaseUrl}");
    }

    [SkippableFact]
    public async Task Container_McpHandshake_ShouldInitializeAndListTools()
    {
        await EnsureContainerRunning();

        // 1. MCP initialize handshake must succeed.
        var sessionId = await InitializeMcpSession();
        sessionId.Should().NotBeNullOrEmpty("MCP initialize should return a session ID");
        Output.WriteLine($"Container MCP Session: {sessionId}");

        // 2. The container must expose the expected tools.
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

        toolNames.Should().HaveCount(3);
        toolNames.Should().Contain("run_finwise_workflow");
        toolNames.Should().Contain("reset_conversation");
        toolNames.Should().Contain("get_storage_info");
    }
}
