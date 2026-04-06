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
        var cosmosDbOptions = new CosmosDbOptions();
        configuration.GetSection(CosmosDbOptions.SectionName).Bind(cosmosDbOptions);

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
}
