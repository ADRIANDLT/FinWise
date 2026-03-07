# The Great Decoupling: From Plan to Code

_March 2, 2026_
_A 520-line monolith becomes a clean two-project architecture — through seven phases, three naming debates, and 87 passing tests._

---

## Picking Up Where the Plan Left Off

The [previous chapter](01-%20The%20Great%20Decoupling%20Plan.md) ended with a meticulously debated spec — every folder justified, every dependency traced. Now the spec needed to become code. What followed was a marathon implementation session: seven phases of the decoupling refactoring, a deep Phase 2 cleanup pass, and an unexpected but illuminating naming war between "Session" and "Conversation" that touched every file in the project.

The starting point: `src/FinWise.Orchestrator/` — a single project with a 520-line `Program.cs`, 13 source files, and one test project that wasn't even in the solution file.

The destination: two clean projects, five test projects, folder-per-agent architecture, externalized prompts, and a naming convention that would make the codebase self-documenting.

---

## Phase 1: The Scaffolding — Clearing Ground

Before any structural change, dead code had to go. `WorkflowExecutionContext` — a record defined in `Models.cs` — had been used in production exactly zero times. Its only proof of existence: two test methods that validated its constructor. A classic "we might need this later" artifact.

> **User:** "Check the specs plan and confirm that it's 100% right for implementing the code."

The spec validation uncovered five issues before a single line of code changed. The test project wasn't in the solution file. The spec assumed `Serilog.AspNetCore` for the class library (which would drag in ASP.NET Core dependencies). `ISessionStore` was hardcoded internally instead of injected. These were caught and fixed in the spec before implementation — preventing hours of debugging later.

The dead code was deleted. Tests confirmed zero regressions. Then `FinWise.MultiAgentWorkflow.csproj` was created — the new class library with six carefully chosen packages and zero MCP dependencies.

**Key files:** [FinWise.MultiAgentWorkflow.csproj](../src/FinWise.MultiAgentWorkflow/FinWise.MultiAgentWorkflow.csproj)

---

## Phase 2-3: The Great Migration

Fourteen source files moved from `FinWise.Orchestrator` to `FinWise.MultiAgentWorkflow`. Every namespace changed. Every `using` statement updated. But the trickiest extraction was `ISessionStore` and `SessionData` — they were hiding at the bottom of `AgentSessionManager.cs`, co-located with the manager class. The spec had flagged this: "Note: `ISessionStore` interface and `SessionData` record are currently defined at the bottom of `AgentSessionManager.cs`, not in their own files." They were surgically extracted into `Services/SessionStore/ISessionStore.cs`.

Then came the crown jewel: `FinWiseWorkflowService`. The 170-line `GetFinancialAdvice()` closure in `Program.cs` was decomposed into a proper class with `ProcessMessageAsync`, `ResetConversationAsync`, and a private `ExecuteWorkflowAsync`. The constructor took `IChatClient`, `IUserProfileStore`, and `ISessionStore` — all injected, all testable, all swappable.

> **Critic Review Finding:** "Null HttpContext throws NullReferenceException instead of caught InvalidOperationException"

The Critic agent caught a regression the Coder had introduced: the original code had a deliberate null guard for `HttpContext` that produced a user-friendly error. The refactored version used a null-forgiving operator (`httpContext!`) that would crash. Fixed before merge.

---

## Phase 4: The MCP Host Slims Down

`Program.cs` went from 520 lines to 125. The old project was renamed from `FinWise.Orchestrator` to `FinWise.McpServer`. Three closure-based MCP tools became two attribute-based tools in `Tools/FinWiseTools.cs`. The session-to-conversation mapping logic — previously loose local functions and a `ConcurrentDictionary` floating in `Program.cs` — became `McpSessionMapping`, a clean helper class.

The `ManageUserProfile` tool was eliminated entirely. It had been a pure passthrough to `GetFinancialAdvice` with zero added logic. Two identical tools confused LLM clients. Now there's one entry point: `run_finwise_workflow`.

> **Critic Review Finding:** "ResetConversation lost the 'no active session' path"

Another behavioral regression caught by the Critic: the original `ResetConversation` checked whether a conversation existed before clearing it. The refactored version always created a new one, losing the "No active conversation to reset" user feedback. A `TryGetConversationId` method was added to `McpSessionMapping` to restore the original behavior.

**Key files:** [Program.cs](../src/FinWise.McpServer/Program.cs) | [FinWiseTools.cs](../src/FinWise.McpServer/Tools/FinWiseTools.cs) | [McpSessionMapping.cs](../src/FinWise.McpServer/McpSessionMapping.cs)

