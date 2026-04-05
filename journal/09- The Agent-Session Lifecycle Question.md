# 09 — The Agent-Session Lifecycle Question

_March 22, 2026 — Tracing how agent sessions live and die in Redis: ClearSessionAsync, TTL expiration, eviction policy, and plugging the one gap that would have killed writes under pressure._

---

## The Starting Point

Journals 06–08 left us with a working Redis `AgentSessionStore` and a clear scale-out picture. Sessions were persisting, the architecture was sound, and `McpSessionMapping` was flagged as the last in-memory blocker. But a quieter question had been lurking:

> **User:** "The Redis database is going to grow and grow with risks of overflow and reach limits... What should be the strategy about limiting the number of sessions in Redis for the long term?"

Sessions were flowing _into_ Redis. But what was making sure they flowed _out?_

---

## Act I — Tracing ClearSessionAsync

Before thinking about growth, we needed to map what session deletion mechanisms already existed. The investigation started with [`ClearSessionAsync`](../src/FinWise.MultiAgentWorkflow/Session/AgentSessionManager.cs) — a method sitting inside `AgentSessionManager` that looked deceptively simple:

```csharp
public async Task ClearSessionAsync(string agentSessionId)
{
    if (_sessionStore is IClearableSessionStore clearable)
    {
        await clearable.ClearSessionAsync(agentSessionId);
    }
}
```

A capability check. Not every store can delete — the SDK's built-in `InMemoryAgentSessionStore` has no `Delete` method. So [`IClearableSessionStore`](../src/FinWise.MultiAgentWorkflow/Infrastructure/AgentSessionStores/IClearableSessionStore.cs) was born as an optional contract, implemented only by [`RedisAgentSessionStore`](../src/FinWise.MultiAgentWorkflow/Infrastructure/AgentSessionStores/Redis/RedisAgentSessionStore.cs):

```csharp
public async Task ClearSessionAsync(string conversationId)
{
    var key = GetKey(_agentId, conversationId);
    await _redis.GetDatabase().KeyDeleteAsync(key);
}
```

Straightforward — a `KeyDeleteAsync` call. But the interesting part was _who calls it_ and _when_.

### The Two Triggers

Tracing the call graph revealed two distinct paths to session deletion:

**Path 1 — The orchestrator's reset tool.** When a user says "start over," the LLM invokes `request_session_reset` inside `OrchestratorAgentFactory`. That calls `SessionResetFlag.Current?.Request()`, which sets a mutable token via `AsyncLocal`. Back in [`FinWiseWorkflowService.ProcessMessageAsync`](../src/FinWise.MultiAgentWorkflow/Workflow/FinWiseWorkflowService.cs), the flag is detected after the workflow completes:

```csharp
if (wasReset)
{
    response = "Your session has been reset. Please provide your email address...";
    await _sessionManager.ClearSessionAsync(agentSessionId);
    agentSessionId = Guid.NewGuid().ToString();
}
```

Old session deleted. New GUID assigned. Clean slate.

**Path 2 — The explicit MCP tool.** [`FinWiseTools`](../src/FinWise.McpServer/Tools/FinWiseTools.cs) exposes a `reset_session` tool that calls `workflowService.ResetSessionAsync(agentSessionId)` — which delegates to the same `ClearSessionAsync`.

Both paths converge on the same `KeyDeleteAsync`. The session dies immediately when the user asks for it.

---

## Act II — The Three Layers of Session Lifecycle

With the deletion paths mapped, the full lifecycle picture emerged. Three mechanisms, three timescales, three purposes:

| Layer | Mechanism | Trigger | Timescale | Purpose |
|-------|-----------|---------|-----------|---------|
| **Application** | `ClearSessionAsync` | User says "start over" | Immediate | Clean slate on demand |
| **TTL** | Redis key expiration | 24h of inactivity | Passive, sliding | Automatic cleanup of abandoned sessions |
| **Eviction** | `volatile-lru` policy | Redis hits memory limit | Emergency | Prevent OOM — shed oldest sessions to make room |

The first layer was already working — the Critic Loop in Journal 07 had hardened the `IClearableSessionStore` interface and the `AgentSessionManager` delegation. The second layer was also in place: every `SaveSessionAsync` sets the key with a 24-hour TTL via `StringSetAsync(key, json, _ttl)`. Inactive sessions expire automatically.

But the third layer — the safety net — was **missing**.

---

## Act III — The Missing Safety Net

Looking at [`docker-compose.yml`](../docker-compose.yml), the Redis container had a memory limit:

```yaml
redis:
    image: redis:7.4-alpine
    command: redis-server --save 60 1 --loglevel warning
    mem_limit: 256m
```

A 256 MB ceiling. But **no `maxmemory-policy`**. Without it, Redis defaults to `noeviction` — when memory fills up, _every write fails_. The server returns OOM errors. `SaveSessionAsync` calls start throwing. Sessions silently stop persisting. The user keeps chatting, thinking everything is fine, but their conversation history is gone.

> **User:** "Is there any easy but critical improvement related to this that you'd do?"

One line:

```yaml
command: redis-server --save 60 1 --loglevel warning --maxmemory 200mb --maxmemory-policy volatile-lru
```

Two flags:
- **`--maxmemory 200mb`** — Redis's own memory cap, below the 256 MB container limit (leaving headroom for Redis overhead and OS buffers)
- **`--maxmemory-policy volatile-lru`** — when memory is full, evict the least-recently-used keys _that have a TTL set_

Why `volatile-lru` specifically? Every session key has a TTL from `StringSetAsync(..., _ttl)`. This policy only touches keys with expiry — if any TTL-less keys ever exist, they're protected. The oldest, least-active sessions get evicted first. Exactly the right behavior.

Without this, a busy day with many concurrent users fills 256 MB and the server goes dark. With it, Redis gracefully sheds stale sessions to make room for active ones.

---

## What We Learned

### About Defense in Depth

Session lifecycle isn't one mechanism — it's three layers working at different timescales. Application-level deletion for user intent. TTL for abandoned session cleanup. Eviction policy for memory pressure. Each one catches what the others miss.

### About the Gap Between "Works" and "Survives"

The Redis session store was fully functional. Tests were green. Sessions persisted and restored correctly. But a single missing configuration flag — `maxmemory-policy` — meant a production deployment under load would silently lose data. The feature wasn't broken; the operational posture was.

### About the IClearableSessionStore Pattern

The capability-check pattern (`is IClearableSessionStore`) exists because the SDK's `AgentSessionStore` base class has no delete contract. `InMemoryAgentSessionStore` is a sealed SDK class — we can't add deletion to it. The interface lets the `AgentSessionManager` remain store-agnostic while supporting deletion on stores that can do it. It's not optional nice-to-have — without it, session resets on Redis would leave 24 hours of orphaned data.

---

## What's Next

1. **Move `McpSessionMapping` to Redis** — the last in-memory state blocker for horizontal scaling
2. **Research MCP SDK distributed session support** — `Stateless = false` implications for multi-instance deployment
3. **Consider message history cap** — prevent individual sessions from growing unbounded within their 24h window

The sessions have a lifecycle now. They're born, they persist, and they die — by user choice, by time, or by memory pressure. Three layers, zero gaps.

---

_Written: March 22, 2026_
