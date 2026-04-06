# 06 — Researching the Redis AgentSessionStore Plan

*March 21, 2026 — Turning a one-paragraph sketch into a 600-line implementation plan through deep research, then catching five bugs before writing a single line of code*

---

## The Starting Point

Journal 05 ended with a clean finish: the custom `IAgentSessionStore` was gone, the SDK's `InMemoryAgentSessionStore` was in, and the path to Redis was "clear." Spec 006's Phase 2 had sketched a `RedisAgentSessionStore` in broad strokes — a Redis container, a `StackExchange.Redis` client, TTL-based expiration. But the sketch was just that: a sketch. No Docker Compose config, no configuration options class, no step-by-step implementation order, no testing strategy.

The goal for this session: turn that sketch into a complete, implementable spec — [007-redis-agent-session-store-plan.md](../specs/007-redis-agent-session-store-plan/007-redis-agent-session-store-plan.md).

---

## Act I — Deep Research

The first prompt was explicit:

> **Developer:** "Create a new specs document. Extract the plan/phase 2 from the 006 spec, but expand it by deep-diving on the steps for implementing the REDIS session store. Use the skill 'microsoft-tech-deep-research' for what's related to Microsoft technologies, and the Internet deep dive research for what's related to the REDIS implementation on a Docker container. Research deep, take your time."

"Research deep, take your time" — permission to be thorough. The research protocol kicked in at Level 2 (Full Research).

**Step 1 — Inspect the repo.** [`Directory.Packages.props`](../Directory.Packages.props) revealed the exact versions: `Microsoft.Agents.AI.Hosting` at `1.0.0-preview.260311.1`, `Microsoft.Agents.AI` at `1.0.0-rc4`. The [`docker-compose.yml`](../docker-compose.yml) showed the CosmosDB emulator pattern — the template to mirror for Redis. [`appsettings.json`](../src/FinWise.McpServer/appsettings.json) had the `CosmosDb:Enabled` toggle we'd replicate.

