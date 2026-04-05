# Feature: Orchestration Pattern Evaluation for FinWise (CheckpointManager and graph)

> **Created with:** feature-spec skill
>
> **Status:** BACKLOG — Research complete. Recommendation: **Stay with Handoff + adopt framework-level checkpointing & HITL.**

---

## Executive Summary

After deep analysis of FinWise's current workflow implementation, the Microsoft Agent Framework's six orchestration patterns, and the specific requirements of a financial advisory multi-agent system, the recommendation is:

> **Handoff is the right pattern for FinWise today.** The key research finding is that HITL and checkpointing — previously thought to require graph migration — are **framework-level features available within handoff**. FinWise can adopt `CheckpointManager`, `ApprovalRequiredAIFunction`, and `WorkflowEvent` without changing the orchestration pattern. Graph-based workflows are only needed if FinWise grows to 5+ agents with non-linear routing or needs concurrent agent execution.

---

## Current Architecture Analysis

### How FinWise Works Today

FinWise uses a **history-gated, prompt-routed, hub-and-spoke handoff** pattern:

```
User → MCP Server → FinWiseWorkflowService
  │
  ├─ Build workflow (orchestrator + available agents)
  ├─ Restore session from Redis/in-memory
  ├─ Scan history for PROFILE_READY marker
  │   ├─ NOT found → only profile_agent is reachable
  │   └─ FOUND → rebuild workflow adding advisor_agent + stock_agent
  ├─ Execute handoff workflow (orchestrator routes via LLM prompt)
  ├─ Persist session
  └─ Return response
```

### Current Agents

| Agent | Type | Tools | Role |
|-------|------|-------|------|
| `orchestrator_agent` | Hub (router) | `request_session_reset` + framework handoff functions | Silent router — never outputs text, only calls handoff tools |
| `profile_agent` | Spoke | `get_profile`, `set_profile`, `delete_profile` | Collects user profile (email, risk tolerance, goals, timeframe) |
| `advisor_agent` | Spoke | None (chat-only) | Provides investment recommendations based on profile |
| `stock_agent` | Spoke (Azure AI Foundry) | Foundry-provisioned | Stock-specific investment analysis |

### Key Design Patterns

1. **PROFILE_READY marker**: A text prefix (`PROFILE_READY:`) emitted by `profile_agent` when all required fields are collected. `FinWiseWorkflowService` scans the entire message history with regex to detect it.

2. **Workflow rebuilding**: The service builds the workflow **twice per request** when profile is ready — first with only `profile_agent`, then (after detecting PROFILE_READY) rebuilds with all agents. This ensures the orchestrator can't accidentally route to `advisor_agent` before the profile exists.

3. **Session reset via AsyncLocal**: `SessionResetFlag` uses `AsyncLocal<SessionResetToken>` to propagate a mutable flag from the orchestrator's `request_session_reset` tool back to `FinWiseWorkflowService`. Clever but opaque.

4. **Prompt-driven routing**: The orchestrator's LLM prompt contains explicit routing rules (stock intent → stock agent, profile request → profile agent, general advice → advisor agent). Routing correctness depends on the LLM following these rules.

### What Works Well

- ✅ Simple and easy to understand — 3 agents + 1 router
- ✅ 78 unit tests pass, 8 integration tests pass (when server running)
- ✅ Hub-and-spoke prevents uncontrolled agent-to-agent communication
- ✅ Session persistence via Redis works across requests
- ✅ The PROFILE_READY gate ensures users can't get advice without a profile

### Friction Points

| Issue | Severity | Impact |
|-------|----------|--------|
| **Non-deterministic routing** — LLM decides routing; code has fallback for when orchestrator emits text instead of routing | Medium | Occasional wrong routing, requires defensive code |
| **PROFILE_READY is a text scan** — regex scan over entire chat history per request | Low | Works but fragile; a user message containing "PROFILE_READY:" would break gating |
| **Workflow rebuilt twice per request** — builds handoff graph, loads session, checks marker, rebuilds if needed | Low | Minor perf overhead, code complexity in `ProcessMessageAsync` |
| **AsyncLocal reset flag** — ambient mutable state is hard to trace/test | Low | Works but non-obvious; new developers would struggle to understand it |
| **No checkpointing** — if workflow execution fails mid-conversation, partial state may be lost | Low (currently) | Not an issue for simple flows, becomes critical for longer workflows |

---

