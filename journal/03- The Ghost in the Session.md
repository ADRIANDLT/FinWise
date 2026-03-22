# 03 — The Ghost in the Session

*March 15, 2026 — A debugging story about invisible messages, infinite loops, and the perils of pre-release SDKs*

---

## The Setup

The Stock Specialized Agent was the exciting new addition — a Foundry-hosted agent in Azure AI that could ground its stock recommendations in actual annual reports from Apple, Microsoft, Tesla, Nvidia, and Amazon. The plumbing was done: `Microsoft.Agents.AI.AzureAI` wired up, the orchestrator learned a new handoff, and the NuGet packages got bumped from `1.0.0-preview.260212.1` to `1.0.0-rc4`.

Everything compiled. The agent responded in isolation. Ship it?

Not quite.

## Act I — "Please provide your email address" (Again)

The first sign of trouble was mundane. A user typed *"Give me financial advice"*, provided their email, answered the risk tolerance question, and then… the system asked for email again. Every single time. Every message was a first date.

The server logs told the story in black and white:

```
Restored AgentSession with 6 messages (expected), actual store has 0 messages, StoreType: null
MessageStore from session: null, IsNull: true
Loaded 0 messages from messageStore
```

The session was being "restored" — the framework dutifully deserialized the `AgentSession` blob — but the **messages inside were ghosts**. Present in the metadata count, absent in reality. The `InMemoryChatHistoryProvider` service that should have held the conversation? `null`. Every time. Even on brand-new sessions fresh from `CreateSessionAsync()`.

## Act II — The Invisible Breaking Change

The initial hypothesis was compelling: *"The stock agent changed something in the session lifecycle."* But `OrchestratorAgentFactory.cs` showed zero changes. The orchestrator was still a `ChatClientAgent`, same as always.

The real culprit was hiding in `Directory.Packages.props`:

```diff
- <PackageVersion Include="Microsoft.Agents.AI" Version="1.0.0-preview.260212.1" />
+ <PackageVersion Include="Microsoft.Agents.AI" Version="1.0.0-rc4" />
```

A version bump. Three packages. One silent behavioral change.

In the preview SDK, `ChatClientAgent.CreateSessionAsync()` automatically registered an `InMemoryChatHistoryProvider` as a queryable service on the `AgentSession`. The code called `session.GetService<InMemoryChatHistoryProvider>()` to read and write messages, and it Just Worked.

In RC4, the SDK restructured its internals. The `InMemoryChatHistoryProvider` was still there — the agent's constructor still creates one — but it was no longer exposed through `GetService<T>()`. Messages moved to `StateBag`-based management, handled internally during `RunAsync()` calls. The public service query API? Silently broken.

**The API names changed** (the team adapted: `ToList()` → `GetMessages()`, `Clear()` → `SetMessages()`, `StreamAsync()` → `RunStreamingAsync()`). **The API behavior also changed** — but nobody told us that part.

## Act III — The Fix: Independence Day

The fix was deliberate: **stop depending on the SDK's internal session service mechanism entirely.**

Messages now serialize independently alongside the `AgentSession` blob, stored in a new `SerializedMessages` field on `AgentSessionData`:

```csharp
public async Task<(AgentSession Session, List<ChatMessage> Messages)> GetOrCreateSessionAsync(...)
{
    // Messages restored from our own store, not from InMemoryChatHistoryProvider
    List<ChatMessage> messages = DeserializeMessages(sessionData.SerializedMessages);
    return (resumedSession, messages);
}
```

Three files changed. The session manager returns a tuple now — session *and* messages. The workflow service reads messages from the manager, not from a framework service that may or may not exist. And `PersistSessionAsync` serializes messages with `AIJsonUtilities.DefaultOptions`, writing them to `AgentSessionData` where we control the format.

The conversation started flowing again. Email asked once. Risk tolerance remembered. Profile completed. Advice delivered.

