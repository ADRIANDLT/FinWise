using FluentAssertions;
using FinWise.MultiAgentWorkflow.DomainModel;
using FinWise.MultiAgentWorkflow.Agents.UserProfileAgent;
using FinWise.MultiAgentWorkflow.Session;
using FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStores;
using FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStores.InMemory;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace FinWise.MultiAgentWorkflow.UnitTests;

[Trait("Category", "Unit")]
public class WorkflowTests
{
    [Fact]
    public void IsComplete_Should_ReturnTrue_WhenAllFieldsProvided()
    {
        var profile = new UserProfile("user@test.com", "Moderate", "Retirement", "Long-term");
        profile.IsComplete.Should().BeTrue();
    }

    [Theory]
    [InlineData("user@test.com", null, "Goals", "Long-term")]
    [InlineData("user@test.com", "Moderate", null, "Long-term")]
    [InlineData("user@test.com", "Moderate", "Goals", null)]
    [InlineData("user@test.com", "", "Goals", "Long-term")]
    [InlineData("user@test.com", "Moderate", "  ", "Long-term")]
    [InlineData("", "Moderate", "Goals", "Long-term")]
    public void IsComplete_Should_ReturnFalse_WhenAnyFieldMissing(string userId, string? risk, string? goals, string? timeframe)
    {
        var profile = new UserProfile(userId, risk, goals, timeframe);
        profile.IsComplete.Should().BeFalse();
    }

    [Fact]
    public void WithUpdates_Should_OverrideProvidedFields_AndPreserveExisting()
    {
        var original = new UserProfile("user@test.com", "Conservative", "Retirement", "Long-term");

        var updated = original.WithUpdates(risk: "Aggressive");

        updated.RiskTolerance.Should().Be("Aggressive");
        updated.InvestmentGoals.Should().Be("Retirement", because: "goals was not updated");
        updated.InvestmentTimeframe.Should().Be("Long-term", because: "timeframe was not updated");
        updated.UserId.Should().Be("user@test.com", because: "userId is immutable");
    }

    [Fact]
    public void WithUpdates_Should_NotOverride_WhenValueIsNullOrWhitespace()
    {
        var original = new UserProfile("user@test.com", "Moderate", "Growth", "Short-term");

        var updated = original.WithUpdates(risk: null, goals: "", timeframe: "  ");

        updated.RiskTolerance.Should().Be("Moderate", because: "null should not override");
        updated.InvestmentGoals.Should().Be("Growth", because: "empty string should not override");
        updated.InvestmentTimeframe.Should().Be("Short-term", because: "whitespace should not override");
    }

    // Note: Integration tests for ChatClientAgent workflow execution require:
    // 1. Azure AI Foundry environment variables configured (FINWISE_AZURE_AI_FOUNDRY_* + FINWISE_AZURE_* SP creds)
    // 2. Live LLM calls (not unit testable with mocks)
    // 3. Manual testing via Claude Desktop MCP integration
    //
    // These tests validate data models only. Workflow execution testing
    // is done through end-to-end manual testing per Implementation Phase 1 plan.

