using System.Text.Json;
using Microsoft.AspNetCore.Http;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Protocol;
using Serilog;
using StackExchange.Redis;

namespace FinWise.McpServer.Infrastructure.McpSession.Redis;

/// <summary>
/// Redis-backed implementation of <see cref="ISessionMigrationHandler"/> for cross-instance MCP session migration.
/// Stores the MCP initialize handshake parameters in Redis so any instance can reconstruct the session.
///
/// Key format: mcpinit:{sessionId} — namespaced to separate from agent session keys.
///
/// TTL is refreshed on every successful migration read (true sliding window).
/// </summary>
public sealed class RedisSessionMigrationHandler : ISessionMigrationHandler
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _ttl;

    public RedisSessionMigrationHandler(IConnectionMultiplexer redis, TimeSpan ttl)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _ttl = ttl;
    }

    public async ValueTask OnSessionInitializedAsync(
        HttpContext context, string sessionId,
        InitializeRequestParams initializeParams,
        CancellationToken cancellationToken)
    {
        var key = GetKey(sessionId);
        var json = JsonSerializer.Serialize(initializeParams);
        await _redis.GetDatabase().StringSetAsync(key, json, _ttl);

        Log.Debug("Redis: Stored MCP init params for session {Key} (TTL: {Ttl})", key, _ttl);
    }

    public async ValueTask<InitializeRequestParams?> AllowSessionMigrationAsync(
        HttpContext context, string sessionId,
        CancellationToken cancellationToken)
    {
        var key = GetKey(sessionId);
        var json = await _redis.GetDatabase().StringGetAsync(key);

        if (json.IsNullOrEmpty)
        {
            Log.Debug("Redis: No MCP init params found for {Key}, migration denied", key);
            return null;
        }

        // Refresh TTL on successful migration — makes this a true sliding window.
        // Without this, a session initialized at hour 0 could expire at hour 24
        // even if actively migrating between instances.
        await _redis.GetDatabase().KeyExpireAsync(key, _ttl);

        Log.Debug("Redis: Restored MCP init params from {Key} (TTL refreshed)", key);
        return JsonSerializer.Deserialize<InitializeRequestParams>(json.ToString());
    }

    internal static string GetKey(string sessionId) => $"mcpinit:{sessionId}";
}
