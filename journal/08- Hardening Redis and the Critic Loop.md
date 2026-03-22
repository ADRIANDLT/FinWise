# 08 — Hardening Redis and the Critic Loop

_March 22, 2026 — Turning Critic code review findings into production-grade improvements: interface decoupling, security hardening, corrupt data resilience, and real Redis integration tests._

---

## The Starting Point

Journal 07 left us with a working Redis `AgentSessionStore` — sessions persisting across restarts, live MCP conversations flowing through Redis, integration tests green. The implementation was functional. But "functional" isn't the same as "hardened."

The Critic had spoken. Five Important findings and three Suggestions. Time to fix them.

---

## Act I — The Design Fix That Touched Everything

The Critic's sharpest finding cut to the core of `AgentSessionManager`:

```csharp
if (_sessionStore is RedisAgentSessionStore redisStore)
```

A concrete type check. The session manager — which was supposed to be store-agnostic — was directly referencing the Redis implementation. Worse, `FinWiseWorkflowService.ResetSessionAsync` was creating an entire four-agent workflow graph just to get a string:

```csharp
var (orchestratorAgent, _) = CreateAgentsAndWorkflow(agentSessionId, isProfileReady: false);
await _sessionManager.ClearSessionAsync(agentSessionId, orchestratorAgent);
```

Four agents instantiated, a complete handoff graph wired up — all to pass `orchestratorAgent.Id`, which is always `"orchestrator_agent"`.

> **User:** "Do the findings 3, 4, 5, 6, 7. And for finding 8 add a note about not using auth in the specs document, but say we need to use authentication when moving to production in Azure."

The fix required coordinated changes across seven files. The approach: introduce an [`IClearableSessionStore`](../src/FinWise.MultiAgentWorkflow/Infrastructure/AgentSessionStores/IClearableSessionStore.cs) interface — a capability contract separate from the SDK's `AgentSessionStore` abstract class.

```csharp
public interface IClearableSessionStore
{
    Task ClearSessionAsync(string conversationId);
}
```

[`RedisAgentSessionStore`](../src/FinWise.MultiAgentWorkflow/Infrastructure/AgentSessionStores/Redis/RedisAgentSessionStore.cs) now takes `agentId` at construction time and implements this interface. The `agentId` is baked in — no need to pass an `AIAgent` just to get its ID. [`AgentSessionManager`](../src/FinWise.MultiAgentWorkflow/Session/AgentSessionManager.cs) checks `is IClearableSessionStore` — no Redis-specific import needed:

```csharp
if (_sessionStore is IClearableSessionStore clearable)
{
    await clearable.ClearSessionAsync(agentSessionId);
}
```

Both call sites in [`FinWiseWorkflowService`](../src/FinWise.MultiAgentWorkflow/Workflow/FinWiseWorkflowService.cs) reverted to the clean single-parameter call. The wasteful `CreateAgentsAndWorkflow` in `ResetSessionAsync` vanished entirely.

[`Program.cs`](../src/FinWise.McpServer/Program.cs) now passes the agent ID at construction:

```csharp
sessionStore = new RedisAgentSessionStore(redis, TimeSpan.FromMinutes(...), "orchestrator_agent");
```

Seven files changed. Zero new coupling introduced. The session manager can now work with _any_ clearable store — Redis, CosmosDB, whatever comes next — without knowing what's behind the interface.

---

## Act II — The Security Fix and the Small Things

### Connection String Redaction

The Critic caught a subtle security issue: [Program.cs](../src/FinWise.McpServer/Program.cs) was logging the full Redis connection string at `Information` level. In production, that string looks like `host:6380,password=secret,ssl=True`. The fix was surgical:

```csharp
Log.Information("Using Redis session store (Host: {RedisHost}, TTL: {Ttl} min)",
    redisOptions.ConnectionString.Split(',')[0], redisOptions.SessionTtlMinutes);
```

Only the hostname makes it to the logs. Passwords stay out.

### The Auth Note

The [spec document](../specs/007-redis-agent-session-store-plan/007-redis-agent-session-store-plan.md) now carries a clear callout about the intentional lack of authentication on the local Docker Redis container — and what production requires (Azure Cache for Redis with access keys or Microsoft Entra ID).

### The Null Check

The Critic noticed `agentId` wasn't null-checked in the constructor, while `redis` was. Inconsistent with the project pattern established by `FinWiseWorkflowService`, which validates all injected dependencies. One line added:

```csharp
_agentId = agentId ?? throw new ArgumentNullException(nameof(agentId));
```

---

## Act III — Surviving Corrupt Data

The most consequential refinement came from the Critic's second pass. What happens when Redis returns invalid JSON? Before the fix: `JsonException` propagates up, the workflow catches it with a generic "I apologize" error, and the user is stuck — every retry hits the same corrupt data. The only escape was waiting 24 hours for TTL expiration or manually deleting the Redis key.

The fix in [`RedisAgentSessionStore.GetSessionAsync`](../src/FinWise.MultiAgentWorkflow/Infrastructure/AgentSessionStores/Redis/RedisAgentSessionStore.cs):

```csharp
try
{
    var element = JsonSerializer.Deserialize<JsonElement>(json.ToString());
    return await agent.DeserializeSessionAsync(element, cancellationToken: cancellationToken);
}
catch (JsonException ex)
{
    Log.Warning(ex, "Redis: Corrupt session data for {Key}, creating new session", key);
    await _redis.GetDatabase().KeyDeleteAsync(key);
    return await agent.CreateSessionAsync(cancellationToken);
}
```

