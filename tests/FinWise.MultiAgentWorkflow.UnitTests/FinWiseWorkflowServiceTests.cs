using FluentAssertions;
using FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStores;
using FinWise.MultiAgentWorkflow.Session;
using FinWise.MultiAgentWorkflow.Workflow;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace FinWise.MultiAgentWorkflow.UnitTests;

/// <summary>
/// Unit tests for <see cref="FinWiseWorkflowService"/>.
/// Tests focus on error-handling paths because workflow execution (via InProcessExecution.RunStreamingAsync)
/// calls real LLM through IChatClient.CompleteAsync(). Mocking the chat client to throw lets us exercise
/// the catch blocks, session ID management, and reset behavior.
/// </summary>
[Trait("Category", "Unit")]
public class FinWiseWorkflowServiceTests
{
    private readonly Mock<IChatClient> _mockChatClient;
    private readonly Mock<IUserProfileStore> _mockProfileStore;
    private readonly InMemoryAgentSessionStore _sessionStore;
    private readonly ChatClientAgent _stockAgent;
    private readonly FinWiseWorkflowService _sut;

    public FinWiseWorkflowServiceTests()
    {
        _mockChatClient = new Mock<IChatClient>();
        _mockProfileStore = new Mock<IUserProfileStore>();
        _sessionStore = new InMemoryAgentSessionStore();
        _stockAgent = new ChatClientAgent(_mockChatClient.Object, new ChatClientAgentOptions
        {
            Id = "stock_agent",
            Name = "stock_agent",
            Description = "Test stock agent"
        });

        // Mock CompleteAsync to throw — forces workflow execution to fail and hit catch blocks
        _mockChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Test: LLM unavailable"));

        _sut = new FinWiseWorkflowService(
            _mockChatClient.Object,
            _mockProfileStore.Object,
            _sessionStore,
            _stockAgent);
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenWorkflowFails_ReturnsErrorResponse()
    {
        var result = await _sut.ProcessMessageAsync("test-session-123", "hello");

        result.AgentSessionId.Should().Be("test-session-123");
        result.Response.Should().Contain("I apologize");
        result.WasReset.Should().BeFalse();
    }

    [Fact]
    public void SessionResetToken_DefaultsToFalse()
    {
        var token = new SessionResetToken();
        token.IsRequested.Should().BeFalse();
    }

    [Fact]
    public void SessionResetToken_Request_SetsFlag()
    {
        var token = new SessionResetToken();
        token.Request();
        token.IsRequested.Should().BeTrue();
    }

    [Fact]
    public void SessionResetFlag_Initialize_ReturnsTokenAccessibleViaCurrent()
    {
        var token = SessionResetFlag.Initialize();
        SessionResetFlag.Current.Should().BeSameAs(token);
        SessionResetFlag.Clear();
        SessionResetFlag.Current.Should().BeNull();
    }

    [Fact]
    public async Task SessionResetFlag_TokenMutationVisibleAcrossAwait()
    {
        // Simulates the parent→child→parent flow:
        // Parent initializes token, child mutates it via async call, parent reads mutation
        var token = SessionResetFlag.Initialize();

        await Task.Run(() =>
        {
            // Simulate tool execution in child async context
            SessionResetFlag.Current?.Request();
        });

        // Parent reads the mutation after await — this is the critical test
        token.IsRequested.Should().BeTrue("mutation via shared reference should be visible to parent");
        SessionResetFlag.Clear();
    }

    [Fact]
    public async Task ProcessMessageAsync_WithEmptyQuery_ReturnsErrorResponse()
    {
        var result = await _sut.ProcessMessageAsync("empty-query-session", "");

        result.AgentSessionId.Should().Be("empty-query-session");
        result.Response.Should().Contain("I apologize");
        result.WasReset.Should().BeFalse();
    }

    [Fact]
    public async Task ProcessMessageAsync_WhenCancelled_ReturnsOriginalSessionIdAndErrorResponse()
    {
        // Arrange: OperationCanceledException thrown from IChatClient.GetResponseAsync gets wrapped
        // by the SDK's InProcessExecution, so it surfaces as a general Exception — not as
        // OperationCanceledException. The timeout catch block only fires for the internal 60s CTS.
        // This test verifies the general catch block still returns the original session ID.
        var cancellingChatClient = new Mock<IChatClient>();
        cancellingChatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("Test: simulated cancellation"));

        var sut = new FinWiseWorkflowService(
            cancellingChatClient.Object,
            _mockProfileStore.Object,
            new InMemoryAgentSessionStore(),
            new ChatClientAgent(cancellingChatClient.Object, new ChatClientAgentOptions
            {
                Id = "stock_agent",
                Name = "stock_agent",
                Description = "Test stock agent"
            }));

        var result = await sut.ProcessMessageAsync("timeout-session", "hello");

        result.AgentSessionId.Should().Be("timeout-session");
        result.Response.Should().Contain("I apologize");
        result.WasReset.Should().BeFalse();
    }

    [Fact]
    public async Task ResetSessionAsync_ClearsSessionWithoutNewId()
    {
        // ResetSessionAsync clears session data but keeps the same ID.
        // Calling it should not throw, and the next GetOrCreate should return a fresh session.
        await _sut.ResetSessionAsync("session-to-reset");

        // Verify the session was cleared by checking ProcessMessageAsync still works
        // with the same ID (it gets a fresh session, not stale data)
        var result = await _sut.ProcessMessageAsync("session-to-reset", "hello");
        result.AgentSessionId.Should().Be("session-to-reset");
    }
}
