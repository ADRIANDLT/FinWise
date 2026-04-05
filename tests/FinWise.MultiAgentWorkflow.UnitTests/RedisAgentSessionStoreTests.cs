using System.Text.Json;
using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Moq;
using StackExchange.Redis;
using Xunit;
using FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStores.Redis;

namespace FinWise.MultiAgentWorkflow.UnitTests;

public class RedisAgentSessionStoreTests
{
    private readonly Mock<IConnectionMultiplexer> _redisMock = new();
    private readonly Mock<IDatabase> _dbMock = new();
    private readonly TimeSpan _ttl = TimeSpan.FromMinutes(60);
    private readonly RedisAgentSessionStore _store;
    private readonly ChatClientAgent _agent;

    public RedisAgentSessionStoreTests()
    {
        _redisMock.Setup(r => r.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(_dbMock.Object);
        _store = new RedisAgentSessionStore(_redisMock.Object, _ttl, "test_agent");
        _agent = new ChatClientAgent(new Mock<IChatClient>().Object, new ChatClientAgentOptions
        {
            Id = "test_agent",
            Name = "test_agent",
            Description = "Test agent"
        });
    }

    [Theory]
    [InlineData("orchestrator_agent", "abc123", "agentsession:orchestrator_agent:abc123")]
    [InlineData("agent_a", "session-1", "agentsession:agent_a:session-1")]
    public void GetKey_FormatsAsAgentIdColonConversationId(string agentId, string conversationId, string expected)
    {
        RedisAgentSessionStore.GetKey(agentId, conversationId).Should().Be(expected);
    }

    [Fact]
    public async Task SaveSessionAsync_StoresSerializedJsonWithTtl()
    {
        // Create a real session via the agent, then save it
        var session = await _agent.CreateSessionAsync();

        await _store.SaveSessionAsync(_agent, "conv-1", session);

        // Verify StringSetAsync was called with the correct key
        var invocation = _dbMock.Invocations
            .SingleOrDefault(i => i.Method.Name == "StringSetAsync");
        invocation.Should().NotBeNull("StringSetAsync should have been called");
        invocation!.Arguments[0].ToString().Should().Be("agentsession:test_agent:conv-1");

        // Verify TTL was passed (StackExchange.Redis encodes TimeSpan as "EX {seconds}")
        invocation.Arguments[2].ToString().Should().Be($"EX {(int)_ttl.TotalSeconds}");
    }

    [Fact]
    public async Task GetSessionAsync_CreatesNewSession_WhenKeyMissing()
    {
        _dbMock.Setup(db => db.StringGetAsync((RedisKey)"agentsession:test_agent:conv-new", It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisValue.Null);

        var result = await _store.GetSessionAsync(_agent, "conv-new");

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task GetSessionAsync_DeserializesSession_WhenKeyExists()
    {
        var originalSession = await _agent.CreateSessionAsync();
        var serialized = await _agent.SerializeSessionAsync(originalSession);
        var json = serialized.GetRawText();

        _dbMock.Setup(db => db.StringGetAsync((RedisKey)"agentsession:test_agent:conv-existing", It.IsAny<CommandFlags>()))
            .ReturnsAsync(new RedisValue(json));

        var result = await _store.GetSessionAsync(_agent, "conv-existing");

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ClearSessionAsync_DeletesKey()
    {
        await _store.ClearSessionAsync("conv-delete");

        _dbMock.Verify(db => db.KeyDeleteAsync(
            (RedisKey)"agentsession:test_agent:conv-delete",
            It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public void Constructor_ThrowsOnNullRedis()
    {
        var act = () => new RedisAgentSessionStore(null!, TimeSpan.FromMinutes(60), "test_agent");
        act.Should().Throw<ArgumentNullException>().WithParameterName("redis");
    }
}
