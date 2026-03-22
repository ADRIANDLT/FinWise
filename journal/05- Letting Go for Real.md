# 05 — Letting Go the custom implementation of InMemoryAgentSessionStore, for Real

*March 21, 2026 — The session where 145 lines of custom infrastructure died and the SDK took over*

---

## The Plan Meets Reality

Journal 04 ended with a plan. A careful, seven-step plan to migrate from our custom `IAgentSessionStore` to the SDK's `AgentSessionStore`. Spec 006 had analyzed every risk, resolved every blocker, and laid out the exact file changes. All that remained was typing.

The plan said: *"Delete ~150 lines of custom infrastructure. Add ~20 lines. Net reduction ~130 lines. No behavioral change."*

It was right. Almost.

---

## Act I — The Easy Part

Steps 1 and 2 went exactly as planned.

**Step 1** added `Microsoft.Agents.AI.Hosting` to [`Directory.Packages.props`](../Directory.Packages.props) and the project file. One package reference. Build passed.

**Step 2** changed all three agent factories — [`OrchestratorAgentFactory`](../src/FinWise.MultiAgentWorkflow/Agents/OrchestratorAgent/OrchestratorAgentFactory.cs), [`AdvisorAgentFactory`](../src/FinWise.MultiAgentWorkflow/Agents/AdvisorAgent/AdvisorAgentFactory.cs), and [`UserProfileAgentFactory`](../src/FinWise.MultiAgentWorkflow/Agents/UserProfileAgent/UserProfileAgentFactory.cs) — from the convenience constructor to `ChatClientAgentOptions` with a stable `Id`. This was the critical correctness fix: the SDK's store keys by `{agentId}:{conversationId}`, so a random GUID per instance would lose sessions between requests.

> **Developer:** "For STEP 2, you put the ID with the same value as the Agent's name. Is that ok/enough? Or would we need a stronger unique ID?"

The answer: the agent name *is* the right ID. It's deterministic across requests and unique within the workflow. A "stronger" ID (GUID, hash) would either be random (broken) or deterministic (same guarantee, harder to debug). The global uniqueness comes from the composite key — the `conversationId` half is already a GUID.

---

## Act II — The Core Rewrite

**Step 3** was the heart of the refactoring: rewriting [`AgentSessionManager`](../src/FinWise.MultiAgentWorkflow/Session/AgentSessionManager.cs). The old version manually called `agent.SerializeSessionAsync()`, packaged everything into `AgentSessionData` (seven fields), stored serialized messages independently (the Journal 03 workaround), and had a `DeserializeMessages()` helper.

The new version delegates everything to the SDK. `GetOrCreateSessionAsync` calls the store's `GetSessionAsync` (which handles create-or-deserialize internally) and extracts messages via `TryGetInMemoryChatHistory()`. `PersistSessionAsync` writes messages via `SetInMemoryChatHistory()` then saves. No serialize. No deserialize. No helper methods.

One surprise: the compiler flagged `TryGetInMemoryChatHistory(out List<ChatMessage> messages)` with CS8600 — the out parameter might be null. The fix was `out List<ChatMessage>? messages` with an explicit `is not null` check. A comment explains this is defensive coding against a Preview SDK.

**Step 4** updated [`FinWiseWorkflowService`](../src/FinWise.MultiAgentWorkflow/Workflow/FinWiseWorkflowService.cs) — constructor changed from `IAgentSessionStore` to `AgentSessionStore`, `PersistSessionAsync` dropped from 5 args to 4 (no more `persistedUserId`). 

**Step 5** swapped [`Program.cs`](../src/FinWise.McpServer/Program.cs) from two custom `using` lines to one SDK import.

**Step 6** deleted the dead files: `IAgentSessionStore.cs` (the interface + `AgentSessionData` record), `InMemoryAgentSessionStore.cs`, and `InMemoryAgentSessionStoreTests.cs`. 215 lines removed with `git rm`.

Build green. 70 tests pass (75 minus the 5 deleted store tests).

---

## Act III — Testing What We Built

The deleted store tests left a gap: `AgentSessionManager` itself had never been tested directly. So we wrote [`AgentSessionManagerTests.cs`](../tests/FinWise.MultiAgentWorkflow.UnitTests/AgentSessionManagerTests.cs) — initially 5 tests, expanded to 7 after the Critic review:

1. New session returns empty messages
2. Persist-then-restore round-trips preserve messages
3. Multiple requests accumulate messages
4. Different session IDs are isolated
5. `ClearSessionAsync` doesn't throw
6. Empty message list round-trips correctly
7. Different agent instances with the same `Id` share sessions