    [Fact]
    public async Task SetProfile_ShouldSavePartial_WhenNotAllValuesProvided()
    {
        // Arrange - New incremental saving pattern: saves partial data, doesn't reject
        var profileStore = new InMemoryUserProfileStore();
        var agent = new UserProfileAgentFactory(null!, profileStore);
        var conversation = new List<ChatMessage>
        {
            new(ChatRole.User, "Give me financial advice"),
            new(ChatRole.Assistant, "Please provide your email address."),
            new(ChatRole.User, "user@example.com"),
            new(ChatRole.Assistant, "What is your risk tolerance: Conservative, Moderate, or Aggressive?"),
            new(ChatRole.User, "Moderate")
        };

        using var scope = AgentSessionRunContext.Push(new AgentSessionRunSnapshot("conv-1", conversation));

        // Act - Save with only risk (goals and timeframe empty)
        var result = await agent.SetProfile("user@example.com", "Moderate", "", "");

        // Assert - Should save partial profile and return PARTIAL status
        result.Should().Contain("PARTIAL");
        var stored = await profileStore.GetProfileAsync("user@example.com");
        stored.Should().NotBeNull();
        stored!.RiskTolerance.Should().Be("Moderate");
        stored.InvestmentGoals.Should().BeNull();
        stored.InvestmentTimeframe.Should().BeNull();
        stored.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task SetProfile_ShouldReturnComplete_WhenAllValuesProvided()
    {
        // Arrange
        var profileStore = new InMemoryUserProfileStore();
        var agent = new UserProfileAgentFactory(null!, profileStore);
        var conversation = new List<ChatMessage>
        {
            new(ChatRole.User, "Give me financial advice"),
            new(ChatRole.Assistant, "Please provide your email address."),
            new(ChatRole.User, "user@example.com"),
            new(ChatRole.Assistant, "What is your risk tolerance: Conservative, Moderate, or Aggressive?"),
            new(ChatRole.User, "Moderate"),
            new(ChatRole.Assistant, "What are your investment goals?"),
            new(ChatRole.User, "Save for retirement"),
            new(ChatRole.Assistant, "What is your investment timeframe: Short-term (1-3 years), Medium-term (3-7 years), or Long-term (7+ years)?"),
            new(ChatRole.User, "Long-term")
        };

        using var scope = AgentSessionRunContext.Push(new AgentSessionRunSnapshot("conv-2", conversation));

        // Act
        var result = await agent.SetProfile("user@example.com", "Moderate", "Save for retirement", "Long-term");

        // Assert - Should return COMPLETE status
        result.Should().Contain("COMPLETE");
        result.Should().NotContain("PARTIAL", because: "a complete profile should not return PARTIAL status");
        var stored = await profileStore.GetProfileAsync("user@example.com");
        stored.Should().NotBeNull();
        stored!.RiskTolerance.Should().Be("Moderate");
        stored.InvestmentGoals.Should().Be("Save for retirement");
        stored.InvestmentTimeframe.Should().Be("Long-term");
        stored.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task SetProfile_ShouldUpdateIncrementally_WhenCalledMultipleTimes()
    {
        // Arrange - Test incremental updates
        var profileStore = new InMemoryUserProfileStore();
        var agent = new UserProfileAgentFactory(null!, profileStore);
        var conversation = new List<ChatMessage>
        {
            new(ChatRole.User, "user@example.com")
        };

        using var scope = AgentSessionRunContext.Push(new AgentSessionRunSnapshot("conv-3", conversation));

        // Act - Call SetProfile multiple times with incremental data
        var result1 = await agent.SetProfile("user@example.com", "Moderate", "", "");
        result1.Should().Contain("PARTIAL");

        var result2 = await agent.SetProfile("user@example.com", "", "Retirement savings", "");
        result2.Should().Contain("PARTIAL");

        var result3 = await agent.SetProfile("user@example.com", "", "", "Long-term");
        result3.Should().Contain("COMPLETE");

        // Assert - Profile should have all fields merged
        var stored = await profileStore.GetProfileAsync("user@example.com");
        stored.Should().NotBeNull();
        stored!.RiskTolerance.Should().Be("Moderate");
        stored.InvestmentGoals.Should().Be("Retirement savings");
        stored.InvestmentTimeframe.Should().Be("Long-term");
        stored.IsComplete.Should().BeTrue();
    }

    [Fact]
    public async Task GetProfile_ShouldRejectInvalidEmail()
    {
        // Arrange
        var profileStore = new InMemoryUserProfileStore();
        var agent = new UserProfileAgentFactory(null!, profileStore);

        // Act
        var result = await agent.GetProfile("not-an-email");

        // Assert
        result.Should().Contain("ERROR");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetProfile_ShouldRejectNullOrEmptyEmail(string? userId)
    {
        // Arrange
        var profileStore = new InMemoryUserProfileStore();
        var agent = new UserProfileAgentFactory(null!, profileStore);

        // Act
        var result = await agent.GetProfile(userId!);

        // Assert
        result.Should().Contain("ERROR");
    }

    [Fact]
    public async Task DeleteProfile_ShouldDeleteExistingProfile()
    {
        // Arrange
        var profileStore = new InMemoryUserProfileStore();
        var agent = new UserProfileAgentFactory(null!, profileStore);
        await profileStore.SetProfileAsync("user@example.com", new UserProfile("user@example.com", "Moderate", "Retirement", "Long-term"));

        // Act
        var result = await agent.DeleteProfile("user@example.com");

        // Assert
        result.Should().Contain("DELETED", because: "profile existed and was removed");
        var stored = await profileStore.GetProfileAsync("user@example.com");
        stored.Should().BeNull();
    }

    [Fact]
    public async Task DeleteProfile_ShouldHandleNonexistentProfile()
    {
        // Arrange
        var profileStore = new InMemoryUserProfileStore();
        var agent = new UserProfileAgentFactory(null!, profileStore);

        // Act
        var result = await agent.DeleteProfile("nonexistent@example.com");

        // Assert - should handle gracefully (return not-found message)
        result.Should().NotBeEmpty();
        result.Should().Contain("NOT_FOUND");
    }

    [Fact]
    public async Task DeleteProfile_ShouldRejectInvalidEmail()
    {
        // Arrange
        var profileStore = new InMemoryUserProfileStore();
        var agent = new UserProfileAgentFactory(null!, profileStore);

        // Act
        var result = await agent.DeleteProfile("not-an-email");

        // Assert
        result.Should().Contain("ERROR");
    }

    [Fact]
    public async Task DeleteProfile_ShouldDeletePartialProfile()
    {
        // Arrange - Profile with only risk tolerance (PARTIAL state)
        var profileStore = new InMemoryUserProfileStore();
        var agent = new UserProfileAgentFactory(null!, profileStore);
        await profileStore.SetProfileAsync("user@example.com", new UserProfile("user@example.com", "Moderate", null, null));

        var stored = await profileStore.GetProfileAsync("user@example.com");
        stored.Should().NotBeNull();
        stored!.IsComplete.Should().BeFalse("profile is partial");

        // Act
        var result = await agent.DeleteProfile("user@example.com");

        // Assert
        result.Should().Contain("DELETED");
        var afterDelete = await profileStore.GetProfileAsync("user@example.com");
        afterDelete.Should().BeNull();
    }

    [Fact]
    public async Task DeleteProfile_ShouldReturnError_WhenStoreThrows()
    {
        // Arrange
        var mockStore = new Mock<IUserProfileStore>();
        mockStore.Setup(s => s.GetProfileAsync("user@example.com"))
            .ReturnsAsync(new UserProfile("user@example.com", "Moderate", "Goals", "Long-term"));
        mockStore.Setup(s => s.DeleteProfileAsync("user@example.com"))
            .ThrowsAsync(new InvalidOperationException("Store connection failed"));
        var agent = new UserProfileAgentFactory(null!, mockStore.Object);

        // Act
        var result = await agent.DeleteProfile("user@example.com");

        // Assert
        result.Should().Contain("ERROR");
        result.Should().NotContain("Store connection failed", because: "internal error details should not be exposed");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeleteProfile_ShouldRejectNullOrEmptyEmail(string? userId)
    {
        // Arrange
        var profileStore = new InMemoryUserProfileStore();
        var agent = new UserProfileAgentFactory(null!, profileStore);

        // Act
        var result = await agent.DeleteProfile(userId!);

        // Assert
        result.Should().Contain("ERROR");
    }
}
