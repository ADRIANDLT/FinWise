# 007 — Redis AgentSessionStore Implementation Plan

**Status**: Ready for Implementation  
**Date**: 2026-03-21  
**Depends on**: [006 — Custom AgentSessionStore vs SDK Framework Store](006-custom-agent-session-store-vs-framework/006-custom-agent-session-store-vs-framework.md) (Phase 1 completed)  
**Version target**: v0.3.2

---

## 1. Summary

Add a `RedisAgentSessionStore : AgentSessionStore` implementation that persists agent sessions to a Redis container, enabling session durability across application restarts. This replaces the SDK's `InMemoryAgentSessionStore` (used since Phase 1 of spec 006) with a drop-in Redis-backed implementation — zero changes to `AgentSessionManager`, `FinWiseWorkflowService`, or any workflow code.

The approach mirrors the existing CosmosDB infrastructure pattern used for `IUserProfileStore`, including:
- Docker container via `docker-compose.yml`
- Options class for configuration (`RedisOptions`)
- Toggle in `appsettings.json` (`Redis:Enabled`)
- Fallback to in-memory when disabled
- Setup documentation in `docs/REDIS-SETUP.md`

---

## 2. Research Summary

### 2.1 Microsoft Agent Framework — `AgentSessionStore` Contract

**Package version in repo**: `Microsoft.Agents.AI.Hosting` `1.0.0-preview.260311.1`  
**Latest available version**: `1.0.0-preview.260225.1` (docs reference) / `1.0.0-preview.260311.1` (NuGet, used in repo)  
**Stability**: Preview (contract stable — 2 methods, unchanged since initial release)  
**Confidence level**: High

The SDK's `AgentSessionStore` abstract class (verified against [GitHub source](https://github.com/microsoft/agent-framework/blob/main/dotnet/src/Microsoft.Agents.AI.Hosting/AgentSessionStore.cs) and [Microsoft Learn docs](https://learn.microsoft.com/dotnet/api/microsoft.agents.ai.hosting.agentsessionstore?view=agent-framework-dotnet-latest)) defines exactly two methods:

```csharp
public abstract class AgentSessionStore
{
    public abstract ValueTask SaveSessionAsync(
        AIAgent agent, string conversationId, AgentSession session,
        CancellationToken cancellationToken = default);

    public abstract ValueTask<AgentSession> GetSessionAsync(
        AIAgent agent, string conversationId,
        CancellationToken cancellationToken = default);
}
```

**Key design facts** (from SDK source and docs):
- `SaveSessionAsync` must call `agent.SerializeSessionAsync(session)` to get a `JsonElement` before storing.
- `GetSessionAsync` must call `agent.DeserializeSessionAsync(jsonElement)` when restoring, or `agent.CreateSessionAsync()` for new sessions.
- The SDK's `InMemoryAgentSessionStore` keys by `{agentId}:{conversationId}` — our Redis implementation must use the same composite key pattern.
- `AgentSession` is treated as an opaque state object. The store handles only serialization + persistence.
- The SDK has **no `DeleteAsync` / `ClearAsync` method** on the abstract class. Our implementation adds a supplementary `ClearSessionAsync` method directly on the concrete class (not part of the SDK contract).

**Version-sensitive warning**: `Microsoft.Agents.AI.Hosting` is still in Preview (`1.0.0-preview.260311.1`). The `AgentSessionStore` contract is minimal and stable, but pin the version explicitly.

### 2.2 StackExchange.Redis — .NET Redis Client