## Act IV — The Infinite Loop

With session state fixed, the integration tests revealed a second bug hiding behind the first.

Session 2 of the test suite sent *"What stocks should I buy?"* to a fresh MCP session with no profile. The orchestrator's prompt clearly stated: route to `profile_agent` first if no `PROFILE_READY:` marker exists. But `gpt-4o-mini` had other ideas. It routed directly to `advisor_agent`. The advisor couldn't help without a profile and handed back to the orchestrator. The orchestrator routed to the advisor again. And again. And again.

For 100 seconds, the two agents played hot potato while the HTTP client patiently waited for a response that would never come.

The server logs painted the picture:

```
Agent invoked: orchestrator_agent (invocation 1)
Agent invoked: advisor_agent (invocation 2)
Agent invoked: orchestrator_agent (invocation 3)
Agent invoked: advisor_agent (invocation 4)
...
Agent invoked: advisor_agent (invocation 198)
```

**The fix was architectural, not prompt-based.** LLM instructions are *guidelines*, not *guarantees*. The workflow now builds the `AgentWorkflow` conditionally:

```csharp
AIAgent[] availableAgents = isProfileReady
    ? [profileAgent, advisorAgent, stockAgent]
    : [profileAgent];  // Only profile agent until PROFILE_READY
```

If `PROFILE_READY:` isn't in the message history, the advisor and stock agents **physically don't exist** as handoff targets. The orchestrator literally cannot route to them. No prompt compliance required.

Safety nets were added too: a 60-second `CancellationToken` timeout and a 25-invocation max guard, both producing user-friendly messages instead of HTTP timeouts.

## Act V — Teaching the Orchestrator About Stocks

With the loop killed, a subtler routing problem emerged. The orchestrator's prompt mapped *"What stocks should I buy?"* to `advisor_agent`, not to the stock-specialized agent. The stock agent was reserved for narrow queries about *"company financials, annual reports, revenue"*.

But the stock agent from Foundry was built for **everything stock-related** — picks, recommendations, analysis, company data, all grounded in real annual reports. The orchestrator's routing table needed a complete rewrite:

- **Anything touching stocks** → `stock-specialized-investment-agent`
- **General non-stock financial advice** (retirement, bonds, budgeting) → `advisor_agent`
- **Unsupported specializations** (real estate, crypto, commodities) → Orchestrator responds directly: *"We don't currently offer specialized advisory for that area."*

The advisor also learned a new rule: if a user asks *"What should I buy?"* or anything requiring specialized knowledge, hand off to the orchestrator immediately — don't try to answer from general knowledge.

## The Numbers

| Metric | Before | After |
|--------|--------|-------|
| Session messages after restore | 0 (always) | Correct count |
| Max workflow duration (no profile) | 100s timeout | <3s (profile gate) |
| Stock query routing accuracy | ~50% (LLM-dependent) | 100% (code-enforced) |
| Unit tests | 75 passing | 75 passing |
| Integration tests | 6 passing, 1 timeout | 8 passing |

## Files Changed

```
src/FinWise.MultiAgentWorkflow/Infrastructure/AgentSessionStore/IAgentSessionStore.cs  (+7)
src/FinWise.MultiAgentWorkflow/Session/AgentSessionManager.cs                          (+35 -28)
src/FinWise.MultiAgentWorkflow/Session/AgentSessionConstants.cs                        (+11)
src/FinWise.MultiAgentWorkflow/Workflow/FinWiseWorkflowService.cs                      (+94 -56)
src/FinWise.MultiAgentWorkflow/Agents/OrchestratorAgent/OrchestratorAgent.prompt.md    (+54)
src/FinWise.MultiAgentWorkflow/Agents/AdvisorAgent/AdvisorAgent.prompt.md              (+40)
tests/FinWise.McpServer.IntegrationTests/EndToEndMcpTests.cs                           (+235 -73)
```

## Lessons

