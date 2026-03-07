# 004 — Session Naming Convention: Analysis and Decision

**Status**: Implemented  
**Date**: 2026-03-02

---

## 1. Problem

The word "session" was overloaded across two protocols:
- **MCP protocol**: "session" = HTTP client connection (`MCP-Session-Id` header)
- **Microsoft Agent Framework**: "session" = `AgentSession`, a stateful container for one interaction thread between a user and the multi-agent workflow

Using bare "Session" in type names (e.g., `ISessionStore`, `SessionData`) was ambiguous — readers couldn't tell which kind of session was meant.

---

## 2. Decision

**Rule**: No bare "Session" in any type name, file name, or folder name. Always qualify:
- `AgentSession*` — types in the workflow/Agent Framework layer (manages `AgentSession` SDK type)
- `McpSession*` — types in the MCP transport layer

This aligns with the Microsoft Agent Framework convention (which uses `AgentSession` as its official type name) while disambiguating from MCP's transport-level sessions.

---

## 3. What Is an AgentSession?

An **AgentSession** is a stateful container that holds the complete message history and context for one interaction thread between a user and the multi-agent workflow. Key characteristics:

- **Shared across agents**: All agents (orchestrator, profile, advisor) share ONE AgentSession per interaction thread. The session is created from the orchestrator agent but stores messages from all agents in the handoff workflow.
- **Identified by**: `agentSessionId` — a GUID string generated when a user first interacts. (The SDK's `AgentSessionStore` calls this same concept `conversationId`.)
- **Persisted**: Serialized via `agent.SerializeSessionAsync()` and stored in `IAgentSessionStore` between HTTP requests.
- **Reset**: Cleared when the user explicitly requests a reset (via `reset_conversation` tool or natural language triggers like "start over").

---

## 4. MCP Session vs AgentSession — Relationship

```
MCP Client (VS Code, Claude Desktop)
  │
  │  HTTP POST /mcp
  │  Header: MCP-Session-Id: "abc-123"
  │
  ▼
McpSessionMapping
  │  Maps: MCP-Session-Id → agentSessionId
  │  Relationship: 1 MCP Session → 1 AgentSession (at any time)
  │
  ▼
FinWiseWorkflowService.ProcessMessageAsync(agentSessionId, query)
  │
  ▼
AgentSessionManager → IAgentSessionStore
  │  Stores/retrieves AgentSessionData keyed by agentSessionId
```

### Relationship Rules

| Rule | Detail |
|------|--------|
| **1 MCP Session → 1 AgentSession** | Each MCP client connection has exactly one active AgentSession at a time |
| **AgentSession can be replaced** | When a reset occurs, the old AgentSession is cleared and a new `agentSessionId` is generated for the same MCP session |
| **AgentSessions are NOT shared** | Two different MCP sessions never share the same AgentSession, even for the same user |
| **User profiles ARE shared** | Multiple AgentSessions (across different MCP sessions) can access the same user profile via email lookup in `IUserProfileStore` |
| **MCP Session lifetime > AgentSession lifetime** | An MCP session persists as long as the HTTP client is connected. An AgentSession within it can be reset multiple times. |

### Example Flow

1. User opens VS Code → MCP session `abc-123` created → AgentSession `sess-001` created
2. User completes profile, gets advice (all within `sess-001`)
3. User says "start over" → `sess-001` cleared → new AgentSession `sess-002` created, still mapped to MCP `abc-123`
4. User opens a NEW VS Code tab → MCP session `def-456` created → AgentSession `sess-003` created (completely independent)
5. User in tab 2 provides same email → same user profile loaded from store, but different AgentSession

---

## 5. Applied Renames

| Before | After | Layer |
|--------|-------|-------|
| `SessionResetEvaluator` | `AgentSessionResetEvaluator` | Workflow |
| `SessionConstants` | `AgentSessionConstants` | Workflow |
| `ISessionStore` | `IAgentSessionStore` | Infrastructure |
| `InMemorySessionStore` | `InMemoryAgentSessionStore` | Infrastructure |
| `SessionData` | `AgentSessionData` | Infrastructure |
| `ConversationRunContext` | `AgentSessionRunContext` | Workflow |
| `ConversationRunSnapshot` | `AgentSessionRunSnapshot` | Workflow |
| `Infrastructure/SessionStore/` | `Infrastructure/AgentSessionStore/` | Folder |
| `conversationId` (parameter) | `agentSessionId` | All files (note: SDK's `AgentSessionStore` still uses `conversationId` for the same concept) |
| `conversationHistory` (variable) | `messageHistory` | Workflow |
| `ConversationId` (property) | `AgentSessionId` | Records |

### Unchanged (Already Correct)

| Name | Why |
|------|-----|
| `AgentSessionManager` | Already uses `AgentSession` prefix |
| `McpSessionMapping` | Already uses `Mcp` prefix |

### Eliminated

| Name | Reason |
|------|--------|
| `EmailValidator` class | Inlined into `UserProfileAgentFactory` (single-use, one-liner method) |
| `WorkflowHelpers` class | Eliminated — methods distributed to `FinWiseWorkflowService` and `AgentSessionConstants` |

---

## 6. Alternatives Considered

| Option | Description | Why Rejected |
|--------|-------------|-------------|
| **Rename to "Conversation"** | Use `IConversationStore`, `ConversationData`, etc. | Fights the Agent Framework SDK convention where `AgentSession` is the official term. Creates friction for developers reading SDK docs. |
| **Keep bare "Session"** | No renames, just add docs | Relies on tribal knowledge. Newcomers can't tell MCP session from AgentSession by reading code. |
| **Qualified names** (`AgentConversationSessionManager`) | Long but explicit | Too verbose (30+ char names). Hungarian notation feel. |
| **Hybrid** (Session for SDK, Conversation for ours) | Mix of terms | Inconsistent — "why is the store Conversation but the manager Session?" |

The `AgentSession*` prefix approach won because it's a **simple, universal rule** ("always prefix") that aligns with the SDK and eliminates ambiguity without excessive verbosity.

---

## 7. SDK Naming Divergence: `agentSessionId` vs `conversationId`

The Microsoft Agent Framework's `AgentSessionStore` (abstract class in `Microsoft.Agents.AI.Hosting`) uses `conversationId` as the storage key parameter:

```csharp
// SDK's AgentSessionStore contract:
public abstract ValueTask SaveSessionAsync(AIAgent agent, string conversationId, AgentSession session, ...);
public abstract ValueTask<AgentSession> GetSessionAsync(AIAgent agent, string conversationId, ...);
```

We use `agentSessionId` instead. Both refer to the same concept — a unique identifier for one interaction thread — but our name was chosen to:
- Avoid confusion with MCP session IDs (`MCP-Session-Id` header) in a codebase that handles both protocols
- Emphasize the FinWise-managed lifecycle (create → use → reset → new ID), which differs from the SDK's typically stable `conversationId`

**Known trade-off**: Developers reading SDK docs will see `conversationId` and need to map it mentally to `agentSessionId` in our code. This is documented here and in code comments.

---

## 8. SDK `AgentSessionStore` vs Our `IAgentSessionStore`

The SDK ships `AgentSessionStore` (abstract class) with a higher-level contract that takes `AIAgent` and handles serialization internally. Our `IAgentSessionStore` is a lower-level interface:

| Aspect | SDK `AgentSessionStore` | Our `IAgentSessionStore` |
|--------|------------------------|-------------------------|
| **Contract** | `SaveSessionAsync(agent, conversationId, session)` | `SetSessionDataAsync(agentSessionId, data)` |
| **Serialization** | Internal (store receives `AgentSession` directly) | External (done by `AgentSessionManager`, store receives `AgentSessionData`) |
| **Metadata** | None | `UserId`, `MessageCount`, `LastMessageAt`, `CreatedAt` |
| **Clear/Delete** | Not supported | `ClearSessionAsync(agentSessionId)` |
| **Package** | `Microsoft.Agents.AI.Hosting` | No extra dependency |

**Why not adopt the SDK's class?** Our `AgentSessionData` record carries metadata (userId for profile association, message counts for diagnostics, timestamps for timeout detection) that the SDK contract doesn't support. We also need `ClearSessionAsync` for explicit reset flows. Adopting the SDK class would require adding the `Microsoft.Agents.AI.Hosting` package to the class library and losing these capabilities.

**Future consideration**: In v0.4+ (when `Microsoft.Agents.AI.Hosting` may be needed for DI/A2A/durable agents), we could implement the SDK's `AgentSessionStore` as an adapter that delegates to our `IAgentSessionStore`, getting SDK compatibility without losing our metadata.
