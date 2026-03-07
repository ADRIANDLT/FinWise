using FluentAssertions;
using FinWise.MultiAgentWorkflow.Session;
using Microsoft.Extensions.AI;
using Xunit;

namespace FinWise.MultiAgentWorkflow.UnitTests;

public class AgentSessionResetEvaluatorTests
{
    /// <summary>
    /// Helper to build a conversation history that includes a PROFILE_READY marker,
    /// simulating an already-identified user session.
    /// </summary>
    private static List<ChatMessage> HistoryWithProfileReady() =>
    [
        new(ChatRole.User, "Hello"),
        new(ChatRole.Assistant, "PROFILE_READY: email=user@example.com risk=Moderate goals=Retirement timeframe=Long-term"),
        new(ChatRole.User, "Show me my portfolio")
    ];

    /// <summary>
    /// Conversation history without any PROFILE_READY marker.
    /// </summary>
    private static List<ChatMessage> HistoryWithoutProfileReady() =>
    [
        new(ChatRole.User, "Hello"),
        new(ChatRole.Assistant, "Welcome! What is your email?")
    ];

    #region ShouldResetSession - false cases

    [Fact]
    public void ShouldResetSession_Should_ReturnFalse_WhenMessageHistoryIsEmpty()
    {
        // Arrange
        var history = new List<ChatMessage>();

        // Act
        var result = AgentSessionResetEvaluator.ShouldResetSession(history, "start new session");

        // Assert
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ShouldResetSession_Should_ReturnFalse_WhenQueryIsNullOrEmpty(string? query)
    {
        // Arrange
        var history = HistoryWithProfileReady();

        // Act
        var result = AgentSessionResetEvaluator.ShouldResetSession(history, query);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void ShouldResetSession_Should_ReturnFalse_WhenNoProfileReadyMarker()
    {
        // Arrange - reset phrase present but no established profile
        var history = HistoryWithoutProfileReady();

        // Act
        var result = AgentSessionResetEvaluator.ShouldResetSession(history, "start new session");

        // Assert
        result.Should().BeFalse("reset only applies when a profile is already established");
    }

    [Fact]
    public void ShouldResetSession_Should_ReturnFalse_WhenQueryIsNormalQuestion()
    {
        // Arrange - profile exists but query has no reset trigger
        var history = HistoryWithProfileReady();

        // Act
        var result = AgentSessionResetEvaluator.ShouldResetSession(history, "What is my portfolio allocation?");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region ShouldResetSession - true cases

    [Fact]
    public void ShouldResetSession_Should_ReturnTrue_WhenProfileExistsAndQueryContainsStartNewSession()
    {
        // Arrange
        var history = HistoryWithProfileReady();

        // Act
        var result = AgentSessionResetEvaluator.ShouldResetSession(history, "I want to start new session please");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldResetSession_Should_ReturnTrue_WhenProfileExistsAndQueryContainsMyEmailIs()
    {
        // Arrange
        var history = HistoryWithProfileReady();

        // Act
        var result = AgentSessionResetEvaluator.ShouldResetSession(history, "my email is someone@else.com");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldResetSession_Should_ReturnTrue_WhenProfileExistsAndQueryContainsResetSession()
    {
        // Arrange
        var history = HistoryWithProfileReady();

        // Act
        var result = AgentSessionResetEvaluator.ShouldResetSession(history, "Please reset conversation");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void ShouldResetSession_Should_BeCaseInsensitive_ForTriggerMatching()
    {
        // Arrange - uppercase trigger should still match after normalization
        var history = HistoryWithProfileReady();

        // Act
        var result = AgentSessionResetEvaluator.ShouldResetSession(history, "START NEW SESSION");

        // Assert
        result.Should().BeTrue("query is normalized to lowercase before matching triggers");
    }

    [Theory]
    [InlineData("log out")]
    [InlineData("logout")]
    [InlineData("sign out")]
    [InlineData("switch user")]
    [InlineData("re-identify")]
    public void ShouldResetSession_Should_ReturnTrue_ForVariousResetTriggers(string trigger)
    {
        // Arrange
        var history = HistoryWithProfileReady();

        // Act
        var result = AgentSessionResetEvaluator.ShouldResetSession(history, trigger);

        // Assert
        result.Should().BeTrue("$trigger is a recognized reset phrase");
    }

    #endregion
}