## Orchestration Pattern Evaluation

### Pattern-by-Pattern Analysis for FinWise

| Pattern | How It Works | FinWise Fit | Why |
|---------|-------------|-------------|-----|
| **Sequential** | Fixed agent chain: A → B → C | ❌ Low | FinWise routing depends on user intent, not a fixed sequence |
| **Concurrent** | Agents run in parallel, results merged | ⚠️ Low-Medium | Profile must complete before advice. Only useful if advisor + stock ran simultaneously (rare) |
| **Handoff** ← current | Agents transfer control dynamically | ✅ **High** | Matches FinWise's hub-and-spoke routing perfectly |
| **Group Chat** | Agents collaborate in shared conversation | ❌ Low | FinWise agents don't need to converse with each other |
| **Magentic** | Lead agent manages a team with auto-reflection | ⚠️ Medium | Conceptually similar to current orchestrator, but designed for collaborative problem-solving with iteration — overkill for FinWise's linear flow |
| **Graph-based** | Explicit nodes, edges, conditions, checkpointing, HITL | ✅ **High** | Would make the implicit state machine explicit and deterministic |

### Deep Comparison: Handoff vs. Graph for FinWise

| Criterion | Handoff (Current) | Graph-based | Winner |
|-----------|-------------------|-------------|--------|
| **Simplicity** | ~100 lines in `FinWiseWorkflowService` | Would need executor definitions, edge conditions, event handlers | Handoff |
| **Routing determinism** | LLM-prompt driven (probabilistic) | Conditional edges (deterministic code) + LLM for intent classification | Graph |
| **State management** | PROFILE_READY text marker + manual session persistence | Explicit node state + built-in checkpointing | Graph |
| **Testability** | Tests mock agents and verify responses | Can test individual nodes, edges, and conditions in isolation | Graph |
| **Code inspectability** | Routing logic hidden in prompt markdown | Routing logic visible as code (edges + conditions) | Graph |
| **Human-in-the-loop** | Custom `SessionResetFlag` via AsyncLocal | Built-in `WorkflowEvent` with `request_info` pattern | **Both** (HITL is framework-level, available in handoff too) |
| **Fault tolerance** | Session restore from Redis (custom) | Built-in checkpointing with resumption | **Both** (checkpointing is framework-level via `CheckpointManager`) |
| **API stability** | Handoff API is at rc4 | Graph API is at rc4 (same risk) | Tie |
| **Migration effort** | Zero (already in use) | Significant refactor of `FinWiseWorkflowService` | Handoff |
| **Concurrent agents** | Not supported | Native parallel node execution | Graph |
| **Team familiarity** | Known pattern, well-understood | New API to learn | Handoff |

### Score Summary

> **Critical finding from research**: HITL (`ApprovalRequiredAIFunction`, `WorkflowEvent` with `RequestInfoEvent`) and checkpointing (`CheckpointManager.CreateInMemory()` / file-based / custom) are **framework-level features available to all workflow types, including handoff**. They are NOT exclusive to graph-based workflows. This significantly narrows the gap.

| Criterion | Weight | Handoff | Handoff + Framework Features | Graph |
|-----------|--------|---------|------------------------------|-------|
| Works today (proven) | 25% | ⭐⭐⭐ | ⭐⭐⭐ | ⭐ |
| Routing reliability | 20% | ⭐⭐ | ⭐⭐ | ⭐⭐⭐ |
| State management quality | 15% | ⭐⭐ | ⭐⭐⭐ (with CheckpointManager) | ⭐⭐⭐ |
| Code maintainability | 15% | ⭐⭐ | ⭐⭐ | ⭐⭐⭐ |
| Migration risk | 15% | ⭐⭐⭐ | ⭐⭐⭐ (incremental) | ⭐ |
| Future extensibility | 10% | ⭐⭐ | ⭐⭐ | ⭐⭐⭐ |
| **Weighted total** | | **2.35** | **2.55** | **2.2** |

**Result: Handoff with framework-level features (HITL + checkpointing) wins.** Graph migration is unnecessary for FinWise's current complexity level.

---

## Recommendation

### Decision: Stay with Handoff + Adopt Framework-Level HITL and Checkpointing

**Rationale:**

1. **Handoff works and is proven.** 78 tests pass. The friction points are cosmetic, not functional.

