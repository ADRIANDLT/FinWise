using FluentAssertions;
using FinWise.Orchestrator;
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
            RiskTolerance.Moderate,
            "Retirement savings",
            InvestmentTimeframe.LongTerm
        );

        // Assert
        profile.UserIdentifier.Should().Be("user123");
        profile.RiskTolerance.Should().Be(RiskTolerance.Moderate);
        profile.InvestmentGoals.Should().Be("Retirement savings");
        profile.InvestmentTimeframe.Should().Be(InvestmentTimeframe.LongTerm);
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
        context.UserIdentifier.Should().Be("user456");
        context.Query.Should().Be("What should I invest in?");
        context.RequestTime.Should().Be(requestTime);
    }

    [Fact]
    public void RiskTolerance_Enum_Should_Have_Three_Levels()
    {
        // Arrange
        var values = Enum.GetValues<RiskTolerance>();

        // Assert
        values.Should().HaveCount(3);
        values.Should().Contain(RiskTolerance.Conservative);
        values.Should().Contain(RiskTolerance.Moderate);
        values.Should().Contain(RiskTolerance.Aggressive);
    }

    [Fact]
    public void InvestmentTimeframe_Enum_Should_Have_Three_Levels()
    {
        // Arrange
        var values = Enum.GetValues<InvestmentTimeframe>();

        // Assert
        values.Should().HaveCount(3);
        values.Should().Contain(InvestmentTimeframe.ShortTerm);
        values.Should().Contain(InvestmentTimeframe.MediumTerm);
        values.Should().Contain(InvestmentTimeframe.LongTerm);
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
            RiskTolerance.Conservative,
            "Save for house",
            InvestmentTimeframe.ShortTerm
        );
        var profile2 = new UserProfileDto(
            "user2",
            RiskTolerance.Aggressive,
            "Build wealth",
            InvestmentTimeframe.LongTerm
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
}
