# FinWise.MultiAgentWorkflow — Project Instructions

Class library: multi-agent workflow, session management, profile storage.
Zero MCP dependencies. LLM-provider-agnostic (receives IChatClient abstraction).

## Enforcements

- Namespace = folder path. `Session/` → `FinWise.MultiAgentWorkflow.Session`
- Sub-folder names must NOT match class names (C# namespace/type collision)
- `IUserProfileStore` only accessed through `UserProfileAgentFactory` — never called directly
- Orchestrator agent: tool calls only, never text output
- `AgentSessionResetEvaluator`: only triggers reset when PROFILE_READY marker exists in history
- All profile fields free-form text — no validation/enum constraints
- `IAgentSessionStore` implementations must be thread-safe
- Manual DI — no DI container. Dependencies injected via constructors.
- TreatWarningsAsErrors — zero warnings allowed

## Folder Responsibilities

| Folder | Scope |
|--------|-------|
| `Agents/` | Agent factories (stateless). Receive `IChatClient` + dependencies via constructor |
| `Workflow/` | Multi-agent handoff orchestration. `FinWiseWorkflowService` is the entry point. Also owns `WorkflowResponse`, `WorkflowHelpers` |
| `Session/` | Conversation state: persist/restore (`AgentSessionManager`), reset detection (`AgentSessionResetEvaluator`), ambient context (`ConversationRunContext`) |
| `DomainModel/` | Domain model: `UserProfile` |
| `Infrastructure/AgentSessionStore/` | Session persistence infrastructure. Interface + provider implementations (InMemory, future Redis) |
| `Infrastructure/UserProfileStore/` | Profile persistence infrastructure. Interface + provider implementations (InMemory, CosmosDb) |
