using System.Text.Json;
using System.Text.Json.Serialization;
using FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStores;
using FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStores.CosmosDb;
using FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStores.InMemory;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Serilog;

namespace FinWise.McpServer.Infrastructure.UserProfileStorage;

/// <summary>
/// Creates the appropriate <see cref="IUserProfileStore"/> based on configuration.
/// </summary>
public static class UserProfileStoreFactory
{
    /// <summary>
    /// Creates a CosmosDB-backed or in-memory user profile store depending on the
    /// <c>CosmosDb:Enabled</c> configuration flag.
    /// </summary>
    public static IUserProfileStore CreateProfileStore(IConfiguration configuration)
    {
        if (IsForceInMemoryDataEnabled(configuration))
        {
            Log.Information("ForceInMemoryData is enabled — using in-memory user profile store");
            return new InMemoryUserProfileStore();
        }

        var cosmosDbOptions = new CosmosDbOptions();
        configuration.GetSection(CosmosDbOptions.SectionName).Bind(cosmosDbOptions);
        ApplyEnvironmentOverrides(cosmosDbOptions);

        if (cosmosDbOptions.Enabled)
        {
            Log.Information("Using CosmosDB user profile store (Endpoint: {Endpoint}, Database: {Database})",
                cosmosDbOptions.Endpoint, cosmosDbOptions.DatabaseName);

            var cosmosClientOptions = new CosmosClientOptions
            {
                UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                }
            };

            if (cosmosDbOptions.AllowInsecureTls)
            {
                Log.Warning("CosmosDB TLS validation disabled - for development use only");
                cosmosClientOptions.HttpClientFactory = () => new HttpClient(new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
                });
                cosmosClientOptions.ConnectionMode = ConnectionMode.Gateway;
                cosmosClientOptions.LimitToEndpoint = true;
            }

            var cosmosClient = new CosmosClient(cosmosDbOptions.Endpoint, cosmosDbOptions.Key, cosmosClientOptions);
            return new CosmosDbUserProfileStore(cosmosClient, Options.Create(cosmosDbOptions));
        }

        Log.Information("Using in-memory user profile store");
        return new InMemoryUserProfileStore();
    }

    private static void ApplyEnvironmentOverrides(CosmosDbOptions options)
    {
        if (Environment.GetEnvironmentVariable("FINWISE_COSMOSDB_ENABLED") is { Length: > 0 } enabled)
            options.Enabled = string.Equals(enabled, "true", StringComparison.OrdinalIgnoreCase);
        if (Environment.GetEnvironmentVariable("FINWISE_COSMOSDB_ENDPOINT") is { Length: > 0 } endpoint)
            options.Endpoint = endpoint;
        if (Environment.GetEnvironmentVariable("FINWISE_COSMOSDB_KEY") is { Length: > 0 } key)
            options.Key = key;
        if (Environment.GetEnvironmentVariable("FINWISE_COSMOSDB_DATABASE_NAME") is { Length: > 0 } dbName)
            options.DatabaseName = dbName;
        if (Environment.GetEnvironmentVariable("FINWISE_COSMOSDB_CONTAINER_NAME") is { Length: > 0 } containerName)
            options.ContainerName = containerName;
        if (Environment.GetEnvironmentVariable("FINWISE_COSMOSDB_ALLOW_INSECURE_TLS") is { Length: > 0 } allowInsecure)
            options.AllowInsecureTls = string.Equals(allowInsecure, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsForceInMemoryDataEnabled(IConfiguration configuration)
    {
        var inMemory = configuration.GetValue<bool>("ForceInMemoryData");
        if (Environment.GetEnvironmentVariable("FINWISE_FORCE_IN_MEMORY_DATA") is { Length: > 0 } envValue)
            inMemory = string.Equals(envValue, "true", StringComparison.OrdinalIgnoreCase);
        return inMemory;
    }
}
