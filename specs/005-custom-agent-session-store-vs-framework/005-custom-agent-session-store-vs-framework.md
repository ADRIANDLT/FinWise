# 005 — Custom AgentSessionStore vs SDK Framework Store

**Status**: Analysis Complete — Pending Decision  
**Date**: 2026-03-02  
**Depends on**: [004 — Session Naming Convention](../004-session-conversation-refactoring/004-session-conversation-refactoring-analysis.md)

---

## 1. Question

FinWise manages the `AgentSession` lifecycle explicitly in `FinWiseWorkflowService.ProcessMessageAsync()` — manually calling serialize, deserialize, persist, and restore. The Microsoft Agent Framework ships `AgentSessionStore` (abstract class) and `AIHostAgent` (wrapper) that automate this lifecycle. Should we adopt the SDK's abstraction instead of our custom `IAgentSessionStore` interface?

---

## 2. What the SDK Provides

### `AgentSessionStore` (abstract class, `Microsoft.Agents.AI.Hosting`)

```csharp
public abstract class AgentSessionStore
{
    public abstract ValueTask SaveSessionAsync(
        AIAgent agent, string conversationId, AgentSession session, CancellationToken ct = default);

    public abstract ValueTask<AgentSession> GetSessionAsync(
        AIAgent agent, string conversationId, CancellationToken ct = default);
}
```

The SDK ships two implementations:
- **`InMemoryAgentSessionStore`** — `ConcurrentDictionary<string, JsonElement>`. Dev/test only.
- **`NoopAgentSessionStore`** — Always returns a new session (stateless scenarios).

No persistent implementations (Redis, CosmosDB, SQL) are shipped. The SDK docs recommend building your own.

### `AIHostAgent` (wrapper, `Microsoft.Agents.AI.Hosting`)

```csharp
public class AIHostAgent : DelegatingAIAgent
{
    public AIHostAgent(AIAgent innerAgent, AgentSessionStore sessionStore) { ... }

    public ValueTask<AgentSession> GetOrCreateSessionAsync(string conversationId, CancellationToken ct = default)
        => _sessionStore.GetSessionAsync(InnerAgent, conversationId, ct);

    public ValueTask SaveSessionAsync(string conversationId, AgentSession session, CancellationToken ct = default)
        => _sessionStore.SaveSessionAsync(InnerAgent, conversationId, session, ct);
}
```

`AIHostAgent` wraps **one agent** with automatic session persistence. It's wired up via the hosting builder:
```csharp
builder.AddAgent("MyAgent", agent)
       .WithSessionStore(myStore);       // or .WithInMemorySessionStore()
```

### SDK's `InMemoryAgentSessionStore` — Full Source

```csharp
public sealed class InMemoryAgentSessionStore : AgentSessionStore
{
    private readonly ConcurrentDictionary<string, JsonElement> _threads = new();

    public override async ValueTask SaveSessionAsync(AIAgent agent, string conversationId,
        AgentSession session, CancellationToken ct = default)
    {
        var key = GetKey(conversationId, agent.Id);
        _threads[key] = await agent.SerializeSessionAsync(session, cancellationToken: ct);
    }

    public override async ValueTask<AgentSession> GetSessionAsync(AIAgent agent, string conversationId,
        CancellationToken ct = default)
    {
        var key = GetKey(conversationId, agent.Id);
        JsonElement? content = _threads.TryGetValue(key, out var existing) ? existing : null;
        return content switch
        {
            null => await agent.CreateSessionAsync(ct),
            _ => await agent.DeserializeSessionAsync(content.Value, cancellationToken: ct),
        };
    }

    private static string GetKey(string conversationId, string agentId) => $"{agentId}:{conversationId}";
}
```

---

## 3. What FinWise Has Today

### `IAgentSessionStore` (our custom interface)

```csharp
public interface IAgentSessionStore
{
    Task<AgentSessionData?> GetSessionDataAsync(string agentSessionId);
    Task SetSessionDataAsync(string agentSessionId, AgentSessionData data);
    Task ClearSessionAsync(string agentSessionId);
}
```

### `AgentSessionData` (our metadata wrapper)

```csharp
public record AgentSessionData
{
    public required string AgentSessionId { get; init; }
    public required string UserId { get; init; }
    public required JsonElement SerializedSession { get; init; }
    public int MessageCount { get; init; }
    public DateTime LastMessageAt { get; init; }
    public DateTime CreatedAt { get; init; }
}
```

### `AgentSessionManager` (our orchestration class)

Wraps `IAgentSessionStore` and adds:
- `GetOrCreateSessionAsync(agent, agentSessionId)` — deserializes or creates
- `PersistSessionAsync(agentSessionId, session, agent, userId, messageCount)` — serializes + wraps in `AgentSessionData`
- `ClearSessionAsync(agentSessionId)` — delegates to store

### The Manual Lifecycle in `FinWiseWorkflowService.ProcessMessageAsync()`

```
1. Create agents + workflow
2. _sessionManager.GetOrCreateSessionAsync(agent, agentSessionId)    ← RESTORE
3. Extract message history from session
4. Check AgentSessionResetEvaluator.ShouldResetSession()             ← RESET DETECTION
5. If reset: clear old session, create new agentSessionId, rebuild
6. Augment email if standalone
7. Push AgentSessionRunContext scope
8. Execute workflow (StreamAsync)
9. AppendUniqueMessages (dedup)
10. Validate orchestrator didn't leak text
11. Update message store in session
12. _sessionManager.PersistSessionAsync(...)                         ← SAVE
13. Return WorkflowResponse
```

---

## 4. Why Can't We Just Use `AIHostAgent`?

`AIHostAgent` automates: load session → run agent → save session. But it has three blockers for FinWise:

| Blocker | Detail |
|---------|--------|
| **Wraps one agent, not a workflow** | `AIHostAgent` wraps `AIAgent`. We run an `AgentWorkflow` (handoff across 3 agents sharing one session). The SDK hosting pipeline has no equivalent for workflows. |
| **No mid-lifecycle interception** | Reset detection (step 4) must happen *after* restoring the session but *before* running the workflow. `AIHostAgent` doesn't support hooks between load and run. |
| **No session deletion** | `AgentSessionStore` has `Save` and `Get` but no `Clear`/`Delete`. We need explicit deletion for reset flows. |

**Conclusion**: `AIHostAgent` is not usable for FinWise's multi-agent workflow pattern. The manual lifecycle management in `ProcessMessageAsync` is **necessary** for steps 4–10.

---

## 5. Could We Adopt the SDK's `AgentSessionStore` Abstract Class?

**Yes.** The question isn't about `AIHostAgent` — it's about whether our storage contract should be `AgentSessionStore` (SDK abstract class) instead of `IAgentSessionStore` (our interface). We'd still call it manually from `AgentSessionManager`, just with the SDK's contract.

### What Changes

| Component | Current | Proposed |
|-----------|---------|----------|
| **Storage contract** | `IAgentSessionStore` (our interface) | `AgentSessionStore` (SDK abstract class) |
| **In-memory implementation** | `InMemoryAgentSessionStore : IAgentSessionStore` | `InMemoryAgentSessionStore : AgentSessionStore` (or use the SDK's built-in one directly) |
| **AgentSessionManager** | Calls `IAgentSessionStore`, handles serialize/deserialize | Calls `AgentSessionStore`, which handles serialize/deserialize internally |
| **AgentSessionData** | Custom record with metadata | **Eliminated** — the SDK store deals in `AgentSession` objects directly |
| **Package dependency** | `Microsoft.Agents.AI` only | Adds `Microsoft.Agents.AI.Hosting` |

### Side-by-Side: `AgentSessionManager` With Each Approach

**Current (our `IAgentSessionStore`)**:
```csharp
public async Task<AgentSession> GetOrCreateSessionAsync(AIAgent agent, string agentSessionId)
{
    var sessionData = await _sessionStore.GetSessionDataAsync(agentSessionId);
    if (sessionData == null)
        return await agent.CreateSessionAsync();
    return await agent.DeserializeSessionAsync(sessionData.SerializedSession);
}

public async Task PersistSessionAsync(string agentSessionId, AgentSession session,
    AIAgent agent, string userId, int messageCount)
{
    JsonElement serialized = await agent.SerializeSessionAsync(session);
    var data = new AgentSessionData { AgentSessionId = agentSessionId, UserId = userId,
        SerializedSession = serialized, MessageCount = messageCount, LastMessageAt = DateTime.UtcNow };
    await _sessionStore.SetSessionDataAsync(agentSessionId, data);
}
```

**Proposed (SDK's `AgentSessionStore`)**:
```csharp
public async Task<AgentSession> GetOrCreateSessionAsync(AIAgent agent, string agentSessionId)
{
    return await _sessionStore.GetSessionAsync(agent, agentSessionId);
    // SDK handles: check if exists → deserialize or create new
}

public async Task PersistSessionAsync(string agentSessionId, AgentSession session, AIAgent agent)
{
    await _sessionStore.SaveSessionAsync(agent, agentSessionId, session);
    // SDK handles: serialize + store
}
```

The serialize/deserialize logic **moves into the store** instead of being in the manager. `AgentSessionManager` becomes significantly simpler.

---

## 6. What We'd Lose

### 6.1 Metadata Fields

`AgentSessionData` carries `UserId`, `MessageCount`, `LastMessageAt`, `CreatedAt`. The SDK's contract stores only the serialized `JsonElement` — it has no concept of metadata.

**Mitigation options**:

| Field | Alternative |
|-------|------------|
| `UserId` | Already extracted from `PROFILE_READY` marker via `AgentSessionConstants.ExtractUserIdFromMessageHistory()`. Not needed in the store — it's derived data. |
| `MessageCount` | Available from `InMemoryChatHistoryProvider.Count` after deserialization. Used only for debug logging. |
| `LastMessageAt` | Can use `AgentSession.StateBag` (SDK feature, recently added) to store timestamps inside the session itself. |
| `CreatedAt` | Same — `StateBag` or logging. |

**Assessment**: None of the metadata fields are critical for correctness. They're diagnostic. We can move them to `AgentSession.StateBag` or structured logging without losing functionality.

### 6.2 `ClearSessionAsync`

The SDK's `AgentSessionStore` has no `Clear`/`Delete` method.

**Mitigation**: We could either:
- **(a)** Add a `ClearSessionAsync` method to our subclass (it's our implementation — we can add methods beyond the abstract contract)
- **(b)** For the in-memory store: just let the old key become orphaned (new `agentSessionId` means a different key). For Redis: set a TTL so orphaned sessions expire.
- **(c)** Define a small supplementary interface `IAgentSessionCleaner` with just `ClearSessionAsync`, implement it on our store classes alongside `AgentSessionStore`.

Option (a) is the simplest — our `InMemoryAgentSessionStore` implementation can have both `AgentSessionStore` base methods plus a `ClearSessionAsync` method that `AgentSessionManager` calls directly.

### 6.3 New Package Dependency

Adding `Microsoft.Agents.AI.Hosting` to `FinWise.MultiAgentWorkflow.csproj`. This package is lightweight (~20 types: `AIHostAgent`, `AgentSessionStore`, `InMemoryAgentSessionStore`, hosting builder extensions). It does NOT pull in ASP.NET Core or any web framework — it's a pure .NET library.

**Assessment**: Acceptable. The package is already published by the same team that makes `Microsoft.Agents.AI`. Size is minimal.

---

## 7. What We'd Gain

| Benefit | Detail |
|---------|--------|
| **Simpler `AgentSessionManager`** | ~50% fewer lines. No manual serialize/deserialize. Store handles it. |
| **Eliminate `AgentSessionData`** | Custom record with 6 properties → gone. The SDK store deals in `AgentSession` objects. |
| **SDK alignment** | Future Redis/CosmosDB implementations by Microsoft or community will target `AgentSessionStore`, not our interface. Drop-in swappable. |
| **Composite key by default** | SDK keys by `{agentId}:{conversationId}` — automatically namespaces per agent, preventing key collisions in shared stores. |
| **Path to `AIHostAgent`** | If the SDK ever supports workflow hosting (not just single-agent), our store already implements the right contract. |
| **Less code to maintain** | Delete `IAgentSessionStore` interface, `AgentSessionData` record, and serialize/deserialize logic in `AgentSessionManager`. |

---

## 8. Persistent Storage Options

### The Question: Redis vs What?

The SDK ships **zero persistent `AgentSessionStore` implementations** for .NET. The SDK's `InMemoryAgentSessionStore` source code comments say:
> *"For production use with multiple instances or persistence across restarts, use a durable storage implementation such as Redis, SQL Server, or Azure Cosmos DB."*

You write your own by subclassing `AgentSessionStore`. Here's what that looks like for each option:

### Option A: Redis (Recommended for v0.3)

```csharp
public sealed class RedisAgentSessionStore : AgentSessionStore
{
    private readonly IConnectionMultiplexer _redis;
    private readonly TimeSpan _ttl;

    public RedisAgentSessionStore(IConnectionMultiplexer redis, TimeSpan? ttl = null)
    {
        _redis = redis;
        _ttl = ttl ?? TimeSpan.FromHours(24);
    }

    public override async ValueTask SaveSessionAsync(AIAgent agent, string conversationId,
        AgentSession session, CancellationToken ct = default)
    {
        var key = $"{agent.Id}:{conversationId}";
        var json = await agent.SerializeSessionAsync(session, cancellationToken: ct);
        await _redis.GetDatabase().StringSetAsync(key, json.GetRawText(), _ttl);
    }

    public override async ValueTask<AgentSession> GetSessionAsync(AIAgent agent, string conversationId,
        CancellationToken ct = default)
    {
        var key = $"{agent.Id}:{conversationId}";
        var json = await _redis.GetDatabase().StringGetAsync(key);
        if (json.IsNullOrEmpty)
            return await agent.CreateSessionAsync(ct);
        return await agent.DeserializeSessionAsync(
            JsonSerializer.Deserialize<JsonElement>(json!), cancellationToken: ct);
    }

    // Supplementary: not part of SDK contract, called directly by AgentSessionManager
    public async Task ClearSessionAsync(string agentId, string conversationId)
    {
        var key = $"{agentId}:{conversationId}";
        await _redis.GetDatabase().KeyDeleteAsync(key);
    }
}
```

**Pros**: Fast (sub-millisecond reads), natural TTL, lightweight, session blobs are typically small (10–50 KB). Perfect for session state.
**Cons**: Not queryable (can't search "all sessions for user X"). Data lost if Redis restarts without persistence configured.

### Option B: Azure Cosmos DB

**Pros**: Durable, globally distributed, queryable (can find sessions by user).
**Cons**: Higher latency than Redis (5–15ms vs <1ms). Over-engineered for "store a JSON blob by key." Costs more. Already used for user profiles — mixing session blobs adds schema complexity.

**Verdict**: Save CosmosDB for v0.4+ when you need per-message querying, vector search, and RAG.

### Option C: SQL / PostgreSQL

**Pros**: Relational queries, transactions.
**Cons**: Session blobs stored as NVARCHAR(MAX)/JSONB columns — not what relational DBs are optimized for. Schema migrations for a key-value use case is overhead.

**Verdict**: Makes sense for v0.3 if you're already running PostgreSQL in Docker Compose and don't want to add another container for Redis. But Redis is a better fit for ephemeral session state.

### Storage Recommendation by Version

| Version | Storage | Rationale |
|---------|---------|-----------|
| **v0.2** | In-memory (`InMemoryAgentSessionStore` — SDK's or ours) | No infrastructure needed. Dev-only. |
| **v0.3** | Redis (`RedisAgentSessionStore`) | Docker Compose adds a Redis container. Fast, TTL-based, natural fit for session blobs. |
| **v0.4** | Redis for sessions + CosmosDB for chat history (via `ChatHistoryProvider`) | Sessions stay in Redis. Individual messages go to CosmosDB for RAG/vector search. |

---

## 9. Implementation Plan

### Phase 1: Adopt SDK `AgentSessionStore` Abstract Class (v0.2 — small refactor)

**Goal**: Replace our `IAgentSessionStore` interface with the SDK's `AgentSessionStore` abstract class while keeping in-memory storage.

#### Files Changed

| File | Change |
|------|--------|
| `FinWise.MultiAgentWorkflow.csproj` | Add `<PackageReference Include="Microsoft.Agents.AI.Hosting" />` |
| `Directory.Packages.props` | Add version for `Microsoft.Agents.AI.Hosting` |
| `IAgentSessionStore.cs` | **Delete** — replaced by SDK's `AgentSessionStore` |
| `AgentSessionData` record | **Delete** — no longer needed (store handles serialization) |
| `InMemoryAgentSessionStore.cs` | **Delete** — use SDK's `InMemoryAgentSessionStore` directly. Or rewrite as custom subclass of `AgentSessionStore` with `ClearSession`. |
| `OrchestratorAgentFactory.cs` | **Add stable `Id`** — set `Id = "orchestrator_agent"` in `ChatClientAgentOptions`. Critical for SDK store's composite key `{agentId}:{conversationId}`. |
| `AgentSessionManager.cs` | Simplify: remove serialize/deserialize logic, delegate to `AgentSessionStore`. Keep `ClearSessionAsync` (no-op for SDK's in-memory, or calls custom store). Remove `userId`/`messageCount` params from `PersistSessionAsync`. |
| `FinWiseWorkflowService.cs` | Remove `userId` and `messageCount` from `PersistSessionAsync` call. Move diagnostic metadata to logging or `StateBag`. |
| `Program.cs` | Change `IAgentSessionStore sessionStore = new InMemoryAgentSessionStore()` to use SDK's `InMemoryAgentSessionStore` from `Microsoft.Agents.AI.Hosting`. |
| `InMemoryAgentSessionStoreTests.cs` | **Delete or simplify** — if using SDK's built-in store, no custom tests needed. If using our subclass, update tests. |

**Estimated scope**: ~100 lines changed across 6–8 files. No behavioral change.

#### Decision: Use SDK's Built-In `InMemoryAgentSessionStore` or Write Our Own?

**Recommendation: Write our own**, inheriting SDK's `AgentSessionStore`. There are two reasons:

**Reason 1 — No `ClearSessionAsync`**: The SDK's `InMemoryAgentSessionStore` is `sealed` (can't be extended) and has no `Clear`/`Delete` method. Its internal `_threads` dictionary is `private`. For in-memory this is survivable (orphaned keys are harmless and garbage-collected on restart), but for Redis we'd need explicit deletion.

**Reason 2 (CRITICAL) — Composite key uses `agent.Id`, which is random**: The SDK's store keys by `{agent.Id}:{conversationId}`. In `ChatClientAgent`, the `Id` property returns `ChatClientAgentOptions.Id` if set, otherwise falls back to `Guid.NewGuid().ToString("N")` — a **random GUID generated at construction time**.

FinWise's agent factories currently set `Name` (`"orchestrator_agent"`) but do NOT set `Id`. Since `CreateAgentsAndWorkflow` creates **new agent instances on every request**, each request would generate a new random agent ID, producing a different composite key, and the store would never find the previously saved session.

**Could we fix this by setting `Id` on the agent?** Yes — but it changes the agent factory code and introduces a coupling: the orchestrator's agent `Id` must be stable and deterministic across requests to match the store's composite key.

```csharp
// Required fix if using SDK's InMemoryAgentSessionStore:
ChatClientAgent orchestrator = new(_chatClient, new ChatClientAgentOptions
{
    Id = "orchestrator_agent",   // ← MUST be stable, not random
    Name = "orchestrator_agent",
    ChatOptions = new() { Instructions = prompt }
});
```

**Could we use the SDK's `InMemoryAgentSessionStore` directly with this fix?** Technically yes, but with trade-offs:

| Aspect | SDK's `InMemoryAgentSessionStore` directly | Our own `InMemoryAgentSessionStore : AgentSessionStore` |
|--------|-------------------------------------------|--------------------------------------------------------|
| `ClearSessionAsync` | Not available. Orphaned keys stay in memory. Acceptable for dev — old `agentSessionId` keys are never retrieved after reset. | Available. Explicit cleanup. |
| Agent `Id` requirement | **All agents must have stable, deterministic `Id` values.** Requires changing agent factories. | Can use any keying strategy — our implementation controls `GetKey()`. |
| Code to write | Zero — use the class directly from NuGet. | ~35 lines (nearly identical to SDK's implementation + `ClearSession` method). |
| Test coverage | SDK-tested. No unit tests needed. | Need our own tests (already have them). |
| Sealed | `sealed` — can't extend for future needs. | Ours — can add methods as needed. |

**Viable approach: Use the SDK's `InMemoryAgentSessionStore` directly for v0.2** by:
1. Setting stable `Id` on the orchestrator agent (e.g., `Id = "orchestrator_agent"`)
2. Accepting the lack of `ClearSessionAsync` (orphaned keys are fine for in-memory dev)
3. For Redis (v0.3), write `RedisAgentSessionStore : AgentSessionStore` with both the SDK contract and `ClearSessionAsync`

This is the **minimal-change path** — no custom in-memory store code, full SDK alignment from day one. The only code change is adding `Id` to the orchestrator agent factory. The "orphaned keys on reset" behavior is invisible to the user and costs negligible memory during development.

**Alternative approach: Write our own `InMemoryAgentSessionStore : AgentSessionStore`** — identical to the SDK's implementation but with `ClearSession` added and flexibility to change the keying strategy. This is the **safer path** if you want session deletion working from day one.

**Updated recommendation**: Use the SDK's `InMemoryAgentSessionStore` directly for v0.2 (simplest, zero custom store code) and write a custom `RedisAgentSessionStore` for v0.3. If the lack of `ClearSessionAsync` proves to be a problem during v0.2 development (e.g., memory grows noticeably during test sessions), fall back to writing our own in-memory implementation.

Either way, the agent factory must set a **stable `Id`** on the orchestrator agent to make the SDK's composite key work correctly.

```csharp
public sealed class InMemoryAgentSessionStore : AgentSessionStore
{
    private readonly ConcurrentDictionary<string, JsonElement> _sessions = new();

    public override async ValueTask SaveSessionAsync(AIAgent agent, string conversationId,
        AgentSession session, CancellationToken ct = default)
    {
        var key = GetKey(conversationId, agent.Id);
        _sessions[key] = await agent.SerializeSessionAsync(session, cancellationToken: ct);
    }

    public override async ValueTask<AgentSession> GetSessionAsync(AIAgent agent, string conversationId,
        CancellationToken ct = default)
    {
        var key = GetKey(conversationId, agent.Id);
        JsonElement? content = _sessions.TryGetValue(key, out var existing) ? existing : null;
        return content switch
        {
            null => await agent.CreateSessionAsync(ct),
            _ => await agent.DeserializeSessionAsync(content.Value, cancellationToken: ct),
        };
    }

    /// <summary>
    /// Clears a session. Not part of SDK's AgentSessionStore contract —
    /// needed for FinWise's explicit reset flows.
    /// </summary>
    public void ClearSession(string conversationId, string agentId)
    {
        _sessions.TryRemove(GetKey(conversationId, agentId), out _);
    }

    private static string GetKey(string conversationId, string agentId) => $"{agentId}:{conversationId}";
}
```

### Phase 2: Add Redis Implementation (v0.3 — when adding Docker)

Add `RedisAgentSessionStore : AgentSessionStore` as shown in Section 8, Option A. Drop-in replacement — `AgentSessionManager` and `FinWiseWorkflowService` don't change at all.

---

## 10. Risk Assessment

| Risk | Severity | Mitigation |
|------|----------|------------|
| `Microsoft.Agents.AI.Hosting` package is still in preview | Medium | Pin version. The `AgentSessionStore` abstract class is stable (simple 2-method contract). Breaking changes are unlikely for such a minimal API surface. |
| **Agent `Id` must be stable and deterministic** | **High** | SDK's store keys by `{agentId}:{conversationId}`. If agent `Id` is random (default), sessions are lost between requests. **Fix**: Set `Id = "orchestrator_agent"` in `OrchestratorAgentFactory`. This is a single-line change but is critical for correctness. |
| Losing `UserId` metadata on session | Low | Already extracted from `PROFILE_READY` marker at runtime. Not needed in storage. |
| Losing `MessageCount` / `LastMessageAt` | Low | Diagnostic only. Available from `InMemoryChatHistoryProvider.Count` after deserialization. Can use `StateBag` or logging. |
| SDK changes `AgentSessionStore` contract | Low | It's an abstract class with 2 methods. The rename from `AgentThread` → `AgentSession` already happened. Further changes are unlikely before GA. |
| `ClearSession` pattern (not in SDK) | Low | For in-memory: orphaned keys are harmless (new `agentSessionId` means the old key is never read). For Redis: add `ClearSession` on our custom implementation. |

---

## 11. Recommendation

**Adopt the SDK's `AgentSessionStore` abstract class.** The benefits (simpler code, SDK alignment, drop-in persistent stores) outweigh the costs (losing metadata fields that are diagnostic-only, adding one lightweight package dependency).

Implement in Phase 1 as a small v0.2 refactor. The behavioral semantics remain identical — only the storage contract changes.

---

## 12. What This Does NOT Change

For clarity, this refactoring does **not** affect:

- **`FinWiseWorkflowService.ProcessMessageAsync()`** — still manages the full lifecycle (restore → reset check → run workflow → persist). The SDK's `AIHostAgent` is not adopted.
- **Multi-agent workflow pattern** — still uses `AgentWorkflowBuilder` handoffs. No hosting pipeline.
- **`AgentSessionManager`** — still exists, still called by `FinWiseWorkflowService`. Just delegates to `AgentSessionStore` instead of `IAgentSessionStore`.
- **`AgentSessionResetEvaluator`** — unchanged.
- **`AgentSessionRunContext`** / **`AgentSessionRunSnapshot`** — unchanged.
- **`McpSessionMapping`** — unchanged (MCP transport layer, unrelated).
- **Naming convention** — `agentSessionId` parameter name stays (SDK uses `conversationId` but we documented why we diverge in [004](../004-session-conversation-refactoring/004-session-conversation-refactoring-analysis.md)).

---

## 13. The One Concept You Need: How Agent Memory Actually Works

> **TL;DR**: `AgentSessionStore` and `ChatHistoryProvider` are NOT two separate paths. They are two layers in the **same stack**. `ChatHistoryProvider` lives **inside** `AgentSession`. `AgentSessionStore` persists the **entire** `AgentSession` (which contains the `ChatHistoryProvider`'s state). You always need `AgentSessionStore`. `ChatHistoryProvider` is the inner piece that controls *how* messages are managed within a session.

### 13.1 The Suitcase Analogy

Think of it like packing a suitcase for a trip:

```
AgentSession = the suitcase
  ├── ChatHistoryProvider's messages = the clothes inside
  ├── StateBag = the toiletry bag inside
  └── other state = the shoes inside

AgentSessionStore = the hotel safe where you lock the suitcase between outings
```

- **`ChatHistoryProvider`** decides what goes IN the suitcase (which messages to keep, where to physically put them)
- **`AgentSessionStore`** decides WHERE to put the suitcase (in-memory, Redis, file)

They're not alternatives. They're layers:

```
                WHERE is the suitcase stored?
                ┌────────────────────────────┐
                │     AgentSessionStore       │  ← Stores the whole session
                │     (Redis / In-memory)     │     (the "outer container")
                └────────────┬───────────────┘
                             │
                             │ contains
                             ▼
                WHAT is inside the suitcase?
                ┌────────────────────────────┐
                │     AgentSession            │
                │  ┌──────────────────────┐  │
                │  │ ChatHistoryProvider   │  │  ← Manages the messages
                │  │  (InMemory / Cosmos)  │  │     (the "inner contents")
                │  └──────────────────────┘  │
                │  ┌──────────────────────┐  │
                │  │ StateBag             │  │  ← Arbitrary state data
                │  └──────────────────────┘  │
                └────────────────────────────┘
```

### 13.2 The Default Behavior (What FinWise Uses Today)

With the default `InMemoryChatHistoryProvider`:

```
1. User message arrives
2. AgentSessionStore loads the session blob from Redis/memory
3. Session blob contains EVERYTHING — messages are inside it
4. Agent runs (InMemoryChatHistoryProvider reads messages from session's memory)
5. New messages added to the in-memory list
6. AgentSessionStore saves the session blob back (now includes new messages)

Session blob = { StateBag: { InMemoryChatHistoryProvider: { messages: [...all 50 messages...] } } }
               ↑ The blob GROWS with every message
```

The messages live **inside** the session blob. When you save the session, the messages come along. When you load the session, the messages come back. Simple.

**This is fine for v0.2–v0.3.** The blob is typically 10–50KB for normal conversations. Redis handles this easily.

### 13.3 What Changes With a Custom ChatHistoryProvider (v0.4+)

If you swap `InMemoryChatHistoryProvider` for `CosmosDbChatHistoryProvider`:

```
1. User message arrives
2. AgentSessionStore loads the session blob from Redis — now it's TINY
3. Session blob only contains a reference key: { CosmosDbChatHistory: { Key: "abc123" } }
4. ChatHistoryProvider.InvokingAsync() fires → queries CosmosDB for messages by key "abc123"
5. Agent runs with those messages
6. ChatHistoryProvider.InvokedAsync() fires → saves new messages to CosmosDB
7. AgentSessionStore saves the session blob back (still tiny — just the reference key)

Session blob = { StateBag: { CosmosDbChatHistory: { Key: "abc123" } } }
               ↑ The blob stays SMALL regardless of conversation length
               
Messages = in CosmosDB, queryable, individually indexed
```

The messages moved **outside** the suitcase into a separate storage. The suitcase now just has a claim ticket.

### 13.4 When Do You Need Which?

| Scenario | What You Need | Why |
|----------|--------------|-----|
| **v0.2–v0.3**: All agents in-process, sessions persist across requests | `AgentSessionStore` (in-memory → Redis) | Messages live inside the session blob. Default `InMemoryChatHistoryProvider` handles everything. Simple. |
| **v0.3**: Session blobs get too large (>100KB) | `AgentSessionStore` + custom `ChatHistoryProvider` | Swap the inner provider to store messages externally (CosmosDB). Session blob stays small. |
| **v0.4**: Add remote agents (A2A / Foundry) | `AgentSessionStore` for the orchestrator. Remote agents handle their own. | Remote agents are opaque — you call `RunAsync`, they manage their own memory internally. You don't need to change YOUR storage. |
| **v0.4+**: Need to query past messages (analytics, RAG) | `AgentSessionStore` + custom `ChatHistoryProvider` → CosmosDB | Messages in CosmosDB are queryable. Session blobs in Redis are not. |
| **v0.5**: All agents distributed | Each agent has its own `AgentSessionStore` + `ChatHistoryProvider` | Each container manages its own storage independently. |

### 13.5 The Answer: What Should FinWise Use for A2A / Foundry?

Here's the simple answer for each deployment model:

#### All agents in-process (v0.2–v0.3)

```
You need: AgentSessionStore → Redis
You don't need: Custom ChatHistoryProvider (the default InMemory one is fine)

Why: One process, one session, one blob. Put the blob in Redis. Done.
```

#### Some agents remote via A2A or Azure AI Foundry (v0.4)

```
You need: AgentSessionStore → Redis (for the orchestrator's session)
You don't need: Anything new for the remote agents

Why: Remote agents are black boxes. The orchestrator sends them messages via A2A
and gets responses. It doesn't manage their sessions. Azure AI Foundry manages
server-side conversation threads automatically.

Your code:
  // Local agent — unchanged
  ChatClientAgent profileAgent = profileFactory.CreateAgent();

  // Remote agent — just swap the factory
  A2AClient a2aClient = new(new Uri("https://stock-agent.azurecontainerapps.io/a2a"));
  AIAgent stockAgent = a2aClient.AsAIAgent();

  // Same workflow, no storage changes
  AgentWorkflow workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(orchestrator)
      .WithHandoffs(orchestrator, [profileAgent, advisorAgent, stockAgent])
      .Build();
```

The orchestrator's `AgentSessionStore` → Redis stores the orchestrator's session (which includes the conversation history with all agents, including remote ones' responses). Remote agents don't share this session — they have their own.

#### Want queryable message history (v0.4+, optional)

```
You need: AgentSessionStore → Redis AND a custom ChatHistoryProvider → CosmosDB

Why: You want to search messages ("find all sessions discussing crypto"),
do RAG over conversation history, or audit messages for compliance.
AgentSessionStore stores the session. ChatHistoryProvider stores individual
messages in a queryable backend.
```

#### All agents distributed (v0.5)

```
Each agent container has:
  - Its own AgentSessionStore (or uses Azure AI Foundry server-side)
  - Its own ChatHistoryProvider (if it needs queryable history)

The orchestrator is a thin router with minimal session state.
```

### 13.6 Decision Tree

```
Do you need to persist sessions across HTTP requests?
  │
  YES (always, from v0.2)
  │
  └─► Use AgentSessionStore (in-memory → Redis → CosmosDB)
      │
      Do you also need to query individual messages?
      (analytics, RAG, vector search, audit trail)
        │
        ├─ NO (v0.2–v0.3) → Default InMemoryChatHistoryProvider is fine.
        │                     Messages live inside the session blob. Done.
        │
        └─ YES (v0.4+) → Add a custom ChatHistoryProvider that stores
                          messages externally (CosmosDB, VectorStore).
                          Session blob stays small (just a reference key).

      Are some agents remote (A2A / Foundry)?
        │
        ├─ NO (v0.2–v0.3) → Nothing to do. All in-process.
        │
        └─ YES (v0.4+) → Remote agents manage their own sessions.
                          Change the agent factory to return A2AAgent.
                          No changes to YOUR AgentSessionStore or
                          ChatHistoryProvider. The orchestrator keeps
                          its existing setup.
```

### 13.7 Summary Table: What to Use and When

| Version | `AgentSessionStore` | `ChatHistoryProvider` | Remote Agent Storage |
|---------|--------------------|-----------------------|---------------------|
| **v0.2** | In-memory (SDK's built-in) | InMemory (default, inside session blob) | N/A — all local |
| **v0.3** | Redis (custom impl) | InMemory (default, inside session blob) | N/A — all local |
| **v0.4** | Redis (same) | InMemory (default) OR CosmosDB (if you need queryable history) | Azure AI Foundry server-side (automatic) or agent's own store |
| **v0.5** | Redis per container | CosmosDB (shared, queryable) | Each agent has its own |

### 13.8 The Key Insight

**`AgentSessionStore` is always needed.** It's like asking "do I need a database?" — yes, you need somewhere to put the session between requests.

**`ChatHistoryProvider` is an optimization.** The default (`InMemoryChatHistoryProvider`) puts messages inside the session blob. A custom one puts messages somewhere else (CosmosDB) and leaves only a tiny reference in the session blob. You swap the inner provider when the default becomes insufficient (blob too large, need queries, need vector search).

**Remote agents (A2A / Foundry) don't affect YOUR storage architecture.** They're black boxes that manage their own memory. You just call them and get responses. The only thing you store in YOUR session is the conversation flow (messages sent and received — including remote agents' responses).

---

## 14. Deep Dive: ChatHistoryProvider Internals and When to Migrate

### 14.1 What `ChatHistoryProvider` Actually Does

`ChatHistoryProvider` is middleware that runs inside every `agent.RunAsync()` call:

```
  agent.RunAsync(messages, session)
        │
        ▼
  ┌──────────────────────────────────────────────┐
  │  ChatHistoryProvider.InvokingAsync()          │  ← BEFORE: load past messages
  │    Returns: past messages + new messages       │     from wherever they're stored
  ├──────────────────────────────────────────────┤
  │  LLM call (with full message list)             │
  ├──────────────────────────────────────────────┤
  │  ChatHistoryProvider.InvokedAsync()           │  ← AFTER: save new messages
  │    Receives: input messages + response msgs    │     to wherever they go
  └──────────────────────────────────────────────┘
```

The two key override points:

```csharp
public abstract class ChatHistoryProvider
{
    // Override to load messages from your storage
    protected virtual ValueTask<IEnumerable<ChatMessage>> ProvideChatHistoryAsync(
        InvokingContext context, CancellationToken ct = default);

    // Override to save new messages to your storage
    protected virtual ValueTask StoreChatHistoryAsync(
        InvokedContext context, CancellationToken ct = default);

    // Provider state is stored in AgentSession.StateBag under this key
    public virtual string StateKey => this.GetType().Name;
}
```

### 14.2 The Default: `InMemoryChatHistoryProvider`

This is what FinWise uses today (implicitly). It stores messages in `AgentSession.StateBag`:

```csharp
// SDK's InMemoryChatHistoryProvider stores messages in StateBag:
public sealed class InMemoryChatHistoryProvider : ChatHistoryProvider
{
    // ProvideChatHistoryAsync → reads messages from session.StateBag["InMemoryChatHistoryProvider"]
    // StoreChatHistoryAsync  → appends messages to session.StateBag["InMemoryChatHistoryProvider"]
}
```

When you serialize the session (`AgentSessionStore.SaveSessionAsync`), the StateBag (including all messages) gets serialized as JSON. That's why the session blob contains the entire conversation.

**In other words**: `InMemoryChatHistoryProvider` is why messages end up inside the `AgentSessionStore` blob. Replace it, and messages go elsewhere.

### 14.3 FinWise's Current Manual Message Management

FinWise currently **bypasses** the `ChatHistoryProvider` pipeline to some extent:

```csharp
// FinWise reads messages directly from the provider instance:
var messageStore = currentSession.GetService<InMemoryChatHistoryProvider>();
List<ChatMessage> messageHistory = messageStore?.ToList() ?? [];

// Adds user message manually:
messageHistory.Add(new ChatMessage(ChatRole.User, userMessage));

// Runs the workflow with the full list:
await ExecuteWorkflowAsync(workflow, messageHistory);

// Writes everything back manually:
store.Clear();
foreach (var msg in messageHistory) { store.Add(msg); }
```

This works because `InMemoryChatHistoryProvider` is just an in-memory list — reading/writing it directly is fine. But if you replaced it with a `CosmosDbChatHistoryProvider`, you couldn't just call `.ToList()` on it — the messages would be in CosmosDB, and the provider's `InvokingAsync`/`InvokedAsync` hooks handle the database calls.

**This is why migrating to a custom `ChatHistoryProvider` isn't trivial** — FinWise's `ProcessMessageAsync` would need to stop manually managing the message list and let the provider pipeline handle it.

### 14.4 Scenarios That Trigger Migration to Custom ChatHistoryProvider

| Scenario | When | What to Do |
|----------|------|------------|
| **Everything works fine, conversations are short** | v0.2–v0.3 | Do nothing. Default `InMemoryChatHistoryProvider` inside session blob. |
| **Token window limits** (conversations too long for LLM) | v0.2+ | Add `ChatReducer` to `InMemoryChatHistoryProvider` — it trims old messages automatically. No provider change needed. |
| **Session blobs too large** (>100KB, Redis latency) | v0.3 | Replace `InMemoryChatHistoryProvider` with a custom provider that stores messages externally. Session blob becomes tiny. |
| **Need to search past messages** (analytics, compliance) | v0.4 | Add a custom `ChatHistoryProvider` → CosmosDB. Messages become individually queryable. |
| **Need RAG over conversation history** | v0.4+ | Use `VectorChatHistoryProvider` → CosmosDB with vector embeddings. |
| **Adding A2A / Foundry remote agents** | v0.4 | **No ChatHistoryProvider change needed.** Remote agents manage their own memory. Your orchestrator keeps using whatever it already has. |
| **Fully distributed agents** | v0.5 | Each agent container configures its own `ChatHistoryProvider`. The orchestrator's session blob becomes minimal. |

### 14.5 Why Custom ChatHistoryProvider Conflicts With Current Code

FinWise's `ProcessMessageAsync` does custom logic **between** load and save:
1. Load messages from session (manual)
2. **Check for reset triggers** (custom business logic)
3. **Augment email** (custom business logic)
4. Run workflow
5. **Deduplicate messages** (custom business logic)
6. Save messages back (manual)

A custom `ChatHistoryProvider` fires load/save **inside** each agent's `RunAsync()` — you can't inject steps 2, 3, 5 between the provider's load and the LLM call. Adopting it requires restructuring `ProcessMessageAsync` to move that logic elsewhere.

**This is a v0.5 concern, not v0.2–v0.4.** For now, the manual approach works.

### 14.6 Practical Path Forward

| Phase | Action | ChatHistoryProvider | AgentSessionStore |
|-------|--------|--------------------|--------------------|
| **Phase 1 (v0.2)** | Adopt SDK's `AgentSessionStore` | Keep default `InMemoryChatHistoryProvider` (no change) | SDK's `InMemoryAgentSessionStore` or custom subclass |
| **Phase 2 (v0.3)** | Add Redis storage | Keep default `InMemoryChatHistoryProvider` (no change) | `RedisAgentSessionStore` |
| **Phase 3 (v0.4)** | Add remote agents | No change for local agents. Remote agents are opaque. | Same Redis store for orchestrator |
| **Phase 3b (v0.4, if needed)** | Add queryable history | Add `CosmosDbChatHistoryProvider` alongside existing setup | Same Redis store |
| **Phase 4 (v0.5)** | Full distribution | Migrate to `ChatHistoryProvider` → CosmosDB as primary. Refactor `ProcessMessageAsync`. | Per-container stores |

---

## 15. Definitive Answer: Do You Need `ChatHistoryProvider` for Docker / A2A / Foundry?

**No. You don't need a custom `ChatHistoryProvider` for Docker containers, A2A remote agents, or Azure AI Foundry agents. The only trigger is wanting queryable individual messages.**

This is verified from the actual SDK source code (February 2026):

### Source Code Evidence

**`A2AAgent.cs`** ([raw source](https://github.com/microsoft/agent-framework/blob/main/dotnet/src/Microsoft.Agents.AI.A2A/A2AAgent.cs)):
- The string `ChatHistoryProvider` does NOT appear anywhere in the file
- `RunCoreAsync()` does: convert messages → `_a2aClient.SendMessageAsync()` → return response
- Zero interaction with any message storage. The remote agent handles its own memory.

**`ChatClientAgent.cs`** ([raw source](https://github.com/microsoft/agent-framework/blob/main/dotnet/src/Microsoft.Agents.AI/ChatClient/ChatClientAgent.cs)):
- `ChatHistoryProvider` appears 20+ times — it's deeply integrated
- The constructor sets: `this.ChatHistoryProvider = options?.ChatHistoryProvider ?? new InMemoryChatHistoryProvider()`
- `RunCoreAsync()` calls `chatHistoryProvider.InvokingAsync()` before the LLM and `InvokedAsync()` after

**Conclusion**: `ChatHistoryProvider` is a `ChatClientAgent`-only feature. `A2AAgent` doesn't use it at all.

### The Simple Rule

```
┌────────────────────────────────────────────────────────────────────┐
│                                                                     │
│  Do you need to QUERY individual messages from past conversations?  │
│  (search, analytics, RAG, vector search, audit/compliance)          │
│                                                                     │
│          NO ──────────────────► Don't touch ChatHistoryProvider.    │
│          │                       Default InMemory is fine.          │
│          │                       Works for Docker, A2A, Foundry,   │
│          │                       and all deployment models.         │
│          │                                                          │
│          YES ─────────────────► Add custom ChatHistoryProvider      │
│                                  → CosmosDB or VectorStore.         │
│                                  This is a BUSINESS FEATURE         │
│                                  decision, not an infrastructure    │
│                                  requirement.                       │
│                                                                     │
└────────────────────────────────────────────────────────────────────┘
```

### Why Docker Doesn't Change Anything

Docker is a deployment mechanism. Your agents still run in-process inside one container. The session is still one blob stored in `AgentSessionStore` → Redis. Messages are still inside the blob via `InMemoryChatHistoryProvider`. Moving to Docker changes WHERE Redis runs (from localhost to a container), not HOW sessions work.

### Why A2A Remote Agents Don't Change Anything

From the SDK source, `A2AAgent.RunCoreAsync()` does this:
```csharp
// That's literally ALL it does — convert and send HTTP
MessageSendParams sendParams = new()
{
    Message = CreateA2AMessage(typedSession, messages),
};
a2aResponse = await this._a2aClient.SendMessageAsync(sendParams, cancellationToken);
```

The remote agent is a black box. Your orchestrator sends messages, gets responses, and stores everything in its own session blob. The remote agent manages its own memory server-side. You never interact with the remote agent's storage.

### Why Azure AI Foundry Doesn't Change Anything

Foundry agents manage conversation threads server-side. When you use `CreateSessionAsync(conversationId)`, the service stores all messages. You don't need `ChatHistoryProvider` OR `AgentSessionStore` for the Foundry-hosted agents. Your orchestrator still needs its own `AgentSessionStore` → Redis for its local session, but that's the same as v0.2.

### Final Summary

| Change | Requires custom `ChatHistoryProvider`? |
|--------|---------------------------------------|
| Move to Docker | **No** |
| Add Redis for session storage | **No** |
| Add A2A remote agents | **No** |
| Add Azure AI Foundry agents | **No** |
| Scale to multiple orchestrator replicas | **No** (Redis handles shared state) |
| Want to search past messages | **Yes** |
| Want RAG over conversation history | **Yes** |
| Want message-level audit trail | **Yes** |
| Want vector search on messages | **Yes** |
