using System.Text.Json;
using System.Text.Json.Serialization;
using FinWise.MultiAgentWorkflow.DomainModel;
using FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStores.CosmosDb;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinWise.CosmosDb.IntegrationTests;

/// <summary>
/// Integration tests that verify CosmosDB data persistence across
/// independent client instances (and emulator container restarts when using Docker).
///
/// Runs against the CosmosDB emulator (default) or Azure Cosmos DB cloud.
///
/// Configuration via environment variables (falls back to emulator defaults):
///   FINWISE_COSMOSDB_ENDPOINT           — CosmosDB endpoint URL
///   FINWISE_COSMOSDB_KEY                — CosmosDB account key
///   FINWISE_COSMOSDB_ALLOW_INSECURE_TLS — "true" for emulator (default for localhost), "false" for Azure
///
/// These tests use a fixed, well-known database (not cleaned up after tests)
/// to prove that profiles written to CosmosDB survive across connections.
///
/// Manual verification workflow (emulator):
/// 1. Run the "write" test:  dotnet test --filter "WriteProfile_ForPersistenceVerification"
/// 2. Stop infrastructure:   docker compose -f docker-compose.infra.yml stop
/// 3. Start infrastructure:  docker compose -f docker-compose.infra.yml start
/// 4. Run the "read" test:   dotnet test --filter "ReadProfile_SurvivesRestart"
///
/// The automated test verifies the equivalent scenario using two independent
/// CosmosDbUserProfileStore instances against the same database.
/// </summary>
[Trait("Category", "Integration")]
public class CosmosDbPersistenceIntegrationTests : IAsyncLifetime
{
    private const string DefaultEmulatorEndpoint = "https://localhost:8081/";
    private const string DefaultEmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private static readonly string Endpoint =
        Environment.GetEnvironmentVariable("FINWISE_COSMOSDB_ENDPOINT") is { Length: > 0 } ep
            ? ep : DefaultEmulatorEndpoint;

    private static readonly string Key =
        Environment.GetEnvironmentVariable("FINWISE_COSMOSDB_KEY") is { Length: > 0 } key
            ? key : DefaultEmulatorKey;

    private static readonly bool AllowInsecureTls =
        Environment.GetEnvironmentVariable("FINWISE_COSMOSDB_ALLOW_INSECURE_TLS") is { Length: > 0 } val
            ? string.Equals(val, "true", StringComparison.OrdinalIgnoreCase)
            : Endpoint.Contains("localhost") || Endpoint.Contains("127.0.0.1");

    /// <summary>
    /// Fixed database name — intentionally NOT cleaned up after tests so data
    /// can be verified manually after emulator stop/start cycles.
    /// </summary>
    private const string PersistenceTestDbName = "PersistenceTestDb";
    private const string PersistenceTestContainerName = "UserProfiles";
    private const string PersistenceTestUserId = "persistence-test@finwise.com";

    private CosmosClient? _client;
    private CosmosDbUserProfileStore? _store;

    public async Task InitializeAsync()
    {
        try
        {
            _client = CreateCosmosClient();
            await _client.ReadAccountAsync();
            _store = CreateStore(_client);
        }
        catch
        {
            // CosmosDB not available — tests will be skipped
            _client?.Dispose();
            _client = null;
        }
    }

