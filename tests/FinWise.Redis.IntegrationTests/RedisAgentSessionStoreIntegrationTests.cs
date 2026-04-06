using FluentAssertions;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;
using Moq;
using StackExchange.Redis;
using Xunit;
using FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStores.Redis;

namespace FinWise.Redis.IntegrationTests;

/// <summary>
/// Integration tests for RedisAgentSessionStore against a real Redis instance.
/// Requires: docker compose up -d redis
/// </summary>
[Trait("Category", "Integration")]
public class RedisAgentSessionStoreIntegrationTests : IAsyncLifetime
{
    private const string RedisConnection = "localhost:6379";

    private IConnectionMultiplexer? _redis;
    private RedisAgentSessionStore? _store;
    private ChatClientAgent? _agent;

    // Unique key prefix per test run to avoid collisions
    private readonly string _testPrefix = $"test_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        try
        {
            _redis = await ConnectionMultiplexer.ConnectAsync(
                $"{RedisConnection},connectTimeout=3000,abortConnect=false");

            // Verify connection is alive
            var db = _redis.GetDatabase();
            await db.PingAsync();
        }
        catch
        {
            // Redis not available - tests will skip
            _redis = null;
            return;
        }

        _store = new RedisAgentSessionStore(_redis, TimeSpan.FromMinutes(60), "integration_test_agent");
        _agent = new ChatClientAgent(new Mock<IChatClient>().Object, new ChatClientAgentOptions
        {
            Id = "integration_test_agent",
            Name = "integration_test_agent",
            Description = "Test agent for integration tests"
        });
    }

    public async Task DisposeAsync()
    {
        if (_redis != null)
        {
            var db = _redis.GetDatabase();
            var server = _redis.GetServer(_redis.GetEndPoints()[0]);

            // Clean up all keys created by this test run
            await foreach (var key in server.KeysAsync(pattern: $"agentsession:integration_test_agent:{_testPrefix}*"))
            {
                await db.KeyDeleteAsync(key);
            }

            _redis.Dispose();
        }
    }

    private void SkipIfRedisNotAvailable()
    {
        Skip.If(_redis == null || !_redis.IsConnected,
            "Redis is not available. Run: docker compose up -d redis");
    }

    [SkippableFact]
    public async Task SaveAndGetSession_RoundTrips_WithRealRedis()
    {
        SkipIfRedisNotAvailable();

        var conversationId = $"{_testPrefix}_roundtrip";
        var session = await _agent!.CreateSessionAsync();
        session.SetInMemoryChatHistory([new ChatMessage(ChatRole.User, "Hello integration test")]);

        await _store!.SaveSessionAsync(_agent, conversationId, session);

        var result = await _store.GetSessionAsync(_agent, conversationId);

        result.Should().NotBeNull();
        result.TryGetInMemoryChatHistory(out var messages).Should().BeTrue();
        messages.Should().ContainSingle(m => m.Text == "Hello integration test");
    }

    [SkippableFact]
    public async Task GetSession_ReturnsNewSession_WhenKeyNotInRedis()
    {
        SkipIfRedisNotAvailable();

        var result = await _store!.GetSessionAsync(_agent!, $"{_testPrefix}_nonexistent");

        result.Should().NotBeNull();
        result.TryGetInMemoryChatHistory(out _).Should().BeFalse(
            "a new session for a nonexistent key should have no chat history");
    }

    [SkippableFact]
    public async Task ClearSession_RemovesKey_FromRedis()
    {
        SkipIfRedisNotAvailable();

        var conversationId = $"{_testPrefix}_clear";
        var session = await _agent!.CreateSessionAsync();
        session.SetInMemoryChatHistory([new ChatMessage(ChatRole.User, "To be cleared")]);

        await _store!.SaveSessionAsync(_agent, conversationId, session);
        await _store.ClearSessionAsync(conversationId);

        var result = await _store.GetSessionAsync(_agent, conversationId);
        result.Should().NotBeNull();
        result.TryGetInMemoryChatHistory(out var messages).Should().BeFalse();
    }

    [SkippableFact]
    public async Task ClearSession_NonExistentKey_DoesNotThrow()
    {
        SkipIfRedisNotAvailable();

        var act = () => _store!.ClearSessionAsync($"{_testPrefix}_never_existed");

        await act.Should().NotThrowAsync();
    }

    [SkippableFact]
    public async Task SessionExpires_AfterTtl()
    {
        SkipIfRedisNotAvailable();

        var shortTtlStore = new RedisAgentSessionStore(_redis!, TimeSpan.FromSeconds(2), "integration_test_agent");
        var conversationId = $"{_testPrefix}_ttl";

        var session = await _agent!.CreateSessionAsync();
        session.SetInMemoryChatHistory([new ChatMessage(ChatRole.User, "Ephemeral")]);

        await shortTtlStore.SaveSessionAsync(_agent, conversationId, session);

        // Verify session exists immediately
        var immediate = await shortTtlStore.GetSessionAsync(_agent, conversationId);
        immediate.TryGetInMemoryChatHistory(out var beforeMessages).Should().BeTrue();
        beforeMessages.Should().ContainSingle(m => m.Text == "Ephemeral");

        // Wait for TTL to expire
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Session should be gone - GetSessionAsync returns a new empty session
        var expired = await shortTtlStore.GetSessionAsync(_agent, conversationId);
        expired.Should().NotBeNull();
        expired.TryGetInMemoryChatHistory(out _).Should().BeFalse();
    }

    [SkippableFact]
    public async Task SaveSession_OverwritesPreviousValue()
    {
        SkipIfRedisNotAvailable();

        var conversationId = $"{_testPrefix}_overwrite";

        var first = await _agent!.CreateSessionAsync();
        first.SetInMemoryChatHistory([new ChatMessage(ChatRole.User, "first")]);
        await _store!.SaveSessionAsync(_agent, conversationId, first);

        var second = await _agent.CreateSessionAsync();
        second.SetInMemoryChatHistory([new ChatMessage(ChatRole.User, "second")]);
        await _store.SaveSessionAsync(_agent, conversationId, second);

        var result = await _store.GetSessionAsync(_agent, conversationId);

        result.TryGetInMemoryChatHistory(out var messages).Should().BeTrue();
        messages.Should().ContainSingle(m => m.Text == "second");
    }
}
