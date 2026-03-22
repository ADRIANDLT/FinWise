# FinWise.MultiAgentWorkflow — Project Instructions

Class library: multi-agent workflow, session management, profile storage.
Zero MCP dependencies. LLM-provider-agnostic (receives `IChatClient` abstraction).

## Enforcements

- Namespace = folder path
- Sub-folder names must NOT match class names (C# namespace/type collision)
- Manual DI — no DI container
- All profile fields free-form text — no validation/enum constraints
- TreatWarningsAsErrors — zero warnings allowed

## Folder Responsibilities

| Folder | Scope |
|--------|-------|
| `Agents/` | Agent factories (stateless). Receive `IChatClient` + dependencies via constructor |
| `Workflow/` | Multi-agent handoff orchestration. `FinWiseWorkflowService` is the entry point |
| `Session/` | Conversation state: persist/restore, reset signaling, ambient context |
| `DomainModel/` | Domain model: `UserProfile` |
| `Infrastructure/UserProfileStore/` | Profile persistence. Interface + provider implementations (InMemory, CosmosDb) |