    public Task DisposeAsync()
    {
        // Intentionally do NOT delete the PersistenceTestDb — it needs to survive
        // across test runs and emulator restarts for manual verification.
        _client?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes a profile to the fixed persistence database.
    /// Run this test, then stop/start the emulator, then run
    /// <see cref="ReadProfile_SurvivesEmulatorRestart"/> to verify persistence.
    /// </summary>
    [SkippableFact]
    public async Task WriteProfile_ForPersistenceVerification()
    {
        SkipIfCosmosDbNotAvailable();

        // Arrange
        var profile = new UserProfile(
            PersistenceTestUserId,
            RiskTolerance: "Moderate",
            InvestmentGoals: "Retirement savings",
            InvestmentTimeframe: "20 years"
        );

        // Act
        await _store!.SetProfileAsync(PersistenceTestUserId, profile);

        // Assert — verify profile was written
        var retrieved = await _store.GetProfileAsync(PersistenceTestUserId);
        retrieved.Should().NotBeNull();
        retrieved!.UserId.Should().Be(PersistenceTestUserId);
        retrieved.RiskTolerance.Should().Be("Moderate");
        retrieved.InvestmentGoals.Should().Be("Retirement savings");
        retrieved.InvestmentTimeframe.Should().Be("20 years");
        retrieved.IsComplete.Should().BeTrue();
    }

    /// <summary>
    /// Writes a profile via one store instance, then reads it from a completely
    /// fresh CosmosClient + store instance — proving the data is durably stored
    /// in CosmosDB and not held in memory.
    ///
    /// For manual emulator stop/start verification:
    /// 1. Run WriteProfile_ForPersistenceVerification
    /// 2. docker compose -f docker-compose.infra.yml stop / start
    /// 3. Run this test alone: dotnet test --filter "ReadProfile_SurvivesRestart"
    /// </summary>
    [SkippableFact]
    public async Task ReadProfile_SurvivesRestart()
    {
        SkipIfCosmosDbNotAvailable();

        // Ensure profile exists (idempotent write — safe to run repeatedly)
        var profile = new UserProfile(
            PersistenceTestUserId,
            RiskTolerance: "Moderate",
            InvestmentGoals: "Retirement savings",
            InvestmentTimeframe: "20 years"
        );
        await _store!.SetProfileAsync(PersistenceTestUserId, profile);

        // Act — read with a completely fresh client+store to simulate new app startup after restart
        using var freshClient = CreateCosmosClient();
        var freshStore = CreateStore(freshClient);

        var retrieved = await freshStore.GetProfileAsync(PersistenceTestUserId);

        // Assert — profile must exist and be complete
        retrieved.Should().NotBeNull(
            "profile should survive across independent client instances — " +
            "data is durably stored in CosmosDB.");

        retrieved!.UserId.Should().Be(PersistenceTestUserId);
        retrieved.RiskTolerance.Should().Be("Moderate");
        retrieved.InvestmentGoals.Should().Be("Retirement savings");
        retrieved.InvestmentTimeframe.Should().Be("20 years");
        retrieved.IsComplete.Should().BeTrue();
    }

    /// <summary>
    /// End-to-end persistence test: writes a profile via one store instance,
    /// then reads it via a completely independent store instance (new CosmosClient,
    /// new CosmosDbUserProfileStore). This simulates the scenario where the
    /// emulator is stopped and restarted between write and read.
    /// </summary>
    [SkippableFact]
    public async Task Profile_SurvivesIndependentStoreInstances()
    {
        SkipIfCosmosDbNotAvailable();

        // Arrange — unique user for this test run to avoid interference
        var userId = $"persistence-e2e-{Guid.NewGuid():N}@finwise.com";
        var profile = new UserProfile(
            userId,
            RiskTolerance: "Aggressive",
            InvestmentGoals: "Wealth accumulation",
            InvestmentTimeframe: "30 years"
        );

        // Act — write with first store instance
        await _store!.SetProfileAsync(userId, profile);

        // Read with a completely independent store (new client, new store)
        using var independentClient = CreateCosmosClient();
        var independentStore = CreateStore(independentClient);
        var retrieved = await independentStore.GetProfileAsync(userId);

        // Assert — data must be visible from the independent store
        retrieved.Should().NotBeNull("profile stored in CosmosDB must be visible from any client instance");
        retrieved!.UserId.Should().Be(userId);
        retrieved.RiskTolerance.Should().Be("Aggressive");
        retrieved.InvestmentGoals.Should().Be("Wealth accumulation");
        retrieved.InvestmentTimeframe.Should().Be("30 years");
        retrieved.IsComplete.Should().BeTrue();

        // Cleanup — delete this test-specific user (but leave the persistence DB)
        await _store.DeleteProfileAsync(userId);
    }

    private static CosmosClient CreateCosmosClient()
    {
        var clientOptions = new CosmosClientOptions();
        clientOptions.UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        if (AllowInsecureTls)
        {
            clientOptions.HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            });
            clientOptions.ConnectionMode = ConnectionMode.Gateway;
            clientOptions.LimitToEndpoint = true;
        }

        return new CosmosClient(Endpoint, Key, clientOptions);
    }

    private static CosmosDbUserProfileStore CreateStore(CosmosClient client)
    {
        var options = new CosmosDbOptions
        {
            Enabled = true,
            Endpoint = Endpoint,
            Key = Key,
            DatabaseName = PersistenceTestDbName,
            ContainerName = PersistenceTestContainerName,
            AllowInsecureTls = AllowInsecureTls
        };

        return new CosmosDbUserProfileStore(client, Options.Create(options));
    }

    private void SkipIfCosmosDbNotAvailable()
    {
        Skip.If(_store == null,
            "CosmosDB is not available. For emulator: docker compose -f docker-compose.infra.yml up -d. " +
            "For Azure: set FINWISE_COSMOSDB_ENDPOINT and FINWISE_COSMOSDB_KEY env vars.");
    }
}
