# 003 — Decoupling Refactoring: Extract Multi-Agent Workflow from MCP Server

**Status**: Approved — ready for implementation  
**Date**: 2026-03-01

---

## 1. Summary

Decouple the multi-agent workflow logic (agent creation, workflow execution, session management, conversation orchestration) from the MCP server hosting code currently monolithed in `Program.cs`.

**Outcome**: A new **`FinWise.MultiAgentWorkflow`** class library (zero MCP dependencies, LLM-provider-agnostic) that contains all workflow, agent, session, and service logic. The MCP host is renamed to **`FinWise.McpServer`** and reduced to a thin adapter (~125 lines of composition). The solution is renamed to **`FinWise.slnx`**. Two dedicated test projects replace the single existing test project.

---

## 2. Motivation

Today, `Program.cs` (~520 lines) mixes three categories of concern:

1. **MCP transport** — HTTP session header extraction, session-to-conversation mapping, MCP tool registration
2. **Multi-agent workflow** — agent creation, handoff workflow construction, workflow event streaming, response validation
3. **Session/conversation management** — session restore/persist, reset detection, message deduplication

This coupling means:
- The workflow cannot be tested without the MCP server
- The workflow cannot be reused by a different host (REST API, WebJob, CLI)
- `Program.cs` is difficult to navigate and modify safely
- Package dependencies are entangled (MCP packages + Agent Framework + Azure SDK all in one project)

---

## 3. Architecture After Refactoring

```
FinWise.slnx
├── src/FinWise.MultiAgentWorkflow/← NEW class library
│   ├── Agents/
│   │   ├── OrchestratorAgent/
│   │   │   ├── OrchestratorAgentFactory.cs
│   │   │   └── OrchestratorAgent.prompt.md
│   │   ├── UserProfileAgent/
│   │   │   ├── UserProfileAgentFactory.cs
│   │   │   └── UserProfileAgent.prompt.md
│   │   └── AdvisorAgent/
│   │       ├── AdvisorAgentFactory.cs
│   │       └── AdvisorAgent.prompt.md
│   ├── Workflow/
│   │   ├── FinWiseWorkflowService.cs            ← NEW: core orchestration class
│   │   └── WorkflowResponse.cs                  ← NEW: result DTO
│   ├── Session/
│   │   ├── AgentSessionManager.cs
│   │   ├── SessionResetEvaluator.cs
│   │   ├── SessionConstants.cs                  ← shared constants + ExtractUserIdFromConversationHistory
│   │   ├── EmailValidator.cs
│   │   └── ConversationRunContext.cs
│   ├── DomainModel/
│   │   └── UserProfile.cs                       ← domain model
│   ├── Infrastructure/
│   │   ├── SessionStore/
│   │   │   ├── ISessionStore.cs
│   │   │   └── InMemory/
│   │   │       └── InMemorySessionStore.cs
│   │   └── UserProfileStore/
│   │       ├── IUserProfileStore.cs
│   │       ├── InMemory/
│   │       │   └── InMemoryUserProfileStore.cs
│   │       └── CosmosDb/
│   │           ├── CosmosDbUserProfileStore.cs
│   │           ├── CosmosDbOptions.cs
│   │           └── UserProfileDocument.cs
│   ├── AGENTS.md
│   └── FinWise.MultiAgentWorkflow.csproj
│
├── src/FinWise.McpServer/                       ← RENAMED from FinWise.Orchestrator
│   ├── Tools/
│   │   └── FinWiseTools.cs                      ← NEW: attribute-based MCP tools
│   ├── Program.cs                               ← SLIMMED: composition root only
│   ├── Infrastructure.cs                        ← stays (Azure-specific composition)
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   ├── AGENTS.md
│   └── FinWise.McpServer.csproj                 ← references MultiAgentWorkflow
│
├── tests/FinWise.MultiAgentWorkflow.UnitTests/  ← NEW
│   └── FinWise.MultiAgentWorkflow.UnitTests.csproj
│
└── tests/FinWise.McpServer.IntegrationTests/    ← RENAMED from FinWise.Orchestrator.Tests
    └── FinWise.McpServer.IntegrationTests.csproj
```

### Dependency Flow

```
Workflow/FinWiseWorkflowService
    → Session/AgentSessionManager              (conversation state logic)
        → Infrastructure/SessionStore/ISessionStore  (storage infrastructure)
    → Session/SessionResetEvaluator            (reset detection logic)
    → Session/ConversationRunContext            (in-flight ambient state)
    → Agents/*                                 (agent factories)
    → Infrastructure/UserProfileStore/IUserProfileStore  (profile storage)
```

---

## 4. Package Distribution

| Package | `MultiAgentWorkflow` | `McpServer` | Rationale |
|---------|:---:|:---:|---|
| `Microsoft.Extensions.AI` | **Yes** | (transitive) | `IChatClient` abstraction — agents receive this |
| `Microsoft.Extensions.AI.OpenAI` | No | **Yes** | `.AsIChatClient()` — Azure-specific composition |
| `Azure.AI.OpenAI` | No | **Yes** | `AzureOpenAIClient` — deployment decision |
| `Microsoft.Agents.AI` | **Yes** | No | Agent Framework core (`AIAgent`, `ChatClientAgent`) |
| `Microsoft.Agents.AI.Workflows` | **Yes** | No | `Workflow`, `AgentWorkflowBuilder`, `InProcessExecution` |
| `ModelContextProtocol` | No | **Yes** | MCP protocol |
| `ModelContextProtocol.AspNetCore` | No | **Yes** | MCP HTTP transport |
| `Microsoft.Azure.Cosmos` | **Yes** | No | CosmosDB profile store provider |
| `Serilog` | **Yes** | No | Structured logging (core package — `Log.*`, `LogContext`) |
| `Serilog.AspNetCore` | No | **Yes** | ASP.NET Core Serilog integration (host-level only) |
| `Newtonsoft.Json` | **Yes** | No | Agent Framework dependency |

