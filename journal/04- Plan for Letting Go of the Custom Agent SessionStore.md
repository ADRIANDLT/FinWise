# 04 — Letting Go of the Custom Store

*March 21, 2026 — How we planned to delete 150 lines of session infrastructure by trusting the SDK we were already paying for*

---

## The Question That Started It All

Journal 03 ended with a workaround: messages serialized independently in `AgentSessionData.SerializedMessages` because the SDK's `InMemoryChatHistoryProvider` couldn't survive a round trip through `agent.DeserializeSessionAsync()`. It worked. The ghosts were gone. But the code had grown a tumor — a parallel serialization path, a custom `IAgentSessionStore` interface, a `AgentSessionData` record with seven fields, a `DeserializeMessages()` helper, and an `InMemoryAgentSessionStore` that reimplemented what the SDK already shipped.

The question from [spec 006](../specs/006-custom-agent-session-store-vs-framework/006-custom-agent-session-store-vs-framework.md) was surgical: *should we replace our custom `IAgentSessionStore` with the SDK's `AgentSessionStore` abstract class?*

The answer turned out to be more interesting than expected.

## Act I — The Three Blockers

The initial analysis identified three reasons we couldn't just use the SDK's `InMemoryAgentSessionStore` directly:

**Blocker 1: Messages vanish after deserialization.** This was the ghost from Journal 03. `GetService<InMemoryChatHistoryProvider>()` returned `null` after deserializing a session. Our workaround serialized messages separately. Using the SDK's store meant trusting that messages would survive — and they didn't.

**Blocker 2: No `ClearSessionAsync`.** The SDK's `AgentSessionStore` has exactly two methods: `SaveSessionAsync` and `GetSessionAsync`. No delete. No clear. FinWise's reset flow explicitly clears the old session when the user says "start over." Without delete, orphaned session blobs would pile up.

**Blocker 3: Random agent IDs break the composite key.** The SDK keys sessions by `{agentId}:{conversationId}`. Our `OrchestratorAgentFactory` created a new `ChatClientAgent` on every request — each with a different random `Guid` as its `Id`. The store would save under one key and look up under a different one. Sessions lost between every request.

The spec's original recommendation: write our own `InMemoryAgentSessionStore : AgentSessionStore` — 35 lines that duplicate the SDK's logic plus a `ClearSession()` method. Safe. Conservative. And completely unnecessary.

## Act II — The SDK Fixed Blocker 1 (And We Almost Missed It)

While researching the SDK source on GitHub for the spec validation, a file caught our eye: `AgentSessionExtensions.cs`. Committed February 26, 2026. Three weeks before our analysis. The commit message: *".NET: Add helpers to more easily access in-memory ChatHistory."*

Two extension methods:

```csharp
// Read messages from StateBag directly — bypasses GetService<>() entirely
session.TryGetInMemoryChatHistory(out List<ChatMessage> messages);

// Write messages back into StateBag before saving
session.SetInMemoryChatHistory(messages);
```

These don't go through `GetService<InMemoryChatHistoryProvider>()`. They read `StateBag["InMemoryChatHistoryProvider"]` directly with typed JSON deserialization. The messages were always there — serialized inside the `StateBag` as part of `InMemoryChatHistoryProvider.State.Messages`. The ghost from Journal 03 was a **service registration issue**, not a data loss issue. The data survived the round trip. We just couldn't access it through the old API.

And they were already in our `1.0.0-rc4` package. We'd been carrying the workaround for three weeks while the fix sat in the NuGet package we'd already installed.

## Act III — Blocker 2 Was Never a Blocker

The "no delete" problem dissolved under scrutiny. Both call sites that clear sessions do this:

```csharp
await _sessionManager.ClearSessionAsync(previousAgentSessionId);
agentSessionId = Guid.NewGuid().ToString();   // ← new key immediately
```

The old `agentSessionId` is never looked up again. The `McpSessionMapping` is updated to point to the new one. The orphaned entry in the `ConcurrentDictionary`? It sits there, consuming a few KB, until the process restarts and everything is cleared. For an in-memory dev store, this is nothing. For Redis (v0.3.2), we'd add `KeyDeleteAsync` + TTL as a safety net.

`ClearSessionAsync` became a no-op: log the event and return. Zero behavioral impact.

## Act IV — Blocker 3 Was a One-Line Fix

The random agent `Id` problem was real but trivial. Our factory used the convenience constructor:

```csharp
return new ChatClientAgent(_chatClient, Prompt, Name, Description);
```

This sets `Name = "orchestrator_agent"` but leaves `Id = null`, which falls back to `Guid.NewGuid().ToString("N")` — a **random GUID generated at construction time**. Since `CreateAgentsAndWorkflow()` creates new instances per request, every request gets a different key.

The fix: switch to `ChatClientAgentOptions` with an explicit stable `Id`:

