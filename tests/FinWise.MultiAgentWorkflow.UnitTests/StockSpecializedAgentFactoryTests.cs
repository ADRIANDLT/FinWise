using Azure.AI.Projects;
using Azure.Core;
using FluentAssertions;
using FinWise.MultiAgentWorkflow.Agents.StockSpecializedAgent;
using Xunit;

namespace FinWise.MultiAgentWorkflow.UnitTests;

public class StockSpecializedAgentFactoryTests
{
    private const string ValidAgentName = "stock-data-agent";

    // AIProjectClient requires a real endpoint + credential; use a dummy for constructor tests.
    private static readonly AIProjectClient DummyClient =
        new(new Uri("https://dummy.services.ai.azure.com/api/projects/dummy"), new DummyTokenCredential());

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenProjectClientIsNull()
    {
        var act = () => new StockSpecializedAgentFactory(null!, ValidAgentName);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("projectClient");
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenAgentNameIsNull()
    {
        var act = () => new StockSpecializedAgentFactory(DummyClient, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("agentName");
    }

    [Fact]
    public void Constructor_ThrowsArgumentException_WhenAgentNameIsEmpty()
    {
        var act = () => new StockSpecializedAgentFactory(DummyClient, "");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("agentName");
    }

    [Fact]
    public void Constructor_ThrowsArgumentException_WhenAgentNameIsWhitespace()
    {
        var act = () => new StockSpecializedAgentFactory(DummyClient, "   ");

        act.Should().Throw<ArgumentException>()
            .WithParameterName("agentName");
    }

    [Fact]
    public void Constructor_Succeeds_WithValidArguments()
    {
        var act = () => new StockSpecializedAgentFactory(DummyClient, ValidAgentName);

        act.Should().NotThrow();
    }

    // NOTE: CreateAgentAsync calls the Foundry API (GetAIAgentAsync())
    // which uses sealed Azure SDK types that cannot be mocked. Covered by integration tests.

    private class DummyTokenCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) => default;
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) => default;
    }
}
