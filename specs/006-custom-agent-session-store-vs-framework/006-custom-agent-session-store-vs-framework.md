# 006 — Custom AgentSessionStore vs SDK Framework Store

**Status**: Decision Confirmed — Use SDK's `InMemoryAgentSessionStore` Directly (March 21, 2026)  
**Date**: 2026-03-02 (updated 2026-03-21)  
**Depends on**: [004 — Session Naming Convention](../004-session-conversation-refactoring/004-session-conversation-refactoring-analysis.md)

> **Note (March 21, 2026):** This spec was validated against the latest SDK source at [microsoft/agent-framework](https://github.com/microsoft/agent-framework) and NuGet packages (`1.0.0-rc4`, published March 11, 2026). The `AgentSessionStore` contract is stable (2 methods, unchanged since the spec was written). `Microsoft.Agents.AI.Hosting` is at `1.0.0-preview.260311.1`, compatible with our `1.0.0-rc4` packages.
>
> **Decision (March 21, 2026):** Use the SDK's `InMemoryAgentSessionStore` directly — zero custom store code. The previous `SerializedMessages` workaround is eliminated by the SDK's new `TryGetInMemoryChatHistory()` / `SetInMemoryChatHistory()` extension methods (added to `Microsoft.Agents.AI.Abstractions` in RC4, committed Feb 26, 2026). These access `StateBag["InMemoryChatHistoryProvider"]` directly, bypassing the broken `GetService<InMemoryChatHistoryProvider>()` path. See [Journal 03 — The Ghost in the Session](../../journal/03-%20The%20Ghost%20in%20the%20Session.md) for the full story of the original workaround.

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

**Resolution**: Orphaned keys are harmless for in-memory dev. Both call sites (`ProcessMessageAsync` inline reset and `ResetSessionAsync` explicit reset) generate a new `agentSessionId` via `Guid.NewGuid()` immediately after clearing — the old key is never looked up again. The `McpSessionMapping` is updated to point to the new ID.

- **v0.3 (in-memory)**: `AgentSessionManager.ClearSessionAsync()` becomes a **no-op** (log + return). Orphaned entries in the `ConcurrentDictionary` cost negligible memory and are cleared on process restart.
- **v0.3.2 (Redis)**: Custom `RedisAgentSessionStore` adds `ClearSessionAsync` with `KeyDeleteAsync` + TTL as a safety net for orphaned keys.

### 6.3 New Package Dependency

Adding `Microsoft.Agents.AI.Hosting` (`1.0.0-preview.260311.1`) to `FinWise.MultiAgentWorkflow.csproj`. This package contains ~20 types (`AIHostAgent`, `AgentSessionStore`, `InMemoryAgentSessionStore`, hosting builder extensions). It does NOT pull in ASP.NET Core — but it does bring transitive dependencies including `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging.Console`, `Microsoft.Extensions.Configuration.*`, and `Microsoft.ML.Tokenizers`. These are all Microsoft packages already in our dependency graph transitively via the McpServer project.

**Assessment**: Acceptable. The dependency footprint is heavier than "lightweight" but these packages are already resolved at app level. The `AgentSessionStore` contract and `InMemoryAgentSessionStore` are the only types we consume. Compatible with our existing `1.0.0-rc4` packages (Hosting depends on `Microsoft.Agents.AI >= 1.0.0-rc4`).

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

### Option A: Redis (Recommended for v0.3.2)

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

**Verdict**: Makes sense if you're already running PostgreSQL and don't want to add another container for Redis. But Redis is a better fit for ephemeral session state.

### Storage Recommendation by Version

| Version | Storage | Rationale |
|---------|---------|-----------|
| **v0.3** | In-memory (SDK's `InMemoryAgentSessionStore` directly) | No infrastructure needed. Dev/test. |
| **v0.3.2** | Redis (`RedisAgentSessionStore`) | Durable sessions with TTL. |
| **v0.4+** | Redis for sessions (same as v0.3.2) | No session storage changes needed for Docker or additional agents. |
| **Backlog** | Redis + CosmosDB `ChatHistoryProvider` | **Potential future capability — not needed in current scope.** Would enable: (1) RAG over conversation history — feed the LLM with semantically relevant past messages via vector similarity search, improving personalization beyond what a single session blob provides; (2) fast full-text search across all sessions ("find conversations about crypto"); (3) analytics and compliance auditing at the individual message level. Redis stores session blobs as opaque JSON by key — you can retrieve session X but cannot search *across* sessions. CosmosDB with vector indexing would store each message individually with embeddings, enabling cross-session semantic retrieval. This is a business feature decision triggered by specific needs (personalization, compliance), not an infrastructure requirement. |

---

## 9. Implementation Plan

### Phase 1: Adopt SDK `AgentSessionStore` Abstract Class (v0.3 — small refactor)

**Goal**: Replace our `IAgentSessionStore` interface with the SDK's `AgentSessionStore` abstract class while keeping in-memory storage.

#### Files Changed

| File | Change |
|------|--------|
| `Directory.Packages.props` | Add `<PackageVersion Include="Microsoft.Agents.AI.Hosting" Version="1.0.0-preview.260311.1" />` |
| `FinWise.MultiAgentWorkflow.csproj` | Add `<PackageReference Include="Microsoft.Agents.AI.Hosting" />` |
| `IAgentSessionStore.cs` | **Delete** — replaced by SDK's `AgentSessionStore`. `AgentSessionData` record is also deleted (defined in same file). |
| `InMemoryAgentSessionStore.cs` | **Delete** — replaced by SDK's `InMemoryAgentSessionStore` from `Microsoft.Agents.AI.Hosting`. |
| `OrchestratorAgentFactory.cs` | Switch to `ChatClientAgentOptions` constructor with stable `Id = "orchestrator_agent"`. Critical for SDK store's composite key `{agentId}:{conversationId}`. |
| `AdvisorAgentFactory.cs` | Switch to `ChatClientAgentOptions` constructor with stable `Id = "advisor_agent"` for consistency across all factories. |
| `UserProfileAgentFactory.cs` | Switch to `ChatClientAgentOptions` constructor with stable `Id = "profile_agent"` for consistency. Tools passed via `ChatOptions.Tools`. |
| `AgentSessionManager.cs` | Major simplification: remove serialize/deserialize logic, remove `DeserializeMessages()`, remove `userId`/`messageCount` params. Use `session.TryGetInMemoryChatHistory()` / `session.SetInMemoryChatHistory()` for message access. `ClearSessionAsync` becomes a no-op (log + return). Constructor takes `AgentSessionStore` (SDK type) instead of `IAgentSessionStore`. |
| `FinWiseWorkflowService.cs` | Replace manual message restoration with `session.TryGetInMemoryChatHistory()`. Replace manual message write-back with `session.SetInMemoryChatHistory()`. Remove `userId` and `messageCount` from `PersistSessionAsync` call. Constructor takes `AgentSessionStore` instead of `IAgentSessionStore`. |
| `Program.cs` | Change `IAgentSessionStore sessionStore = new InMemoryAgentSessionStore()` to `AgentSessionStore sessionStore = new InMemoryAgentSessionStore()` (SDK types from `Microsoft.Agents.AI.Hosting`). Update `AgentSessionManager` constructor call. |
| `InMemoryAgentSessionStoreTests.cs` | **Delete** — SDK's built-in store is SDK-tested. No custom store code to test. |

**Estimated scope**: Delete ~150 lines of custom infrastructure. Add ~20 lines (package reference, 3 agent factory `ChatClientAgentOptions` constructors, `TryGetInMemoryChatHistory` calls). Net reduction ~130 lines across 10 files. No behavioral change.

#### Implementation Step Order

Apply in this order to keep the build green between steps. Run `dotnet build FinWise.slnx` after each group.

| Step | Files | Why this order |
|------|-------|----------------|
| 1 | `Directory.Packages.props`, `FinWise.MultiAgentWorkflow.csproj` | Add package refs first — everything else depends on them. |
| 2 | `OrchestratorAgentFactory.cs`, `AdvisorAgentFactory.cs`, `UserProfileAgentFactory.cs` | Stable agent `Id`s. No other file depends on this change, so it's safe early. Build validates constructor API. |
| 3 | `AgentSessionManager.cs` | Rewrite to use `AgentSessionStore` + `TryGetInMemoryChatHistory` / `SetInMemoryChatHistory`. Will break `FinWiseWorkflowService` until step 4. |
| 4 | `FinWiseWorkflowService.cs` | Update to match new `AgentSessionManager` signatures and message access pattern. Build should pass after this step. |
| 5 | `Program.cs` | Swap `IAgentSessionStore` → SDK `AgentSessionStore` type + `InMemoryAgentSessionStore`. Build + unit tests should pass. |
| 6 | Delete `IAgentSessionStore.cs`, `InMemoryAgentSessionStore.cs`, `InMemoryAgentSessionStoreTests.cs` | Remove dead code. Final build + full test run to confirm. |
| 7 | Update `AGENTS.md` files | Remove references to `IAgentSessionStore` / `Infrastructure/AgentSessionStore/` folder. Keep docs accurate. |

#### Decision: Use SDK's Built-In `InMemoryAgentSessionStore` Directly

**Confirmed (March 21, 2026): Use the SDK's `InMemoryAgentSessionStore` directly.** Zero custom store code. Three previous blockers are resolved:

**Blocker 1 — Messages lost after deserialization: RESOLVED.** The SDK added `TryGetInMemoryChatHistory()` and `SetInMemoryChatHistory()` extension methods to `AgentSessionExtensions` (committed Feb 26, 2026, included in `1.0.0-rc4`). These access `StateBag["InMemoryChatHistoryProvider"]` directly with typed deserialization — bypassing the broken `GetService<InMemoryChatHistoryProvider>()` path entirely. Messages survive the serialize → deserialize round trip through the `StateBag`. This eliminates our `SerializedMessages` workaround, the `DeserializeMessages()` helper, and most of `AgentSessionData`.

```csharp
// READ messages from a deserialized session (SDK extension method):
if (session.TryGetInMemoryChatHistory(out List<ChatMessage> messages))
{
    // messages contains the full conversation history from StateBag
}

// WRITE messages back into the session before saving:
session.SetInMemoryChatHistory(messages);
```

**Blocker 2 — No `ClearSessionAsync`: NOT A CORRECTNESS ISSUE.** Both call sites (`ProcessMessageAsync` inline reset and `ResetSessionAsync`) generate a new `agentSessionId` via `Guid.NewGuid()` immediately after clearing. The old key is never looked up again. Orphaned entries in the `ConcurrentDictionary` are negligible in dev and cleared on restart. `AgentSessionManager.ClearSessionAsync()` becomes a no-op that logs the event.

**Blocker 3 — Agent `Id` must be stable: ONE-LINE FIX.** The SDK keys by `{agentId}:{conversationId}`. The orchestrator factory must switch from the convenience constructor to `ChatClientAgentOptions` with a stable `Id`:

```csharp
// Before (Id falls back to random Guid per instance):
return new ChatClientAgent(_chatClient, Prompt, Name, Description);

// After (Id is stable across requests):
return new ChatClientAgent(_chatClient, new ChatClientAgentOptions
{
    Id = "orchestrator_agent",
    Name = "orchestrator_agent",
    Description = "Silent router - calls handoff functions only, never outputs text",
    ChatOptions = new() { Instructions = Prompt }
});
```

**Why not write our own?** The SDK's implementation is `sealed` with no `Clear`/`Delete`, but that's acceptable for v0.3. Writing a custom implementation adds ~35 lines of code that duplicates SDK logic for zero behavioral benefit — orphaned keys are never read. For v0.3.2 (Redis), we'll write `RedisAgentSessionStore : AgentSessionStore` with explicit `ClearSessionAsync` + TTL.

### Phase 2: Add Redis Implementation (v0.3.2)

Add `RedisAgentSessionStore : AgentSessionStore` as shown in Section 8, Option A. Drop-in replacement — `AgentSessionManager` and `FinWiseWorkflowService` don't change at all.

---

## 10. Risk Assessment

| Risk | Severity | Mitigation |
|------|----------|------------|
| `Microsoft.Agents.AI.Hosting` package is still in preview | Medium | Pin to `1.0.0-preview.260311.1`. The `AgentSessionStore` abstract class is stable (simple 2-method contract). Breaking changes are unlikely for such a minimal API surface. |
| **Agent `Id` must be stable and deterministic** | **High** | SDK's store keys by `{agentId}:{conversationId}`. If agent `Id` is random (default), sessions are lost between requests. **Fix**: Set `Id = "orchestrator_agent"` in `OrchestratorAgentFactory` via `ChatClientAgentOptions`. This is a constructor change but is critical for correctness. |
| `TryGetInMemoryChatHistory()` behavior changes | Low | These are thin wrappers over `StateBag` dictionary access. The `InMemoryChatHistoryProvider.State` class is a simple `{ Messages: List<ChatMessage> }` record. Unlikely to break. |
| Losing `UserId` metadata on session | Low | Already extracted from `PROFILE_READY` marker at runtime. Not needed in storage. |
| Losing `MessageCount` / `LastMessageAt` | Low | Diagnostic only. Available from message list `.Count` after `TryGetInMemoryChatHistory()`. Use structured logging. |
| SDK changes `AgentSessionStore` contract | Low | It's an abstract class with 2 methods. The rename from `AgentThread` → `AgentSession` already happened. Further changes are unlikely before GA. |
| `ClearSession` not in SDK | Low | For in-memory: orphaned keys are harmless (new `agentSessionId` means the old key is never read). `AgentSessionManager.ClearSessionAsync()` is a no-op. For Redis (v0.3.2): add explicit `ClearSessionAsync` on `RedisAgentSessionStore`. |
| Transitive dependency footprint of `Microsoft.Agents.AI.Hosting` | Low | Brings `Microsoft.Extensions.Hosting`, `DependencyInjection`, `Logging.Console`, `Configuration.*`, `ML.Tokenizers`. All already resolved transitively via McpServer. No new runtime behavior. |

---

## 11. Recommendation

**Use the SDK's `InMemoryAgentSessionStore` directly — zero custom store code.** All three previous blockers are resolved:

1. **Messages**: `TryGetInMemoryChatHistory()` / `SetInMemoryChatHistory()` (SDK RC4) replace our `SerializedMessages` workaround.
2. **ClearSession**: No-op for in-memory (orphaned keys are harmless). Explicit delete for Redis (v0.3.2).
3. **Agent Id**: Set `Id = "orchestrator_agent"` in `OrchestratorAgentFactory` via `ChatClientAgentOptions`.

The benefits (delete ~150 lines of custom infrastructure, full SDK alignment, drop-in persistent stores) far outweigh the costs (one package dependency, orphaned keys in dev). The behavioral semantics remain identical — only the storage contract and message access pattern change.

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

**This is fine for v0.3.** The blob is typically 10–50KB for normal conversations. Redis handles this easily.

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
| **v0.3**: All agents in-process, sessions persist across requests | `AgentSessionStore` (in-memory → Redis) | Messages live inside the session blob. Default `InMemoryChatHistoryProvider` handles everything. Simple. |
| **v0.3**: Session blobs get too large (>100KB) | `AgentSessionStore` + custom `ChatHistoryProvider` | Swap the inner provider to store messages externally (CosmosDB). Session blob stays small. |
| **v0.4**: Add remote agents (A2A / Foundry) | `AgentSessionStore` for the orchestrator. Remote agents handle their own. | Remote agents are opaque — you call `RunAsync`, they manage their own memory internally. You don't need to change YOUR storage. |
| **Backlog**: Need to query past messages (analytics, RAG) | `AgentSessionStore` + custom `ChatHistoryProvider` → CosmosDB | Messages in CosmosDB are queryable. Session blobs in Redis are not. Triggered by business need, not infrastructure. |

### 13.5 The Answer: What Should FinWise Use for A2A / Foundry?

Here's the simple answer for each deployment model:

#### All agents in-process (v0.3)

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

#### All agents distributed (backlog)

Even if custom agents (Profile, Advisor) are decoupled into separate A2A containers, the session architecture doesn't change. The orchestrator still owns the single Redis store. Remote agents are black boxes — they don't access the orchestrator's session.

### 13.6 Decision Tree

```
Do you need to persist sessions across HTTP requests?
  │
  YES (always, from v0.3)
  │
  └─► Use AgentSessionStore (in-memory → Redis → CosmosDB)
      │
      Do you also need to query individual messages?
      (analytics, RAG, vector search, audit trail)
        │
        ├─ NO (v0.3) → Default InMemoryChatHistoryProvider is fine.
        │                     Messages live inside the session blob. Done.
        │
        └─ YES (v0.4+) → Add a custom ChatHistoryProvider that stores
                          messages externally (CosmosDB, VectorStore).
                          Session blob stays small (just a reference key).

      Are some agents remote (A2A / Foundry)?
        │
        ├─ NO (v0.3) → Nothing to do. All in-process.
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
| **v0.3** | In-memory (SDK's built-in) | InMemory (default, inside session blob) | Foundry stock agent (server-side) |
| **v0.3.2** | Redis (custom impl) | InMemory (default, inside session blob) | Foundry stock agent (server-side) |
| **v0.4** | Redis (same) | InMemory (default) | Azure AI Foundry server-side (automatic) or agent's own store |
| **Backlog** | Redis (same — orchestrator only) | CosmosDB (shared, queryable) — only if queryable messages needed | Remote agents manage their own state; don't access orchestrator's Redis |

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
| **Everything works fine, conversations are short** | v0.3 | Do nothing. Default `InMemoryChatHistoryProvider` inside session blob. |
| **Token window limits** (conversations too long for LLM) | v0.3+ | Add `ChatReducer` to `InMemoryChatHistoryProvider` — it trims old messages automatically. No provider change needed. |
| **Session blobs too large** (>100KB, Redis latency) | v0.3 | Replace `InMemoryChatHistoryProvider` with a custom provider that stores messages externally. Session blob becomes tiny. |
| **Need to search past messages** (analytics, compliance) | v0.4 | Add a custom `ChatHistoryProvider` → CosmosDB. Messages become individually queryable. |
| **Need RAG over conversation history** | v0.4+ | Use `VectorChatHistoryProvider` → CosmosDB with vector embeddings. |
| **Adding A2A / Foundry remote agents** | v0.4 | **No ChatHistoryProvider change needed.** Remote agents manage their own memory. Your orchestrator keeps using whatever it already has. |
| **Fully distributed agents** | Backlog | **No ChatHistoryProvider change needed.** The orchestrator still owns the single Redis store. Remote agents (A2A) are black boxes. |

### 14.5 Why Custom ChatHistoryProvider Conflicts With Current Code

FinWise's `ProcessMessageAsync` does custom logic **between** load and save:
1. Load messages from session (manual)
2. **Check for reset triggers** (custom business logic)
3. **Augment email** (custom business logic)
4. Run workflow
5. **Deduplicate messages** (custom business logic)
6. Save messages back (manual)

A custom `ChatHistoryProvider` fires load/save **inside** each agent's `RunAsync()` — you can't inject steps 2, 3, 5 between the provider's load and the LLM call. Adopting it requires restructuring `ProcessMessageAsync` to move that logic elsewhere.

**This is a backlog concern.** For now, the manual approach works and is sufficient through all planned versions.

### 14.6 Practical Path Forward

| Phase | Action | ChatHistoryProvider | AgentSessionStore |
|-------|--------|--------------------|--------------------|
| **Phase 1 (v0.3)** | Adopt SDK's `AgentSessionStore` | Keep default `InMemoryChatHistoryProvider` (no change) | SDK's `InMemoryAgentSessionStore` |
| **Phase 2 (v0.3.2)** | Add Redis storage | Keep default `InMemoryChatHistoryProvider` (no change) | `RedisAgentSessionStore` |
| **v0.4** | Docker + additional agents | No change for local agents. Remote agents are opaque. | Same Redis store for orchestrator |
| **Backlog** | Add queryable history | Add `CosmosDbChatHistoryProvider` alongside existing setup | Same Redis store |
| **Backlog** | Full distribution | Only needed if agents are decoupled into separate containers. The orchestrator still uses a single Redis store for its session — remote agents (A2A) are black boxes and don't access it. No `ChatHistoryProvider` or `ProcessMessageAsync` changes needed. | Single Redis store (orchestrator only) |

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

Foundry agents manage conversation threads server-side. When you use `CreateSessionAsync(conversationId)`, the service stores all messages. You don't need `ChatHistoryProvider` OR `AgentSessionStore` for the Foundry-hosted agents. Your orchestrator still needs its own `AgentSessionStore` → Redis for its local session, but that's the same as v0.3.

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