```csharp
return new ChatClientAgent(_chatClient, new ChatClientAgentOptions
{
    Id = "orchestrator_agent",
    Name = "orchestrator_agent",
    Description = "Silent router - calls handoff functions only, never outputs text",
    ChatOptions = new() { Instructions = Prompt }
});
```

The plan applies the same pattern to all three factories (orchestrator, advisor, profile) for consistency. The `ChatClientAgentOptions` constructor is the "real" constructor — the convenience overload just creates the options internally. Using it directly makes the configuration explicit and gives us the hook for future options like `ChatReducer` without another refactor.

## The Deletion

With all three blockers resolved, the decision crystallized: **use the SDK's `InMemoryAgentSessionStore` directly. Zero custom store code.**

What will be deleted:
- `IAgentSessionStore.cs` — the custom interface + `AgentSessionData` record (7 fields, ~80 lines)
- `InMemoryAgentSessionStore.cs` — our implementation (~40 lines)
- `InMemoryAgentSessionStoreTests.cs` — tests for code that no longer exists
- `DeserializeMessages()` in `AgentSessionManager` — the workaround helper
- Serialize/deserialize logic in `AgentSessionManager` — the SDK store handles it
- `userId` and `messageCount` parameters from `PersistSessionAsync` — metadata we don't store anymore

What will be added:
- `Microsoft.Agents.AI.Hosting` package reference (provides `AgentSessionStore` + `InMemoryAgentSessionStore`)
- `session.TryGetInMemoryChatHistory()` calls (2–3 lines)
- `session.SetInMemoryChatHistory()` calls (1–2 lines)
- `ChatClientAgentOptions` with stable `Id` on three factories (~15 lines)

Net: **~130 lines to delete.** The session infrastructure will go from "custom framework we maintain" to "SDK types we consume."

## The Package Dependency Trade-Off

The one cost worth noting: `Microsoft.Agents.AI.Hosting` (`1.0.0-preview.260311.1`) brings transitive dependencies — `Microsoft.Extensions.Hosting`, `DependencyInjection`, `Logging.Console`, `Configuration.*`, `ML.Tokenizers`. These are heavier than "lightweight" but already resolved transitively through the McpServer project. The class library's dependency closure grows, but no new runtime behavior is introduced.

The `AgentSessionStore` abstract class and `InMemoryAgentSessionStore` sealed class are the only types we consume. The rest comes along for the ride.

## What the Research Revealed

1. **Check your own NuGet packages before building workarounds.** The `TryGetInMemoryChatHistory()` fix was in `1.0.0-rc4` for three weeks before we found it. The workaround was correct when written, but the SDK team was working on the same problem from their side.

2. **"No delete" isn't a problem when the key changes.** The pattern `clear → new GUID → update mapping` means the old key is never accessed again. Orphaned keys are a cleanliness concern, not a correctness concern.

3. **Random `Id` is the default.** The SDK's `AIAgent.Id` falls back to `Guid.NewGuid().ToString("N")` unless you explicitly set `ChatClientAgentOptions.Id`. This is fine for single-request scenarios but breaks any keyed storage. The fix is one line, but if you don't know about it, you'll debug for hours.

4. **StateBag is the real message store.** `InMemoryChatHistoryProvider` stores messages in `AgentSession.StateBag["InMemoryChatHistoryProvider"]`. When you serialize the session, the messages come along. When you deserialize, they're back — accessible via `TryGetInMemoryChatHistory()`. The `GetService<>()` path that broke in RC4 was a convenience layer, not the source of truth.

5. **Prefer the SDK's "real constructors."** The convenience overload `new ChatClientAgent(client, prompt, name, desc)` hides configuration. `ChatClientAgentOptions` makes everything explicit: `Id`, `Name`, `Description`, `ChatOptions`, `ChatHistoryProvider`. When the SDK adds new options, you're already using the extensible pattern.

## Implementation Plan

Phase 1 (v0.3): Implement the 10-file change set from [spec 006, Section 9](../specs/006-custom-agent-session-store-vs-framework/006-custom-agent-session-store-vs-framework.md#9-implementation-plan). Delete the custom infrastructure, adopt SDK types, verify with existing tests.

Phase 2 (v0.3.2): Write `RedisAgentSessionStore : AgentSessionStore` — the SDK's contract makes this a drop-in replacement. `AgentSessionManager` and `FinWiseWorkflowService` don't change at all. Add a Redis Docker container and `ClearSessionAsync` with `KeyDeleteAsync` + TTL.

Once implemented, the session infrastructure will match the pattern the SDK intended all along: the framework handles serialization, persistence, and key management. We handle the workflow lifecycle — reset detection, email augmentation, message deduplication, handoff orchestration. Each side does what it's good at.

---

*The custom store served its purpose. It was the right decision when we didn't trust the SDK's deserialization. But the SDK caught up. The research is done, the blockers are resolved, and the plan is clear. Now it's time to execute.*