1. **Pre-release SDK upgrades are behavioral upgrades.** API renames get caught by the compiler. Behavioral changes in service registration don't. When upgrading preview → RC, test the *semantics* of every integration point, not just the syntax.

2. **Don't trust LLMs for control flow.** Prompt instructions are probabilistic. If a routing decision is critical (like gating advisor access behind profile completion), enforce it in code. Make the wrong path physically impossible, not just discouraged.

3. **Own your message persistence.** Framework-managed message stores are convenient until they silently change. Serializing messages independently — in a format you control — makes your system resilient to SDK internals shifting under you.

4. **Infinite loops need circuit breakers.** Any multi-agent workflow with bidirectional handoffs can loop. Max-iteration guards and CancellationToken timeouts are not optimizations — they're safety requirements.

## Looking Forward: The SDK's AgentSessionStore Contract

The independent message serialization fix solved the immediate problem, but it's a **tactical workaround**, not the long-term architecture. A deep review of the [spec 006](../specs/006-custom-agent-session-store-vs-framework/006-custom-agent-session-store-vs-framework.md) against the current SDK source (`microsoft/agent-framework` on GitHub, March 2026) reveals why — and points to a cleaner path.

### What the SDK Actually Provides (Verified March 15, 2026)

The `Microsoft.Agents.AI.Hosting` package (latest: `1.0.0-preview.260311.1`) ships a clear contract:

```csharp
// AgentSessionStore — the standard persistence contract
public abstract class AgentSessionStore
{
    public abstract ValueTask SaveSessionAsync(AIAgent agent, string conversationId, 
        AgentSession session, CancellationToken ct = default);
    public abstract ValueTask<AgentSession> GetSessionAsync(AIAgent agent, 
        string conversationId, CancellationToken ct = default);
}
```

Two implementations ship: `InMemoryAgentSessionStore` (dev/test) and `NoopAgentSessionStore` (stateless). The SDK explicitly recommends building your own for Redis, SQL, or CosmosDB. The serialization — `agent.SerializeSessionAsync()` / `agent.DeserializeSessionAsync()` — happens **inside the store**, not in calling code.

### Why Our Independent Message Serialization is Temporary

The session bug revealed that `GetService<InMemoryChatHistoryProvider>()` broke silently in RC4. But the SDK's **actual intended pattern** is:

1. Messages live in `AgentSession.StateBag` under key `"InMemoryChatHistoryProvider"` as `InMemoryChatHistoryProvider.State { Messages = [...] }`
2. Extension methods `session.TryGetInMemoryChatHistory()` / `session.SetInMemoryChatHistory()` provide the correct read/write API
3. When the session is serialized via `SerializeSessionAsync()`, the StateBag — including all messages — is serialized automatically
4. When deserialized, messages come back in the StateBag and can be read via `TryGetInMemoryChatHistory()`

This means the SDK **already handles message persistence through session serialization** — there's no need for a separate `SerializedMessages` field. Our workaround bypasses the StateBag and duplicates what the framework would do natively.

### The Recommended Migration Path

| Version | Session Store | Message Handling | Notes |
|---------|--------------|------------------|-------|
| **v0.2 (now)** | Custom `IAgentSessionStore` + `SerializedMessages` workaround | Independent serialization | **Current state.** Works but duplicates SDK functionality. |
| **v0.3 next** | Adopt SDK's `AgentSessionStore` abstract class | Use `TryGetInMemoryChatHistory()` / `SetInMemoryChatHistory()` for StateBag-based access | Drop `IAgentSessionStore`, `AgentSessionData`, and `SerializedMessages`. Custom store inherits `AgentSessionStore` and adds `ClearSession`. Set stable `Id` on orchestrator agent. |
| **v0.3 next** | Redis `AgentSessionStore` subclass | Same StateBag-based messages (inside session blob) | Durable session persistence with TTL-based expiry. Drop-in replacement — only the store changes. |
| **v0.5** | Redis for sessions + CosmosDB `ChatHistoryProvider` | Messages externalized to CosmosDB via custom `ChatHistoryProvider` | For queryable history, RAG, vector search. Session blob becomes tiny (just a reference key). |

