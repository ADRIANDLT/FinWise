using FluentAssertions;
using FinWise.MultiAgentWorkflow.Session;
using FinWise.MultiAgentWorkflow.Workflow;
using Microsoft.Extensions.AI;
using Xunit;

namespace FinWise.MultiAgentWorkflow.UnitTests;

public class MessageDeduplicationTests
{
    #region AppendUniqueMessages

    [Fact]
    public void AppendUniqueMessages_Should_AddNewMessages()
    {
        // Arrange
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello")
        };
        var newMessages = new List<ChatMessage>
        {
            new(ChatRole.Assistant, "Hi there!")
        };

        // Act
        FinWiseWorkflowService.AppendUniqueMessages(history, newMessages);

        // Assert
        history.Should().HaveCount(2);
        history[1].Text.Should().Be("Hi there!");
    }

    [Fact]
    public void AppendUniqueMessages_Should_NotAddDuplicateMessages()
    {
        // Arrange
        var existingMessage = new ChatMessage(ChatRole.User, "Hello") { AuthorName = "Alice" };
        var history = new List<ChatMessage> { existingMessage };
        var duplicateMessage = new ChatMessage(ChatRole.User, "Hello") { AuthorName = "Alice" };
        var newMessages = new List<ChatMessage> { duplicateMessage };

        // Act
        FinWiseWorkflowService.AppendUniqueMessages(history, newMessages);

        // Assert — duplicate not added
        history.Should().HaveCount(1);
    }

    [Fact]
    public void AppendUniqueMessages_Should_NoOp_WhenNewMessagesEmpty()
    {
        // Arrange
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello")
        };
        var newMessages = new List<ChatMessage>();

        // Act
        FinWiseWorkflowService.AppendUniqueMessages(history, newMessages);

        // Assert
        history.Should().HaveCount(1);
    }

    [Fact]
    public void AppendUniqueMessages_Should_HandleMessagesWithNullAuthorNameAndText()
    {
        // Arrange
        var history = new List<ChatMessage>();
        var newMessages = new List<ChatMessage>
        {
            new(ChatRole.User, (string?)null),
            new(ChatRole.Assistant, "response") { AuthorName = null }
        };

        // Act
        FinWiseWorkflowService.AppendUniqueMessages(history, newMessages);

        // Assert — both added without error
        history.Should().HaveCount(2);
    }

    [Fact]
    public void AppendUniqueMessages_Should_AddMixOfUniqueAndSkipDuplicates()
    {
        // Arrange
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "existing")
        };
        var newMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "existing"),    // duplicate
            new(ChatRole.User, "brand new")    // unique
        };

        // Act
        FinWiseWorkflowService.AppendUniqueMessages(history, newMessages);

        // Assert
        history.Should().HaveCount(2);
        history[1].Text.Should().Be("brand new");
    }

    #endregion

    #region BuildMessageSignature

    [Fact]
    public void BuildMessageSignature_Should_ProduceRoleAuthorTextFormat()
    {
        // Arrange
        var message = new ChatMessage(ChatRole.User, "Hello world") { AuthorName = "Alice" };

        // Act
        var signature = FinWiseWorkflowService.BuildMessageSignature(message);

        // Assert
        signature.Should().Be("user:Alice:Hello world");
    }

    [Fact]
    public void BuildMessageSignature_Should_UseEmptyString_WhenAuthorNameIsNull()
    {
        // Arrange
        var message = new ChatMessage(ChatRole.Assistant, "response");

        // Act
        var signature = FinWiseWorkflowService.BuildMessageSignature(message);

        // Assert
        signature.Should().Be("assistant::response");
    }

    [Fact]
    public void BuildMessageSignature_Should_UseEmptyString_WhenTextIsNull()
    {
        // Arrange
        var message = new ChatMessage(ChatRole.User, (string?)null) { AuthorName = "Bot" };

        // Act
        var signature = FinWiseWorkflowService.BuildMessageSignature(message);

        // Assert
        signature.Should().Be("user:Bot:");
    }

    #endregion

    #region ExtractUserIdFromMessageHistory

    [Fact]
    public void ExtractUserIdFromMessageHistory_Should_ExtractEmail_FromProfileReadyMessage()
    {
        // Arrange
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "PROFILE_READY: email=user@example.com risk=Moderate goals=Retirement timeframe=Long-term")
        };

        // Act
        var userId = AgentSessionConstants.ExtractUserIdFromMessageHistory(history);

        // Assert
        userId.Should().Be("user@example.com");
    }

    [Fact]
    public void ExtractUserIdFromMessageHistory_Should_ReturnNull_WhenNoProfileReadyMessage()
    {
        // Arrange
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "How can I help you?")
        };

        // Act
        var userId = AgentSessionConstants.ExtractUserIdFromMessageHistory(history);

        // Assert
        userId.Should().BeNull();
    }

    [Fact]
    public void ExtractUserIdFromMessageHistory_Should_ReturnNull_WhenHistoryIsEmpty()
    {
        // Arrange
        var history = new List<ChatMessage>();

        // Act
        var userId = AgentSessionConstants.ExtractUserIdFromMessageHistory(history);

        // Assert
        userId.Should().BeNull();
    }

    [Fact]
    public void ExtractUserIdFromMessageHistory_Should_FindProfileReady_CaseInsensitive()
    {
        // Arrange — mixed case "profile_ready:"
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "Hi"),
            new(ChatRole.Assistant, "Some preamble"),
            new(ChatRole.Assistant, "profile_ready: email=other@domain.org risk=Aggressive goals=Growth timeframe=Short"),
            new(ChatRole.User, "Thanks")
        };

        // Act
        var userId = AgentSessionConstants.ExtractUserIdFromMessageHistory(history);

        // Assert
        userId.Should().Be("other@domain.org");
    }

    [Fact]
    public void ExtractUserIdFromMessageHistory_Should_IgnoreUserRoleMessages()
    {
        // Arrange — PROFILE_READY in a User message should be ignored (only Assistant role)
        var history = new List<ChatMessage>
        {
            new(ChatRole.User, "PROFILE_READY: email=sneaky@example.com risk=Moderate")
        };

        // Act
        var userId = AgentSessionConstants.ExtractUserIdFromMessageHistory(history);

        // Assert
        userId.Should().BeNull();
    }

    #endregion
}
