# 07 — The Scale-Out Question

_March 22, 2026 — Asking the hard question: can this MCP server scale?_

---

## The Scale-Out Question

Looking at [Program.cs](../src/FinWise.McpServer/Program.cs), lines 124–125:

```csharp
builder.Services.AddSingleton(workflowService);
builder.Services.AddSingleton(new McpSessionMapping());
```

> **User:** "these lines are creating singleton objects. When scaling out the mcp-server in the cloud in azure will have multiple instances... Research and make sure that these singletons impact or do not impact since we have the states userprofile and agentsession in external databases CosmosDB and Redis."

The question cut to the heart of cloud-readiness: can this MCP server sit behind an Azure load balancer with multiple instances?

### The Investigation

A thorough exploration mapped every piece of state in the application:

| Component | State Location | Scale-Out Safe? |
|-----------|---------------|-----------------|
| `FinWiseWorkflowService` | Stateless (holds only references) | ✅ Yes |
| `McpSessionMapping` | **In-memory `ConcurrentDictionary`** | ❌ **Blocker** |
| `RedisAgentSessionStore` | Redis (external) | ✅ Yes |
| `CosmosDbUserProfileStore` | CosmosDB (external) | ✅ Yes |
| `AgentSessionManager` | Stateless wrapper | ✅ Yes |
| `AgentSessionRunContext` | `AsyncLocal` (per-request) | ✅ Yes |
| `SessionResetFlag` | `AsyncLocal` (per-request) | ✅ Yes |

### The Verdict

The good news: the investment in Redis for session storage and CosmosDB for profiles had already externalized the heavy state. `FinWiseWorkflowService` holds only injected references — no conversational state. The `AsyncLocal` fields (`AgentSessionRunContext`, `SessionResetFlag`) are per-async-flow and never cross request boundaries.

The bad news: **`McpSessionMapping` is a scale-out blocker**. It holds a `ConcurrentDictionary<string, string>` mapping MCP session IDs to agent session IDs entirely in memory. In a multi-instance deployment:

1. Request hits **Instance A** → creates mapping `session-abc → agent-xyz`
2. Next request from same client hits **Instance B** → no mapping → new agent session → conversation lost

The fix is clear: move `McpSessionMapping` to Redis, just like the session store. The pattern already exists — the `RedisAgentSessionStore` provides the template.

There's also the MCP transport layer consideration. The `Stateless = false` setting means the MCP SDK maintains per-session state internally. Azure would need **sticky sessions (ARR affinity)** on the load balancer, or the MCP SDK would need distributed session support.

---

## What We Learned

- The decision to externalize state to Redis and CosmosDB was the right call — it already solved 90% of the scale-out problem
- `McpSessionMapping` is the last piece of in-memory state blocking horizontal scaling
- The MCP transport's `Stateless = false` adds an infrastructure-level concern (sticky sessions) that code alone can't solve

---

## What's Next

1. **Move `McpSessionMapping` to Redis** — the last in-memory state blocker. Follow the `RedisAgentSessionStore` pattern
2. **Research MCP SDK distributed session support** — determine if `Stateless = false` requires sticky sessions or if there's a pluggable transport session store
3. **Azure deployment planning** — Container Apps or App Service, ARR affinity configuration, Redis and CosmosDB provisioning

The MCP server works. The agents talk. The sessions persist. Now it needs to scale.

---

_Written: March 22, 2026_