**Step 2 — Microsoft Learn.** The `AgentSessionStore` abstract class was verified against both the [GitHub source](https://github.com/microsoft/agent-framework/blob/main/dotnet/src/Microsoft.Agents.AI.Hosting/AgentSessionStore.cs) and the [official API docs](https://learn.microsoft.com/dotnet/api/microsoft.agents.ai.hosting.agentsessionstore). Two methods, unchanged since the Preview. The Storage docs on Microsoft Learn confirmed the pattern: `agent.SerializeSessionAsync()` → store → `agent.DeserializeSessionAsync()` → restore. The key insight: the SDK's `InMemoryAgentSessionStore` keys by `{agentId}:{conversationId}` — our Redis implementation must follow the same composite key convention.

**Step 3 — Web research.** Docker Hub confirmed `redis:7.4-alpine` as the target (~15MB Alpine variant). Why 7.4 and not 8.x? Redis 8.0+ introduced a tri-license (RSALv2/SSPLv1/AGPLv3), while 7.4.x remains under the dual RSALv2/SSPLv1 license — simpler for a dev container. NuGet confirmed `StackExchange.Redis` at `2.12.4` — GA, MIT, 926M+ downloads. Microsoft's Redis best practices docs emphasized the singleton `ConnectionMultiplexer` pattern and `AbortOnConnectFail = false` for automatic reconnection.

**Step 4 — Synthesize.** All the research landed in the spec's Section 2: a Research Summary covering the SDK contract, the Redis client, the Docker image, and a side-by-side comparison with the existing CosmosDB infrastructure pattern.

---

## Act II — The Spec Takes Shape

The spec grew into a 600+ line document organized around a central question: *How do you add a Redis-backed `AgentSessionStore` that mirrors the existing CosmosDB infrastructure pattern?*

### The Architecture

The design is a config-driven toggle in `Program.cs` — identical to how CosmosDB works for profiles:

```
Program.cs reads Redis:Enabled from appsettings.json
  ├── true  → ConnectionMultiplexer.ConnectAsync → RedisAgentSessionStore
  └── false → InMemoryAgentSessionStore (SDK built-in)
Either way → AgentSessionStore sessionStore → FinWiseWorkflowService
```

The key design decisions:

**Key format**: `{agentId}:{conversationId}` — `"orchestrator_agent:a1b2c3d4-..."`. Matches the SDK's built-in convention.

**TTL strategy**: Sliding 24-hour expiration for dev, configurable via `RedisOptions.SessionTtlMinutes`. Applied on every `SaveSessionAsync` call — every interaction resets the clock.

**Session blob size**: 10–50KB typical. Redis considers anything under 100KB "small." No compression needed.

**No `DeleteAsync` in the SDK**: The `AgentSessionStore` abstract class has only `SaveSessionAsync` and `GetSessionAsync`. No delete. Our `RedisAgentSessionStore` adds a supplementary `ClearSessionAsync(string agentId, string conversationId)` method — not part of the SDK contract, but needed for explicit session resets.

### The Infrastructure

Docker Compose gets a Redis service alongside CosmosDB:

```yaml
redis:
  image: redis:7.4-alpine
  container_name: finwise-redis
  ports: ["6379:6379"]
  command: redis-server --save 60 1 --loglevel warning
  volumes: [redis-data:/data]
  healthcheck:
    test: ["CMD", "redis-cli", "ping"]
```

The `--save 60 1` flag enables RDB persistence — snapshot every 60 seconds if at least 1 write occurred. Data survives container restarts.

### The Files

The spec identified 10 files to change — 3 new, 7 edits — with an 8-step implementation order designed to keep the build green between each step:

1. Package references (`Directory.Packages.props`, `.csproj`)
2. `RedisOptions.cs` — configuration class
3. `RedisAgentSessionStore.cs` — core implementation
4. `AgentSessionManager.cs` + `FinWiseWorkflowService.cs` — `ClearSessionAsync` update
5. `Program.cs` + `appsettings.json` — wiring
6. `docker-compose.yml`
7. `docs/REDIS-SETUP.md`
8. Unit tests

---

## Act III — The Coexistence Principle

During review, the developer caught something fundamental the spec got wrong:

> **Developer:** "Very important to highlight in the specs document and implementation plan. When implementing the REDIS Agent Session Store, it won't be a simple migration from inMemory. We want to have both approaches implemented. By default it'll be the REDIS implementation the one working, but the InMemory Agent Session Store has also to work if we switch to it at the abstraction level."

The spec's summary had said *"This replaces the SDK's InMemoryAgentSessionStore."* That was wrong. Both implementations must coexist — Redis as default, InMemory as fallback, toggled by `Redis:Enabled` in config. The spec already *had* the toggle logic, but the framing contradicted it. The word "replaces" appeared in the summary, module diagram, and "What This Does NOT Change" section — all needed correction.

This wasn't just a wording issue. It reflected a design principle: the abstraction must work with either implementation. `AgentSessionManager` and `FinWiseWorkflowService` should never know or care which store is behind the `AgentSessionStore` abstract class.

---

## Act IV — Five Bugs in a Plan

The review found five real issues — all code-level correctness problems that would have surfaced during implementation:

### Bug 1 — "Replaces" Framing

Already covered in Act III. Every "replaces" needed to say "adds alongside."

### Bug 2 — `ResetSessionAsync` Doesn't Have an Agent

The spec proposed changing `ClearSessionAsync(string)` to `ClearSessionAsync(string, AIAgent)` and claimed *"Callers already have the orchestrator agent in scope."*

Except [`ResetSessionAsync`](../src/FinWise.MultiAgentWorkflow/Workflow/FinWiseWorkflowService.cs) doesn't:

```csharp
public async Task<string> ResetSessionAsync(string agentSessionId)
{
    await _sessionManager.ClearSessionAsync(agentSessionId);  // ← no agent here
    var newAgentSessionId = Guid.NewGuid().ToString();
    return newAgentSessionId;
}
```

It doesn't create agents. It doesn't have an `orchestratorAgent`. The proposed signature change would have broken the build on Step 4. The fix: inject the `agentId` string (always `"orchestrator_agent"`) at `AgentSessionManager` construction time. Keep the caller signature unchanged.

### Bug 3 — `is RedisAgentSessionStore` Breaks Abstraction

The proposed `AgentSessionManager.ClearSessionAsync` cast directly to the concrete type:

```csharp
if (_sessionStore is RedisAgentSessionStore redisStore)
```

If we ever add a third store (CosmosDB, SQL), we'd need another `is` branch. The fix: keep the `is` check inside `AgentSessionManager` (pragmatic for two implementations), but use the constructor-injected `agentId` string instead of requiring an `AIAgent` parameter per-call.

### Bug 4 — Missing Test File in Change Table

The existing [`AgentSessionManagerTests.cs`](../tests/FinWise.MultiAgentWorkflow.UnitTests/AgentSessionManagerTests.cs) has a `ClearSessionAsync_ShouldNotThrow` test that calls `_manager.ClearSessionAsync("any-session-id")`. Any signature change to `ClearSessionAsync` would break it. The spec's file changes table didn't mention this test file.

### Bug 5 — Namespace/Type Collision

The spec put files in `Infrastructure/AgentSessionStore/Redis/`. Per [`AGENTS.md`](../AGENTS.md): *"Sub-folder names must NOT match class names."* `AgentSessionStore` is exactly the SDK class name from `Microsoft.Agents.AI.Hosting`. The namespace `FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStore.Redis` would collide with the type, requiring fully-qualified names or `using` aliases everywhere. The fix: rename the folder to `SessionStore/` or `AgentSessionPersistence/`.

---

## What We Learned

### About the Technology

- The SDK's `AgentSessionStore` contract is minimal and stable: 2 abstract methods, no delete. Any durable implementation needs to handle session deletion as a supplementary method outside the SDK contract.
- `StackExchange.Redis` at `2.12.4` is the clear choice for .NET Redis — GA, MIT licensed, singleton `ConnectionMultiplexer` pattern matches the `CosmosClient` singleton pattern already in the codebase.
- Redis `7.4-alpine` is the right Docker image for dev: ~15MB, stable license, production-grade. The `--save 60 1` flag gives you persistence without configuration complexity.
- Session blobs (10–50KB) are well within Redis's sweet spot. The sliding TTL pattern (reset on every save) matches how conversations naturally expire.

### About the Process

- Having the CosmosDB infrastructure pattern ([`CosmosDbOptions`](../src/FinWise.MultiAgentWorkflow/Infrastructure/UserProfileStores/CosmosDb/CosmosDbOptions.cs), [`CosmosDbUserProfileStore`](../src/FinWise.MultiAgentWorkflow/Infrastructure/UserProfileStores/CosmosDb/CosmosDbUserProfileStore.cs), Docker Compose, setup docs) as a template made the Redis spec significantly easier to write. The pattern transfers cleanly: Options class → Enabled toggle → Docker service → singleton client → `IAsyncDisposable`.
- Five bugs found in a plan is five bugs not discovered during implementation. Each would have cost 15–30 minutes to debug. The `ResetSessionAsync` bug (#2) would have been particularly confusing — a compilation error in a file not mentioned in the spec's change list.
- The "replaces vs coexists" framing issue (#1) was the most important catch. It wasn't a code bug — it was an intent bug. The spec's own toggle logic contradicted its summary. Only the developer could catch this, because only the developer knew the actual requirement.

---

## The Artifact

| Artifact | Location |
|----------|----------|
| Redis spec | [specs/007-redis-agent-session-store-plan/007-redis-agent-session-store-plan.md](../specs/007-redis-agent-session-store-plan/007-redis-agent-session-store-plan.md) |

---

## What's Next

The spec needs five fixes applied before implementation begins:

1. Fix "replaces" → "adds alongside" framing throughout
2. Fix `ClearSessionAsync` design — inject `agentId` at construction, keep caller signature unchanged
3. Rename `AgentSessionStore/` folder → `SessionStore/` to avoid namespace collision
4. Add `AgentSessionManagerTests.cs` to the file changes table
5. Update the `ClearSessionAsync_ShouldNotThrow` test to cover both store types

Once the spec is patched, implementation follows the 8-step order in Section 6. Step 1: add `StackExchange.Redis` to [`Directory.Packages.props`](../Directory.Packages.props).

---

*Written: March 22, 2026*