### Why LLM Client Creation Stays in the MCP Host

The class library receives `IChatClient` — a pure abstraction from `Microsoft.Extensions.AI`. The agents never know they're talking to Azure OpenAI.

`Infrastructure.CreateAzureOpenAIChatClient()` makes a **deployment decision**: which cloud provider, which endpoint, which API key. That is **composition-root work**, not domain logic.

```
MCP Host (Infrastructure.cs):
    new AzureOpenAIClient(endpoint, key)     ← Azure.AI.OpenAI
        .GetChatClient(deployment)
        .AsIChatClient()                      ← Microsoft.Extensions.AI.OpenAI

Program.cs:
    var chatClient = Infrastructure.CreateAzureOpenAIChatClient();
    var workflowService = new FinWiseWorkflowService(chatClient, profileStore, sessionStore);
```

This makes the class library **LLM-provider-agnostic**:
- Swap Azure OpenAI for Ollama → only `Program.cs` changes
- Swap for OpenAI direct → only `Program.cs` changes
- The entire workflow lib, all agents, all tests — untouched

Same principle as profile stores: `Program.cs` decides "CosmosDB" or "in-memory" and passes `IUserProfileStore`. The **interface** lives in the class library. The **concrete creation** (reading config, creating clients) lives in the composition root.

---

## 5. Session/MCP Boundary Design

The tightest coupling point in the current codebase. The decoupling strategy:

### MCP Host Responsibilities (`Program.cs`)

1. Extract `MCP-Session-Id` from HTTP headers
2. Maintain `ConcurrentDictionary<string, string>` mapping sessionId → conversationId
3. Pass plain `conversationId` string to the workflow service
4. If workflow signals a reset, update the mapping with the new conversationId

### Workflow Service Responsibilities (`FinWiseWorkflowService`)

1. Create agents + build handoff workflow per conversation
2. Restore or create `AgentSession` for the conversationId
3. Detect session resets via `SessionResetEvaluator`
4. Execute workflow, collect + deduplicate messages
5. Persist session state
6. Return `WorkflowResponse` with text + new conversationId if reset occurred

### Contract — The Only Bridge

```csharp
// Process a user message through the multi-agent workflow
Task<WorkflowResponse> ProcessMessageAsync(string conversationId, string query);

// Explicitly reset a conversation
Task<string> ResetConversationAsync(string conversationId);
```

```csharp
record WorkflowResponse(string Response, string ConversationId, bool WasReset);
```

### MCP Tool Design (Attribute-Based)

The current 3 closure-based tools (`GetFinancialAdvice`, `ManageUserProfile`, `ResetConversation`) are replaced by 2 attribute-based tools in `Tools/FinWiseTools.cs`:

| Tool | Purpose |
|------|---------|
| `run_finwise_workflow` | Single entry point for all user messages. The internal multi-agent workflow handles routing: profile collection, profile management, and financial advice. Pass the user's exact message verbatim. |
| `reset_conversation` | Explicit session reset (clear history, keep profiles). Provides a guaranteed, deterministic reset for MCP clients. |

**Why merge `ManageUserProfile` + `GetFinancialAdvice`?** `ManageUserProfile` is `return await GetFinancialAdvice(query)` — zero added logic. The orchestrator agent routes internally. Two identical tools confuse LLM clients.

**Why keep `reset_conversation`?** `SessionResetEvaluator` handles natural-language resets but is heuristic-based. An explicit tool provides a guaranteed reset for MCP clients wanting a "clear chat" button.

```csharp
// Tools/FinWiseTools.cs — attribute-based, auto-discovered
[McpServerToolType]
public static class FinWiseTools
{
    [McpServerTool(Name = "run_finwise_workflow")]
    [Description("Send user messages to FinWise financial advisor. Pass the user's exact message verbatim...")]
    public static async Task<string> RunFinWiseWorkflow(
        IServiceProvider serviceProvider,
        [Description("The user's exact message")] string query,
        CancellationToken cancellationToken = default)
    {
        var workflowService = serviceProvider.GetRequiredService<FinWiseWorkflowService>();
        var sessionMapping = serviceProvider.GetRequiredService<McpSessionMapping>();
        var httpContext = serviceProvider.GetRequiredService<IHttpContextAccessor>().HttpContext;

        var sessionId = McpSessionMapping.GetSessionId(httpContext!);
        var conversationId = sessionMapping.GetOrCreateConversationId(sessionId);
        var result = await workflowService.ProcessMessageAsync(conversationId, query);
        if (result.WasReset)
            sessionMapping.UpdateConversationId(sessionId, result.ConversationId);
        return result.Response;
    }

    [McpServerTool(Name = "reset_conversation")]
    [Description("Clear conversation history to start fresh. User profiles are retained.")]
    public static async Task<string> ResetConversation(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default) { ... }
}

// Program.cs — auto-discovery, no manual tool creation
builder.Services.AddMcpServer().WithHttpTransport().WithToolsFromAssembly();
```