2. **HITL and checkpointing don't require graph migration.** This was the key research finding. `CheckpointManager` and `ApprovalRequiredAIFunction`/`WorkflowEvent` are framework-level features. FinWise can adopt them **within the current handoff architecture** to replace the custom `SessionResetFlag` (AsyncLocal).

3. **Redis session persistence serves a different purpose than `CheckpointManager`, but both can scale.** Deep analysis revealed:
   - **Agent session persistence** (`agentsession:*` keys) — stores the full SDK `AgentSession` object including chat history **across MCP requests**. This is **request-level** persistence.
   - **MCP session migration** (`mcpinit:*` keys) — stores MCP initialize params for cross-instance transport resumption. Only Redis can serve this (shared state with sliding TTL).
   - `CheckpointManager` operates at **workflow-level** (superstep boundaries **within** a single request) — different granularity.
   - **However**, `CheckpointManager` CAN scale via **`CosmosCheckpointStore`** (`Microsoft.Agents.AI.CosmosNoSql` package, preview). This stores checkpoints in Cosmos DB — globally distributed, shared across all AKS/ACA instances. FinWise already uses Cosmos DB for user profiles, so the infrastructure exists.
   - The `ICheckpointStore<T>` interface also allows custom implementations (e.g., Redis-backed).
   - **Conclusion**: `CheckpointManager` is additive. Redis stays for MCP migration. For agent session persistence, a future option is to migrate from Redis `AgentSessionStore` to Cosmos-backed checkpointing — consolidating on Cosmos for both profiles and workflow state. But this is a significant architectural change.

4. **Graph is only better for routing determinism and concurrency.** The remaining advantages of graph (conditional edges, parallel execution) don't apply to FinWise's simple 3-4 agent linear flow.

5. **Both APIs are at rc4.** Migrating from one rc4 API to another gains zero stability.

### Scalability Considerations for Load-Balanced MCP Servers

For FinWise running on AKS/ACA with multiple load-balanced pods (no sticky sessions):

| Component | Current Approach | Scalable? | Alternative |
|-----------|-----------------|-----------|------------|
| **Agent session persistence** | Redis (`agentsession:*`) | ✅ Redis is shared | Could migrate to Cosmos via custom `ChatHistoryProvider` or `AgentSessionStore` |
| **MCP session migration** | Redis (`mcpinit:*`) | ✅ Redis is shared | No alternative — Redis is the right tool for short-lived cross-instance state |
| **Workflow checkpointing** | Not implemented | N/A | `CosmosCheckpointStore` via `Microsoft.Agents.AI.CosmosNoSql` (preview) |
| **User profiles** | Cosmos DB | ✅ Globally distributed | Already optimal |

### Scalable `CheckpointManager` — Deep Research Findings

The framework provides a complete extensibility hierarchy for checkpoint persistence:

```
ICheckpointStore<T> (interface — in Microsoft.Agents.AI.Workflows)
  └── JsonCheckpointStore (abstract base)
        ├── InMemoryJsonCheckpointStore (built-in — NOT scalable)
        ├── FileSystemJsonCheckpointStore (built-in — NOT scalable, local disk only)
        ├── CosmosCheckpointStore<T> (built-in — ✅ SCALABLE via Cosmos DB)
        │     Package: Microsoft.Agents.AI.CosmosNoSql v1.0.0-preview.260311.1
        │     Constructors: CosmosClient, connection string, or TokenCredential
        └── Custom (implement ICheckpointStore<T> for Redis, SQL, etc.)
```

**`CosmosCheckpointStore` details:**
- **Package**: `Microsoft.Agents.AI.CosmosNoSql` (preview — same release track as `Microsoft.Agents.AI.Hosting`)
- **Constructors**: `new CosmosCheckpointStore(cosmosClient, databaseId, containerId)` — or connection string / TokenCredential variants
- **Convenience extension**: `CosmosDBWorkflowExtensions.CreateCheckpointStore<T>(connectionString, databaseId, containerId)`
- **Integration**: Pass to `CheckpointManager.CreateJson(store)`, then to `InProcessExecution.RunStreamingAsync(workflow, input, checkpointManager)`
- **Scalability**: Cosmos DB is globally distributed — checkpoints are accessible from any AKS/ACA pod
- **FinWise fit**: FinWise **already uses Cosmos DB** (`Microsoft.Azure.Cosmos 3.46.1`) for user profiles. The same Cosmos account/database could host a `workflow-checkpoints` container.

