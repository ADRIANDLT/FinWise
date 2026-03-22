namespace FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStores.Redis;

/// <summary>
/// Configuration options for Redis-backed session storage.
/// Bind to the "Redis" section in appsettings.json.
/// </summary>
public class RedisOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Redis";

    /// <summary>
    /// Whether to use Redis for session storage.
    /// When false, uses in-memory storage (SDK's InMemoryAgentSessionStore).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Redis connection string.
    /// For local Docker: "localhost:6379"
    /// For Azure Cache for Redis: "{name}.redis.cache.windows.net:6380,password={key},ssl=True,abortConnect=False"
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Time-to-live for session entries in minutes.
    /// Applied on every save (sliding expiration).
    /// Default: 1440 (24 hours).
    /// </summary>
    public int SessionTtlMinutes { get; set; } = 1440;
}
