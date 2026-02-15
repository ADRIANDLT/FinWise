using System.Collections.Generic;
using FluentAssertions;
using FinWise.Orchestrator;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace FinWise.Orchestrator.Tests;

public class WorkflowTests
{
    [Fact]
    public void UserProfileDto_Should_Initialize_Correctly()
    {
        // Arrange & Act
        var profile = new UserProfileDto(
            "user123",
            "Moderate",
            "Retirement savings",
            "Long-term"
        );

        // Assert
        profile.UserId.Should().Be("user123");
        profile.RiskTolerance.Should().Be("Moderate");
        profile.InvestmentGoals.Should().Be("Retirement savings");
        profile.InvestmentTimeframe.Should().Be("Long-term");
    }

    [Fact]
    public void WorkflowExecutionContext_Should_Capture_Request_Details()
    {
        // Arrange
        var requestTime = DateTime.UtcNow;

        // Act
        var context = new WorkflowExecutionContext(
            "user456",
            "What should I invest in?",
            requestTime
        );

        // Assert
        context.UserId.Should().Be("user456");
        context.Query.Should().Be("What should I invest in?");
        context.RequestTime.Should().Be(requestTime);
    }

    [Fact]
    public void ProfileFields_Should_Accept_FreeformText()
    {
        // Arrange & Act - Profile fields now accept any string value
        var profile1 = new UserProfileDto("user1", "very conservative", "save for house down payment", "about 5 years");
        var profile2 = new UserProfileDto("user2", "I'm okay with some risk", "retirement and kids college", "15-20 years until retirement");
        var profile3 = new UserProfileDto("user3", "YOLO aggressive", "get rich quick", "as soon as possible");

        // Assert - All freeform values should be stored as-is
        profile1.RiskTolerance.Should().Be("very conservative");
        profile1.InvestmentGoals.Should().Be("save for house down payment");
        profile1.InvestmentTimeframe.Should().Be("about 5 years");

        profile2.RiskTolerance.Should().Be("I'm okay with some risk");
        profile2.InvestmentGoals.Should().Be("retirement and kids college");
        profile2.InvestmentTimeframe.Should().Be("15-20 years until retirement");

        profile3.RiskTolerance.Should().Be("YOLO aggressive");
        profile3.InvestmentGoals.Should().Be("get rich quick");
        profile3.InvestmentTimeframe.Should().Be("as soon as possible");
    }

    // Note: Integration tests for ChatClientAgent workflow execution require:
    // 1. Azure OpenAI environment variables configured
    // 2. Live LLM calls (not unit testable with mocks)
    // 3. Manual testing via Claude Desktop MCP integration
    //
    // These tests validate data models only. Workflow execution testing
    // is done through end-to-end manual testing per Implementation Phase 1 plan.

    [Fact]
    public async Task ProfileStore_Should_Support_InMemory_Storage()
    {
        // Arrange
        var profileStore = new Dictionary<string, UserProfileDto>();
        var profile1 = new UserProfileDto(
            "user1",
            "Conservative",
            "Save for house",
            "Short-term"
        );
        var profile2 = new UserProfileDto(
            "user2",
            "Aggressive",
            "Build wealth",
            "Long-term"
        );

        // Act
        profileStore["user1"] = profile1;
        profileStore["user2"] = profile2;

        // Assert
        profileStore.Should().HaveCount(2);
        profileStore["user1"].Should().Be(profile1);
        profileStore["user2"].Should().Be(profile2);
        await Task.CompletedTask; // Make method async for consistency
    }

    [Fact]
    public void WorkflowExecutionContext_Should_Support_Timestamp_Tracking()
    {
        // Arrange
        var now = DateTime.UtcNow;

        // Act
        var context1 = new WorkflowExecutionContext("user1", "query1", now);
        var context2 = new WorkflowExecutionContext("user2", "query2", now.AddMinutes(5));

        // Assert
        context1.RequestTime.Should().BeBefore(context2.RequestTime);
        (context2.RequestTime - context1.RequestTime).TotalMinutes.Should().BeApproximately(5, 0.1);
    }

    [Fact]
    public async Task SetProfile_ShouldSavePartial_WhenNotAllValuesProvided()
    {
        // Arrange - New incremental saving pattern: saves partial data, doesn't reject
        var profileStore = new InMemoryUserProfileStore();
        var agent = new UserProfileAgent(null!, profileStore);
        var conversation = new List<ChatMessage>
        {
            new(ChatRole.User, "Give me financial advice"),
            new(ChatRole.Assistant, "Please provide your email address."),
            new(ChatRole.User, "user@example.com"),
            new(ChatRole.Assistant, "What is your risk tolerance: Conservative, Moderate, or Aggressive?"),
            new(ChatRole.User, "Moderate")
        };

        using var scope = ConversationRunContext.Push(new ConversationRunSnapshot("conv-1", conversation));

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
        var agent = new UserProfileAgent(null!, profileStore);
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

        using var scope = ConversationRunContext.Push(new ConversationRunSnapshot("conv-2", conversation));

        // Act
        var result = await agent.SetProfile("user@example.com", "Moderate", "Save for retirement", "Long-term");

        // Assert - Should return COMPLETE status
        result.Should().Contain("COMPLETE");
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
        var agent = new UserProfileAgent(null!, profileStore);
        var conversation = new List<ChatMessage>
        {
            new(ChatRole.User, "user@example.com")
        };

        using var scope = ConversationRunContext.Push(new ConversationRunSnapshot("conv-3", conversation));

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
        var agent = new UserProfileAgent(null!, profileStore);

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
        var agent = new UserProfileAgent(null!, profileStore);

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
        var agent = new UserProfileAgent(null!, profileStore);
        await profileStore.SetProfileAsync("user@example.com", new UserProfileDto("user@example.com", "Moderate", "Retirement", "Long-term"));

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
        var agent = new UserProfileAgent(null!, profileStore);

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
        var agent = new UserProfileAgent(null!, profileStore);

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
        var agent = new UserProfileAgent(null!, profileStore);
        await profileStore.SetProfileAsync("user@example.com", new UserProfileDto("user@example.com", "Moderate", null, null));

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
            .ReturnsAsync(new UserProfileDto("user@example.com", "Moderate", "Goals", "Long-term"));
        mockStore.Setup(s => s.DeleteProfileAsync("user@example.com"))
            .ThrowsAsync(new InvalidOperationException("Store connection failed"));
        var agent = new UserProfileAgent(null!, mockStore.Object);

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
        var agent = new UserProfileAgent(null!, profileStore);

        // Act
        var result = await agent.DeleteProfile(userId!);

        // Assert
        result.Should().Contain("ERROR");
    }
}