### Key Findings from the Research

1. **The repo was renamed**: `microsoft/Agents-for-net` → `microsoft/agent-framework` (both .NET and Python in one monorepo now)
2. **Two storage systems, not one**: `AgentSessionStore` (outer container = where the session blob lives) and `ChatHistoryProvider` (inner contents = where messages are managed within the session). The spec correctly identified this suitcase/hotel-safe analogy.
3. **`InMemoryAgentSessionStore` is `sealed`**: Can't extend it. For `ClearSession` support, we'll write our own subclass of `AgentSessionStore` — identical logic, unsealed, with `Clear` added.
4. **Agent `Id` must be stable**: The SDK's composite key is `{agentId}:{conversationId}`. Our orchestrator currently gets a random GUID on each request. Setting `Id = "orchestrator_agent"` in `ChatClientAgentOptions` is a prerequisite.
5. **`AIHostAgent` wraps one agent, not workflows**: It can't be used for FinWise's multi-agent orchestration pattern (which needs mid-lifecycle interception for reset detection, email augmentation, and message deduplication). Manual lifecycle management stays.
6. **Microsoft Learn docs now explicitly say**: *"Persist the full AgentSession, not only message text"* and *"Treat AgentSession as an opaque state object."* — confirming the StateBag-based approach is the intended pattern.

### What This Means for the Spec

The [006 spec](../specs/006-custom-agent-session-store-vs-framework/006-custom-agent-session-store-vs-framework.md) analysis was **validated against the latest SDK source** (`microsoft/agent-framework` on GitHub, March 15, 2026). All technical claims remain accurate:

- `AgentSessionStore` contract: 2 methods (`SaveSessionAsync`, `GetSessionAsync`), unchanged
- `InMemoryAgentSessionStore`: still `sealed`, keys by `{agentId}:{conversationId}`
- `TryGetInMemoryChatHistory()` / `SetInMemoryChatHistory()`: confirmed in `AgentSessionExtensions`
- `InMemoryChatHistoryProvider.State.Messages`: confirmed as `List<ChatMessage>` in `StateBag`
- `AIHostAgent`: still wraps one agent, not workflows
- No persistent store implementations shipped (Redis, CosmosDB, SQL must be built by user)

Updates applied to spec:
1. **Status**: Changed from "Pending Decision" → "Validated Against SDK RC4 (March 15, 2026)"
2. **Validation note**: Added context about the v0.3 `SerializedMessages` workaround and link to this journal entry
3. **Package version**: `Microsoft.Agents.AI.Hosting` is at `1.0.0-preview.260311.1` (March 11, 2026)

New SDK features discovered (not yet in spec, for future reference):
- **`FoundryMemoryProvider`**: External memory provider for Azure AI Foundry — uses `AIProjectClient` for RAG-style memory
- **`Mem0Provider`**: Third-party memory integration via HTTP
- **`WorkflowSession` checkpointing**: `InMemoryCheckpointManager` for workflow state snapshots
- **`InMemoryStorageOptions`**: Size limits (default 1000 items) and sliding expiration (default 1 hour) for in-memory stores

The bottom line: our independent message serialization was the right emergency fix for today. The v0.3 next phase should adopt the SDK's `AgentSessionStore` contract and use `StateBag`-based message management — letting the framework handle what it was designed to handle, and only adding what we uniquely need (session clearing, stable agent IDs, mid-lifecycle workflow hooks).

---

*The stock agent finally delivered its first personalized recommendation through the full pipeline: Tesla, Nvidia, and Microsoft, grounded in actual annual report data, tailored to an aggressive short-term profile. The ghosts were gone. The messages remembered.*
