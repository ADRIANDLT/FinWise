# FinWise.MultiAgentWorkflow

Class library: multi-agent orchestration, session management usage, profile storage usage. Zero MCP package/type dependencies. LLM-provider-agnostic (receives `IChatClient`).

## Technology

.NET 10, C# latest, Microsoft.Agents.AI (1.0 GA), Microsoft.Agents.AI.Foundry, Microsoft.Extensions.AI, Redis, CosmosDB.
Packages centralized in `Directory.Packages.props`. Testing: xUnit, FluentAssertions, Moq.

## MUST

- Namespace = folder path
- Sub-folder names must NOT match class names (C# namespace/type collision)
- Hub-and-spoke handoffs — all agents route through Orchestrator
- Any external infrastructure dependency (datastores, search/grounding, APIs, tools) must be accessed via workflow abstractions (interfaces/abstract classes); concrete adapters live in host/infrastructure and are injected via DI. Aligned to Domain-Driven Design patterns on ingrastructure dependency depends on the domain/workflow layer, not vice-versa.
- If consuming an MCP tool for agent context/grounding, keep interface contracts and neutral DTOs in this workflow and implement MCP client/protocol mechanics in adapter/host infrastructure wired via DI to return plain domain data only
- All profile fields free-form text — no validation/enum constraints
- Agents are stateless — all state lives in `AgentSession`
- TreatWarningsAsErrors — zero warnings

## MUST NOT

- Never reference MCP packages or types
- Never call agents directly from another agent (always through Orchestrator)

## Folder Responsibilities

| Folder | Scope |
|--------|-------|
| `Agents/` | Agent factories (stateless). Each has a `.prompt.md` for system prompt |
| `Workflow/` | `FinWiseWorkflowService` — hub-and-spoke orchestration (max 25 invocations) |
| `Session/` | `AgentSessionManager`, reset signaling, run context, `PROFILE_READY` constant |
| `DomainModel/` | `UserProfile` record |
| `Infrastructure/` | Storage implementations (Redis sessions, CosmosDB/InMemory profiles) |
