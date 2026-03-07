using System.Text.Json;
using System.Text.Json.Serialization;
using FinWise.MultiAgentWorkflow.DomainModel;
using FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStore;
using FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStore.CosmosDb;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Xunit;

namespace FinWise.CosmosDb.IntegrationTests;

/// <summary>
/// Integration tests for CosmosDbUserProfileStore.
/// These tests require the CosmosDB emulator to be running.
/// Run: docker compose up -d
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
    private const string EmulatorEndpoint = "https://localhost:8081/";
    private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

    private CosmosClient? _client;
    private CosmosDbUserProfileStore? _store;
    private readonly string _testDatabaseName = $"TestDb_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        // Skip if emulator is not available
        if (!await IsEmulatorAvailable())
        {
            return;
        }

        var options = new CosmosDbOptions
        {
            Enabled = true,
            Endpoint = EmulatorEndpoint,
            Key = EmulatorKey,
            DatabaseName = _testDatabaseName,
            ContainerName = "UserProfiles",
            AllowInsecureTls = true
        };

        var clientOptions = new CosmosClientOptions
        {
            HttpClientFactory = () => new HttpClient(new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            }),
            ConnectionMode = ConnectionMode.Gateway
        };
        clientOptions.UseSystemTextJsonSerializerWithOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        _client = new CosmosClient(EmulatorEndpoint, EmulatorKey, clientOptions);
        _store = new CosmosDbUserProfileStore(_client, Options.Create(options));
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

    private static async Task<bool> IsEmulatorAvailable()
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
            };
            using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            var response = await httpClient.GetAsync($"{EmulatorEndpoint}_explorer/emulator.pem");
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private void SkipIfEmulatorNotAvailable()
    {
        if (_store == null)
        {
            throw new SkipException("CosmosDB emulator is not available. Run: docker compose up -d");
        }
    }

    [Fact]
    public async Task SetAndGetProfile_RoundTrip_Success()
    {
        SkipIfEmulatorNotAvailable();

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

    [Fact]
    public async Task GetProfile_NonExistent_ReturnsNull()
    {
        SkipIfEmulatorNotAvailable();

        // Arrange
        var userId = $"nonexistent_{Guid.NewGuid():N}@example.com";

        // Act
        var result = await _store!.GetProfileAsync(userId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetProfile_Update_OverwritesPrevious()
    {
        SkipIfEmulatorNotAvailable();

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

    [Fact]
    public async Task HasProfile_WhenExists_ReturnsTrue()
    {
        SkipIfEmulatorNotAvailable();

        // Arrange
        var userId = $"exists_{Guid.NewGuid():N}@example.com";
        var profile = new UserProfile(userId, "Medium", "Balance", "10 years");
        await _store!.SetProfileAsync(userId, profile);

        // Act
        var exists = await _store.HasProfileAsync(userId);

        // Assert
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task HasProfile_WhenNotExists_ReturnsFalse()
    {
        SkipIfEmulatorNotAvailable();

        // Arrange
        var userId = $"notexists_{Guid.NewGuid():N}@example.com";

        // Act
        var exists = await _store!.HasProfileAsync(userId);

        // Assert
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteProfile_RemovesProfile()
    {
        SkipIfEmulatorNotAvailable();

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

    [Fact]
    public async Task DeleteProfile_NonExistent_DoesNotThrow()
    {
        SkipIfEmulatorNotAvailable();

        // Arrange
        var userId = $"nonexistent_delete_{Guid.NewGuid():N}@example.com";

        // Act
        var act = () => _store!.DeleteProfileAsync(userId);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Profile_WithNullFields_PersistsCorrectly()
    {
        SkipIfEmulatorNotAvailable();

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

/// <summary>
/// Custom exception for skipping tests when prerequisites are not met.
/// </summary>
public class SkipException : Exception
{
    public SkipException(string message) : base(message) { }
}