**Note on DI**: The attribute-based approach injects `IServiceProvider` into tool methods. `FinWiseWorkflowService` and session mapping are registered in DI. The session-to-conversation `ConcurrentDictionary` is wrapped in a small `McpSessionMapping` helper class (replaces the loose dict + local functions). `GetSessionId()` moves to a static method on this helper.

The workflow library has **zero MCP dependencies**. The MCP host has **no direct Microsoft.Agents.AI.Workflows dependency**.

---

## 6. Folder Placement Rationale

### `Session/` at Root Level

| Question tested | Answer |
|---|---|
| Can `AgentSessionManager` be used without a workflow? | **Yes** — it takes any `AIAgent`, not a `Workflow` |
| Can `SessionResetEvaluator` be used without a workflow? | **Yes** — zero workflow dependency; works with any conversation containing `PROFILE_READY` markers |
| Can a single-agent scenario use these? | **Yes** — a single `ChatClientAgent` running profile collection would use the exact same session and reset logic |

**Conclusion**: Session is a reusable conversation-state concern, not a workflow implementation detail.

### `ConversationRunContext` in `Session/`

- `AgentSessionManager` manages **persisted** conversation state (between requests)
- `ConversationRunContext` manages **in-flight** conversation state (during a request)
- Both deal with "current conversation state" — conceptual siblings
- Writer: `FinWiseWorkflowService` (in `Workflow/`), currently `Program.cs`. No production readers yet — designed for future tool implementations to access conversation data without parameter threading.
- `Session/` is the neutral, accurate home.

### `SessionStore` in `Infrastructure/`

`InMemorySessionStore` is pure key-value storage infrastructure — identical in nature to `InMemoryUserProfileStore`. It implements `ISessionStore` (a generic interface), has zero knowledge of workflows or agents, and will gain a Redis implementation in the future. The `Infrastructure/` pattern (interface at root + implementation sub-folders by provider) applies exactly.

### `SessionResetEvaluator` in `Session/`

Tested dependency: its code imports only `Microsoft.Extensions.AI` (`ChatMessage`, `ChatRole`) and does string matching. It references `PROFILE_READY:`, which is emitted by the profile agent — an agent that could run standalone (no workflow). Zero dependency on `Workflow`, `AgentWorkflowBuilder`, `InProcessExecution`, or any type from `Microsoft.Agents.AI.Workflows`.

---

## 7. Implementation Steps

### Phase 1: Scaffolding

| # | Step | Details |
|---|------|---------|
| 1 | Delete dead code | Delete `WorkflowExecutionContext` record from `Models.cs` and its 2 test methods from `WorkflowTests.cs` (`WorkflowExecutionContext_Should_Capture_Request_Details`, `WorkflowExecutionContext_Should_Support_Timestamp_Tracking`). Defined but never used in production. Run tests to confirm zero regressions. Commit before structural changes. |
| 2 | Rename solution | `FinWise-orchestrator-mcp.sln` → `FinWise.slnx`. Note: .NET 10 SDK generates `.slnx` (modern XML solution format) by default instead of legacy `.sln`. |
| 3 | Create class library project | `src/FinWise.MultiAgentWorkflow/FinWise.MultiAgentWorkflow.csproj` — net10.0, `TreatWarningsAsErrors=true`. Packages: `Microsoft.Agents.AI`, `Microsoft.Agents.AI.Workflows`, `Microsoft.Extensions.AI`, `Microsoft.Extensions.Options`, `Microsoft.Azure.Cosmos`, `Serilog`, `Newtonsoft.Json`. **No** `ModelContextProtocol`, `Azure.AI.OpenAI`, `Microsoft.Extensions.AI.OpenAI`, `Serilog.AspNetCore`. Note: `Serilog` (core) 4.2.0 is the minimum dependency of `Serilog.AspNetCore` 8.0.3 and is already listed in `Directory.Packages.props`. |
| 4 | Create folder structure | `Agents/OrchestratorAgent/`, `Agents/UserProfileAgent/`, `Agents/AdvisorAgent/`, `Workflow/`, `Session/`, `DomainModel/`, `Infrastructure/SessionStore/InMemory/`, `Infrastructure/UserProfileStore/` |
| 5 | Add to solution | Add all projects to `FinWise.slnx` under `src` and `tests` solution folders. Note: the current `FinWise-orchestrator-mcp.sln` only contains `FinWise.Orchestrator` — the existing test project is not in the solution. The new `FinWise.slnx` must include all 4 projects: `FinWise.MultiAgentWorkflow`, `FinWise.McpServer` (under `src/`), `FinWise.MultiAgentWorkflow.UnitTests`, `FinWise.McpServer.IntegrationTests` (under `tests/`). |

### Phase 2: Move Existing Files

| # | Step | Source → Destination | Namespace Change |
|---|------|---------------------|-----------------|
| 6 | Move agent factories | `Agents/*.cs` → `MultiAgentWorkflow/Agents/` | `FinWise.Orchestrator.Agents` → `FinWise.MultiAgentWorkflow.Agents` |
| 7 | Move session logic | `AgentSessionManager.cs`, `SessionResetEvaluator.cs`, `ConversationRunContext.cs` → `MultiAgentWorkflow/Session/` | `FinWise.Orchestrator` → `FinWise.MultiAgentWorkflow.Session` |
| 8 | Move session store | Extract `ISessionStore` and `SessionData` from `AgentSessionManager.cs` → `Infrastructure/SessionStore/ISessionStore.cs`; `InMemorySessionStore.cs` → `Infrastructure/SessionStore/InMemory/`. Note: `ISessionStore` interface and `SessionData` record are currently defined at the bottom of `AgentSessionManager.cs`, not in their own files. | `FinWise.MultiAgentWorkflow.Infrastructure.SessionStore` / `.InMemory` |
| 9 | Move model | `Models.cs` → `DomainModel/UserProfile.cs` (only `UserProfile` remains after step 1 deleted `WorkflowExecutionContext`) | `FinWise.Orchestrator` → `FinWise.MultiAgentWorkflow.DomainModel` |
| 10 | Move profile store | `Infrastructure/UserProfileStore/` entire subtree | `FinWise.Orchestrator.Services.*` → `FinWise.MultiAgentWorkflow.Infrastructure.*` |
| 11 | Update namespaces | All moved files | Verify all `using` statements compile |

