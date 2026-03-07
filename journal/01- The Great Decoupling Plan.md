# The Great Decoupling: Extracting a Multi-Agent Workflow from its MCP Shell

_March 1, 2026_  
_A monolithic 350-line Program.cs becomes two clean projects — and every folder placement gets debated down to its dependencies._

---

## Setting the Scene

FinWise is a multi-agent financial advisor. Three AI agents — an orchestrator, a profile collector, and an investment advisor — collaborate through Microsoft Agent Framework handoff workflows, exposed to the world via an MCP (Model Context Protocol) server. It works. Users connect from VS Code, the orchestrator routes requests, profiles get collected, advice gets dispensed.

But everything lives in one project: `FinWise.Orchestrator`. And within it, too many things live in `Program.cs` — 350 lines mixing MCP transport concerns, multi-agent workflow construction, session management, message deduplication, and tool registration. The workflow logic buried inside closures and local functions, tightly coupled to MCP header extraction and HTTP context. Impossible to test without spinning up the full MCP server. Impossible to reuse from a different host.

Although not everything was tangled. The agent factories (`OrchestratorAgentFactory`, `UserProfileAgentFactory`, `AdvisorAgentFactory`) already lived in their own `Agents/` folder with clean separation. The `UserProfileStore` followed a proper interface + provider pattern (`IUserProfileStore` with `InMemory/` and `CosmosDb/` implementations). Those were solid architectural decisions from earlier work — and they'd move cleanly into the new class library.

The goal: rip it apart into two projects. A class library that knows nothing about MCP. An MCP server that knows nothing about agents. Clean boundary. Clean dependencies. Testable in isolation.

What followed was not a quick refactoring plan but a deep architectural dialogue — every folder questioned, every dependency traced, every placement justified.

---

## The Opening Question

The conversation started with a code selection in `Program.cs` — the `createAgentsAndWorkflow` lambda at lines 98-130 — and a request:

> **User:** "I'd like to extract and decouple the code related to the multi-agent workflow... not sure if that should be a /Service or within a /AgentsWorkflow folder."

