using FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStores.Redis;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.Configuration;
using Serilog;
using StackExchange.Redis;

namespace FinWise.McpServer.Infrastructure.AgentSessionStorage;

/// <summary>
/// Creates the appropriate <see cref="AgentSessionStore"/> (Redis or in-memory) based on configuration.
/// </summary>
public static class AgentSessionStoreFactory
{
    /// <summary>
    /// Creates a Redis-backed or in-memory agent session store depending on the
    /// <c>Redis:Enabled</c> configuration flag.
    /// Returns the Redis connection and options alongside the store because the
    /// MCP session migration handler (a separate infrastructure concern) reuses
    /// the same Redis connection.
    /// </summary>
    public static async Task<(AgentSessionStore SessionStore, IConnectionMultiplexer? Redis, RedisOptions RedisOptions)> CreateSessionStoreAsync(
        IConfiguration configuration)
    {
        var redisOptions = new RedisOptions();
        configuration.GetSection(RedisOptions.SectionName).Bind(redisOptions);

        if (redisOptions.Enabled)
        {
            Log.Information("Using Redis agent session store (Host: {RedisHost}, TTL: {Ttl} min)",
                redisOptions.ConnectionString.Split(',')[0], redisOptions.SessionTtlMinutes);

            var redis = await ConnectionMultiplexer.ConnectAsync(redisOptions.ConnectionString);
            var sessionStore = new RedisAgentSessionStore(redis, TimeSpan.FromMinutes(redisOptions.SessionTtlMinutes), "orchestrator_agent");
            return (sessionStore, redis, redisOptions);
        }

        Log.Information("Using in-memory agent session store");
        return (new InMemoryAgentSessionStore(), null, redisOptions);
    }
}
