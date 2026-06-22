using FluentAssertions;
using FinWise.MultiAgentWorkflow.Session;
using FinWise.MultiAgentWorkflow.Workflow;
using Microsoft.Extensions.AI;
using Xunit;

namespace FinWise.MultiAgentWorkflow.UnitTests;

[Trait("Category", "Unit")]
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

    #region IsEphemeralProfileContext

    [Fact]
    public void IsEphemeralProfileContext_Should_ReturnTrue_ForProfileContextAuthoredMessage()
    {
        // Arrange
        var message = new ChatMessage(ChatRole.System, "CURRENT USER PROFILE ...")
        {
            AuthorName = FinWiseWorkflowService.ProfileContextAuthorName
        };

        // Act / Assert
        FinWiseWorkflowService.IsEphemeralProfileContext(message).Should().BeTrue();
    }

    [Fact]
    public void IsEphemeralProfileContext_Should_ReturnFalse_ForNormalMessages()
    {
        // Arrange
        var assistantMessage = new ChatMessage(ChatRole.Assistant, "Here is my advice") { AuthorName = "advisor_agent" };
        var userMessage = new ChatMessage(ChatRole.User, "What should I invest in?");

        // Act / Assert
        FinWiseWorkflowService.IsEphemeralProfileContext(assistantMessage).Should().BeFalse();
        FinWiseWorkflowService.IsEphemeralProfileContext(userMessage).Should().BeFalse();
    }

    [Fact]
    public void AppendUniqueMessages_Should_NotPersistEphemeralProfileContext_WhenEchoedInOutputs()
    {
        // Arrange — existing history: a user message and an assistant reply
        var userMessage = new ChatMessage(ChatRole.User, "What should I invest in?");
        var assistantReply = new ChatMessage(ChatRole.Assistant, "Earlier advice") { AuthorName = "advisor_agent" };
        var messageHistory = new List<ChatMessage> { userMessage, assistantReply };

        const string profileContextText =
            "CURRENT USER PROFILE\nEmail: jane@example.com\nRisk: aggressive\nGoals: retirement\nTimeframe: 20 years";

        // Simulate the SDK echo: the ephemeral profile-context message, the echoed user
        // message, and a NEW assistant message all come back in the workflow outputs.
        var workflowOutputs = new List<ChatMessage>
        {
            new(ChatRole.System, profileContextText) { AuthorName = FinWiseWorkflowService.ProfileContextAuthorName },
            new(ChatRole.User, "What should I invest in?"),
            new(ChatRole.Assistant, "Fresh advice based on your profile") { AuthorName = "advisor_agent" }
        };

        // Act — apply the SAME filter used in production before appending
        var filtered = workflowOutputs.Where(m => !FinWiseWorkflowService.IsEphemeralProfileContext(m)).ToList();
        FinWiseWorkflowService.AppendUniqueMessages(messageHistory, filtered);

        // Assert — the new assistant message is persisted...
        messageHistory.Should().Contain(m => m.Text == "Fresh advice based on your profile");
        // ...but no profile-context message and no PII text leaked into history
        messageHistory.Should().NotContain(m => m.AuthorName == FinWiseWorkflowService.ProfileContextAuthorName);
        messageHistory.Should().NotContain(m => (m.Text ?? string.Empty).Contains("CURRENT USER PROFILE"));
        messageHistory.Should().NotContain(m => (m.Text ?? string.Empty).Contains("jane@example.com"));
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