**Latest version**: `2.12.4` (released March 2026, MIT license)  
**Target frameworks**: .NET 6.0+, .NET Standard 2.0, .NET Framework 4.6.1+  
**Stability**: GA — mature, production-grade, 926M+ total NuGet downloads  
**Source**: [github.com/StackExchange/StackExchange.Redis](https://github.com/StackExchange/StackExchange.Redis/)

**Key API surface used**:
- `IConnectionMultiplexer` — singleton connection interface
- `ConnectionMultiplexer.ConnectAsync(string)` — create connection
- `IDatabase.StringSetAsync(RedisKey, RedisValue, TimeSpan?)` — store with TTL
- `IDatabase.StringGetAsync(RedisKey)` — retrieve
- `IDatabase.KeyDeleteAsync(RedisKey)` — delete (for session clear)

**Best practices from Microsoft and StackExchange docs**:
1. **Singleton `ConnectionMultiplexer`** — use one long-lived instance, never create per-request.
2. **`AbortOnConnectFail = false`** — let it reconnect automatically.
3. **Smaller values preferred** — Redis works best under 100KB per key. Session blobs are typically 10–50KB.
4. **`connectTimeout` ≥ 5000ms** — give time to (re)establish connections.
5. **`IConnectionMultiplexer` interface** for testability (mock in unit tests).

### 2.3 Redis Docker Container

**Image**: `redis:7.4-alpine` (BSD-3-Clause license; v7.4.x is still under the dual RSALv2/SSPLv1 license — not the newer v8 tri-license)  
**Why 7.4 and not 8.x**: Redis 8.0+ uses a tri-license (RSALv2/SSPLv1/AGPLv3). For a development emulator container the license doesn't matter, but 7.4 is the most widely deployed stable branch and avoids any license review questions. Alpine variant is ~15MB vs ~50MB for Debian.

**Default behavior**:
- Port `6379` (TCP)
- No authentication by default (protected mode off in Docker)
- No persistence by default — data lost on restart unless configured
- `/data` volume for persistence when `--save` is used

**Persistence configuration** (for dev — we want data to survive container restarts):
- `--save 60 1` — snapshot every 60 seconds if at least 1 write occurred
- Volume mount to `/data` for RDB file persistence

**Health check**: `redis-cli ping` returns `PONG`.

> **⚠️ Authentication**: The local Docker Redis container runs without authentication (`--requirepass` is not set). This is intentional for local development convenience. For production deployments (e.g., Azure Cache for Redis), authentication must be enabled — Azure Cache for Redis enforces access keys or Microsoft Entra ID authentication by default.

### 2.4 Comparison with CosmosDB Pattern in Codebase

The existing `CosmosDbUserProfileStore` provides the infrastructure pattern to replicate:

| Aspect | CosmosDB (existing) | Redis (new) |
|--------|---------------------|-------------|
| **Docker image** | `mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest` | `redis:7.4-alpine` |
| **Port** | 8081 (+ 10250–10254) | 6379 |
| **Options class** | `CosmosDbOptions` with `Enabled`, `Endpoint`, `Key`, etc. | `RedisOptions` with `Enabled`, `ConnectionString`, `SessionTtlMinutes`, etc. |
| **Config section** | `"CosmosDb"` in `appsettings.json` | `"Redis"` in `appsettings.json` |
| **Toggle** | `CosmosDb:Enabled` (bool) | `Redis:Enabled` (bool) |
| **Fallback** | `InMemoryUserProfileStore` | SDK's `InMemoryAgentSessionStore` |
| **Interface** | `IUserProfileStore` | `AgentSessionStore` (SDK abstract class) |
| **Store class** | `CosmosDbUserProfileStore` | `RedisAgentSessionStore` |
| **Client type** | `CosmosClient` (singleton) | `IConnectionMultiplexer` (singleton) |
| **Initialization** | Lazy `GetContainerAsync()` with `SemaphoreSlim` | Eager connect in `Program.cs` |
| **Cleanup** | `IAsyncDisposable` for `CosmosClient` | `IAsyncDisposable` for `ConnectionMultiplexer` |
| **Docs** | `docs/COSMOSDB-SETUP.md` | `docs/REDIS-SETUP.md` |

---

## 3. Architecture

### 3.1 Module Diagram

```
FinWise.McpServer (Program.cs)
  │
  ├── if Redis:Enabled == true
  │     ConnectionMultiplexer.ConnectAsync(connectionString)
  │     └── RedisAgentSessionStore(multiplexer, ttl)
  │
  ├── else
  │     └── InMemoryAgentSessionStore()  (SDK built-in)
  │
  └── AgentSessionStore sessionStore → FinWiseWorkflowService(... sessionStore ...)
```

### 3.2 Data Flow

```
SaveSessionAsync:
  1. Build key: "{agentId}:{conversationId}"
  2. Serialize: JsonElement json = await agent.SerializeSessionAsync(session)
  3. Store: await redis.StringSetAsync(key, json.GetRawText(), ttl)

GetSessionAsync:
  1. Build key: "{agentId}:{conversationId}"
  2. Read: RedisValue json = await redis.StringGetAsync(key)
  3a. If null/empty → return await agent.CreateSessionAsync()
  3b. If exists → JsonElement element = JsonSerializer.Deserialize<JsonElement>(json!)
                   return await agent.DeserializeSessionAsync(element)

ClearSessionAsync (supplementary, not part of SDK contract):
  1. Build key: "{agentId}:{conversationId}"
  2. Delete: await redis.KeyDeleteAsync(key)
```

### 3.3 Key Format

Following the SDK's `InMemoryAgentSessionStore` convention:

```
{agentId}:{conversationId}

Example: orchestrator_agent:a1b2c3d4-5678-90ab-cdef-1234567890ab
```

The `agentId` comes from the agent's `Id` property (set to `"orchestrator_agent"` in `OrchestratorAgentFactory` since Phase 1 of spec 006). The `conversationId` is the `agentSessionId` passed by `FinWiseWorkflowService`.

### 3.4 TTL Strategy

| Environment | TTL | Rationale |
|-------------|-----|-----------|
| Development | 24 hours (1440 min) | Long enough for day-long dev sessions, short enough to avoid clutter |
| Production (future) | 1–4 hours | Conversations are typically brief; stale sessions consume memory |

TTL is configured via `RedisOptions.SessionTtlMinutes` (default: 1440). Applied on every `SaveSessionAsync` call — effectively a sliding expiration that resets with each interaction.

### 3.5 Session Blob Size

Typical session blobs contain:
- Message history (10–100 messages): 5–50KB
- StateBag metadata: <1KB
- Total: **10–50KB per session**

Redis handles this easily. Redis best practices consider values under 100KB "small." No compression needed.

---

## 4. Implementation

### 4.1 `RedisOptions` — Configuration Class

**Location**: `src/FinWise.MultiAgentWorkflow/Infrastructure/AgentSessionStore/Redis/RedisOptions.cs`

```csharp
namespace FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStore.Redis;

/// <summary>
/// Configuration options for Redis-backed session storage.
/// Bind to the "Redis" section in appsettings.json.
/// </summary>
public class RedisOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string SectionName = "Redis";

    /// <summary>
    /// Whether to use Redis for session storage.
    /// When false, uses in-memory storage (SDK's InMemoryAgentSessionStore).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Redis connection string.
    /// For local Docker: "localhost:6379"
    /// For Azure Cache for Redis: "{name}.redis.cache.windows.net:6380,password={key},ssl=True,abortConnect=False"
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Time-to-live for session entries in minutes.
    /// Applied on every save (sliding expiration).
    /// Default: 1440 (24 hours).
    /// </summary>
    public int SessionTtlMinutes { get; set; } = 1440;
}
```

### 4.2 `RedisAgentSessionStore` — Store Implementation

**Location**: `src/FinWise.MultiAgentWorkflow/Infrastructure/AgentSessionStore/Redis/RedisAgentSessionStore.cs`

```csharp
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Serilog;
using StackExchange.Redis;

namespace FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStore.Redis;

/// <summary>
/// Redis-backed implementation of <see cref="AgentSessionStore"/> for durable session persistence.
/// Uses StackExchange.Redis with TTL-based expiration.
///
/// Key format: {agentId}:{conversationId} (matches SDK's InMemoryAgentSessionStore convention).
///
/// For production use with multiple instances or persistence across restarts.
/// Falls back to SDK's InMemoryAgentSessionStore when Redis is disabled.
/// </summary>
public sealed class RedisAgentSessionStore : AgentSessionStore, IAsyncDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _ttl;

    public RedisAgentSessionStore(IConnectionMultiplexer redis, TimeSpan ttl)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _ttl = ttl;
    }

    /// <inheritdoc/>
    public override async ValueTask SaveSessionAsync(
        AIAgent agent, string conversationId, AgentSession session,
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(agent.Id, conversationId);
        var json = await agent.SerializeSessionAsync(session, cancellationToken: cancellationToken);
        await _redis.GetDatabase().StringSetAsync(key, json.GetRawText(), _ttl);

        Log.Debug("Redis: Saved session {Key} (TTL: {Ttl})", key, _ttl);
    }

    /// <inheritdoc/>
    public override async ValueTask<AgentSession> GetSessionAsync(
        AIAgent agent, string conversationId,
        CancellationToken cancellationToken = default)
    {
        var key = GetKey(agent.Id, conversationId);
        var json = await _redis.GetDatabase().StringGetAsync(key);

        if (json.IsNullOrEmpty)
        {
            Log.Debug("Redis: No session found for {Key}, creating new", key);
            return await agent.CreateSessionAsync(cancellationToken);
        }

        Log.Debug("Redis: Restored session from {Key}", key);
        var element = JsonSerializer.Deserialize<JsonElement>(json!);
        return await agent.DeserializeSessionAsync(element, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Deletes a session from Redis. Not part of the SDK's <see cref="AgentSessionStore"/> contract,
    /// but needed by <c>AgentSessionManager.ClearSessionAsync</c> for explicit session resets.
    /// </summary>
    /// <param name="agentId">The agent's stable identifier.</param>
    /// <param name="conversationId">The agent session identifier to delete.</param>
    public async Task ClearSessionAsync(string agentId, string conversationId)
    {
        var key = GetKey(agentId, conversationId);
        await _redis.GetDatabase().KeyDeleteAsync(key);
        Log.Debug("Redis: Deleted session {Key}", key);
    }

    /// <summary>
    /// Disposes the Redis connection multiplexer.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _redis.Dispose();
        return ValueTask.CompletedTask;
    }

    private static string GetKey(string agentId, string conversationId) => $"{agentId}:{conversationId}";
}
```

### 4.3 `AgentSessionManager` — Update for Redis Clear

The current `AgentSessionManager.ClearSessionAsync` is a no-op (appropriate for in-memory). With Redis, it should forward to the store's delete when a `RedisAgentSessionStore` is in use.

**Current code** (no-op):
```csharp
public Task ClearSessionAsync(string agentSessionId)
{
    Log.Debug("ClearSessionAsync called for {AgentSessionId} (no-op for in-memory store)", agentSessionId);
    return Task.CompletedTask;
}
```

**Updated code**:
```csharp
/// <summary>
/// Clears an agent session. For InMemoryAgentSessionStore this is a no-op — orphaned keys
/// are harmless. For RedisAgentSessionStore, performs an explicit key delete with TTL as safety net.
/// </summary>
public async Task ClearSessionAsync(string agentSessionId, AIAgent agent)
{
    if (_sessionStore is RedisAgentSessionStore redisStore)
    {
        await redisStore.ClearSessionAsync(agent.Id, agentSessionId);
        Log.Debug("Cleared Redis session for {AgentSessionId}", agentSessionId);
    }
    else
    {
        Log.Debug("ClearSessionAsync called for {AgentSessionId} (no-op for in-memory store)", agentSessionId);
    }
}
```

> **Note**: The `AIAgent` parameter is needed to obtain the stable `agent.Id` for the composite key. Callers (`FinWiseWorkflowService.ProcessMessageAsync` and `ResetSessionAsync`) already have the orchestrator agent in scope.

### 4.4 Docker Compose — Add Redis Service

Add the Redis service alongside the existing CosmosDB emulator in `docker-compose.yml`:

```yaml
  redis:
    image: redis:7.4-alpine
    container_name: finwise-redis
    ports:
      - "6379:6379"
    command: redis-server --save 60 1 --loglevel warning
    volumes:
      - redis-data:/data
    mem_limit: 256m
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5
      start_period: 5s
```

Add the volume to the `volumes:` section:

```yaml
volumes:
  cosmosdb-data:
    driver: local
  redis-data:
    driver: local
```

### 4.5 Configuration — `appsettings.json`

Add the `Redis` section alongside the existing `CosmosDb` section:

```json
{
  "Redis": {
    "Enabled": true,
    "ConnectionString": "localhost:6379",
    "SessionTtlMinutes": 1440
  }
}
```

### 4.6 Package References

**`Directory.Packages.props`** — Add:
```xml
<PackageVersion Include="StackExchange.Redis" Version="2.12.4" />
```

**`FinWise.MultiAgentWorkflow.csproj`** — Add:
```xml
<PackageReference Include="StackExchange.Redis" />
```

### 4.7 Composition Root — `Program.cs`

Replace the current session store initialization:

```csharp
// Current:
AgentSessionStore sessionStore = new InMemoryAgentSessionStore();
```

With Redis-aware initialization (pattern mirrors the CosmosDB toggle):

```csharp
// Configure Redis options
var redisOptions = new RedisOptions();
configuration.GetSection(RedisOptions.SectionName).Bind(redisOptions);

// Create session store based on configuration
AgentSessionStore sessionStore;
if (redisOptions.Enabled)
{
    Log.Information("Using Redis session store (Connection: {Connection}, TTL: {Ttl} min)",
        redisOptions.ConnectionString, redisOptions.SessionTtlMinutes);

    var redis = await ConnectionMultiplexer.ConnectAsync(redisOptions.ConnectionString);
    sessionStore = new RedisAgentSessionStore(redis, TimeSpan.FromMinutes(redisOptions.SessionTtlMinutes));
}
else
{
    Log.Information("Using in-memory session store");
    sessionStore = new InMemoryAgentSessionStore();
}
```

---

## 5. File Changes Summary

| # | File | Change Type | Description |
|---|------|-------------|-------------|
| 1 | `Directory.Packages.props` | **Edit** | Add `<PackageVersion Include="StackExchange.Redis" Version="2.12.4" />` |
| 2 | `FinWise.MultiAgentWorkflow.csproj` | **Edit** | Add `<PackageReference Include="StackExchange.Redis" />` |
| 3 | `Infrastructure/AgentSessionStore/Redis/RedisOptions.cs` | **New** | Configuration options class |
| 4 | `Infrastructure/AgentSessionStore/Redis/RedisAgentSessionStore.cs` | **New** | Redis implementation of `AgentSessionStore` |
| 5 | `Session/AgentSessionManager.cs` | **Edit** | Update `ClearSessionAsync` to forward to Redis store when available |
| 6 | `Workflow/FinWiseWorkflowService.cs` | **Edit** | Pass `orchestratorAgent` to `ClearSessionAsync` calls (2 call sites) |
| 7 | `docker-compose.yml` | **Edit** | Add `redis` service and `redis-data` volume |
| 8 | `src/FinWise.McpServer/appsettings.json` | **Edit** | Add `Redis` configuration section |
| 9 | `src/FinWise.McpServer/Program.cs` | **Edit** | Add Redis toggle logic (mirrors CosmosDB pattern) |
| 10 | `docs/REDIS-SETUP.md` | **New** | Setup documentation (mirrors COSMOSDB-SETUP.md) |

**Estimated scope**: ~120 lines of new code (2 new files + edits). No behavioral change when `Redis:Enabled = false`.

---

## 6. Implementation Step Order

Apply in this order to keep the build green between steps. Run `dotnet build FinWise.slnx` after each group.

| Step | Files | Why This Order |
|------|-------|----------------|
| **1** | `Directory.Packages.props`, `FinWise.MultiAgentWorkflow.csproj` | Add package references first — everything depends on them. |
| **2** | `RedisOptions.cs` | Configuration class, no dependencies on other changes. Build validates. |
| **3** | `RedisAgentSessionStore.cs` | Core implementation. Depends on package ref (step 1) and options (step 2). Build validates. |
| **4** | `AgentSessionManager.cs`, `FinWiseWorkflowService.cs` | Update `ClearSessionAsync` signature and call sites. Build validates. |
| **5** | `Program.cs`, `appsettings.json` | Wiring. Build validates. Functional with docker (if running). |
| **6** | `docker-compose.yml` | Infrastructure. No build impact. `docker compose up -d` to test. |
| **7** | `docs/REDIS-SETUP.md` | Documentation. No build impact. |
| **8** | Unit tests for `RedisAgentSessionStore` | Verify serialization round-trip and key format. |

---

## 7. Docker Compose — Full Updated File

```yaml
# Docker Compose for FinWise infrastructure (dev)
# - Azure CosmosDB Emulator (Linux) for user profiles
# - Redis for agent session persistence

services:
  cosmosdb-emulator:
    image: mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest
    container_name: cosmosdb-emulator
    hostname: cosmosdb-emulator
    ports:
      - "8081:8081"
      - "10250:10250"
      - "10251:10251"
      - "10252:10252"
      - "10253:10253"
      - "10254:10254"
    environment:
      - AZURE_COSMOS_EMULATOR_PARTITION_COUNT=10
      - AZURE_COSMOS_EMULATOR_ENABLE_DATA_PERSISTENCE=true
      - AZURE_COSMOS_EMULATOR_IP_ADDRESS_OVERRIDE=127.0.0.1
    volumes:
      - cosmosdb-data:/tmp/cosmos/appdata
    mem_limit: 3g
    cpu_count: 2
    healthcheck:
      test: [ "CMD-SHELL", "curl -f -k https://localhost:8081/_explorer/emulator.pem || exit 1" ]
      interval: 30s
      timeout: 10s
      retries: 5
      start_period: 60s

  redis:
    image: redis:7.4-alpine
    container_name: finwise-redis
    ports:
      - "6379:6379"
    command: redis-server --save 60 1 --loglevel warning
    volumes:
      - redis-data:/data
    mem_limit: 256m
    healthcheck:
      test: ["CMD", "redis-cli", "ping"]
      interval: 10s
      timeout: 5s
      retries: 5
      start_period: 5s

volumes:
  cosmosdb-data:
    driver: local
  redis-data:
    driver: local

# Usage:
#   Start all:   docker compose up -d
#   Start Redis only: docker compose up -d redis
#   Stop:    docker compose down
#   Logs:    docker compose logs -f redis
#   Status:  docker compose ps
#
# Redis Connection Details:
#   Host: localhost
#   Port: 6379
#   Connection String: localhost:6379
#
# CosmosDB Connection Details:
#   Endpoint: https://localhost:8081/
#   Key: C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==
#
# Data Explorer: https://localhost:8081/_explorer/index.html
#
# Redis CLI:
#   docker exec -it finwise-redis redis-cli
#   > KEYS *              (list all keys)
#   > GET key             (get value)
#   > TTL key             (check TTL in seconds)
#   > DBSIZE              (count keys)
#   > FLUSHDB             (clear all keys)
```

---

## 8. Testing Strategy

### 8.1 Unit Tests for `RedisAgentSessionStore`

**Location**: `tests/FinWise.MultiAgentWorkflow.UnitTests/Infrastructure/AgentSessionStore/Redis/`

Using `Moq` to mock `IConnectionMultiplexer` and `IDatabase`:

| Test | Validates |
|------|-----------|
| `SaveSessionAsync_StoresSerializedJsonWithTtl` | Calls `StringSetAsync` with correct key format, serialized JSON, and TTL |
| `GetSessionAsync_ReturnsDeserializedSession_WhenKeyExists` | Calls `StringGetAsync`, deserializes, returns restored session |
| `GetSessionAsync_CreatesNewSession_WhenKeyMissing` | Returns `agent.CreateSessionAsync()` when key has no value |
| `ClearSessionAsync_DeletesKey` | Calls `KeyDeleteAsync` with correct key format |
| `GetKey_FormatsAsAgentIdColonConversationId` | Verifies `"orchestrator_agent:abc123"` key format |

### 8.2 Integration Tests (Optional, With Docker)

**Location**: `tests/FinWise.McpServer.IntegrationTests/`

Test against a real Redis container:

| Test | Validates |
|------|-----------|
| `SaveAndGetSession_RoundTrips_WithRealRedis` | Full serialize → store → retrieve → deserialize cycle |
| `GetSession_ReturnsNewSession_WhenKeyNotInRedis` | Fresh key returns new session |
| `ClearSession_RemovesKey_FromRedis` | Delete removes the key, subsequent get creates new |
| `SessionExpires_AfterTtl` | Set a 2-second TTL, wait, verify key is gone |

Use `[SkippableFact]` (already in test dependencies) to skip when Redis is not running:

```csharp
[SkippableFact]
public async Task SaveAndGetSession_RoundTrips_WithRealRedis()
{
    Skip.IfNot(await IsRedisAvailable(), "Redis not available");
    // ...
}
```

### 8.3 Manual Smoke Test

1. Start Redis: `docker compose up -d redis`
2. Run app: `dotnet run --project src/FinWise.McpServer/ --urls http://localhost:5000`
3. Send messages via MCP client
4. Verify sessions in Redis: `docker exec -it finwise-redis redis-cli KEYS "*"`
5. Restart the app (`Ctrl+C` → re-run)
6. Resume conversation — messages should be restored from Redis
7. Trigger session reset (e.g., "start new session")
8. Verify old key is deleted: `docker exec -it finwise-redis redis-cli KEYS "*"`

---

## 9. Redis Setup Documentation

The full `docs/REDIS-SETUP.md` will follow the same structure as `docs/COSMOSDB-SETUP.md`:

```markdown
# Redis Setup Guide

This guide explains how to set up Redis for local development session storage.

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) installed and running
- Port 6379 available on localhost

## Quick Start

### 1. Start Redis

From the repository root, run:

    docker compose up -d redis

### 2. Verify Redis is Running

    docker compose ps
    docker exec -it finwise-redis redis-cli ping
    # Should return: PONG

### 3. Configure the Application

Redis is configured in `appsettings.json`:

    {
      "Redis": {
        "Enabled": true,
        "ConnectionString": "localhost:6379",
        "SessionTtlMinutes": 1440
      }
    }

To disable Redis and use in-memory storage, set `Enabled: false`.

### 4. Run the Application

    dotnet run --project src/FinWise.McpServer/ --urls http://localhost:5000

## Connection Details

| Property | Value |
|----------|-------|
| Host | `localhost` |
| Port | `6379` |
| Connection String | `localhost:6379` |

## Useful Redis CLI Commands

    docker exec -it finwise-redis redis-cli

    KEYS *              # List all session keys
    GET <key>           # Read a session blob
    TTL <key>           # Check remaining TTL (seconds)
    DBSIZE              # Count total keys
    FLUSHDB             # Clear all keys (careful!)
    INFO memory         # Memory usage stats

## Common Commands

    # Start Redis only
    docker compose up -d redis

    # Start all infrastructure (Redis + CosmosDB)
    docker compose up -d

    # Stop all
    docker compose down

    # View Redis logs
    docker compose logs -f redis

    # Restart Redis
    docker compose restart redis

    # Remove Redis and data
    docker compose down -v

## Troubleshooting

| Issue | Resolution |
|-------|------------|
| Connection refused on port 6379 | Ensure Docker is running and Redis container is up: `docker compose ps` |
| Data lost after restart | Check that the `redis-data` volume exists: `docker volume ls`. The `--save 60 1` flag enables RDB persistence. |
| "READONLY" errors | Container may be in a bad state. Restart: `docker compose restart redis` |
| Session not restored | Verify `Redis:Enabled` is `true` in config. Check logs for "Using Redis session store" at startup. |
```

---

## 10. Risk Assessment

| Risk | Severity | Mitigation |
|------|----------|------------|
| Redis container not running when app starts | Medium | `ConnectionMultiplexer.ConnectAsync` will throw on startup with a clear error message. Log includes connection string. Dev knows to run `docker compose up -d redis`. |
| Redis connection drops during operation | Low | StackExchange.Redis automatically reconnects (`AbortOnConnectFail = false` by default). Individual operations may fail transiently but recover. Session will be recreated on next `GetSessionAsync` if key is lost. |
| Session blob exceeds Redis value size limit (512MB) | Very Low | Session blobs are 10–50KB. Redis max value is 512MB. Not a concern. |
| `StackExchange.Redis` package version conflict | Low | Pin to `2.12.4` in `Directory.Packages.props`. No other dependency in the solution uses StackExchange.Redis. |
| `AgentSessionStore` contract changes in SDK GA | Low | 2-method abstract class. Minimal surface area. Pin `Microsoft.Agents.AI.Hosting` to `1.0.0-preview.260311.1`. |
| Orphaned keys consuming Redis memory | Low | TTL (24h default) ensures automatic cleanup. Session reset explicitly deletes keys. Redis `maxmemory-policy` can be configured for eviction if needed. |
| No authentication on dev Redis | Low | Dev-only container on localhost. For production/Azure, use connection strings with `password=`,`ssl=True`. The `RedisOptions.ConnectionString` supports full StackExchange.Redis connection strings. |
| `ClearSessionAsync` was a no-op in Phase 1 (in-memory) | Low | The SDK's `AgentSessionStore` has no `Delete`/`Clear` method. For in-memory, orphaned keys are harmless (callers always generate new GUIDs). This implementation adds explicit `KeyDeleteAsync` + TTL as safety net — resolving the Phase 1 gap identified during code review. |
| Triple session restore in `ProcessMessageAsync` causes 2-3 Redis round-trips per request | Medium | Pre-existing control flow: `ProcessMessageAsync` calls `GetOrCreateSessionAsync` up to 3 times (initial restore, post-reset restore, post-profile-gate restore with rebuilt agent). With in-memory this was negligible; with Redis each call is a network round-trip (sub-millisecond for Redis, but unnecessary). **Mitigation**: Track as a follow-up optimization — restructure `ProcessMessageAsync` to determine `isProfileReady` before creating agents, reducing to 1 restore per request. See Future Considerations. |

---

## 11. Folder Structure After Implementation

```
src/FinWise.MultiAgentWorkflow/
  ├── Infrastructure/
  │   ├── AgentSessionStore/
  │   │   └── Redis/
  │   │       ├── RedisOptions.cs
  │   │       └── RedisAgentSessionStore.cs
  │   └── UserProfileStores/
  │       ├── IUserProfileStore.cs
  │       ├── CosmosDb/
  │       │   ├── CosmosDbOptions.cs
  │       │   ├── CosmosDbUserProfileStore.cs
  │       │   └── UserProfileDocument.cs
  │       └── InMemory/
  │           └── InMemoryUserProfileStore.cs
  ├── Session/
  │   ├── AgentSessionManager.cs          ← updated ClearSessionAsync
  │   ├── AgentSessionConstants.cs
  │   ├── AgentSessionResetEvaluator.cs
  │   └── AgentSessionRunContext.cs
  └── Workflow/
      └── FinWiseWorkflowService.cs       ← updated ClearSessionAsync calls

tests/FinWise.MultiAgentWorkflow.UnitTests/
  └── Infrastructure/
      └── AgentSessionStore/
          └── Redis/
              └── RedisAgentSessionStoreTests.cs
```

> **Note on folder naming**: The folder is `AgentSessionStore/Redis/` (not `SessionStore/`). This follows the namespace convention: `FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStore.Redis`. The folder name `AgentSessionStore` matches the SDK type name without collision because it's a folder, not a class file.

---

## 12. What This Does NOT Change

- **`AgentSessionManager` logic** — still delegates to `AgentSessionStore`. Only `ClearSessionAsync` signature changes (adds `AIAgent` parameter).
- **`FinWiseWorkflowService.ProcessMessageAsync()` flow** — identical lifecycle (restore → reset check → workflow → persist). Only the 2 `ClearSessionAsync` call sites pass the agent.
- **Message access pattern** — still uses `TryGetInMemoryChatHistory()` / `SetInMemoryChatHistory()`.
- **Workflow, agents, handoffs** — no changes.
- **`McpSessionMapping`** — no changes (MCP transport layer).
- **`InMemoryAgentSessionStore`** — still used as fallback when `Redis:Enabled = false`.
- **User profile storage** — `IUserProfileStore` and CosmosDB are unrelated.

---

## 13. Future Considerations

| When | What | Why |
|------|------|-----|
| v0.4 (Docker deployment) | Update `Redis:ConnectionString` to `redis:6379` for inter-container networking | When the FinWise app itself runs in Docker, it connects to Redis via Docker network name, not `localhost` |
| v0.4+ | Add `redis.conf` file for production settings | Password authentication, maxmemory policy, persistence tuning |
| Backlog | Azure Cache for Redis | Drop-in replacement — change only `RedisOptions.ConnectionString` to the Azure connection string with TLS. Add `ssl=True,abortConnect=False` |
| Backlog | Connection resilience / `ForceReconnect` pattern | For long-running production deployments. See [Microsoft docs on connection resilience](https://learn.microsoft.com/azure/redis/best-practices-connection#using-forcereconnect-with-stackexchangeredis) |
| Backlog | Redis Sentinel / Cluster | High-availability scenarios. StackExchange.Redis supports both modes natively |
| v0.3.2 or v0.4 | **Optimize `ProcessMessageAsync` session restore** | Currently calls `GetOrCreateSessionAsync` up to 3 times per request (initial, post-reset, post-profile-gate rebuild). Restructure to: (1) read messages once from store, (2) determine `isProfileReady`, (3) create agents/workflow once. Reduces Redis round-trips from 2-3 to 1. Identified during Phase 1 code review (Critic finding #3). |