---

## Phase 5-6: Tests and Documentation

The single test project split into three:
- `FinWise.MultiAgentWorkflow.UnitTests` — 70 unit tests
- `FinWise.McpServer.IntegrationTests` — 7 E2E MCP protocol tests
- `FinWise.CosmosDb.IntegrationTests` — 8 CosmosDB integration tests

The test quality analysis identified a test that was testing `Dictionary<string, UserProfileDto>` — a .NET BCL type — instead of any production code. It was deleted. Two tests that merely validated C# record constructor behavior were rewritten to test actual business logic (`IsComplete`, `WithUpdates`). New tests were added for `InMemorySessionStore`, `SessionResetEvaluator`, `ConversationRunContext`, and `WorkflowHelpers` — components that had previously had zero unit test coverage.

A `ToolDiscovery` integration test was added that calls MCP's `tools/list` and asserts exactly two tools are exposed. A `ResetConversation` E2E test was added. An `EmptyQuery` corner case test was added.

The old `FinWise.Orchestrator/` directory and `FinWise.Orchestrator.Tests/` were deleted. The old `.sln` file was deleted. .NET 10's `FinWise.slnx` format took its place.

---

## Phase 2 Cleanup: The Code Gets Polished

With the decoupling verified (all 87 tests green), the Phase 2 refinement began — each improvement a separate atomic change:

**Constants extracted.** The `PROFILE_READY:` magic string appeared 8+ times across 5 files. Now it's `AgentSessionConstants.ProfileReadyMarker` — one source of truth.

**Email validation consolidated.** Three identical `!userId.Contains('@')` checks in `UserProfileAgentFactory` became a single `IsValidEmail` private method (the standalone `EmailValidator` class was later inlined — too small for its own file).

**Response codes formalized.** `"FOUND_COMPLETE:"`, `"PARTIAL:"`, `"ERROR:"` and four other string literals became private constants in `UserProfileAgentFactory`.

**Dead code purged.** `IsNewLogicalSessionAsync()` — a 65-line method with two private helpers and two timeout properties — had zero callers. It was designed for MCP STDIO transport (which has no session headers) but the system uses HTTP transport. Deleted. `AgentSessionManager` shrank from 248 to 106 lines.

**WorkflowHelpers eliminated.** The `*Helpers` code smell was addressed by distributing its three methods: `ExtractUserIdFromMessageHistory` moved to `AgentSessionConstants` (session concern), `AppendUniqueMessages` and `BuildMessageSignature` became `internal static` methods in `FinWiseWorkflowService` (their only caller).

**Agent prompts externalized.** 60-96 line string literals in each agent factory moved to `.prompt.md` embedded resource files in a folder-per-agent structure:

```
Agents/
  OrchestratorAgent/
    OrchestratorAgentFactory.cs
    OrchestratorAgent.prompt.md
  UserProfileAgent/
    UserProfileAgentFactory.cs
    UserProfileAgent.prompt.md
  AdvisorAgent/
    AdvisorAgentFactory.cs
    AdvisorAgent.prompt.md
```

**`UserProfileDto` renamed to `UserProfile`.** It's a domain model with business logic (`IsComplete`, `WithUpdates`), not a data transfer object. A comment was added explaining why it's a `record` — immutability prevents accidental in-place mutation of store-held references in concurrent scenarios.

**`Model/` renamed to `DomainModel/`.** In an AI project, "Model" is ambiguous (LLM model vs domain model).

**`Services/` renamed to `Infrastructure/`.** DDD-aligned — these are persistence implementations.

---

## The Naming War: Session vs Conversation

What started as a simple tech debt finding — "the naming is confusing" — became the deepest design discussion of the session.

The problem: "session" meant three different things depending on where you read it. `MCP-Session-Id` meant an HTTP client connection. `AgentSession` (the Microsoft Agent Framework SDK type) meant a conversation state container. And our code used bare "Session" for both.

> **User:** "What about always saying AgentSession or AgentSessionStore, etc. or MCPSession, so it's clear the type of session? No 'Session' in isolation as naming?"

Six naming approaches were evaluated:

| Option | Verdict |
|--------|---------|
| Rename everything to "Conversation" | Rejected — fights the SDK convention |
| Keep bare "Session" with docs | Rejected — relies on tribal knowledge |
| `AgentConversationSessionManager` | Rejected — too verbose |
| Hybrid (Session for SDK, Conversation for ours) | Rejected — inconsistent |
| Keep as-is, just add documentation | Rejected — newcomers still confused |
| **Always-qualified prefix (CHOSEN)** | Every "session" type gets `AgentSession*` or `McpSession*` prefix |

