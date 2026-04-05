namespace FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStores.CosmosDb;

/// <summary>
/// Configuration options for Azure CosmosDB connection.
/// Bind to the "CosmosDb" section in appsettings.json.
/// </summary>
public class CosmosDbOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "CosmosDb";

    /// <summary>
    /// Whether to use CosmosDB for profile storage.
    /// When false, uses in-memory storage.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// CosmosDB account endpoint URL.
    /// For emulator: https://localhost:8081/
    /// </summary>
    public string Endpoint { get; set; } = "https://localhost:8081/";

    /// <summary>
    /// CosmosDB account key.
    /// For emulator: C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==
    /// IMPORTANT: Use user secrets or environment variables for this value.
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Name of the CosmosDB database.
    /// </summary>
    public string DatabaseName { get; set; } = "FinWise";

    /// <summary>
    /// Name of the container for user profiles.
    /// </summary>
    public string ContainerName { get; set; } = "UserProfiles";

    /// <summary>
    /// Whether to allow insecure TLS connections (for emulator with self-signed certs).
    /// Should be false in production.
    /// </summary>
    public bool AllowInsecureTls { get; set; } = true;
}