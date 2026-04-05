using System.ClientModel;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
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

        ProjectsAgentRecord agentRecord;
        try
        {
            agentRecord = await _projectClient.AgentAdministrationClient.GetAgentAsync(_agentName);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException(
                $"No Foundry agent found with name '{_agentName}'", ex);
        }

        FoundryAgent agent = _projectClient.AsAIAgent(agentRecord);
        Log.Information("Resolved Foundry agent '{AgentName}'", agent.Name);
        return agent;
    }
}
