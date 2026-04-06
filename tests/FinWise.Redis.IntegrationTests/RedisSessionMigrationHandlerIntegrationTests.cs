using System.Text.Json;
using FluentAssertions;
using FinWise.McpServer.Infrastructure.McpSession.Redis;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.Protocol;
using StackExchange.Redis;
using Xunit;

namespace FinWise.Redis.IntegrationTests;

/// <summary>
/// Integration tests for <see cref="RedisSessionMigrationHandler"/> against a real Redis instance.
/// Verifies MCP session migration (mcpinit:* keys) — the ability to store and restore
/// MCP initialize handshake parameters across server instances.
/// Requires: docker compose up -d redis
/// </summary>
[Trait("Category", "Integration")]
public class RedisSessionMigrationHandlerIntegrationTests : IAsyncLifetime
{
    private const string RedisConnection = "localhost:6379";

    private IConnectionMultiplexer? _redis;
    private RedisSessionMigrationHandler? _handler;
    private readonly DefaultHttpContext _httpContext = new();

    // Unique key prefix per test run to avoid collisions
    private readonly string _testPrefix = $"test_{Guid.NewGuid():N}";

    public async Task InitializeAsync()
    {
        try
        {
            _redis = await ConnectionMultiplexer.ConnectAsync(
                $"{RedisConnection},connectTimeout=3000,abortConnect=false");

            var db = _redis.GetDatabase();
            await db.PingAsync();
        }
        catch
        {
            _redis = null;
            return;
        }

        _handler = new RedisSessionMigrationHandler(_redis, TimeSpan.FromMinutes(60));
    }