### Phase 3: Create Workflow Service

| # | Step | Details |
|---|------|---------|
| 12 | Create `WorkflowResponse.cs` | `record WorkflowResponse(string Response, string ConversationId, bool WasReset)` |
| 13 | Move helpers | `ExtractUserIdFromConversationHistory()` moves to `Session/SessionConstants.cs`. `AppendUniqueMessages()` and `BuildMessageSignature()` are inlined as `internal static` methods in `FinWiseWorkflowService`. No separate `WorkflowHelpers.cs` file. |
| 14 | Create `FinWiseWorkflowService.cs` | Constructor: `(IChatClient chatClient, IUserProfileStore profileStore, ISessionStore sessionStore)`. Creates `AgentSessionManager` internally from injected `sessionStore`. `Program.cs` creates and injects the session store — same composition-root principle as profile store. Methods: `ProcessMessageAsync`, `ResetConversationAsync`, private `ExecuteWorkflowAsync` |

**`FinWiseWorkflowService.ProcessMessageAsync`** extracts from current `GetFinancialAdvice()`:
- Agent creation + handoff workflow construction (from `createAgentsAndWorkflow` lambda)
- Session restore via `AgentSessionManager.GetOrCreateSessionAsync()`
- Reset evaluation via `SessionResetEvaluator.ShouldResetSession()` — if triggered, returns new conversationId + `WasReset=true`
- Email query augmentation (standalone email detection)
- `ConversationRunContext.Push()` scope
- Workflow execution via private `ExecuteWorkflowAsync()`
- Orchestrator text-emission validation
- Message deduplication via `AppendUniqueMessages()` (inlined in `FinWiseWorkflowService`)
- Session persistence via `AgentSessionManager.PersistSessionAsync()`
- Returns `WorkflowResponse`

**`FinWiseWorkflowService.ResetConversationAsync`**:
- Clears session via `AgentSessionManager.ClearSessionAsync()`
- Returns new conversationId

### Phase 4: Rename + Slim MCP Host

| # | Step | Details |
|---|------|---------|
| 15 | Rename project | `src/FinWise.Orchestrator/` → `src/FinWise.McpServer/`. Rename csproj. Update `FinWise.slnx`. Add `<ProjectReference>` to `FinWise.MultiAgentWorkflow`. Remove workflow-owned packages. Keep: `ModelContextProtocol`, `.AspNetCore`, `Serilog.AspNetCore`, `Microsoft.Extensions.AI`, `Azure.AI.OpenAI`, `Microsoft.Extensions.AI.OpenAI`. Delete sub-solution `src/FinWise.Orchestrator/FinWise.Orchestrator.sln` (single-project sln, no longer needed). |
| 16 | Create `Tools/FinWiseTools.cs` | **NEW** attribute-based MCP tool class with 2 tools: `run_finwise_workflow` (merges `GetFinancialAdvice` + `ManageUserProfile`) and `reset_conversation`. Uses `[McpServerToolType]` / `[McpServerTool]` attributes. Resolves `FinWiseWorkflowService` and `McpSessionMapping` from `IServiceProvider`. |
| 17 | Create `McpSessionMapping.cs` | **NEW** small helper class wrapping `ConcurrentDictionary<string, string>` + `GetSessionId()` static method + `GetOrCreateConversationId()` + `UpdateConversationId()`. Replaces loose local functions + dict in Program.cs. |
| 18 | Rewrite `Program.cs` | **Keep**: Serilog setup, Azure OpenAI client, config loading, CosmosDB store creation, `WebApplication.CreateBuilder`, `app.MapMcp()`. **Add**: `ISessionStore sessionStore = new InMemorySessionStore()`, `new FinWiseWorkflowService(chatClient, profileStore, sessionStore)`, `builder.Services.AddSingleton(workflowService)`, `builder.Services.AddSingleton(new McpSessionMapping())`, `builder.Services.AddHttpContextAccessor()`, `.WithToolsFromAssembly()`. **Remove entirely**: all 3 closure-based tool functions, all `McpServerTool.Create()` blocks, `createAgentsAndWorkflow` lambda, `ExecuteWorkflowAsync()`, `AppendUniqueMessages()`, `BuildMessageSignature()`, `ExtractUserIdFromConversationHistory()`, `sessionManager` creation (now internal to workflow service), `GetSessionId()`, `GetOrCreateConversationId()`, `sessionConversations` dict (~400 lines removed). |
| 19 | Update `Infrastructure.cs` | Namespace `FinWise.Orchestrator` → `FinWise.McpServer` |

### Phase 5: AGENTS.md

