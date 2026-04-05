using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Serilog;
using FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStores;
using StackExchange.Redis;

namespace FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStores.Redis;

/// <summary>
/// Redis-backed implementation of <see cref="AgentSessionStore"/> for durable session persistence.
/// Uses StackExchange.Redis with TTL-based expiration.
///
/// Key format: agentsession:{agentId}:{conversationId} — namespaced to separate from other Redis stores.
///
/// For production use with multiple instances or persistence across restarts.
/// Falls back to SDK's InMemoryAgentSessionStore when Redis is disabled.
/// </summary>
public sealed class RedisAgentSessionStore : AgentSessionStore, IClearableSessionStore, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _ttl;
    private readonly string _agentId;

    public RedisAgentSessionStore(IConnectionMultiplexer redis, TimeSpan ttl, string agentId)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _ttl = ttl;
        _agentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
    }

    /// <inheritdoc/>
    public override async ValueTask SaveSessionAsync(
        AIAgent agent, string conversationId, AgentSession session,
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(agent.Id, conversationId);
        var json = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
        await _redis.GetDatabase().StringSetAsync(key, json.GetRawText(), _ttl);

        Log.Debug("Redis: Saved session {Key} (TTL: {Ttl})", key, _ttl);
    }

    /// <inheritdoc/>
    public override async ValueTask<AgentSession> GetSessionAsync(
        AIAgent agent, string conversationId,
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(agent.Id, conversationId);
        var json = await _redis.GetDatabase().StringGetAsync(key);

        if (json.IsNullOrEmpty)
        {
            Log.Debug("Redis: No session found for {Key}, creating new", key);
            return await agent.CreateSessionAsync(cancellationToken);
        }

        try
        {
            Log.Debug("Redis: Restored session from {Key}", key);
            var element = JsonSerializer.Deserialize<JsonElement>(json.ToString());
            return await agent.DeserializeSessionAsync(element, cancellationToken: cancellationToken);
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Redis: Corrupt session data for {Key}, creating new session", key);
            await _redis.GetDatabase().KeyDeleteAsync(key);
            return await agent.CreateSessionAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Deletes a session from Redis. Implements <see cref="IClearableSessionStore"/>
    /// for explicit session resets via <c>AgentSessionManager.ClearSessionAsync</c>.
    /// </summary>
    /// <param name="conversationId">The agent session identifier to delete.</param>
    public async Task ClearSessionAsync(string conversationId)
    {
        var key = GetKey(_agentId, conversationId);
        await _redis.GetDatabase().KeyDeleteAsync(key);
        Log.Debug("Redis: Deleted session {Key}", key);
    }

    /// <summary>
    /// Disposes the Redis connection multiplexer.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _redis.Dispose();
        return ValueTask.CompletedTask;
    }

    internal static string GetKey(string agentId, string conversationId) => $"agentsession:{agentId}:{conversationId}";
}
