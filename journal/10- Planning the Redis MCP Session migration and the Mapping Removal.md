# 10 — Planning the Redis MCP Session migration and the Mapping Removal

_March 22, 2026 — Adding Redis-backed MCP session migration for cloud scale, and discovering the session mapping was never needed._

---

## Starting Point: The Scale-Out Question

[Journal 08](08-%20The%20MCP%20Server%20Scale-Out%20Question.md) identified `McpSessionMapping` as the last in-memory state blocking horizontal scaling. The plan seemed clear: move the mapping to Redis. But before writing code, we asked a harder question — **does this mapping need to exist at all?**

---

## The Discovery: An Abstraction That Solved Its Own Problem

The `McpSessionMapping` class holds a `ConcurrentDictionary<string, string>` mapping MCP session IDs to agent session IDs. Tracing _why_ it exists:

1. Session resets generate a new agent session ID (`Guid.NewGuid()`)
2. The MCP session stays the same (same client connection)
3. Therefore, something must track "MCP session X now points to agent session Y"

But what if resets _didn't_ generate a new ID? What if reset just meant "delete the session data, keep the same key"?

The `RedisAgentSessionStore.GetSessionAsync` already handles missing keys:

```csharp
var json = await _redis.GetDatabase().StringGetAsync(key);
if (json.IsNullOrEmpty)
    return await agent.CreateSessionAsync(cancellationToken); // fresh session!
```

An empty key slot = fresh start. The ID is just a storage key. **The mapping was never necessary** — not even in the original single-instance design. It was an accidental consequence of writing reset as "new GUID" instead of "clear data."

---

## Two Spec Variants

We produced two implementation plans for comparison:

| | [008.A — No Mapping](../specs/008-redis-mcp-session/008.A-redis-mcp-session-store-plan.md) | [008.B — Redis Mapping](../specs/008-redis-mcp-session/008.B-redis-mcp-session-mapping-plan.md) |
|---|---|---|
| **Approach** | MCP Session ID = agent session ID. Delete `McpSessionMapping`. | Move mapping to Redis. Keep separate IDs. |
| **New classes** | 1 (`RedisSessionMigrationHandler`) | 3 (handler + interface + Redis mapping) |
| **Deleted classes** | 1 (`McpSessionMapping`) | 0 |
| **Redis key families** | 2 | 3 |
| **Race conditions** | 0 | 1 (atomic get-or-create) |
| **Workflow changes** | Yes (reset keeps same ID) | No |

Both share the same foundation: upgrade MCP SDK to 1.1.0 GA and implement `ISessionMigrationHandler` for cross-instance session migration.

---

## Deep Research: Why Not Pure Stateless MCP?

The MCP SDK supports `Stateless = true` which eliminates all server-side session state. We evaluated and **discarded** it for FinWise:

- Without `MCP-Session-Id`, the server can't correlate requests to the same conversation
- FinWise's multi-agent workflow is inherently multi-turn (profile collection → advisor access)
- Every workaround (client-provided IDs, auth tokens, IP address) either breaks the MCP spec or shifts burden to clients
- Permanently closes the door on SSE, notifications, sampling, and other MCP features

**The `ISessionMigrationHandler` approach** solves scale-out while keeping standard MCP sessions — the protocol works as designed, clients don't change.

---

## The MCP SDK's `ISessionMigrationHandler`

The critical API discovery: the MCP C# SDK (since v0.9.0-preview.1, GA in 1.0.0) provides `ISessionMigrationHandler` — an official hook for horizontal scaling:

```csharp
public interface ISessionMigrationHandler
{
    // Called after initialize — persist handshake data externally
    ValueTask OnSessionInitializedAsync(..., InitializeRequestParams initializeParams, ...);
    
    // Called when request arrives with unknown session ID — restore from external store
    ValueTask<InitializeRequestParams?> AllowSessionMigrationAsync(..., string sessionId, ...);
}
```

The flow: Instance A handles `initialize` and stores the handshake params in Redis. When a request hits Instance B, the SDK can't find the session locally, calls `AllowSessionMigrationAsync`, reads from Redis, and reconstructs the session. The client never knows it switched instances.

---

## Key Questions We Answered

### Does reset still work without the mapping?

**Yes.** `ClearSessionAsync` deletes the Redis key. Next request finds nothing. Fresh session created. The user gets asked for email again, can switch profiles. Zero user-visible change from current behavior.

### Can different MCP clients use the same user account simultaneously?

**Yes.** Each client gets its own MCP session ID → its own agent session → fully isolated conversations. The CosmosDB profile (keyed by email) is shared — the conversation state is not.

```
VS Code  → mcp-abc → [conversation 1]
ChatGPT  → mcp-xyz → [conversation 2]    ← same user adrian@outlook.com
Claude   → mcp-def → [conversation 3]
```

### What about future authentication?

MCP OAuth is per-request (`Authorization` header), independent of `MCP-Session-Id`. Changing users doesn't change the MCP session. Under 008.A, composite keys handle this:

```csharp
var agentSessionId = userId != null ? $"{mcpSessionId}:{userId}" : mcpSessionId;
```

No mapping needed. A mapping layer (008.B) only adds value for scenarios FinWise doesn't need — forking conversations, multiple concurrent chats per user per session, admin takeover.

### Why does the Redis store for MCP init params exist?

The MCP client keeps the same `MCP-Session-Id` across all requests. But each server instance keeps the SDK's internal session state in memory. When a load balancer routes to a different instance, that instance doesn't recognize the session → 404. The `mcpinit:*` Redis keys let any instance reconstruct the SDK session from the stored handshake data. It's a one-time migration cost per instance, per session.

### Does it work without Redis (in-memory only)?

Mostly. Single-instance conversations work fine. **But reset is broken** — the SDK's `InMemoryAgentSessionStore` has no delete method. Current code works by accident (new GUID has no data). Under 008.A (same ID), stale data persists. 

**Decision**: For v0.4.0, we accept this limitation in no-Redis mode. Workaround: restart the server connection. A custom `ClearableInMemoryAgentSessionStore` is documented as a potential future fix but not in scope.

### Was the mapping _ever_ technically necessary?

**No.** Even in the original single-instance design. If `ProcessMessageAsync` had been written from the start as "clear data, keep same ID," `McpSessionMapping` would never have existed. 008.A corrects this unnecessary abstraction.

---

## VS Code Session Lifecycle

Understanding when VS Code creates new MCP sessions was critical for validating 008.A:

**New session** (new `MCP-Session-Id`): Close/reopen VS Code, Stop/Start server, edit `mcp.json`, `MCP: Restart Server`, idle timeout (30 min), window reload.

**Same session**: New chat panels, multiple Copilot conversations, changing auth tokens.

One VS Code window = one MCP session = one conversation. Natural 1:1 alignment with 008.A.

---

## The Verdict

**008.A wins.** It's simpler (fewer classes, fewer Redis keys, simpler tool code), solves the same scale-out problem, handles all realistic current and future scenarios (reset, multi-client, auth), and corrects an abstraction that was never needed.

008.B remains documented as the alternative for codebases that need many-to-many session relationships. For FinWise's 1:1 model, it's unnecessary complexity.

---

## What's Next

1. Implement 008.A — upgrade MCP SDK to 1.1.0, add `RedisSessionMigrationHandler`, simplify tools, change reset behavior, delete `McpSessionMapping`
2. Integration testing with actual multi-instance deployment
3. Azure deployment planning (Container Apps, no affinity)

The MCP server will finally be truly stateless — every instance identical, any request on any node, conversations never lost.

---

_Written: March 22, 2026_