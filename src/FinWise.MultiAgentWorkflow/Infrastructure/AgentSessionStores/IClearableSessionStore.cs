namespace FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStores;

/// <summary>
/// Optional capability for session stores that support explicit session deletion.
/// Not part of the SDK's <see cref="Microsoft.Agents.AI.Hosting.AgentSessionStore"/> contract.
/// </summary>
public interface IClearableSessionStore
{
    Task ClearSessionAsync(string conversationId);
}
