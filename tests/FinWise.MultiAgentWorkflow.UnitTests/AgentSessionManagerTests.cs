using FluentAssertions;
using FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStores;
using FinWise.MultiAgentWorkflow.Session;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace FinWise.MultiAgentWorkflow.UnitTests;

[Trait("Category", "Unit")]
public class AgentSessionManagerTests
{
    private readonly AgentSessionManager _manager;
    private readonly ChatClientAgent _agent;

    public AgentSessionManagerTests()
    {
        var store = new InMemoryAgentSessionStore();
        _manager = new AgentSessionManager(store);
        _agent = new ChatClientAgent(new Mock<IChatClient>().Object, new ChatClientAgentOptions
        {
            Id = "test_agent",
            Name = "test_agent",
            Description = "Test agent for session manager tests"
        });
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_NewSession_ReturnsEmptyMessages()
    {
        var (session, messages) = await _manager.GetOrCreateSessionAsync(_agent, "new-session-id");

        session.Should().NotBeNull();
        messages.Should().BeEmpty();
    }

    [Fact]
    public async Task PersistAndRestore_RoundTrip_PreservesMessages()
    {
        const string sessionId = "roundtrip-session";
        var originalMessages = new List<ChatMessage>
        {
            new(ChatRole.User, "Hello"),
            new(ChatRole.Assistant, "Hi there!")
        };

        // Create and persist
        var (session, _) = await _manager.GetOrCreateSessionAsync(_agent, sessionId);
        await _manager.PersistSessionAsync(sessionId, session, _agent, originalMessages);

        // Restore
        var (restoredSession, restoredMessages) = await _manager.GetOrCreateSessionAsync(_agent, sessionId);

        restoredSession.Should().NotBeNull();
        restoredMessages.Should().HaveCount(2);
        restoredMessages[0].Text.Should().Be("Hello");
        restoredMessages[1].Text.Should().Be("Hi there!");
    }

    [Fact]
    public async Task PersistAndRestore_MultipleRequests_AccumulatesMessages()
    {
        const string sessionId = "accumulate-session";

        // Request 1: 2 messages
        var (session1, _) = await _manager.GetOrCreateSessionAsync(_agent, sessionId);
        var messages1 = new List<ChatMessage>
        {
            new(ChatRole.User, "First question"),
            new(ChatRole.Assistant, "First answer")
        };
        await _manager.PersistSessionAsync(sessionId, session1, _agent, messages1);

        // Request 2: add 2 more messages
        var (session2, restored) = await _manager.GetOrCreateSessionAsync(_agent, sessionId);
        restored.Should().HaveCount(2);
        restored.Add(new ChatMessage(ChatRole.User, "Second question"));
        restored.Add(new ChatMessage(ChatRole.Assistant, "Second answer"));
        await _manager.PersistSessionAsync(sessionId, session2, _agent, restored);

        // Request 3: verify all 4 messages
        var (_, finalMessages) = await _manager.GetOrCreateSessionAsync(_agent, sessionId);
        finalMessages.Should().HaveCount(4);
        finalMessages[2].Text.Should().Be("Second question");
        finalMessages[3].Text.Should().Be("Second answer");
    }

    [Fact]
    public async Task GetOrCreateSessionAsync_DifferentSessionIds_AreIsolated()
    {
        var messagesA = new List<ChatMessage> { new(ChatRole.User, "Session A") };
        var messagesB = new List<ChatMessage> { new(ChatRole.User, "Session B") };

        var (sessionA, _) = await _manager.GetOrCreateSessionAsync(_agent, "session-a");
        await _manager.PersistSessionAsync("session-a", sessionA, _agent, messagesA);

        var (sessionB, _) = await _manager.GetOrCreateSessionAsync(_agent, "session-b");
        await _manager.PersistSessionAsync("session-b", sessionB, _agent, messagesB);

        var (_, restoredA) = await _manager.GetOrCreateSessionAsync(_agent, "session-a");
        var (_, restoredB) = await _manager.GetOrCreateSessionAsync(_agent, "session-b");

        restoredA.Should().HaveCount(1);
        restoredA[0].Text.Should().Be("Session A");
        restoredB.Should().HaveCount(1);
        restoredB[0].Text.Should().Be("Session B");
    }

    [Fact]
    public async Task ClearSessionAsync_ShouldNotThrow()
    {
        var act = () => _manager.ClearSessionAsync("any-session-id");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ClearSessionAsync_WithClearableStore_DelegatesToStore()
    {
        var mockStore = new Mock<AgentSessionStore>();
        var mockClearable = mockStore.As<IClearableSessionStore>();
        mockClearable.Setup(c => c.ClearSessionAsync(It.IsAny<string>())).Returns(Task.CompletedTask);

        var manager = new AgentSessionManager(mockStore.Object);
        await manager.ClearSessionAsync("session-to-clear");

        mockClearable.Verify(c => c.ClearSessionAsync("session-to-clear"), Times.Once);
    }

    [Fact]
    public async Task PersistAndRestore_EmptyMessageList_RoundTrips()
    {
        const string sessionId = "empty-messages-session";

        var (session, _) = await _manager.GetOrCreateSessionAsync(_agent, sessionId);
        await _manager.PersistSessionAsync(sessionId, session, _agent, []);

        var (_, restoredMessages) = await _manager.GetOrCreateSessionAsync(_agent, sessionId);

        restoredMessages.Should().BeEmpty();
    }

    [Fact]
    public async Task PersistAndRestore_DifferentAgentInstance_SameId_SharesSession()
    {
        const string sessionId = "shared-agent-id-session";
        var messages = new List<ChatMessage> { new(ChatRole.User, "Hello from agent 1") };

        // Persist with first agent instance
        var (session, _) = await _manager.GetOrCreateSessionAsync(_agent, sessionId);
        await _manager.PersistSessionAsync(sessionId, session, _agent, messages);

        // Restore with a different agent instance that has the same Id
        var agent2 = new ChatClientAgent(new Mock<IChatClient>().Object, new ChatClientAgentOptions
        {
            Id = "test_agent",
            Name = "test_agent",
            Description = "Second instance with same Id"
        });

        var (_, restoredMessages) = await _manager.GetOrCreateSessionAsync(agent2, sessionId);

        restoredMessages.Should().HaveCount(1);
        restoredMessages[0].Text.Should().Be("Hello from agent 1");
    }
}