**`AgentSessionStore` extensibility (for conversation-level persistence):**
The framework's `AgentSessionStore` is abstract with this explicit guidance in the docs:
> *"For production use with multiple instances or persistence across restarts, use a durable storage implementation such as Redis, SQL Server, or Azure Cosmos DB."*

The framework also provides a newer **`ChatHistoryProvider`** pipeline pattern for custom storage:
- Override `ProvideChatHistoryAsync` (load) and `StoreChatHistoryAsync` (persist)
- The docs reference [Redis-backed Sessions sample](https://github.com/microsoft/agent-framework/blob/main/python/samples/02-agents/conversations/redis_chat_message_store_session.py) as a pattern for external persistence

**What this means for FinWise's scalability strategy:**

| Scenario | Architecture |
|----------|-------------|
| **Current (working)** | Redis for agent sessions + MCP migration. Cosmos for profiles. No checkpointing. |
| **Add checkpointing** | Add `CosmosCheckpointStore` to the existing workflow execution. Redis stays. Cosmos adds a `workflow-checkpoints` container. |
| **Consolidate on Cosmos** | Migrate `RedisAgentSessionStore` to a Cosmos-backed `AgentSessionStore` or `ChatHistoryProvider`. Redis reduced to MCP migration only. Single database for profiles + sessions + checkpoints. |
| **Full Cosmos + Graph** | Graph workflows with `CosmosCheckpointStore`, Cosmos-backed session store, Cosmos profiles. Redis only for MCP migration. Maximum scalability with minimum moving parts. |

Each step is independent and can be adopted incrementally.

### Immediate Opportunities (Within Handoff, No Migration)

These improvements can be done incrementally without changing the orchestration pattern:

| Improvement | What | Replaces | Effort |
|-------------|------|----------|--------|
| **Adopt `CheckpointManager` with `CosmosCheckpointStore`** | Add Cosmos DB-backed checkpointing to handoff execution for mid-workflow fault tolerance, scalable across pods | Nothing (new capability) — Redis session store remains for cross-request persistence and MCP migration | Medium |
| **Adopt `ApprovalRequiredAIFunction`** | Wrap sensitive tools (e.g., `set_profile`, `delete_profile`) with approval middleware | N/A (no approval exists today) | Low |
| **Replace `SessionResetFlag`** | Use framework `WorkflowEvent` with `RequestInfoEvent` for reset requests | Custom `AsyncLocal<SessionResetToken>` pattern | Medium |
| **Replace `PROFILE_READY` text marker** | Store profile-ready state as structured data in session state, not as a text marker in chat history | Regex scan of entire chat history | Medium |
| **Migrate agent session to Cosmos** (optional, longer-term) | Implement Cosmos-backed `AgentSessionStore` or `ChatHistoryProvider`, reducing Redis to MCP migration only | `RedisAgentSessionStore` | High |

> **Note**: The `RedisSessionMigrationHandler` (MCP `mcpinit:*` keys) stays regardless — it serves short-lived cross-instance transport state with sliding TTL, which is Redis's sweet spot.

### Graph Migration Triggers (When Handoff is No Longer Sufficient)

Migrate to graph-based workflows only when **ALL** of these are true:

1. Agent Framework reaches **GA (1.0.0)** — API stability makes migration safe
2. **AND** at least one of these complexity triggers:

| Trigger | Why Graph Is Needed |
|---------|---------------------|
| FinWise adds **5+ agents** with non-linear routing | Handoff prompt-routing becomes unwieldy |
| FinWise needs **concurrent agent execution** | Advisor + Stock running in parallel |
| FinWise needs **complex conditional routing** beyond "is profile ready?" | Switch-case edges with multiple conditions |
| FinWise needs **declarative workflow definitions** | YAML/JSON workflow specs for compliance/audit |
| The orchestrator prompt **reliably misroutes** despite prompt improvements | Deterministic graph edges replace probabilistic LLM routing |

### Incremental Path

Whether adopting framework features now or graph later, don't do a big-bang migration:

**Phase 1 (Now — within handoff):**
1. Adopt `CheckpointManager` with `CosmosCheckpointStore` for scalable workflow state persistence
2. Replace `PROFILE_READY` text marker with structured state in session/checkpoint
3. Replace `SessionResetFlag` AsyncLocal with framework `WorkflowEvent`

**Phase 2 (Consolidate storage — reduce Redis dependency):**
4. Migrate `RedisAgentSessionStore` to Cosmos-backed `AgentSessionStore` or `ChatHistoryProvider`
5. Redis reduced to MCP migration only (`mcpinit:*` keys)

**Phase 3 (At GA + complexity trigger — graph migration):**
6. Replace double-build pattern with graph conditional edges
7. Move routing from orchestrator prompt to graph switch-case edges
8. Add concurrent execution for advisor + stock agents where applicable

Each step is independently testable and deployable.

---

## Appendix: What a Graph-Based FinWise Would Look Like

```
[Entry] → [Profile Check Node]
              │
              ├─ profile NOT ready → [Profile Agent Executor]
              │                            │
              │                            └─ emits PROFILE_COMPLETE event → back to [Profile Check Node]
              │
              └─ profile IS ready → [Intent Classification Node]
                                         │
                                         ├─ stock intent → [Stock Agent Executor]
                                         ├─ profile update → [Profile Agent Executor]
                                         ├─ reset request → [Reset Handler Node]
                                         └─ general advice → [Advisor Agent Executor]
                                                                  │
                                                                  └─ [Response Aggregation] → [Exit]
```

Key differences from current:
- **Profile Check** is a deterministic code node (check structured state), not an LLM scanning chat history
- **Intent Classification** is a dedicated node that can be an LLM call or a rule engine — separate from routing
- **Routing** is graph edges with conditions — not embedded in a prompt
- **Checkpoints** at each node enable resumption

---

## Learnings & Notes

### Key Insight from the Analysis

The current FinWise architecture is **already graph-like in spirit** — it has nodes (agents), edges (handoff permissions), conditions (PROFILE_READY check), and state (session history). The handoff builder is just the simplest way to express this particular graph. The real question isn't "handoff vs. graph" — it's "should the graph be implicit (in prompts and code) or explicit (in the framework)?"

For 3-4 agents with linear progression, implicit is simpler. For 5+ agents with complex routing, explicit wins.

### Discovered: Handoff is Implemented on Top of Graphs

The Microsoft docs state: "Internally, the handoff orchestration is implemented using a mesh topology." This means handoff IS a graph — just a pre-built one. Migration to explicit graphs is a refinement, not a paradigm shift.

### Discovered: HITL and Checkpointing Are Framework-Level (Not Graph-Exclusive)

**This was the most important finding of the deep research.** The initial assumption was that HITL and checkpointing required graph-based workflows. The Microsoft Learn docs and code samples prove otherwise:

- **`CheckpointManager`** (`Microsoft.Agents.AI.Workflows.Checkpointing`) works with `InProcessExecution.RunStreamingAsync(workflow, input, checkpointManager)` — any workflow type, including handoff.
- **`CosmosCheckpointStore`** (`Microsoft.Agents.AI.CosmosNoSql` package, preview) provides Cosmos DB-backed checkpoint persistence — globally distributed and scalable across AKS/ACA pods.
- **`ApprovalRequiredAIFunction`** wraps any `AIFunction` to require human approval before execution. Works with any agent, not just graph executors.
- **`WorkflowEvent` with `RequestInfoEvent`** enables request/response patterns for external input. Available in all workflow types.
- **`AgentExecutor`** bridges agents into workflow graphs and manages session state, conversation context, streaming — available whether using handoff or custom graph.

This means FinWise can get HITL, checkpointing, AND scalable Cosmos persistence **without leaving handoff**.

### Discovered: Persistence Layers Serve Different Granularities

`CheckpointManager` and Redis `AgentSessionStore` are **complementary, not competing**:

| Layer | Granularity | What It Stores | Scalable Backend |
|-------|-------------|---------------|-----------------|
| **`CheckpointManager`** | Workflow (superstep) | Executor states, pending messages, shared state — within a single workflow execution | `CosmosCheckpointStore` (Cosmos DB) or custom `ICheckpointStore<T>` |
| **`AgentSessionStore`** | Request (conversation) | Full `AgentSession` including chat history — across MCP requests | Custom implementation (Redis today, could be Cosmos) |
| **`RedisSessionMigrationHandler`** | Transport (instance) | MCP initialize params for cross-instance session migration | Redis only (short-lived, sliding TTL) |

**`CheckpointManager` is additive.** Redis stays for MCP migration. For agent sessions, the framework's `AgentSessionStore` is abstract and explicitly designed for custom backends — the docs recommend Redis, SQL Server, or Cosmos DB for production. The newer `ChatHistoryProvider` pipeline offers a more flexible alternative with `ProvideChatHistoryAsync`/`StoreChatHistoryAsync` overrides.

**Future consolidation path:**

```
Current:  Redis (sessions + MCP migration) + Cosmos (profiles)
Step 1:   Redis (sessions + MCP migration) + Cosmos (profiles + workflow checkpoints)
Step 2:   Redis (MCP migration only) + Cosmos (profiles + sessions + checkpoints)
```

### Discovered: Context Synchronization in Handoff — Deep Research (2026-03-28)

> **Preview API**: Uses `Microsoft.Agents.AI` `1.0.0-rc4`. May change before GA.

The Microsoft Learn docs state: _"In a Handoff orchestration, agents **do not** share the same session instance, participants are responsible for ensuring context consistency. To achieve this, participants are designed to broadcast their responses or user inputs received to all others in the workflow whenever they generate a response."_

**Initial concern**: FinWise uses a shared `messageHistory` (`List<ChatMessage>`) that all agents see — which appeared to conflict with the framework's "separate session instances" model.

**Resolution after deep research: FinWise's approach is CORRECT and ALIGNED with the framework.**

Examining the framework source code (`HandoffAgentExecutor.cs`, `HandoffState.cs`, `AIAgentsAbstractionsExtensions.cs` in `microsoft/agent-framework`) reveals that the framework internally uses the same pattern FinWise does:

#### How the Framework Actually Works (Source Code Analysis)

1. **`HandoffState` carries a single shared `List<ChatMessage> Messages`** — the full conversation history is passed as one list between executors via `HandoffState(TurnToken, InvokedHandoff, Messages, CurrentAgentId)`. This is the same pattern as FinWise's shared `messageHistory`.

2. **Role reassignment is the broadcasting mechanism** — Before each agent runs, `ChangeAssistantToUserForOtherParticipants(targetAgentName)` temporarily changes all `Assistant`-role messages from OTHER agents to `User` role. This ensures the LLM sees other agents' outputs as "user context" rather than its own prior responses. After the agent completes, `ResetUserToAssistantForChangedRoles()` reverts the changes.

   ```csharp
   // From HandoffAgentExecutor.HandleAsync() — framework source code
   List<ChatMessage> allMessages = message.Messages;  // ← shared list
   List<ChatMessage>? roleChanges = allMessages.ChangeAssistantToUserForOtherParticipants(
       this._agent.Name ?? this._agent.Id);
   // ... agent runs with role-modified shared history ...
   allMessages.AddRange(agentResponse.Messages);  // ← append to shared list
   roleChanges.ResetUserToAssistantForChangedRoles();  // ← revert roles
   return new(message.TurnToken, requestedHandoff, allMessages, currentAgentId);
   ```

3. **Tool call filtering preserves clean context** — `HandoffMessagesFilter` strips handoff-specific `FunctionCallContent` and `FunctionResultContent` from the history before forwarding to the next agent. Only user and agent TEXT messages are synchronized. The docs confirm: _"Tool related contents, including handoff tool calls, are not broadcasted to other agents."_

4. **Dedicated `HandoffAgentExecutor`** — Unlike standard workflows that use `AIAgentHostExecutor`, handoff orchestration uses a specialized `HandoffAgentExecutor` with custom routing logic (handoff tool injection, handoff detection, tool call filtering). This executor manages the shared message list through the graph's switch-case routing.

#### What "Separate Session Instances" Actually Means

The documentation's warning about separate sessions refers to the **`AgentSession` abstraction** — the provider-specific wrapper that manages:
- Token counts and context windows
- Remote conversation IDs (e.g., OpenAI thread IDs)
- Service-managed chat history (for providers like Azure OpenAI Responses API)
- Session serialization format

The docs explain: _"Agents do not share the same session instance because different agent types (`ChatClientAgent`, `OpenAIResponseAgent`, `AzureAIAgent`, `A2AAgent`) may have different implementations of the `AgentSession` abstraction. Sharing the same session instance could lead to inconsistencies in how each agent processes and maintains context."_

This is a **type-safety concern**, not a message-history-isolation concern. The message history (the actual conversation content) IS shared — just with role reassignment.

#### FinWise Alignment

| Aspect | Framework Internal Mechanism | FinWise Implementation | Aligned? |
|--------|------------------------------|------------------------|----------|
| **Message history** | Single shared `List<ChatMessage>` via `HandoffState.Messages` | Single shared `List<ChatMessage>` via `messageHistory` | ✅ Yes |
| **Role reassignment** | `ChangeAssistantToUserForOtherParticipants()` before each agent run | Handled automatically by framework when FinWise calls `InProcessExecution.RunStreamingAsync()` | ✅ Yes (framework handles it) |
| **Tool call filtering** | `HandoffMessagesFilter` strips handoff tool calls | Handled automatically by framework's `HandoffAgentExecutor` | ✅ Yes (framework handles it) |
| **AgentSession per agent** | Each `HandoffAgentExecutor` calls `_agent.RunStreamingAsync()` which creates/uses its own session | FinWise creates one session via `AgentSessionManager` for persistence; framework creates per-agent sessions internally during workflow execution | ✅ Compatible |
| **Message deduplication** | Not explicitly handled (each agent appends to shared list) | `AppendUniqueMessages()` deduplicates by `Role:Author:Text` signature | ⚠️ FinWise adds extra safety |
| **Cross-request persistence** | Not handled by framework (framework is stateless per workflow run) | FinWise persists `messageHistory` to Redis via `AgentSessionStore` between MCP requests | ✅ Orthogonal (different concern) |

#### Key `AIAgentHostOptions` Defaults (for standard executors)

| Option | Default | Relevance |
|--------|---------|-----------|
| `ReassignOtherAgentsAsUsers` | `true` | Messages from other agents → `User` role (same as handoff's role reassignment) |
| `ForwardIncomingMessages` | `true` | Incoming messages forwarded to downstream executors |
| `EmitAgentUpdateEvents` | `null` (disabled) | Streaming update events |
| `EmitAgentResponseEvents` | `false` | Aggregated response events |

#### Mesh Topology Note

The docs confirm: _"Even with custom handoff rules, all agents are still connected in a mesh topology. This is because agents need to share context with each other to maintain conversation history. The handoff rules only govern which agents can take over the conversation next."_

This validates FinWise's hub-and-spoke topology where the orchestrator sees all messages and spoke agents see the full conversation when control is handed to them.

#### Comparison: Handoff vs. Group Chat Context Sync

| Aspect | Handoff | Group Chat |
|--------|---------|------------|
| **Session sharing** | Agents do NOT share session instances | Agents do NOT share session instances |
| **Broadcasting** | Participants broadcast to all others | Orchestrator broadcasts to all others |
| **Who broadcasts** | Each participant (peer-to-peer) | Central orchestrator (star topology) |
| **Tool filtering** | Handoff tool calls filtered out | N/A (no handoff tools) |
| **History visibility** | Full conversation history (role-reassigned) | Full conversation history (role-reassigned) |

#### Implications for FinWise

1. **No migration needed.** FinWise's shared `messageHistory` pattern is how the framework works internally. The framework's `HandoffAgentExecutor` handles role reassignment and tool filtering automatically when `InProcessExecution.RunStreamingAsync(workflow, messageHistory)` is called.

2. **`AppendUniqueMessages()` is defensive but safe.** The framework doesn't deduplicate — it relies on each executor appending only its own new messages. FinWise's deduplication is an extra safety layer that prevents duplicate messages if the event stream yields messages already present in the history.

3. **`AgentSessionRunContext` (AsyncLocal) is orthogonal.** The framework's context sync happens at the workflow executor level. FinWise's `AgentSessionRunContext` provides ambient access to the shared history for TOOLS (like `SetProfile`) — a different concern (tool input validation) that doesn't conflict with the framework's broadcasting.

4. **Future-safe.** If the framework changes its internal broadcasting mechanism, FinWise is protected because it delegates workflow execution to `InProcessExecution.RunStreamingAsync()` rather than implementing broadcasting manually.

#### Research Sources

- Microsoft Learn: Handoff Orchestration C# — Context Synchronization section (`learn.microsoft.com/agent-framework/workflows/orchestrations/handoff#context-synchronization`)
- Microsoft Learn: Agent Executor — Configuration Options, Output and Chaining, Shared Sessions (`learn.microsoft.com/agent-framework/workflows/advanced/agent-executor`)
- Microsoft Learn API Reference: `AIAgentHostOptions` class and properties (`learn.microsoft.com/dotnet/api/microsoft.agents.ai.workflows.aiagenthostoptions`)
- GitHub source: `HandoffAgentExecutor.cs` (`microsoft/agent-framework/dotnet/src/Microsoft.Agents.AI.Workflows/Specialized/HandoffAgentExecutor.cs`)
- GitHub source: `HandoffState.cs` — `record class HandoffState(TurnToken, InvokedHandoff, List<ChatMessage> Messages, CurrentAgentId)`
- GitHub source: `AIAgentsAbstractionsExtensions.cs` — `ChangeAssistantToUserForOtherParticipants()` and `ResetUserToAssistantForChangedRoles()`
- GitHub source: `HandoffsWorkflowBuilder.cs` — mesh topology construction with `WorkflowBuilder.AddSwitch()`
- Microsoft Learn: Python 2026 Significant Changes — Handoff refactored to broadcasting model (`learn.microsoft.com/agent-framework/support/upgrade/python-2026-significant-changes`)

### Research Sources

- Microsoft Learn: Agent Framework Workflows Overview (`learn.microsoft.com/agent-framework/workflows/overview`)
- Microsoft Learn: Handoff Orchestration C# (`learn.microsoft.com/agent-framework/workflows/orchestrations/handoff`)
- Microsoft Learn: Edges (`learn.microsoft.com/agent-framework/workflows/edges`) — conditional, switch-case, fan-out, fan-in
- Microsoft Learn: Checkpoints (`learn.microsoft.com/agent-framework/workflows/checkpoints`) — `CheckpointManager` API
- Microsoft Learn: HITL (`learn.microsoft.com/agent-framework/workflows/hitl`) — `RequestPort`, `RequestInfoEvent`
- Microsoft Learn: Agent Executor (`learn.microsoft.com/agent-framework/workflows/executors`)
- Microsoft Learn: AG-UI HITL (`learn.microsoft.com/agent-framework/integrations/ag-ui/human-in-the-loop`) — `ApprovalRequiredAIFunction`
- Microsoft Learn: Storage (`learn.microsoft.com/agent-framework/agents/conversations/storage`) — `AgentSessionStore`, `ChatHistoryProvider`, built-in storage modes
- Microsoft Learn: Declarative Workflows (`learn.microsoft.com/agent-framework/workflows/declarative`) — YAML/JSON
- Microsoft Learn: AutoGen Migration Guide (`learn.microsoft.com/agent-framework/autogen-migration`)
- Microsoft Learn API Reference: `CosmosCheckpointStore<T>` (`learn.microsoft.com/dotnet/api/microsoft.agents.ai.workflows.checkpointing.cosmoscheckpointstore-1`)
- Microsoft Learn API Reference: `CosmosDBWorkflowExtensions.CreateCheckpointStore` (`learn.microsoft.com/dotnet/api/microsoft.agents.ai.workflows.cosmosdbworkflowextensions.createcheckpointstore`)
- Microsoft Learn API Reference: `AgentSessionStore` abstract class (`learn.microsoft.com/dotnet/api/microsoft.agents.ai.hosting.agentsessionstore`)
- Microsoft Learn API Reference: `InMemoryAgentSessionStore` — docs explicitly recommend Redis/SQL/Cosmos for production
- Microsoft Learn API Reference: `ICheckpointStore<T>` interface (`learn.microsoft.com/dotnet/api/microsoft.agents.ai.workflows.checkpointing.icheckpointstore-1`)
- Microsoft Learn: AI agents in Azure Cosmos DB (`learn.microsoft.com/azure/cosmos-db/ai-agents`) — agent memory patterns
- Microsoft Learn: Cosmos DB integration with Foundry Agent Service (`learn.microsoft.com/azure/cosmos-db/gen-ai/azure-agent-service`)
- NuGet: `Microsoft.Agents.AI` package page — feature descriptions
- NuGet: `Microsoft.Agents.AI.CosmosNoSql` version history via flat-container API — confirmed preview-only, latest `1.0.0-preview.260311.1`
- NuGet: `Microsoft.Agents.AI.Hosting` version history — confirmed preview-only release track
- GitHub: Agent Framework dotnet samples (`github.com/microsoft/agent-framework/tree/main/dotnet/samples/03-workflows`)
- GitHub: Agent Framework Python Redis session sample (`github.com/microsoft/agent-framework/blob/main/python/samples/02-agents/conversations/redis_chat_message_store_session.py`)
