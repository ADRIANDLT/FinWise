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
        if (IsForceInMemoryDataEnabled(configuration))
        {
            Log.Information("ForceInMemoryData is enabled — using in-memory agent session store");
            return (new InMemoryAgentSessionStore(), null, new RedisOptions());
        }

        var redisOptions = new RedisOptions();
        configuration.GetSection(RedisOptions.SectionName).Bind(redisOptions);
        ApplyEnvironmentOverrides(redisOptions);

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

    private static void ApplyEnvironmentOverrides(RedisOptions options)
    {
        if (Environment.GetEnvironmentVariable("FINWISE_REDIS_ENABLED") is { Length: > 0 } enabled)
            options.Enabled = string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase);
        if (Environment.GetEnvironmentVariable("FINWISE_REDIS_CONNECTION_STRING") is { Length: > 0 } connStr)
            options.ConnectionString = connStr;
        if (Environment.GetEnvironmentVariable("FINWISE_REDIS_SESSION_TTL_MINUTES") is { Length: > 0 } ttl && int.TryParse(ttl, out var ttlMinutes))
            options.SessionTtlMinutes = ttlMinutes;
    }

    private static bool IsForceInMemoryDataEnabled(IConfiguration configuration)
    {
        var inMemory = configuration.GetValue<bool>("ForceInMemoryData");
        if (Environment.GetEnvironmentVariable("FINWISE_FORCE_IN_MEMORY_DATA") is { Length: > 0 } envValue)
            inMemory = string.Equals(envValue, "true", StringComparison.OrdinalIgnoreCase);
        return inMemory;
    }
}
