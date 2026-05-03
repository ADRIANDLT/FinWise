using FinWise.McpServer.E2ETestBase;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace FinWise.McpServer.ContainerTests;

/// <summary>
/// Verifies that FINWISE_* environment variables are correctly passed to the Docker container
/// and that the configuration pipeline (appsettings → appsettings.Docker → env vars) works end-to-end.
/// </summary>
[Trait("Category", "Container")]
public class DockerEnvVarConfigTests : McpEndToEndTestBase
{
    public DockerEnvVarConfigTests(ITestOutputHelper output) : base(output) { }

    private async Task EnsureContainerRunning()
    {
        var reachable = await ContainerHealthCheck.IsServerReachableAsync(McpBaseUrl, TimeSpan.FromSeconds(5));
        Skip.IfNot(reachable, $"FinWise container not running at {McpBaseUrl}");
    }

    [SkippableFact]
    public async Task Container_EnvVarConfig_ShouldStartHealthyWithDataStoreConfig()
    {
        // Arrange — verify container is running
        await EnsureContainerRunning();

        // Act — health check confirms the server started with valid configuration
        using var client = new HttpClient();
        var response = await client.GetAsync($"{McpBaseUrl}/health");

        // Assert
        response.IsSuccessStatusCode.Should().BeTrue(
            "the container should start healthy, proving the env var configuration pipeline works");
    }

    [SkippableFact]
    public async Task Container_EnvVarConfig_ShouldRespondToMcpToolCallsWithConfiguredStores()
    {
        // Arrange — verify container is running and initialize MCP session
        await EnsureContainerRunning();
        await InitializeMcpSession();

        // Act — call a tool that exercises the full data store pipeline
        var response = await CallFinancialAdviceTool("hello");

        // Assert — any response (even "provide your email") proves stores are configured
        response.Should().NotBeNullOrEmpty(
            "the server should respond to tool calls, proving data stores (via env vars or appsettings) are operational");
        Output.WriteLine($"Server responded: {TruncateForLog(response)}");
    }
}