The user attached a reference repo ([Azure-Samples/app-service-maf-openai-travel-agent-dotnet](https://github.com/Azure-Samples/app-service-maf-openai-travel-agent-dotnet)) for structural inspiration — but with a critical caveat: the Agent Framework code in that sample was outdated. Only the structural patterns were to be trusted.

This set the tone for what followed: investigate everything, assume nothing.

---

## Naming the Children

The first concrete decision: what to call things.

The user proposed `FinWise.MultiAgentWorkflow` for the class library and wanted to rename the MCP server. A comparison was laid out:

| Candidate | Verdict |
|-----------|---------|
| `FinWise.MultiAgentWorkflow` | Explicit, industry-standard terminology, self-documenting |
| `FinWise.AgentsWorkflow` | Grammatically awkward — "Agents" as adjective reads oddly |
| `FinWise.AgentWorkflow` | Clean but loses the plural emphasis |

> **User:** "I put Agents in plural as I want to highlight it's about multiple agents, not a single one."

`FinWise.MultiAgentWorkflow` won. The compound adjective "multi-agent" is precise and matches Microsoft's own framework terminology.

For the server, an interesting redirect:

> **User:** "Let's call it FinWise.McpServer."

The recommendation had been `FinWise.McpServer` over `FinWise.MCP` — the `.Server` suffix is conventional in .NET and distinguishes the host from the protocol. The user agreed immediately.

Solution file: `FinWise.sln` — dropping the verbose `FinWise-orchestrator-mcp.sln`. With two projects, the old name no longer made sense.

---

## The Session Folder Debate

This became the longest and deepest discussion. Where should session management live?

### Four Options, Methodically Eliminated

**Option A — Root level `Session/`**: Clear visibility, independent folder.  
**Option B — Inside `Workflow/`**: Groups all workflow concerns, but deeper namespace.  
**Option C — Inside `Services/`**: Consistent with `UserProfileStore/` pattern, but conflates service vs. logic.  
**Option D — Root level with `SessionResetEvaluator` moved to `Workflow/`**: Splits by concern type.

The debate went through several rounds, each driven by a single question:

> **User:** "Is the Session only related to the Workflow? Or can it be used in a generic session in another scenario?"

This was the pivotal question. The answer changed the conclusion:

`AgentSessionManager` takes any `AIAgent`, not a `Workflow`. A single `ChatClientAgent` running alone would use the exact same session management. Session is a **conversation-state concern**, not a workflow implementation detail.

**Decision: Option A — root level.** Session is reusable.

### But What About SessionStore?

> **User:** "What about within /Services? ...in the future we'll probably provide two SessionStores, one in-memory and another in Redis."

`InMemorySessionStore` is pure key-value storage — identical in nature to `InMemoryUserProfileStore`. It implements a generic `ISessionStore` interface with zero knowledge of agents or workflows. The `Services/` pattern (interface at root + implementation sub-folders by provider) applies perfectly.

**Decision: `Services/SessionStore/` with `ISessionStore.cs` and `InMemory/InMemorySessionStore.cs`.** Ready for a future `Redis/` folder.

### And SessionResetEvaluator?

One more round. Does it belong in `Session/` or `Workflow/`?

> **User:** "Can it be used out of the workflow context? Or is it only usable in the context of a workflow?"

The code was examined line by line. `SessionResetEvaluator` imports only `Microsoft.Extensions.AI` types and does string matching. It checks for `PROFILE_READY:` markers — emitted by the profile agent, which could run standalone. Zero dependency on `Workflow`, `AgentWorkflowBuilder`, or any workflow type.

**Decision: `Session/`.** It's session logic, not workflow logic.

---

## The ConversationRunContext Puzzle

Where does `ConversationRunContext.cs` live? It's an `AsyncLocal<T>`-based ambient context — a cross-cutting utility used by `Workflow/` (writer) and designed for `Agents/` (future readers).

Initial placement: project root. The user pushed back:

> **User:** "I'm not convinced... Why at the root that file? Can you find a better place?"

The analysis revealed: `AgentSessionManager` manages **persisted** conversation state (between requests). `ConversationRunContext` manages **in-flight** conversation state (during a request). They're conceptual siblings — both deal with "current conversation state."

A deeper investigation also revealed that `ConversationRunContext.Current` is **never actually read in any production code**. It was designed for future tool implementations but hasn't been consumed yet. The writer (`Program.cs`, soon `FinWiseWorkflowService`) pushes state; no one reads it.

**Decision: `Session/ConversationRunContext.cs`.** Conceptual sibling of `AgentSessionManager`. No production readers yet, but correctly positioned for when they arrive.

---

## The LLM Client Question

> **User:** "Are you moving all dependencies to the AzureOpenAIChatClient into the class library? Note that the MCP server does not use it, only the Agents."

This was a sharp observation. The agents receive `IChatClient` — a pure abstraction from `Microsoft.Extensions.AI`. They never import `Azure.AI.OpenAI`. The concrete `AzureOpenAIClient` creation (`Infrastructure.CreateAzureOpenAIChatClient()`) is a **deployment decision**: which cloud, which endpoint, which key. That's composition-root work.

The class library becomes **LLM-provider-agnostic**. Swap Azure for Ollama? Only `Program.cs` changes. The entire workflow lib, all agents, all tests — untouched.

**Decision: `Azure.AI.OpenAI` and `Microsoft.Extensions.AI.OpenAI` stay in McpServer only. The class library gets only `Microsoft.Extensions.AI` (the abstraction).**

This also meant `Infrastructure.cs` stays in the MCP host — it's the factory for the concrete LLM client.

---

## The Model Folder and a Naming Correction

`Models.cs` contained two types: `UserProfileDto` and `WorkflowExecutionContext`. The plan was to split them into a `Model/` folder. But then:

> **User:** "Why do we have WorkflowResponse.cs within the Workflow folder but WorkflowExecutionContext.cs is within the /Model folder?"

Good catch — inconsistent. Both start with "Workflow", both exist because of the workflow. The analysis: if it starts with "Workflow", it belongs in `Workflow/`. `Model/` should have only cross-cutting domain types.

**Decision: Both `Workflow*` types in `Workflow/`. `Model/` gets only `UserProfileDto.cs`.**

Then came a deeper question:

> **User:** "It should be named UserProfile.cs if it's really a model, not UserProfileDto.cs. A DTO is usually a Data Transfer Object... Is our class a model or only a DTO?"

The record has `IsComplete` (business logic) and `WithUpdates()` (merge behavior). It's the canonical representation everywhere — the in-memory store persists it directly. It's a **domain model**, not a transfer object. The `Dto` suffix was a misnaming from early development.

**Decision: Defer rename to Phase 2 (16 references across 7 files — too risky to mix with structural decoupling). But the plan documents it as a known future correction.**

---

## Dead Code Discovery

During the refactoring review, a grepping expedition revealed that `WorkflowExecutionContext` is **defined but never used in production code**. Only two trivial test methods exercise it (constructor verification, timestamp arithmetic).

> **User:** "If you are 100% sure we don't need it, and it's 100% safe to delete it... let's do it at the beginning."

A final `grep` confirmed: 1 definition in `Models.cs`, 5 test references, zero production usage. 100% safe.

**Decision: Delete `WorkflowExecutionContext` and its 2 test methods as step 1 of Phase 1 — before any structural changes.** Cleanest approach: less code to move.

---

## MCP Tools: From Three to Two

The existing MCP tools:
- `get_financial_advice` — the main entry point
- `manage_user_profile` — literally `return await GetFinancialAdvice(query)`
- `reset_conversation` — explicit session reset

> **User:** "Merge ManageUserProfile() and GetFinancialAdvice() into RunFinWiseMultiAgentWorkflow(). Confirm this approach makes more sense?"

`ManageUserProfile` is a pure pass-through. Zero added logic. The orchestrator agent already routes profile vs. advice requests internally based on `PROFILE_READY` markers. Two identical tools confuse LLM clients ("which tool should I call?").

`reset_conversation` stays because `SessionResetEvaluator` (natural-language resets) is heuristic-based. An explicit tool provides a **guaranteed, deterministic** reset.

**Decision: 2 tools — `run_finwise_workflow` and `reset_conversation`.**

### Attribute-Based Refactor

> **User:** "Refactor to use the more advanced attributes-based approach and in different classes like in the attached code from AIResearchAgents.MCPServer."

The reference pattern was clear: `[McpServerToolType]` + `[McpServerTool]` attributes + `WithToolsFromAssembly()` auto-discovery. Already proven in the repo's own `samples/Hollow-Mcp-server/` with the same MCP SDK version (`0.5.0-preview.1`).

Should this be Phase 1 or Phase 2?

> **User:** "Does it make sense to do it sooner or at Phase 2?"

Since `Program.cs` is already being completely rewritten, writing closure-based adapters first only to rewrite them as attribute-based immediately after would be double work. The tool rename (`get_financial_advice` → `run_finwise_workflow`) is also a breaking change — but so is the project rename. Bundle breaking changes together.

**Decision: Attribute-based tools in Phase 1, not Phase 2.**

---

## The Final Review Loop

Three full review passes were conducted, each time cross-referencing every claim in the spec against the actual codebase:

**Pass 1 found:** `ISessionStore` and `SessionData` are defined inside `AgentSessionManager.cs` (not in their own file as the plan implied). The spec was corrected.

**Pass 2 found:**
- Duplicate step 2 in Phase 1 (copy-paste artifact)
- `Microsoft.Agents.AI.Abstractions` listed but not actually in the production csproj (transitive dependency only)
- `ConversationRunContext` reader claim was wrong — no agents read it
- Orphan files: `FinWise.Orchestrator.sln` (sub-solution) and `.vscode/launch.json` (debugger config) not mentioned in the plan

**Pass 3:** Clean. All 32 steps verified. No remaining issues.

---

## The WHY Behind Every Decision

### Why two projects, not just better folders?

Package isolation. The class library has **zero MCP dependencies** and **zero Azure dependencies**. It can be reused by a REST API, a WebJob, a CLI, or any other host. The MCP server is a thin adapter that could be swapped entirely without touching workflow logic.

### Why `FinWise.MultiAgentWorkflow` and not `FinWise.Core`?

Precision. "Core" is vague — core of what? "MultiAgentWorkflow" explicitly communicates the architectural pattern. It matches Microsoft's own terminology and is self-documenting for newcomers.

### Why `Session/` at root level?

Reusability. `AgentSessionManager` takes any `AIAgent`, not a `Workflow`. A future single-agent scenario would use the exact same session management. Nesting it under `Workflow/` would be architecturally dishonest.

### Why `SessionStore` in `Services/` but `SessionResetEvaluator` in `Session/`?

Different natures. `InMemorySessionStore` is pure storage infrastructure (like `InMemoryUserProfileStore`). `SessionResetEvaluator` is domain logic (phrase detection, `PROFILE_READY` gating). Infrastructure goes in `Services/`. Logic goes in `Session/`.

### Why `ConversationRunContext` in `Session/`?

It manages in-flight conversation state. `AgentSessionManager` manages persisted conversation state. Both are "conversation state" — one between requests, one during a request. Conceptual siblings belong together.

### Why Azure OpenAI packages in McpServer only?

The class library is **LLM-provider-agnostic**. It receives `IChatClient` — a pure abstraction. `Infrastructure.CreateAzureOpenAIChatClient()` is a deployment decision. Swap Azure for Ollama, and only `Program.cs` changes. The entire workflow lib stays untouched.

### Why merge 3 MCP tools to 2?

`ManageUserProfile()` was `return await GetFinancialAdvice(query)`. Zero logic. The orchestrator agent already routes internally. Two identical tools confused LLM clients. One entry point is cleaner.

### Why attribute-based MCP tools?

Clean separation. Tools in `Tools/FinWiseTools.cs`, not closures in `Program.cs`. Testable in isolation. Auto-discovered via `WithToolsFromAssembly()`. Matches latest MCP SDK patterns. And since `Program.cs` was being rewritten anyway, doing it right the first time avoids double work.

### Why delete `WorkflowExecutionContext`?

Dead code. Defined in `Models.cs`, never referenced in production. Only 2 trivial test methods exercise it. Deleting it before the structural move means less code to relocate, less namespace updates, zero risk.

### Why defer `UserProfileDto → UserProfile` rename?

Safety. 16 references across 7 files. During the risky structural decoupling, minimizing diff size is critical. The rename is a safe, isolated refactoring — perfect for Phase 2 where each change is one commit + test run.

### Why `Model/` folder with only one file?

The folder exists for the right reason: domain models that are cross-cutting (used by agents, stores, workflow). Today it's one model. Tomorrow it may have more. A folder with one file is better than a model in the wrong folder.

---

## What We Learned

### About Architecture

- **Ask "who else could use this?"** before placing anything in a subfolder. The Session debate was resolved by a single question: can this work without a workflow? Yes → root level.
- **Dead code has a cost during refactoring.** Every file you move needs namespace updates, using statement fixes, and test adjustments. Deleting `WorkflowExecutionContext` first eliminated work on a type nobody uses.
- **Deployment decisions are not domain decisions.** Which LLM provider to use is a `Program.cs` concern. The workflow library should never know.

### About the Process

- **Name things by dependency, not by intent.** `SessionResetEvaluator` has "Session" in its name and lives in `Session/` — but that was decided by analyzing its `using` statements, not its name. It imports zero workflow types. That's what matters.
- **Multiple review passes work.** The first pass caught the `ISessionStore` location error. The second caught orphan files. The third found nothing — confidence earned.
- **Bundle breaking changes.** Tool rename + project rename + attribute refactor in one phase, not three. Users update their config once.

---

## What's Next

The spec is complete: 32 implementation steps across 7 phases, verified against every file in the codebase. Phase 2 post-decoupling refinement has 8 items waiting.

Next step: implement Phase 1, step 1 — delete `WorkflowExecutionContext`. Run tests. Commit. Then begin the structural work.

**Spec document:** [specs/003-decoupling-refactoring-tech-specs-plan/003-decoupling-refactoring-tech-specs-plan.md](../specs/003-decoupling-refactoring-tech-specs-plan/003-decoupling-refactoring-tech-specs-plan.md)

---

_Written: March 1, 2026_  
