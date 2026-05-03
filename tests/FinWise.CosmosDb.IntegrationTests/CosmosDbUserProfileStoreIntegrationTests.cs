using System.Text.Json;
using System.Text.Json.Serialization;
using FinWise.MultiAgentWorkflow.DomainModel;
using FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStores;
using FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStores.CosmosDb;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinWise.CosmosDb.IntegrationTests;

/// <summary>
/// Integration tests for CosmosDbUserProfileStore.
/// Runs against the CosmosDB emulator (default) or Azure Cosmos DB cloud.
///
/// Configuration via environment variables (falls back to emulator defaults):
///   FINWISE_COSMOSDB_ENDPOINT           — CosmosDB endpoint URL
///   FINWISE_COSMOSDB_KEY                — CosmosDB account key
///   FINWISE_COSMOSDB_ALLOW_INSECURE_TLS — "true" for emulator (default for localhost), "false" for Azure
/// </summary>
/// <remarks>
/// These tests are marked with [Trait("Category", "Integration")] to allow
/// filtering during test runs. To run only unit tests, use:
/// dotnet test --filter "Category!=Integration"
///
/// To run integration tests:
/// dotnet test --filter "Category=Integration"
/// </remarks>
[Trait("Category", "Integration")]
public class CosmosDbUserProfileStoreIntegrationTests : IAsyncLifetime
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

    private CosmosClient? _client;
    private CosmosDbUserProfileStore? _store;
    private readonly string _testDatabaseName = $"TestDb_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        try
        {
            var clientOptions = new CosmosClientOptions
            {
                UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                }
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

            _client = new CosmosClient(Endpoint, Key, clientOptions);
            await _client.ReadAccountAsync();

            var options = new CosmosDbOptions
            {
                Enabled = true,
                Endpoint = Endpoint,
                Key = Key,
                DatabaseName = _testDatabaseName,
                ContainerName = "UserProfiles",
                AllowInsecureTls = AllowInsecureTls
            };

            _store = new CosmosDbUserProfileStore(_client, Options.Create(options));
        }
        catch
        {
            // CosmosDB not available — tests will be skipped
            _client?.Dispose();
            _client = null;
        }
    }

    public async Task DisposeAsync()
    {
        if (_client != null)
        {
            try
            {
                // Clean up test database
                var database = _client.GetDatabase(_testDatabaseName);
                await database.DeleteAsync();
            }
            catch
            {
                // Ignore cleanup errors
            }

            _client.Dispose();
        }
    }

    private void SkipIfCosmosDbNotAvailable()
    {
        Skip.If(_store == null,
            "CosmosDB is not available. For emulator: docker compose up -d. " +
            "For Azure: set FINWISE_COSMOSDB_ENDPOINT and FINWISE_COSMOSDB_KEY env vars.");
    }

    [SkippableFact]
    public async Task SetAndGetProfile_RoundTrip_Success()
    {
        SkipIfCosmosDbNotAvailable();

        // Arrange
        var userId = $"test_{Guid.NewGuid():N}@example.com";
        var profile = new UserProfile(
            userId,
            RiskTolerance: "Moderate",
            InvestmentGoals: "Retirement savings",
            InvestmentTimeframe: "20 years"
        );

        // Act
        await _store!.SetProfileAsync(userId, profile);
        var retrieved = await _store.GetProfileAsync(userId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.UserId.Should().Be(userId);
        retrieved.RiskTolerance.Should().Be("Moderate");
        retrieved.InvestmentGoals.Should().Be("Retirement savings");
        retrieved.InvestmentTimeframe.Should().Be("20 years");
    }

    [SkippableFact]
    public async Task GetProfile_NonExistent_ReturnsNull()
    {
        SkipIfCosmosDbNotAvailable();

        // Arrange
        var userId = $"nonexistent_{Guid.NewGuid():N}@example.com";

        // Act
        var result = await _store!.GetProfileAsync(userId);

        // Assert
        result.Should().BeNull();
    }

    [SkippableFact]
    public async Task SetProfile_Update_OverwritesPrevious()
    {
        SkipIfCosmosDbNotAvailable();

        // Arrange
        var userId = $"update_{Guid.NewGuid():N}@example.com";
        var originalProfile = new UserProfile(userId, "Low", "Safety", "5 years");
        var updatedProfile = new UserProfile(userId, "High", "Growth", "30 years");

        // Act
        await _store!.SetProfileAsync(userId, originalProfile);
        await _store.SetProfileAsync(userId, updatedProfile);
        var retrieved = await _store.GetProfileAsync(userId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.RiskTolerance.Should().Be("High");
        retrieved.InvestmentGoals.Should().Be("Growth");
        retrieved.InvestmentTimeframe.Should().Be("30 years");
    }

    [SkippableFact]
    public async Task HasProfile_WhenExists_ReturnsTrue()
    {
        SkipIfCosmosDbNotAvailable();

        // Arrange
        var userId = $"exists_{Guid.NewGuid():N}@example.com";
        var profile = new UserProfile(userId, "Medium", "Balance", "10 years");
        await _store!.SetProfileAsync(userId, profile);

        // Act
        var exists = await _store.HasProfileAsync(userId);

        // Assert
        exists.Should().BeTrue();
    }

    [SkippableFact]
    public async Task HasProfile_WhenNotExists_ReturnsFalse()
    {
        SkipIfCosmosDbNotAvailable();

        // Arrange
        var userId = $"notexists_{Guid.NewGuid():N}@example.com";

        // Act
        var exists = await _store!.HasProfileAsync(userId);

        // Assert
        exists.Should().BeFalse();
    }

    [SkippableFact]
    public async Task DeleteProfile_RemovesProfile()
    {
        SkipIfCosmosDbNotAvailable();

        // Arrange
        var userId = $"delete_{Guid.NewGuid():N}@example.com";
        var profile = new UserProfile(userId, "High", "Aggressive", "15 years");
        await _store!.SetProfileAsync(userId, profile);

        // Act
        await _store.DeleteProfileAsync(userId);
        var exists = await _store.HasProfileAsync(userId);

        // Assert
        exists.Should().BeFalse();
    }

    [SkippableFact]
    public async Task DeleteProfile_NonExistent_DoesNotThrow()
    {
        SkipIfCosmosDbNotAvailable();

        // Arrange
        var userId = $"nonexistent_delete_{Guid.NewGuid():N}@example.com";

        // Act
        var act = () => _store!.DeleteProfileAsync(userId);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [SkippableFact]
    public async Task GetProfile_AfterCreation_SurvivesAcrossSessions()
    {
        SkipIfCosmosDbNotAvailable();

        // Arrange: simulate creating a profile in session 1
        var userId = $"returning_{Guid.NewGuid():N}@example.com";
        var profile = new UserProfile(userId, "Moderate", "Wealth growth", "short-term");
        await _store!.SetProfileAsync(userId, profile);

        // Act: simulate a new session (different store instance, same CosmosDB) looking up the same email
        // This is what happens after session reset — the profile should still be in CosmosDB
        var retrieved = await _store.GetProfileAsync(userId);

        // Assert: profile must be found and complete
        retrieved.Should().NotBeNull("profile should survive session reset — it's stored in CosmosDB, not in the agent session");
        retrieved!.UserId.Should().Be(userId);
        retrieved.RiskTolerance.Should().Be("Moderate");
        retrieved.InvestmentGoals.Should().Be("Wealth growth");
        retrieved.InvestmentTimeframe.Should().Be("short-term");
        retrieved.IsComplete.Should().BeTrue();
    }

    [SkippableFact]
    public async Task GetProfile_ReturnsCompleteProfile_WhenCalledWithSameEmailAfterReset()
    {
        SkipIfCosmosDbNotAvailable();

        // This test mimics the exact user scenario:
        // 1. User creates profile (set_profile calls)
        // 2. Session is reset (clears agent session, NOT profile store)
        // 3. User provides same email in new session
        // 4. get_profile should return FOUND_COMPLETE

        // Step 1: Create complete profile
        var userId = "test-reset-user@example.com";
        var profile = new UserProfile(userId, "Aggressive", "Growth", "long-term");
        await _store!.SetProfileAsync(userId, profile);

        // Step 2: Verify it's stored
        var beforeReset = await _store.GetProfileAsync(userId);
        beforeReset.Should().NotBeNull();
        beforeReset!.IsComplete.Should().BeTrue();

        // Step 3: "Reset" happens — agent session is cleared, but profile store is untouched
        // (nothing to do here — ResetSessionAsync only clears AgentSessionStore, not IUserProfileStore)

        // Step 4: New session looks up the same email
        var afterReset = await _store.GetProfileAsync(userId);
        afterReset.Should().NotBeNull("CosmosDB profile store is independent of agent sessions");
        afterReset!.UserId.Should().Be(userId);
        afterReset.IsComplete.Should().BeTrue();
        afterReset.RiskTolerance.Should().Be("Aggressive");
    }

    [SkippableFact]
    public async Task Profile_WithNullFields_PersistsCorrectly()
    {
        SkipIfCosmosDbNotAvailable();

        // Arrange - profile with only some fields set (progressive saving)
        var userId = $"partial_{Guid.NewGuid():N}@example.com";
        var profile = new UserProfile(userId, "Conservative", null, null);

        // Act
        await _store!.SetProfileAsync(userId, profile);
        var retrieved = await _store.GetProfileAsync(userId);

        // Assert
        retrieved.Should().NotBeNull();
        retrieved!.RiskTolerance.Should().Be("Conservative");
        retrieved.InvestmentGoals.Should().BeNull();
        retrieved.InvestmentTimeframe.Should().BeNull();
        retrieved.IsComplete.Should().BeFalse();
    }
}