Then came a deeper question:

> **User:** "Why not removing 'conversation' from any type and any variable name, completely?"

This led to a full purge: `conversationId` → `agentSessionId`, `conversationHistory` → `messageHistory`, `ConversationRunContext` → `AgentSessionRunContext`. Approximately 30+ variable renames across 15 files. Zero logic changes — pure naming consistency.

The final naming convention, documented in [004-session-conversation-refactoring-analysis.md](../specs/004-session-conversation-refactoring/004-session-conversation-refactoring-analysis.md):

> **No bare "Session" in any type name.** `AgentSession*` = Agent Framework layer. `McpSession*` = MCP transport layer.

The relationship was clarified: **1 MCP Session → 1 AgentSession** at any time. All three agents share one AgentSession per user interaction. The session can be reset (generating a new `agentSessionId`) while the MCP session persists.

**Key file:** [004 Analysis](../specs/004-session-conversation-refactoring/004-session-conversation-refactoring-analysis.md)

---

## The Orchestrator Text-Leak Fix

A debugging moment worth noting: E2E tests that had previously passed started failing intermittently. The orchestrator agent — designed to emit ONLY tool calls, never text — was sometimes emitting raw JSON like `{"reasonforhandoff":null}` instead of executing the handoff function.

The original code had a pattern-matching approach with a hardcoded array of suspicious strings. The fix was elegant in its simplicity: **reject ALL text from the orchestrator**, unconditionally. The orchestrator's design contract is "tool calls only, never text" — any text output is definitionally a failure, regardless of what it contains. The pattern-matching array was deleted and replaced with a single condition check.

---

## What We Learned

### About Architecture
- **Naming conventions are architectural decisions.** The session/conversation debate wasn't bikeshedding — it was about making the codebase self-documenting for anyone who reads it. The `AgentSession*` prefix instantly tells you which layer you're in.
- **Dead code is expensive.** `IsNewLogicalSessionAsync` was 65 lines of complex timeout logic that nobody called. Deleting it simplified `AgentSessionManager` by 57%.
- **Folder-per-agent is the right granularity.** Co-locating factory + prompt makes each agent a self-contained unit. Adding a fourth agent means adding one folder.

### About Process
- **Critic reviews catch real bugs.** Two behavioral regressions (null HttpContext guard, reset path) were caught by the Critic agent before they shipped.
- **E2E tests with live LLMs are inherently flaky.** The orchestrator text-leak is an LLM non-determinism issue, not a code bug. Retry loops and graceful skips are the right patterns — not stricter assertions.
- **Spec validation before implementation saves time.** Five issues were caught in the spec that would have been debugging sessions during implementation.

### About Naming
- **"One word, one meaning" is worth the rename cost.** Having "session" mean two different things across 30 files was a constant source of confusion. The prefix convention cost ~50 renames but eliminated an entire category of ambiguity.
- **Align with your SDKs.** Fighting the Agent Framework's "session" terminology to use "conversation" would have created perpetual friction for anyone reading SDK docs.

---

## The Final Numbers

| Metric | Before | After |
|--------|--------|-------|
| Source projects | 1 (`FinWise.Orchestrator`) | 2 (`FinWise.McpServer`, `FinWise.MultiAgentWorkflow`) |
| Test projects | 1 (not in solution) | 3 (all in solution) |
| `Program.cs` lines | 520 | 125 |
| `AgentSessionManager` lines | 248 | 106 |
| Unit tests | 26 | 70 |
| Integration tests | 5 (E2E only) | 15 (7 MCP + 8 CosmosDB) |
| MCP tools | 3 (closure-based) | 2 (attribute-based) |
| Hardcoded URLs | 2 | 0 (config-driven) |
| Solution format | `.sln` (legacy) | `.slnx` (.NET 10) |

---

## What's Next

The decoupling is complete. The codebase is clean, well-tested, and well-named. The immediate horizon:

- **Production deployment** — the architecture is ready for Docker containerization
- **Redis session store** — `IAgentSessionStore` is designed for this; add `Infrastructure/AgentSessionStore/Redis/`
- **Additional agents** — the folder-per-agent pattern makes this straightforward
- **CancellationToken forwarding** — deferred tech debt; LLM calls can't be cancelled yet

The monolith is gone. Two clean projects, five test projects, 87 passing tests, and a naming convention that makes the architecture visible in every type name.

---

_Written: March 2, 2026_
