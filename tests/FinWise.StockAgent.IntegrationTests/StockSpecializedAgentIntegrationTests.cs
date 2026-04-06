using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
using Azure.Identity;
using FluentAssertions;
using FinWise.MultiAgentWorkflow.Agents.StockSpecializedAgent;
using OpenAI.Responses;
using Xunit;

namespace FinWise.StockAgent.IntegrationTests;

[Trait("Category", "Integration")]
public class StockSpecializedAgentIntegrationTests
{
    private readonly string? _endpoint;
    private readonly string? _tenantId;
    private readonly string? _clientId;
    private readonly string? _clientSecret;
    private readonly string _agentName;

    public StockSpecializedAgentIntegrationTests()
    {
        _endpoint = Environment.GetEnvironmentVariable("STOCK_AGENT_PROJECT_ENDPOINT");
        _tenantId = Environment.GetEnvironmentVariable("FINWISE_AZURE_TENANT_ID");
        _clientId = Environment.GetEnvironmentVariable("FINWISE_AZURE_CLIENT_ID");
        _clientSecret = Environment.GetEnvironmentVariable("FINWISE_AZURE_CLIENT_SECRET");
        var envName = Environment.GetEnvironmentVariable("STOCK_AGENT_NAME");
        _agentName = string.IsNullOrWhiteSpace(envName)
            ? "stock-specialized-investment-agent"
            : envName;
    }

    private AIProjectClient CreateClient()
    {
        Skip.If(string.IsNullOrWhiteSpace(_endpoint),
            "STOCK_AGENT_PROJECT_ENDPOINT not set — skipping Foundry integration test.");
        Skip.If(string.IsNullOrWhiteSpace(_tenantId),
            "FINWISE_AZURE_TENANT_ID not set — skipping Foundry integration test.");
        Skip.If(string.IsNullOrWhiteSpace(_clientId),
            "FINWISE_AZURE_CLIENT_ID not set — skipping Foundry integration test.");
        Skip.If(string.IsNullOrWhiteSpace(_clientSecret),
            "FINWISE_AZURE_CLIENT_SECRET not set — skipping Foundry integration test.");

        var credential = new ClientSecretCredential(_tenantId!, _clientId!, _clientSecret!);
        return new AIProjectClient(new Uri(_endpoint!), credential);
    }

    [SkippableFact]
    public async Task CreateAgentAsync_ResolvesFoundryAgent_ByName()
    {
        var client = CreateClient();
        var factory = new StockSpecializedAgentFactory(client, _agentName);

        var agent = await factory.CreateAgentAsync();

        agent.Should().NotBeNull();
        agent.Name.Should().Be(_agentName);
    }

    [SkippableFact]
    public async Task CreateAgentAsync_ThrowsInvalidOperationException_WhenAgentNameNotFound()
    {
        var client = CreateClient();
        var factory = new StockSpecializedAgentFactory(client, "nonexistent-agent-12345");

        var act = () => factory.CreateAgentAsync();

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*nonexistent-agent-12345*");
    }

    [SkippableFact]
    public void FoundryConnectivity_CanListAgents_SmokeTest()
    {
        var client = CreateClient();
        var agents = client.AgentAdministrationClient.GetAgents().ToList();

        agents.Should().NotBeEmpty("at least one agent should exist in the Foundry project");
    }

    [SkippableFact]
    public async Task AgentResponds_WithMeaningfulStockInformation_WhenAskedAboutMicrosoft()
    {
        var projectClient = CreateClient();
        ProjectResponsesClient responsesClient = projectClient.ProjectOpenAIClient
            .GetProjectResponsesClientForAgent(defaultAgent: _agentName);

#pragma warning disable OPENAI001 // Preview API
        ResponseResult response = await responsesClient.CreateResponseAsync(
            "give me information about microsoft stock as a company");
#pragma warning restore OPENAI001

        string outputText = response.GetOutputText();

        outputText.Should().NotBeNullOrWhiteSpace(
            "the agent should return a meaningful response about Microsoft stock");

        var keywords = new[] { "Microsoft", "MSFT", "stock", "revenue", "market" };
        outputText.Should().ContainAny(keywords,
            "the response should contain at least one relevant keyword");
    }
}
