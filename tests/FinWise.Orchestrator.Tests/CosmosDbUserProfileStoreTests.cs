using FinWise.Orchestrator;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;
using Moq;
using System.Net;
using Xunit;

namespace FinWise.Orchestrator.Tests;

/// <summary>
/// Unit tests for CosmosDbUserProfileStore.
/// Uses mocking to verify correct SDK interactions without requiring actual CosmosDB.
/// </summary>
public class CosmosDbUserProfileStoreTests
{
    private readonly Mock<CosmosClient> _mockClient;
    private readonly Mock<Database> _mockDatabase;
    private readonly Mock<Container> _mockContainer;
    private readonly CosmosDbOptions _options;
    private readonly CosmosDbUserProfileStore _store;

    public CosmosDbUserProfileStoreTests()
    {
        _mockClient = new Mock<CosmosClient>();
        _mockDatabase = new Mock<Database>();
        _mockContainer = new Mock<Container>();

        _options = new CosmosDbOptions
        {
            Enabled = true,
            Endpoint = "https://localhost:8081/",
            Key = "test-key",
            DatabaseName = "TestDb",
            ContainerName = "TestContainer"
        };

        // Setup chain: Client -> Database -> Container
        _mockClient
            .Setup(c => c.CreateDatabaseIfNotExistsAsync(
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<DatabaseResponse>(r => r.Database == _mockDatabase.Object));

        _mockDatabase
            .Setup(d => d.CreateContainerIfNotExistsAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int?>(),
                It.IsAny<RequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ContainerResponse>(r => r.Container == _mockContainer.Object));

        _store = new CosmosDbUserProfileStore(_mockClient.Object, Options.Create(_options));
    }

    [Fact]
    public async Task GetProfileAsync_WhenProfileExists_ReturnsProfile()
    {
        // Arrange
        var userId = "test@example.com";
        var documentId = UserProfileDocument.EmailToDocumentId(userId);
        var document = new UserProfileDocument
        {
            Id = documentId,
            UserId = userId,
            RiskTolerance = "Moderate",
            InvestmentGoals = "Retirement",
            InvestmentTimeframe = "10 years"
        };

        var mockResponse = new Mock<ItemResponse<UserProfileDocument>>();
        mockResponse.Setup(r => r.Resource).Returns(document);
        mockResponse.Setup(r => r.RequestCharge).Returns(1.0);

        _mockContainer
            .Setup(c => c.ReadItemAsync<UserProfileDocument>(
                documentId,
                It.Is<PartitionKey>(pk => pk.Equals(new PartitionKey(userId))),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        // Act
        var result = await _store.GetProfileAsync(userId);

        // Assert
        result.Should().NotBeNull();
        result!.UserId.Should().Be(userId);
        result.RiskTolerance.Should().Be("Moderate");
        result.InvestmentGoals.Should().Be("Retirement");
        result.InvestmentTimeframe.Should().Be("10 years");
    }

    [Fact]
    public async Task GetProfileAsync_WhenProfileNotFound_ReturnsNull()
    {
        // Arrange
        var userId = "notfound@example.com";
        var documentId = UserProfileDocument.EmailToDocumentId(userId);

        _mockContainer
            .Setup(c => c.ReadItemAsync<UserProfileDocument>(
                documentId,
                It.Is<PartitionKey>(pk => pk.Equals(new PartitionKey(userId))),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not found", HttpStatusCode.NotFound, 0, "", 0));

        // Act
        var result = await _store.GetProfileAsync(userId);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetProfileAsync_UpsertsDocument()
    {
        // Arrange
        var userId = "test@example.com";
        var documentId = UserProfileDocument.EmailToDocumentId(userId);
        var profile = new UserProfileDto(userId, "High", "Growth", "5 years");

        // ReadItemAsync is called first to fetch existing document; throw NotFound for new profile
        _mockContainer
            .Setup(c => c.ReadItemAsync<UserProfileDocument>(
                documentId,
                It.Is<PartitionKey>(pk => pk.Equals(new PartitionKey(userId))),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not found", HttpStatusCode.NotFound, 0, "", 0));

        var mockResponse = new Mock<ItemResponse<UserProfileDocument>>();
        mockResponse.Setup(r => r.RequestCharge).Returns(5.0);

        _mockContainer
            .Setup(c => c.UpsertItemAsync(
                It.Is<UserProfileDocument>(d => d.UserId == userId),
                It.Is<PartitionKey>(pk => pk.Equals(new PartitionKey(userId))),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        // Act
        await _store.SetProfileAsync(userId, profile);

        // Assert
        _mockContainer.Verify(c => c.UpsertItemAsync(
            It.Is<UserProfileDocument>(d =>
                d.UserId == userId &&
                d.RiskTolerance == "High" &&
                d.InvestmentGoals == "Growth" &&
                d.InvestmentTimeframe == "5 years"),
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HasProfileAsync_WhenExists_ReturnsTrue()
    {
        // Arrange
        var userId = "exists@example.com";
        var documentId = UserProfileDocument.EmailToDocumentId(userId);
        var document = new UserProfileDocument { Id = documentId, UserId = userId };

        var mockResponse = new Mock<ItemResponse<UserProfileDocument>>();
        mockResponse.Setup(r => r.Resource).Returns(document);

        _mockContainer
            .Setup(c => c.ReadItemAsync<UserProfileDocument>(
                documentId,
                It.Is<PartitionKey>(pk => pk.Equals(new PartitionKey(userId))),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        // Act
        var result = await _store.HasProfileAsync(userId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task HasProfileAsync_WhenNotExists_ReturnsFalse()
    {
        // Arrange
        var userId = "notexists@example.com";
        var documentId = UserProfileDocument.EmailToDocumentId(userId);

        _mockContainer
            .Setup(c => c.ReadItemAsync<UserProfileDocument>(
                documentId,
                It.Is<PartitionKey>(pk => pk.Equals(new PartitionKey(userId))),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not found", HttpStatusCode.NotFound, 0, "", 0));

        // Act
        var result = await _store.HasProfileAsync(userId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteProfileAsync_WhenExists_DeletesDocument()
    {
        // Arrange
        var userId = "delete@example.com";
        var documentId = UserProfileDocument.EmailToDocumentId(userId);

        var mockResponse = new Mock<ItemResponse<UserProfileDocument>>();
        mockResponse.Setup(r => r.RequestCharge).Returns(5.0);

        _mockContainer
            .Setup(c => c.DeleteItemAsync<UserProfileDocument>(
                documentId,
                It.Is<PartitionKey>(pk => pk.Equals(new PartitionKey(userId))),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse.Object);

        // Act
        await _store.DeleteProfileAsync(userId);

        // Assert
        _mockContainer.Verify(c => c.DeleteItemAsync<UserProfileDocument>(
            documentId,
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DeleteProfileAsync_WhenNotExists_DoesNotThrow()
    {
        // Arrange
        var userId = "notexists@example.com";
        var documentId = UserProfileDocument.EmailToDocumentId(userId);

        _mockContainer
            .Setup(c => c.DeleteItemAsync<UserProfileDocument>(
                documentId,
                It.Is<PartitionKey>(pk => pk.Equals(new PartitionKey(userId))),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not found", HttpStatusCode.NotFound, 0, "", 0));

        // Act
        var act = () => _store.DeleteProfileAsync(userId);

        // Assert
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task InitializesDatabase_OnFirstOperation()
    {
        // Arrange
        var userId = "init@example.com";
        var documentId = UserProfileDocument.EmailToDocumentId(userId);

        _mockContainer
            .Setup(c => c.ReadItemAsync<UserProfileDocument>(
                documentId,
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not found", HttpStatusCode.NotFound, 0, "", 0));

        // Act
        await _store.GetProfileAsync(userId);

        // Assert - verify database and container were created
        _mockClient.Verify(c => c.CreateDatabaseIfNotExistsAsync(
            _options.DatabaseName,
            It.IsAny<int?>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);

        _mockDatabase.Verify(d => d.CreateContainerIfNotExistsAsync(
            _options.ContainerName,
            "/userId",
            It.IsAny<int?>(),
            It.IsAny<RequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