| # | Step | Details |
|---|------|---------|
| 20 | Create `MultiAgentWorkflow/AGENTS.md` | Slim guardrails — see section 8 below |
| 21 | Rewrite `McpServer/AGENTS.md` | Slim guardrails — see section 8 below |
| 22 | Update root `AGENTS.md` | Architecture diagram (two projects), repository structure, build commands (`FinWise.slnx`), project instructions table |

### Phase 6: Test Projects

| # | Step | Details |
|---|------|---------|  
| 23 | Create `FinWise.MultiAgentWorkflow.UnitTests` | New csproj: xUnit, FluentAssertions, Moq, `Microsoft.NET.Test.Sdk`, `coverlet.collector`, ref `FinWise.MultiAgentWorkflow`. **No direct refs** to `Microsoft.Agents.AI.Abstractions`, `Microsoft.Agents.AI.Workflows`, or `Microsoft.Azure.Cosmos` (these come transitively via the project reference — the current test csproj has them redundantly). Move from old test project: all `WorkflowTests.cs` tests (UserProfile, SetProfile, DeleteProfile, GetProfile, incremental updates), `CosmosDbUserProfileStoreTests.cs` (mock-based). Update namespaces. |
| 24 | Rename to `FinWise.McpServer.IntegrationTests` | `tests/FinWise.Orchestrator.Tests/` → `tests/FinWise.McpServer.IntegrationTests/`. Keep: `EndToEndMcpTests.cs` (MCP HTTP protocol tests), `CosmosDbUserProfileStoreIntegrationTests.cs` (live CosmosDB). Ref both projects. Remove redundant package refs that are now transitive. Update namespaces. Update E2E tests to use new tool name `run_finwise_workflow` instead of `get_financial_advice`. |

### Phase 7: Verify

| # | Step | Expected |
|---|------|----------|
| 25 | `dotnet build FinWise.slnx` | Zero errors, zero warnings |
| 26 | `dotnet test tests/FinWise.MultiAgentWorkflow.UnitTests/` | All unit tests pass |
| 27 | `dotnet test tests/FinWise.McpServer.IntegrationTests/` | All integration tests pass (E2E needs running server) |
| 28 | `dotnet run --project src/FinWise.McpServer/ --urls http://localhost:5000` | Server starts successfully |
| 29 | E2E test | New session → email prompt → profile collection → financial advice |
| 30 | Package verification | `FinWise.MultiAgentWorkflow.csproj` has **no** `ModelContextProtocol`, `Azure.AI.OpenAI`, `Microsoft.Extensions.AI.OpenAI`, `Serilog.AspNetCore` (uses `Serilog` core only) |
| 31 | Package verification | `FinWise.McpServer.csproj` has **no** `Microsoft.Agents.AI.Workflows` |
| 32 | Tool verification | MCP server exposes exactly 2 tools: `run_finwise_workflow`, `reset_conversation` |

### `src/FinWise.MultiAgentWorkflow/AGENTS.md`

```markdown
# FinWise.MultiAgentWorkflow — Project Instructions

Class library: multi-agent workflow, session management, profile storage.
Zero MCP dependencies. LLM-provider-agnostic (receives IChatClient abstraction).

## Enforcements

- Namespace = folder path. `Session/` → `FinWise.MultiAgentWorkflow.Session`
- Sub-folder names must NOT match class names (C# namespace/type collision)
- `IUserProfileStore` only accessed through `UserProfileAgentFactory` — never called directly
- Orchestrator agent: tool calls only, never text output
- `SessionResetEvaluator`: only triggers reset when PROFILE_READY marker exists in history
- All profile fields free-form text — no validation/enum constraints
- `ISessionStore` implementations must be thread-safe
- Manual DI — no DI container. Dependencies injected via constructors.
- TreatWarningsAsErrors — zero warnings allowed

## Folder Responsibilities

| Folder | Scope |
|--------|-------|
| `Agents/` | Agent factories (stateless) in folder-per-agent layout. Each folder contains a factory `.cs` and a `.prompt.md` system prompt file. Receive `IChatClient` + dependencies via constructor |
| `Workflow/` | Multi-agent handoff orchestration. `FinWiseWorkflowService` is the entry point. Also owns `WorkflowResponse`. Message dedup helpers are inlined in `FinWiseWorkflowService` |
| `Session/` | Conversation state: persist/restore (`AgentSessionManager`), reset detection (`SessionResetEvaluator`), ambient context (`ConversationRunContext`), shared constants + `ExtractUserIdFromConversationHistory` (`SessionConstants`), email validation (`EmailValidator`) |
| `DomainModel/` | Domain model: `UserProfile` |
| `Infrastructure/SessionStore/` | Session persistence infrastructure. Interface + provider implementations (InMemory, future Redis) |
| `Infrastructure/UserProfileStore/` | Profile persistence infrastructure. Interface + provider implementations (InMemory, CosmosDb) |
```

### `src/FinWise.McpServer/AGENTS.md`

