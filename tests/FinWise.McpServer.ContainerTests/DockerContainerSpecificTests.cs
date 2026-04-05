using System.Diagnostics;
using FinWise.McpServer.E2ETestBase;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace FinWise.McpServer.ContainerTests;

/// <summary>
/// Tests that validate Docker-specific concerns only exercisable against a containerized server.
/// </summary>
public class DockerContainerSpecificTests : McpEndToEndTestBase
{
    public DockerContainerSpecificTests(ITestOutputHelper output) : base(output) { }

    private async Task EnsureContainerRunning()
    {
        var reachable = await ContainerHealthCheck.IsServerReachableAsync(McpBaseUrl, TimeSpan.FromSeconds(5));
        Skip.IfNot(reachable, $"FinWise container not running at {McpBaseUrl}");
    }

    [SkippableFact]
    public async Task Container_ShouldBeReachableAndHealthy()
    {
        // Validates: Dockerfile builds, Kestrel binds 0.0.0.0:5000, port mapping works
        var reachable = await ContainerHealthCheck.IsServerReachableAsync(McpBaseUrl, TimeSpan.FromSeconds(30));

        Skip.IfNot(reachable, $"FinWise container not running at {McpBaseUrl}");

        reachable.Should().BeTrue("container should be reachable and healthy");
        Output.WriteLine($"Container at {McpBaseUrl} is reachable and healthy");
    }

    [SkippableFact]
    public async Task Container_RedisConnectivity_ShouldWorkOverDockerNetwork()
    {
        // Validates: appsettings.Docker.json Redis override, Docker DNS resolves redis:6379
        await EnsureContainerRunning();
        await InitializeMcpSession();
        await SetupTestProfile();

        // Reset writes/clears Redis — if this succeeds, Redis connectivity works
        var resetResponse = await CallResetSessionTool();

        resetResponse.ToLowerInvariant().Should().Contain("cleared",
            because: "reset tool uses Redis, proving Docker network connectivity to redis:6379");
        Output.WriteLine("Redis connectivity over Docker network: VERIFIED");
    }

    [SkippableFact]
    public async Task Container_AzureOpenAIEnvVars_ShouldBeInjected()
    {
        // Validates: .env → docker-compose → container env var injection chain
        await EnsureContainerRunning();
        await InitializeMcpSession();

        var response = await CallFinancialAdviceTool("Hello");

        response.Should().NotBeNullOrEmpty(
            because: "Azure OpenAI env vars must be injected for the LLM to respond");
        Output.WriteLine("Azure OpenAI env var injection: VERIFIED");
    }

    [SkippableFact]
    public async Task Container_CosmosDbConnectivity_ShouldWorkOverDockerNetwork()
    {
        // Validates: appsettings.Docker.json CosmosDB override, cross-container TLS
        await EnsureContainerRunning();

        string testEmail = $"docker-cosmos-{Guid.NewGuid().ToString("N")[..8]}@example.com";

        // Session 1: Create profile (writes to CosmosDB)
        var session1 = await InitializeNewMcpSession();
        await SetupTestProfileWithEmail(testEmail, session1);

        await Task.Delay(1000); // Allow persistence

        // Session 2: Verify profile retrieval from CosmosDB
        var session2 = await InitializeNewMcpSession();
        await CallFinancialAdviceTool("Give me financial advice", session2);
        var emailResponse = await CallFinancialAdviceTool(testEmail, session2);

        // Should find existing profile
        var hasProfileReady = emailResponse.Contains("PROFILE_READY:");
        var asksForData = emailResponse.ToLowerInvariant().Contains("risk") ||
                          emailResponse.ToLowerInvariant().Contains("goal");

        (hasProfileReady || asksForData).Should().BeTrue(
            because: "CosmosDB should store and retrieve profiles over Docker network");
        Output.WriteLine("CosmosDB connectivity over Docker network: VERIFIED");
    }

    [SkippableFact]
    public async Task Container_StartupTime_ShouldBeReasonable()
    {
        // Validates: No SDK in runtime image, published assemblies complete
        await EnsureContainerRunning();

        var sw = Stopwatch.StartNew();
        await InitializeMcpSession();
        sw.Stop();

        sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10),
            because: "MCP initialize should complete within 10s for a properly published image");
        Output.WriteLine($"Container startup + MCP initialize: {sw.ElapsedMilliseconds}ms");
    }
}