    public async Task DisposeAsync()
    {
        if (_redis != null)
        {
            var db = _redis.GetDatabase();
            var server = _redis.GetServer(_redis.GetEndPoints()[0]);

            // Clean up all mcpinit:* keys created by this test run
            await foreach (var key in server.KeysAsync(pattern: $"mcpinit:{_testPrefix}*"))
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

    private static InitializeRequestParams CreateTestInitParams(string clientName = "test-client")
    {
        return new InitializeRequestParams
        {
            ProtocolVersion = "2025-03-26",
            ClientInfo = new Implementation { Name = clientName, Version = "1.0" },
            Capabilities = new ClientCapabilities()
        };
    }

    [SkippableFact]
    public async Task OnSessionInitialized_ThenAllowMigration_RoundTrips_WithRealRedis()
    {
        SkipIfRedisNotAvailable();

        var sessionId = $"{_testPrefix}_roundtrip";
        var initParams = CreateTestInitParams("roundtrip-client");

        // Store init params (simulates Instance A handling initialize)
        await _handler!.OnSessionInitializedAsync(
            _httpContext, sessionId, initParams, CancellationToken.None);

        // Migrate (simulates Instance B receiving a tool call for this session)
        var restored = await _handler.AllowSessionMigrationAsync(
            _httpContext, sessionId, CancellationToken.None);

        restored.Should().NotBeNull();
        restored!.ProtocolVersion.Should().Be("2025-03-26");
        restored.ClientInfo!.Name.Should().Be("roundtrip-client");
        restored.ClientInfo.Version.Should().Be("1.0");
    }

    [SkippableFact]
    public async Task AllowMigration_ReturnsNull_ForUnknownSession_WithRealRedis()
    {
        SkipIfRedisNotAvailable();

        var result = await _handler!.AllowSessionMigrationAsync(
            _httpContext, $"{_testPrefix}_nonexistent", CancellationToken.None);

        result.Should().BeNull();
    }

    [SkippableFact]
    public async Task AllowMigration_RefreshesTtl_WithRealRedis()
    {
        SkipIfRedisNotAvailable();

        var sessionId = $"{_testPrefix}_ttl_refresh";
        var initParams = CreateTestInitParams();

        await _handler!.OnSessionInitializedAsync(
            _httpContext, sessionId, initParams, CancellationToken.None);

        // Read the initial TTL
        var db = _redis!.GetDatabase();
        var key = RedisSessionMigrationHandler.GetKey(sessionId);
        var ttlBefore = await db.KeyTimeToLiveAsync(key);
        ttlBefore.Should().NotBeNull();

        // Wait a moment then migrate — TTL should be refreshed (reset to full duration)
        await Task.Delay(TimeSpan.FromSeconds(2));

        await _handler.AllowSessionMigrationAsync(
            _httpContext, sessionId, CancellationToken.None);

        var ttlAfter = await db.KeyTimeToLiveAsync(key);
        ttlAfter.Should().NotBeNull();
        // After refresh, TTL should be >= the TTL before the delay
        // (it was reset to 60 min, while before it had ~60 min minus 2 sec)
        ttlAfter!.Value.Should().BeGreaterThanOrEqualTo(ttlBefore!.Value);
    }

    [SkippableFact]
    public async Task SessionExpires_AfterTtl_WithRealRedis()
    {
        SkipIfRedisNotAvailable();

        var shortTtlHandler = new RedisSessionMigrationHandler(_redis!, TimeSpan.FromSeconds(2));
        var sessionId = $"{_testPrefix}_expiry";
        var initParams = CreateTestInitParams();

        await shortTtlHandler.OnSessionInitializedAsync(
            _httpContext, sessionId, initParams, CancellationToken.None);

        // Verify exists immediately
        var immediate = await shortTtlHandler.AllowSessionMigrationAsync(
            _httpContext, sessionId, CancellationToken.None);
        immediate.Should().NotBeNull();

        // Wait for TTL to expire
        await Task.Delay(TimeSpan.FromSeconds(3));

        // Should be gone
        var expired = await shortTtlHandler.AllowSessionMigrationAsync(
            _httpContext, sessionId, CancellationToken.None);
        expired.Should().BeNull();
    }

    [SkippableFact]
    public async Task MultipleSessionsAreIsolated_WithRealRedis()
    {
        SkipIfRedisNotAvailable();

        var sessionA = $"{_testPrefix}_session_a";
        var sessionB = $"{_testPrefix}_session_b";

        await _handler!.OnSessionInitializedAsync(
            _httpContext, sessionA, CreateTestInitParams("client-a"), CancellationToken.None);
        await _handler.OnSessionInitializedAsync(
            _httpContext, sessionB, CreateTestInitParams("client-b"), CancellationToken.None);

        var restoredA = await _handler.AllowSessionMigrationAsync(
            _httpContext, sessionA, CancellationToken.None);
        var restoredB = await _handler.AllowSessionMigrationAsync(
            _httpContext, sessionB, CancellationToken.None);

        restoredA!.ClientInfo!.Name.Should().Be("client-a");
        restoredB!.ClientInfo!.Name.Should().Be("client-b");
    }

    [SkippableFact]
    public async Task KeyFormat_UsesCorrectPrefix_WithRealRedis()
    {
        SkipIfRedisNotAvailable();

        var sessionId = $"{_testPrefix}_keyformat";
        var initParams = CreateTestInitParams();

        await _handler!.OnSessionInitializedAsync(
            _httpContext, sessionId, initParams, CancellationToken.None);

        // Verify the key exists in Redis with the expected format
        var db = _redis!.GetDatabase();
        var expectedKey = $"mcpinit:{sessionId}";
        var exists = await db.KeyExistsAsync(expectedKey);
        exists.Should().BeTrue($"key '{expectedKey}' should exist in Redis after OnSessionInitializedAsync");

        // Verify the stored value is valid JSON
        var json = await db.StringGetAsync(expectedKey);
        json.IsNullOrEmpty.Should().BeFalse();
        var deserialized = JsonSerializer.Deserialize<InitializeRequestParams>(json.ToString());
        deserialized.Should().NotBeNull();
        deserialized!.ProtocolVersion.Should().Be("2025-03-26");
    }
}