```markdown
# FinWise.McpServer — Project Instructions

Thin MCP server host. Transport + composition root.
Delegates all workflow logic to `FinWise.MultiAgentWorkflow`.

## Enforcements

- Program.cs is the composition root — manual DI, registers services for attribute-based tool injection
- MCP tools live in `Tools/` folder — attribute-based (`[McpServerToolType]`), auto-discovered via `WithToolsFromAssembly()`
- Tools are thin adapters: resolve services from `IServiceProvider` → call workflow service → return response
- `Infrastructure.cs` owns Azure OpenAI client creation (deployment/config decision, not domain logic)
- Session-to-conversation mapping (`McpSessionMapping`) stays here — MCP transport concern
- Never inspect PROFILE_READY markers or conversation content — that's workflow logic
- Store creation (CosmosDB config binding, session store creation) stays here — host concern. Pass `IUserProfileStore` and `ISessionStore` to workflow.
- TreatWarningsAsErrors — zero warnings allowed

## Files

| File | Purpose |
|------|---------|
| `Program.cs` | Composition root: config, store creation, DI registration, `WithToolsFromAssembly()` |
| `Infrastructure.cs` | Azure OpenAI client factory, Serilog setup, error logging helper |
| `McpSessionMapping.cs` | Session-to-conversation mapping (wraps ConcurrentDictionary + session ID extraction) |
| `Tools/FinWiseTools.cs` | Attribute-based MCP tools: `run_finwise_workflow`, `reset_conversation` |
```

### Root `AGENTS.md` Updates

Update these sections:
- **Architecture Overview**: Two-project diagram
- **Repository Structure**: New tree with both `src/` projects and both `tests/` projects
- **Build & Test**: Commands referencing `FinWise.slnx` and new test project paths
- **Project-Specific Instructions**: Table with both `FinWise.MultiAgentWorkflow/AGENTS.md` and `FinWise.McpServer/AGENTS.md`

---

## 9. Files Inventory

### Files to Move (to `FinWise.MultiAgentWorkflow`)

| Source | Destination | Namespace |
|--------|-------------|-----------|
| `src/FinWise.Orchestrator/Agents/OrchestratorAgentFactory.cs` | `Agents/` | `.Agents` |
| `src/FinWise.Orchestrator/Agents/UserProfileAgentFactory.cs` | `Agents/` | `.Agents` |
| `src/FinWise.Orchestrator/Agents/AdvisorAgentFactory.cs` | `Agents/` | `.Agents` |
| `src/FinWise.Orchestrator/AgentSessionManager.cs` | `Session/` | `.Session` |
| `src/FinWise.Orchestrator/SessionResetEvaluator.cs` | `Session/` | `.Session` |
| `src/FinWise.Orchestrator/ConversationRunContext.cs` | `Session/` | `.Session` |
| `src/FinWise.Orchestrator/InMemorySessionStore.cs` | `Infrastructure/SessionStore/InMemory/` | `.Infrastructure.SessionStore.InMemory` |
| `src/FinWise.Orchestrator/Models.cs` | `DomainModel/UserProfile.cs` (only `UserProfile` remains — `WorkflowExecutionContext` deleted in step 1) | `.DomainModel` |
| `src/FinWise.Orchestrator/Services/UserProfileStore/IUserProfileStore.cs` | `Infrastructure/UserProfileStore/` | `.Infrastructure.UserProfileStore` |
| `src/FinWise.Orchestrator/Services/UserProfileStore/InMemory/InMemoryUserProfileStore.cs` | `Infrastructure/UserProfileStore/InMemory/` | `.Infrastructure.UserProfileStore.InMemory` |
| `src/FinWise.Orchestrator/Services/UserProfileStore/CosmosDb/CosmosDbUserProfileStore.cs` | `Infrastructure/UserProfileStore/CosmosDb/` | `.Infrastructure.UserProfileStore.CosmosDb` |
| `src/FinWise.Orchestrator/Services/UserProfileStore/CosmosDb/CosmosDbOptions.cs` | `Infrastructure/UserProfileStore/CosmosDb/` | `.Infrastructure.UserProfileStore.CosmosDb` |
| `src/FinWise.Orchestrator/Services/UserProfileStore/CosmosDb/UserProfileDocument.cs` | `Infrastructure/UserProfileStore/CosmosDb/` | `.Infrastructure.UserProfileStore.CosmosDb` |

### Files to Create

| File | Purpose |
|------|---------|
| `src/FinWise.MultiAgentWorkflow/FinWise.MultiAgentWorkflow.csproj` | Class library project file |
| `src/FinWise.MultiAgentWorkflow/Workflow/FinWiseWorkflowService.cs` | Core orchestration class |
| `src/FinWise.MultiAgentWorkflow/Workflow/WorkflowResponse.cs` | Result DTO |
| `src/FinWise.MultiAgentWorkflow/Session/SessionConstants.cs` | Shared constants (`ProfileReadyMarker`) + `ExtractUserIdFromConversationHistory` |
| `src/FinWise.MultiAgentWorkflow/Session/EmailValidator.cs` | Email validation utility |
| `src/FinWise.MultiAgentWorkflow/Infrastructure/SessionStore/ISessionStore.cs` | Extracted interface + `SessionData` record (currently co-located in `AgentSessionManager.cs`) |
| `src/FinWise.MultiAgentWorkflow/AGENTS.md` | Project guardrails |
| `src/FinWise.McpServer/Tools/FinWiseTools.cs` | Attribute-based MCP tools (2 tools) |
| `src/FinWise.McpServer/McpSessionMapping.cs` | Session-to-conversation mapping helper |
| `tests/FinWise.MultiAgentWorkflow.UnitTests/FinWise.MultiAgentWorkflow.UnitTests.csproj` | Unit test project |

### Files to Rewrite

| File | Change |
|------|--------|
| `src/FinWise.Orchestrator/Program.cs` → `src/FinWise.McpServer/Program.cs` | From ~520 lines to ~125 lines (composition root including CosmosDB configuration — tools in `Tools/`, session mapping in `McpSessionMapping`) |

### Files to Rename/Update

