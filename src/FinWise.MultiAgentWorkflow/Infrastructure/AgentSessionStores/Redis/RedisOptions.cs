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
    /// For local Docker: "localhost:6379" or "finwise-redis:6379"
    /// For Azure Managed Redis: "{name}.{region}.redis.azure.net:10000,password={key},ssl=False,abortConnect=False"
    ///   ⚠️ ssl=False is required for Plaintext instances — StackExchange.Redis auto-enables SSL for *.redis.azure.net.
    ///   Use ssl=True if clientProtocol is "Encrypted".
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Time-to-live for session entries in minutes.
    /// Applied on every save (sliding expiration).
    /// Default: 1440 (24 hours).
    /// </summary>
    public int SessionTtlMinutes { get; set; } = 1440;
}
