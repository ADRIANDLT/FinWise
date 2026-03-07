using System.Collections.Concurrent;

namespace FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStore.InMemory;

/// <summary>
/// In-memory implementation of IAgentSessionStore for development/testing.
/// Uses thread-safe ConcurrentDictionary for storage.
///
/// Per Microsoft Agent Framework patterns:
/// - Stores serialized AgentSession data (JsonElement from agent.SerializeSessionAsync(session))
/// - Supports resumption via agent.DeserializeSessionAsync(json)
///
/// Production: Replace with Cosmos DB implementation (v0.3+).
/// </summary>
public class InMemoryAgentSessionStore : IAgentSessionStore
{
    private readonly ConcurrentDictionary<string, AgentSessionData> _sessions = new();

    /// <inheritdoc />
    public Task<AgentSessionData?> GetSessionDataAsync(string agentSessionId)
    {
        _sessions.TryGetValue(agentSessionId, out var data);
        return Task.FromResult(data);
    }

    /// <inheritdoc />
    public Task SetSessionDataAsync(string agentSessionId, AgentSessionData data)
    {
        _sessions[agentSessionId] = data;
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ClearSessionAsync(string agentSessionId)
    {
        _sessions.TryRemove(agentSessionId, out _);
        return Task.CompletedTask;
    }
}