| File | Change |
|------|--------|
| `FinWise-orchestrator-mcp.sln` → `FinWise.slnx` | Add new projects, update paths |
| `src/FinWise.Orchestrator/Infrastructure.cs` → `src/FinWise.McpServer/Infrastructure.cs` | Namespace only |
| `src/FinWise.Orchestrator/FinWise.Orchestrator.csproj` → `src/FinWise.McpServer/FinWise.McpServer.csproj` | Rename, update packages, add project ref |
| `src/FinWise.Orchestrator/AGENTS.md` → `src/FinWise.McpServer/AGENTS.md` | Rewrite for slim McpServer guardrails |
| `tests/FinWise.Orchestrator.Tests/` → `tests/FinWise.McpServer.IntegrationTests/` | Rename, update refs + namespaces |
| Root `AGENTS.md` | Update structure, commands, project table |

### Test File Distribution

| Test file | Destination project | Reason |
|-----------|-------------------|--------|
| `WorkflowTests.cs` | `FinWise.MultiAgentWorkflow.UnitTests` | Tests `UserProfile`, `SetProfile`, `DeleteProfile`, `GetProfile` — all workflow lib types |
| `CosmosDbUserProfileStoreTests.cs` | `FinWise.MultiAgentWorkflow.UnitTests` | Mock-based unit tests for CosmosDB store |
| `EndToEndMcpTests.cs` | `FinWise.McpServer.IntegrationTests` | MCP HTTP protocol tests requiring running server |
| `CosmosDbUserProfileStoreIntegrationTests.cs` | `FinWise.McpServer.IntegrationTests` | Live CosmosDB integration tests |

---

## 10. Decisions

| Decision | Rationale |
|----------|-----------|
| **Manual DI with targeted DI registration** | No DI container for agents. `Program.cs` is composition root. `FinWiseWorkflowService`, `ISessionStore`, and `McpSessionMapping` registered in DI for attribute-based tool injection via `IServiceProvider`. |
| **Session-to-conversation mapping in MCP host** | `ConcurrentDictionary<sessionId, conversationId>` is an MCP transport concern. Only plain `conversationId` strings cross the boundary. |
| **`Session/` at root level** | Reusable by any agent scenario (single-agent or multi-agent). `AgentSessionManager` takes any `AIAgent`, not just workflows. |
| **`ConversationRunContext` in `Session/`** | In-flight conversation state — sibling of `AgentSessionManager` (persisted state). Both manage "current conversation state". |
| **`SessionResetEvaluator` in `Session/`** | Zero workflow dependency. Works with any conversation containing `PROFILE_READY` markers. A standalone profile agent would use it identically. |
| **`SessionStore` in `Infrastructure/`** | Pure storage infrastructure. Mirrors `UserProfileStore/` pattern (interface + provider implementations). Ready for future Redis provider. |
| **`ISessionStore` injected into `FinWiseWorkflowService`** | Same composition-root principle as `IUserProfileStore` — `Program.cs` creates and injects the store. Enables mocking in unit tests, and future swap to Redis without modifying the workflow service. |
| **`Serilog` (core) in class library, `Serilog.AspNetCore` in host only** | Class library only uses `Log.*` and `LogContext.PushProperty()` from Serilog core. `Serilog.AspNetCore` would pull ASP.NET Core dependencies into a host-agnostic library. |
| **Azure OpenAI packages in MCP host only** | `Infrastructure.CreateAzureOpenAIChatClient()` is a deployment decision. The class library is LLM-provider-agnostic via `IChatClient`. |
| **`Infrastructure.cs` stays in MCP host** | Azure client creation + Serilog setup are host-level concerns. |
| **AGENTS.md simplified** | Guardrails + enforcements only. Remove verbose tutorials and code samples. |
| **3 MCP tools merged to 2** | `ManageUserProfile` was a pass-through to `GetFinancialAdvice`. Merged into single `run_finwise_workflow`. Orchestrator routes internally. `reset_conversation` kept for deterministic reset. |
| **Attribute-based MCP tools** | `[McpServerToolType]` + `WithToolsFromAssembly()` replaces closure-based `McpServerTool.Create()`. Tools in `Tools/FinWiseTools.cs`, not in `Program.cs`. |
| **Namespace = folder path** | Per existing AGENTS.md convention. E.g. `Session/AgentSessionManager.cs` → `FinWise.MultiAgentWorkflow.Session`. |

---

## 11. Refactoring Review (Skill: Refactor)

A systematic code smell analysis was performed on all files involved in this decoupling. The purpose is to assess whether the current plan adequately addresses existing smells, and to identify what should be deferred to a Phase 2 refinement pass after decoupling.

### Smell Coverage Assessment