Corrupt data? Log a warning, delete the poisoned key, create a fresh session. The user loses their conversation history but gets a working session immediately — a dramatically better outcome than a 24-hour lockout.

---

## Act IV — Real Redis, Real Tests

Unit tests against Moq are necessary but insufficient. They prove the code calls the right methods with the right arguments. They don't prove sessions actually survive a round-trip through Redis serialization.

> **User:** "Now in the folder attached (FinWise.Redis.IntegrationTests) create the Redis integration tests. As project folder do not create another second project folder."

The [integration test project](../tests/FinWise.Redis.IntegrationTests/FinWise.Redis.IntegrationTests.csproj) was scaffolded following the existing CosmosDB pattern — `IAsyncLifetime` for setup/teardown, `[Trait("Category", "Integration")]` for filtering, `[SkippableFact]` with `Skip.If(...)` for graceful degradation when Redis isn't running.

Six tests in [`RedisAgentSessionStoreIntegrationTests`](../tests/FinWise.Redis.IntegrationTests/RedisAgentSessionStoreIntegrationTests.cs), each talking to real Redis on `localhost:6379`:

| Test | What It Proves |
|------|---------------|
| `SaveAndGetSession_RoundTrips_WithRealRedis` | Full serialize → store → retrieve → deserialize. Messages survive the trip. |
| `GetSession_ReturnsNewSession_WhenKeyNotInRedis` | Fresh key returns an empty session, not null or an error. |
| `ClearSession_RemovesKey_FromRedis` | Delete actually removes the key. Subsequent get creates a new session. |
| `ClearSession_NonExistentKey_DoesNotThrow` | Idempotent delete on missing keys — no surprises. |
| `SessionExpires_AfterTtl` | 2-second TTL, 3-second wait, key is gone. Redis expiry actually works. |
| `SaveSession_OverwritesPreviousValue` | Second save replaces first for the same key. Last write wins. |

The round-trip test was the most satisfying. It creates a session, adds a message — "Hello integration test" — saves it to Redis, retrieves it, and verifies the message text came back intact. This is the test that proves the `AgentSessionStore` contract works end-to-end through real Redis serialization, not just through mocked method calls.

### The Cleanup Pattern

Each test run generates a unique GUID prefix (`test_{guid}`), and `DisposeAsync` cleans up only keys matching that prefix. This means parallel CI runs don't step on each other:

```csharp
await foreach (var key in server.KeysAsync(pattern: $"integration_test_agent:{_testPrefix}*"))
{
    await db.KeyDeleteAsync(key);
}
```

The Critic caught an earlier version that was cleaning up _all_ `integration_test_agent:*` keys — a scope mismatch that would cause failures under concurrent execution.

---

## The Multi-Pass Pattern

This session used a consistent pattern: **Coder drafts → Critic reviews → Coder refines**. Each cycle caught real issues:

- **Draft → Critic 1:** Found the concrete type check, missing tests, security leak, corrupt data vulnerability
- **Refine 1 → Critic 2:** Found the cleanup scope mismatch, weak assertions, missing idempotency test

The Critic wasn't always right — some suggestions (AAA comments, slightly wider TTL margins) were triaged as not blocking. But the Important findings were consistently valuable. The corrupt data handling and the interface decoupling both came from Critic passes, not from the initial implementation.

---

## What We Learned

### About Design

- Interface segregation (`IClearableSessionStore`) is the right tool when extending an SDK abstract class that doesn't include the operation you need. Don't force the parameter through the existing API — create a capability interface.
- Injecting stable identifiers at construction time (the `agentId`) eliminates the need to thread runtime objects through call chains just for their metadata.

### About Resilience

- External stores can return corrupt data. Always handle `JsonException` when deserializing from Redis, CosmosDB, or any external source. The graceful degradation pattern (log, delete, recreate) is dramatically better than a stuck session.
- Connection strings in logs are a common security gap. The `Split(',')[0]` pattern is a simple, reliable redaction for StackExchange.Redis connection strings.

### About Testing

- Integration tests against real infrastructure catch a class of bugs that mocks can't — serialization format mismatches, TTL behavior, key collision patterns.
- Test isolation via unique prefixes (`Guid.NewGuid()`) is essential for integration tests that share infrastructure. The prefix must be consistent — creation _and_ cleanup must use the same scope.

### About the Coder-Critic Loop

- The multi-pass pattern (Draft → Critic → Refine) consistently catches issues the initial implementation misses. The Critic is most valuable on design and edge cases — it found the interface decoupling opportunity and the corrupt data vulnerability.
- Not every Critic finding requires action. Triage by severity and confidence, fix the Important ones, skip the Suggestions that don't add meaningful safety.

---

## What's Next

1. **Move `McpSessionMapping` to Redis** — the last in-memory state blocker for horizontal scaling (identified in Journal 07)
2. **Research MCP SDK distributed session support** — determine if `Stateless = false` requires sticky sessions
3. **Azure deployment planning** — Redis is hardened and tested; time to think about provisioning

The Redis session store started this session as "working code." It ends as production-grade infrastructure — with interface decoupling, security hardening, corrupt data resilience, and real integration tests proving it works against actual Redis. The foundation is solid.

---

_Written: March 22, 2026_
