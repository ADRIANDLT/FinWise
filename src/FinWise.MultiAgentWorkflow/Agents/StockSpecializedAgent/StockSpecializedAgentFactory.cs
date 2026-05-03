using System.ClientModel;
using Azure.AI.Extensions.OpenAI;
using Azure.AI.Projects;
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

        try
        {
            await _projectClient.AgentAdministrationClient.GetAgentAsync(_agentName);
        }
        catch (ClientResultException ex) when (ex.Status == 404)
        {
            throw new InvalidOperationException(
                $"No Foundry agent found with name '{_agentName}'", ex);
        }

        // Use AgentReference (name only, no version) so the Responses API always resolves
        // the latest deployed version. AsAIAgent(ProjectsAgentRecord) extracted a specific
        // version ID from the Agent Administration API that the Responses API endpoint of
        // newer Foundry projects does not accept, causing agent invocation to fail.
        FoundryAgent agent = _projectClient.AsAIAgent(new AgentReference(_agentName));
        Log.Information("Resolved Foundry agent '{AgentName}'", agent.Name);
        return agent;
    }
}