| Code Smell | Severity | Addressed by this plan? | Notes |
|---|---|---|---|
| **`GetFinancialAdvice()` — 170 lines, nesting depth 5** | Critical | **Yes** — extracted into `FinWiseWorkflowService.ProcessMessageAsync()` | The extraction naturally decomposes: session restore, reset eval, workflow exec, persist become private methods |
| **`Program.cs` — 350+ lines in single try block** | Critical | **Yes** — slimmed to ~125 lines (composition root including CosmosDB configuration) | Tools in `Tools/FinWiseTools.cs`, session mapping in `McpSessionMapping.cs` |
| **`ManageUserProfile()` — dead indirection** (pure pass-through to `GetFinancialAdvice`) | Medium | **Yes** — merged into single `run_finwise_workflow` tool | Orchestrator routes internally. Two identical tools confused LLM clients. |
| **3 closure-based MCP tools in Program.cs** | Medium | **Yes** — refactored to 2 attribute-based tools in `Tools/FinWiseTools.cs` | `[McpServerToolType]` + `WithToolsFromAssembly()`. Testable, clean separation. |
| **Duplicated MCP header check** (two near-identical blocks in `GetSessionId`) | Low | **Partially** — moves to `McpSessionMapping.cs` | Trivial; can be simplified to case-insensitive lookup. Defer to Phase 2. |
| **`IsNewLogicalSessionAsync()` — 65 lines, duplicated timeout logic** | Medium | **Partially** — file moves to `Session/` but internal structure unchanged | Defer method decomposition to Phase 2 |
| **`PROFILE_READY:` magic string — 8+ occurrences across 5 files** | Medium | **No** — not addressed | Defer to Phase 2: extract to shared constant |
| **Email validation duplicated** (regex in Program.cs, `@` check 3× in UserProfileAgentFactory) | Medium | **No** — not addressed | Defer to Phase 2: extract validator utility |
| **Response code string prefixes** (`FOUND_COMPLETE:`, `PARTIAL:`, `ERROR:` etc.) | Low | **No** — not addressed | Defer to Phase 2: extract constants or enum |
| **Agent prompt strings** (60-96 lines embedded in code) | Low | **No** — not addressed | Defer to Phase 2: externalize to resource files. Note: these are LLM system prompts; embedding in code is common in agent frameworks. Lower priority. |
| **`SetProfile()` — 4 string parameters** | Low | **No** — not addressed | Defer to Phase 2: consider parameter object. Note: these parameters are defined by `AIFunctionFactory.Create()` for LLM tool calling, so a parameter object may not be compatible. |
| **Hardcoded suspicious-pattern array** (`"[function call"`, `"handoff_to_"` etc.) | Low | **Partially** — moves into `FinWiseWorkflowService` | Defer constant extraction to Phase 2 |
| **`SessionTimeoutDuringProfileCollection` = 10 minutes hardcoded** | Low | **No** — not addressed | Already a named property; value is clear. No action needed. |

### Verdict

**The current plan correctly addresses the two critical smells** (massive `GetFinancialAdvice()` method and monolithic `Program.cs`) through structural decomposition. These are the high-value, high-risk issues.

**The plan intentionally does NOT address granular smells** (magic strings, duplicated validation, prompt externalization). This is correct per the refactoring principle: **"one change at a time"**. Mixing project decoupling with internal code cleanup would increase risk and make failures harder to diagnose.

### Phase 2: Post-Decoupling Refinement (Deferred)

After the decoupling is complete, verified, and committed, these internal code cleanup improvements can be tackled in a separate pass:

| # | Improvement | Files | Effort |
|---|---|---|---|
| 1 | ~~**Extract `PROFILE_READY:` constant** to shared location (e.g., `Session/SessionConstants.cs`)~~ | ~~5 files~~ | ~~Small~~ — **Done**: `SessionConstants.ProfileReadyMarker` |
| 2 | ~~**Extract email validation** to `Session/EmailValidator.cs`~~ | ~~`FinWiseWorkflowService.cs`, `UserProfileAgentFactory.cs`~~ | ~~Small~~ — **Done**: `Session/EmailValidator.cs` |
| 3 | **Extract response code constants** (`FOUND_COMPLETE`, `PARTIAL`, `ERROR`, etc.) | `UserProfileAgentFactory.cs` | Small |
| 4 | **Decompose `IsNewLogicalSessionAsync()`** into focused private methods (timeout check, profile-ready check) | `AgentSessionManager.cs` | Small |
| 5 | **Simplify `GetSessionId()`** to case-insensitive header lookup | `McpSessionMapping.cs` (McpServer) | Trivial |
| 6 | ~~**Externalize agent prompts** to embedded resources or `.txt` files~~ | ~~3 agent factories~~ | ~~Medium~~ — **Done**: prompts externalized to `.prompt.md` files in folder-per-agent layout |
| 7 | **Extract suspicious-pattern constants** for orchestrator validation | `FinWiseWorkflowService.cs` | Trivial |
| 8 | ~~**Rename `UserProfileDto` → `UserProfile`** across all files. It's a domain model with business logic (`IsComplete`, `WithUpdates`), not a transfer object.~~ | ~~`DomainModel/`, `Agents/`, `Infrastructure/UserProfileStore/`, tests~~ | ~~Small~~ — **Done**: renamed to `UserProfile` in `DomainModel/` |

**Phase 2 guiding principle**: Each improvement is one commit. Run tests after each. Never mix with feature work.

---

## 12. Follow-up Items (Not in Scope)

1. `.vscode/tasks.json` — update paths for renamed projects and new solution name
2. `.vscode/mcp.json` — update if referencing old project paths
3. `docker-compose.yml` — update if referencing old project paths
4. `README.md` — update project structure documentation
5. Future: `Infrastructure/SessionStore/Redis/RedisSessionStore.cs` — Redis session store provider
6. Future: `Infrastructure/ChatClientFactory/` — if multiple LLM providers become a reusable pattern
7. ~~`src/FinWise.Orchestrator/FinWise.Orchestrator.sln` — sub-solution file~~ **Handled**: Deletion added to Phase 4, Step 15.
8. `src/FinWise.Orchestrator/.vscode/launch.json` — debugger attach config references `FinWise.Orchestrator.exe`. Update process name after rename to `FinWise.McpServer.exe`.