Test #7 was particularly satisfying — it validates that the SDK's composite key (`{agentId}:{conversationId}`) works correctly even when agents are recreated per request (which is exactly what FinWise does).

---

## Act IV — Integration and the GPT-4.1 Misfire

With the server running, all 8 integration tests passed. One initially failed (`SameSession_AfterProfileSetup_ShouldRetainSessionContext`) but passed on re-run — LLM flakiness, not a regression.

The logs confirmed the stock agent in Azure AI Foundry worked end-to-end:

```
aa4b86e3 Agent invoked: stock_specialized_investment_agent_... (invocation 4/25)
aa4b86e3 Assistant message from stock-specialized-investment-agent: Based on your aggressive
         investment profile and short-term capital gain goals, the following stocks ...
```

An amusing detour: a VS Code test showed GPT-4.1 refusing a stock question with "Sorry, I can't assist with that" — zero tool calls. The `0x` in the VS Code UI confirmed it. The host LLM's safety filter short-circuited the MCP tool call before it ever reached FinWise. Not our bug.

---

## Act V — The Critic Speaks

We sent the full refactoring to the Critic agent for review. The verdict: **0 critical issues, 5 important, 3 suggestions**.

The fixes we applied immediately:
- **Inconsistent null guards** — mixed `?? throw` and `ThrowIfNull` in the constructor. Aligned to `ThrowIfNull` everywhere.
- **Factory comments** — added comments explaining why `Id` must be stable (SDK composite key).
- **Two edge-case tests** — empty message list and different agent instance with same Id.
- **Unnecessary GUID allocation** — `persistedUserId` fallback generated a GUID per request just for logging. Changed to static `"anonymous"`.

The findings we deferred (with justification):
- **`ClearSessionAsync` no-op** — by design; resolves in Redis (spec 007).
- **Triple session restore** — pre-existing; tracked in spec 007 as optimization when moving to Redis.
- **Package version mismatch** — intentional; Hosting ships on a different cadence than the rc4 packages.

---

## What We Learned

### About the Technology

- The SDK's `TryGetInMemoryChatHistory()` / `SetInMemoryChatHistory()` extension methods (added in RC4, Feb 26 2026) are the correct way to access messages in `AgentSession`. They bypass the broken `GetService<InMemoryChatHistoryProvider>()` path entirely.
- `ChatClientAgentOptions.Id` must be stable and deterministic for the SDK's store composite key. Using the agent's `Name` property is correct — not a GUID, not a hash.
- The `Microsoft.Agents.AI.Hosting` package ships on a different release cadence than the core `Microsoft.Agents.AI` packages. Version `1.0.0-preview.260311.1` is compatible with `1.0.0-rc4`.

### About the Process

- Having a thorough spec (006) with exact file changes and step order made implementation mechanical — the hard thinking was already done.
- The Critic agent found real issues (inconsistent null guards, missing edge-case tests) that a human reviewer would catch. Running it after implementation but before commit is a good pattern.
- VS Code's host LLM (GPT-4.1) can intercept MCP tool calls with its own safety filters. When stock-related questions get refused with `0x` tool calls, it's the host, not FinWise.

---

## The Numbers

| Metric | Before | After |
|--------|--------|-------|
| Custom session types | 4 (interface, record, class, helper) | 0 |
| Production code lines | +210 | -145 net (65 added, 210 removed) |
| Unit tests | 75 | 77 (5 deleted, 7 added) |
| Integration tests | 8/8 pass | 8/8 pass |
| SDK types consumed | 0 from Hosting | 2 types + 2 extension methods |

---

## What's Next

The session store is now SDK-aligned. The path to Redis is clear — [spec 007](../specs/007-redis-agent-session-store-plan/007-redis-agent-session-store-plan.md) has the full implementation plan. The `RedisAgentSessionStore : AgentSessionStore` will be a drop-in replacement. Zero changes to `AgentSessionManager` or workflow code (except `ClearSessionAsync`, which finally gets a real implementation).

The triple session restore in `ProcessMessageAsync` is tracked for optimization when Redis makes each restore a network round-trip instead of an in-memory lookup.

For now, the ghosts from Journal 03 are truly gone. No more `SerializedMessages`. No more `DeserializeMessages()`. No more custom `IAgentSessionStore`. Just the SDK doing what it was designed to do.

---

*Written: March 21, 2026*
