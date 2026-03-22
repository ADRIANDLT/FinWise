using System.ClientModel;
using Azure.AI.Projects;
using Microsoft.Agents.AI;
using Serilog;

namespace FinWise.MultiAgentWorkflow.Agents.StockSpecializedAgent;

public class StockSpecializedAgentFactory
{
    private readonly AIProjectClient _projectClient;
    private readonly string _agentName;

    public StockSpecializedAgentFactory(AIProjectClient projectClient, string agentName)
    {
        _projectClient = projectClient ?? throw new ArgumentNullException(nameof(projectClient));
        ArgumentException.ThrowIfNullOrWhiteSpace(agentName);
        _agentName = agentName;
    }

    public async Task<AIAgent> CreateAgentAsync()
    {
        Log.Information("Resolving Foundry agent by name: {AgentName}", _agentName);

        AIAgent agent;
        try
        {
            agent = await _projectClient.GetAIAgentAsync(_agentName);
        }
        catch (ClientResultException ex)
        {
            throw new InvalidOperationException(
                $"No Foundry agent found with name '{_agentName}'", ex);
        }

        Log.Information("Resolved Foundry agent '{AgentName}'", agent.Name);
        return agent;
    }
}
