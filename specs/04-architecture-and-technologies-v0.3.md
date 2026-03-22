# FinWise: Architecture and Technology Stack (v0.3)

**Document Version:** 3.0  
**Date:** March 15, 2026  
**Status:** In Progress  
**Previous Version:** [v0.2 Architecture](03-architecture-and-technologies-v0.2.md)

## Introduction

This document describes the **v0.3 architecture** — the first version to incorporate a specialized agent hosted externally in Azure AI Foundry and to plan for durable session storage via Redis.

### What Changed from v0.2

| Aspect | v0.2 | v0.3 |
|--------|------|------|
| **Agents** | 3 in-process (Orchestrator, Profile, Advisor) | 4 agents: 3 in-process + 1 external (Stock Specialized via Azure AI Foundry) |
| **Agent Framework** | `Microsoft.Agents.AI` preview | `Microsoft.Agents.AI` 1.0.0-rc4 + `Microsoft.Agents.AI.AzureAI` (Foundry integration) |
| **Foundry SDK** | N/A | `Azure.AI.Projects` 2.0.0-beta.1, `Azure.AI.Projects.OpenAI` 2.0.0-beta.1 |
| **Stock data** | N/A | Annual reports (Apple, Microsoft, Tesla, Nvidia, Amazon) uploaded to Foundry agent as grounding files |
| **Session persistence** | In-memory only, custom `IAgentSessionStore` | SDK's `AgentSessionStore` + `InMemoryAgentSessionStore` (dev), Redis planned |
| **Message persistence** | Via `InMemoryChatHistoryProvider.GetService<T>()` | SDK's `TryGetInMemoryChatHistory()` / `SetInMemoryChatHistory()` via `StateBag` |
| **Profile gate** | Prompt-only (orchestrator instructions) | Code-enforced: workflow conditionally registers agents based on `PROFILE_READY:` presence |
| **Workflow safety** | No guard against infinite loops | 60s CancellationToken timeout + 25-invocation max guard |
| **Orchestrator routing** | Stock queries → advisor agent | Stock queries → stock-specialized-investment-agent; unsupported specializations → graceful refusal |
| **Advisor behavior** | Answers all questions | Hands off specialized investment questions (stocks, real estate, crypto) to orchestrator |

### Version Context

| Version | Status | Description |
|---------|--------|-------------|
| v0.1 | Historical | Monolithic single-project POC |
| v0.2 | Stable | Decoupled two-project architecture, 3 in-process agents |
| **v0.3** | **In Progress** | Stock specialized agent (Foundry), SDK upgrade, session fixes, Redis planned |
| v0.4 | Planned | Docker Compose, additional agents |
| v0.5 | Planned | Azure Container Apps, decoupling in-process agents into separate containers |

---

## High-Level Architecture

### v0.3: Stock Specialized Agent + Foundry Integration

**Deployment Context**: Developer workstation (Windows), accessed through any MCP-compatible AI assistant via Streamable HTTP. Stock Specialized Agent runs in Azure AI Foundry (cloud-hosted).

```
┌──────────────────────────────────────────────────────────┐
│ User's AI Assistant (VS Code / Claude Desktop / etc.)    │
│   - MCP Client built-in                                  │
└───────────────────┬──────────────────────────────────────┘
                    │ MCP Streamable HTTP
                    │ (localhost:5000, MCP-Session-Id header)
                    ▼
┌──────────────────────────────────────────────────────────┐
│ FinWise.McpServer (ASP.NET Core — Thin Host)             │
│ ┌──────────────────────────────────────────────────────┐ │
│ │ Program.cs (Composition Root)                        │ │
│ │   - Azure OpenAI client (IChatClient)                │ │
│ │   - Azure AI Foundry client (AIProjectClient)        │ │
│ │   - Store creation (InMemory / CosmosDB / Redis*)    │ │
│ │   - StockSpecializedAgentFactory → AIAgent           │ │
│ │   - DI registration + MCP tool auto-discovery        │ │
│ ├──────────────────────────────────────────────────────┤ │
│ │ Tools/FinWiseTools.cs (2 attribute-based MCP tools)  │ │
│ │   🔧 run_finwise_workflow (query → advice/profile)   │ │
│ │   🔧 reset_conversation  (clear session)             │ │
│ ├──────────────────────────────────────────────────────┤ │
│ │ McpSessionMapping.cs                                 │ │
│ │   MCP-Session-Id → agentSessionId mapping            │ │
│ └──────────────────────────────────────────────────────┘ │
│                        │                                  │
│         IChatClient + IUserProfileStore + AgentSession-    │
│         Store (SDK) + AIAgent (stock) injected             │
└────────────────────────┼─────────────────────────────────┘
                         ▼
┌──────────────────────────────────────────────────────────┐
│ FinWise.MultiAgentWorkflow (Class Library — No MCP)      │
│ ┌──────────────────────────────────────────────────────┐ │
│ │ Workflow/                                            │ │
│ │   FinWiseWorkflowService (core orchestration)        │ │
│ │   - Profile gate: conditional agent registration     │ │
│ │   - 60s timeout + 25-invocation max guard            │ │
│ │   - SDK AgentSessionStore for session persistence    │ │
│ ├──────────────────────────────────────────────────────┤ │
│ │ Agents/ (folder-per-agent with .prompt.md files)     │ │
│ │   🤖 OrchestratorAgent  (silent router, tool calls)  │ │
│ │   🤖 UserProfileAgent   (get/set/delete profile)     │ │
│ │   🤖 AdvisorAgent        (general financial advice)   │ │
│ │   🤖 StockSpecializedAgent (Azure AI Foundry)        │ │
│ │       ↳ Grounded in annual reports (5 companies)     │ │
│ │       ↳ Resolved by name via AIProjectClient         │ │
│ │   Handoff: hub-and-spoke via OrchestratorAgent       │ │
│ ├──────────────────────────────────────────────────────┤ │
│ │ Session/                                             │ │
│ │   AgentSessionManager (delegates to SDK store)      │ │
│ │   AgentSessionResetEvaluator                         │ │
│ │   AgentSessionRunContext, AgentSessionConstants       │ │
│ │     ↳ IsProfileReady() helper for code-level gate    │ │
│ ├──────────────────────────────────────────────────────┤ │
│ │ Infrastructure/                                      │ │
│ │   AgentSessionStore → InMemoryAgentSessionStore    │ │
│ │   (SDK: Microsoft.Agents.AI.Hosting)               │ │
│ │                      → RedisAgentSessionStore (plan) │ │
│ │   IUserProfileStore → InMemoryUserProfileStore       │ │
│ │                     → CosmosDbUserProfileStore        │ │
│ └──────────────────────────────────────────────────────┘ │
└──────────────┬───────────────┬───────────────────────────┘
               │               │
  ┌────────────┴──┐    ┌──────┴──────────────────────────┐
  ▼               ▼    ▼                                  │
┌──────────┐ ┌──────────────┐ ┌───────────────────────┐  │
│Azure     │ │Azure Cosmos  │ │Azure AI Foundry       │  │
│OpenAI    │ │DB (profiles) │ │ ┌───────────────────┐ │  │
│Service   │ │(optional)    │ │ │Stock Specialized  │ │  │
│(LLM)    │ │              │ │ │Investment Agent   │ │  │
│          │ │              │ │ │                   │ │  │
│gpt-4o-  │ │              │ │ │Grounding data:   │ │  │
│mini     │ │              │ │ │ • Apple reports   │ │  │
│          │ │              │ │ │ • Microsoft rpts  │ │  │
└──────────┘ └──────────────┘ │ │ • Tesla reports   │ │  │
                              │ │ • Nvidia reports  │ │  │
  ┌─────────────────────┐    │ │ • Amazon reports  │ │  │
  │Redis (planned)      │    │ └───────────────────┘ │  │
  │ Session persistence │    └───────────────────────┘  │
  │ TTL-based expiry    │                               │
  └─────────────────────┘                               │
```

### Agent Workflow — Hub-and-Spoke with Profile Gate

```
┌─────────────────────────────────────────────────────────────────────┐
│                    Hub-and-Spoke Agent Workflow                      │
│                                                                     │
│                    ┌──────────────────────┐                          │
│          ┌────────▶│  🤖 OrchestratorAgent │◀────────┐               │
│          │         │   (Silent Router)     │         │               │
│          │         │   Tool calls only     │         │               │
│          │         └┬─────────┬──────────┬┘         │               │
│          │          │         │          │           │               │
│          │  always  │         │          │  only if  │               │
│          │  avail.  │         │          │  PROFILE_ │               │
│          │          │         │          │  READY    │               │
│          │          ▼         ▼          ▼           │               │
│          │  ┌───────────┐ ┌────────┐ ┌───────────┐  │               │
│          │  │🤖 Profile │ │🤖 Adv. │ │🤖 Stock   │  │               │
│          │  │  Agent    │ │ Agent  │ │ Specialist│  │               │
│  handoff │  │           │ │        │ │(Foundry)  │  │  handoff      │
│  _to_    │  │ Tools:    │ │General │ │           │  │  _to_         │
│  _orch   │  │ get/set/  │ │finance:│ │Grounded   │  │  _orch        │
│          │  │ delete    │ │retire, │ │in annual  │  │               │
│          │  │ profile   │ │budget, │ │reports    │  │               │
│          │  │           │ │bonds   │ │(5 cos.)   │  │               │
│          │  │ Emits     │ │        │ │           │  │               │
│          │  │PROFILE_   │ │Hands   │ │Stock      │  │               │
│          │  │READY:     │ │off any │ │picks,     │  │               │
│          └──┤when done  │ │specl.  │ │analysis,  │──┘               │
│             │           │ │qs to   │ │company    │                  │
│             │           │ │orch.   │ │financials │                  │
│             └───────────┘ └────────┘ └───────────┘                  │
│                                                                     │
├─────────────────────────────────────────────────────────────────────┤
│  CODE-ENFORCED ROUTING (not LLM-dependent):                         │
│                                                                     │
│  if (!PROFILE_READY in history):                                    │
│    availableAgents = [profileAgent]           ← ONLY profile        │
│  else:                                                              │
│    availableAgents = [profileAgent, advisorAgent, stockAgent]       │
│                                                                     │
│  SAFETY NETS:                                                       │
│    • CancellationToken timeout: 60 seconds                          │
│    • Max agent invocations: 25 per request                          │
│    • OperationCanceledException → user-friendly message             │
│                                                                     │
│  PROMPT-BASED ROUTING (within available agents):                    │
│    • Stock-related → stock-specialized-investment-agent              │
│    • General finance (retirement, bonds, budget) → advisor_agent    │
│    • Profile management → profile_agent                             │
│    • Unsupported specialization → orchestrator responds directly    │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Components

### FinWise.McpServer (ASP.NET Core MCP Server Host)

Same thin host as v0.2, with additions:

- **Program.cs** now creates `AIProjectClient` (Azure AI Foundry) and resolves the stock agent by name via `StockSpecializedAgentFactory.CreateAgentAsync()`. The stock agent (`AIAgent`) is injected as a singleton into `FinWiseWorkflowService`.
- **Environment variables** added: `STOCK_AGENT_PROJECT_ENDPOINT`, `STOCK_AGENT_NAME`, `FINWISE_AZURE_TENANT_ID`, `FINWISE_AZURE_CLIENT_ID`, `FINWISE_AZURE_CLIENT_SECRET`

### FinWise.MultiAgentWorkflow (Class Library)

**New Agent: StockSpecializedAgent**
- Hosted in Azure AI Foundry (not in-process LLM calls)
- Resolved by name via `AIProjectClient.GetAIAgentAsync(name)`
- Implements `AIAgent` (SDK abstract class) — same interface as `ChatClientAgent`
- Grounded in uploaded annual reports: Apple, Microsoft, Tesla, Nvidia, Amazon
- Provides stock-specific advice: picks, analysis, company financials, recommendations
- The Foundry agent manages its own conversation threads server-side

**Updated: FinWiseWorkflowService**
- Constructor accepts `AIAgent stockAgent` (4th parameter)
- `CreateAgentsAndWorkflow(agentSessionId, isProfileReady)` — conditionally includes advisor/stock agents
- `ExecuteWorkflowAsync` — accepts `CancellationToken`, tracks invocation count
- `ProcessMessageAsync` — checks `IsProfileReady()` before building workflow; catches `OperationCanceledException`

**Updated: AgentSessionManager**
- Delegates to SDK's `AgentSessionStore` (from `Microsoft.Agents.AI.Hosting`) — no custom serialize/deserialize
- `GetOrCreateSessionAsync` returns `(AgentSession, List<ChatMessage>)` via `TryGetInMemoryChatHistory()`
- `PersistSessionAsync` calls `SetInMemoryChatHistory()` then `SaveSessionAsync()` — 4 params (no `userId`)
- `ClearSessionAsync` is a no-op for in-memory store (orphaned keys are harmless)

**Updated: AgentSessionConstants**
- Added `IsProfileReady(List<ChatMessage>)` — code-level check for profile gate

**Deleted: IAgentSessionStore, AgentSessionData, InMemoryAgentSessionStore**
- Custom store interface, metadata record, and in-memory implementation replaced by SDK's `AgentSessionStore` + `InMemoryAgentSessionStore`

**Updated: Agent Factories (All 3)**
- Switched from convenience constructor to `ChatClientAgentOptions` with stable `Id` for SDK store composite key

**Updated: Agent Prompts**
- **OrchestratorAgent**: Stock-related queries → stock agent; unsupported specializations → direct response
- **AdvisorAgent**: New STEP 2 — hands off all specialized investment questions to orchestrator

---

## Technology Stack

| Category | Technology | Version |
|----------|------------|---------|
| Runtime | .NET 10, C# latest | — |
| AI (LLM) | Microsoft.Extensions.AI + Azure OpenAI | — |
| Agent Framework | Microsoft.Agents.AI | 1.0.0-rc4 |
| Agent Framework (Abstractions) | Microsoft.Agents.AI.Abstractions | 1.0.0-rc4 |
| Agent Framework (Workflows) | Microsoft.Agents.AI.Workflows | 1.0.0-rc4 |
| Agent Framework (Hosting) | Microsoft.Agents.AI.Hosting | 1.0.0-preview.260311.1 |
| Agent Framework (Foundry) | Microsoft.Agents.AI.AzureAI | 1.0.0-rc4 |
| Foundry SDK | Azure.AI.Projects | 2.0.0-beta.1 |
| Foundry SDK (OpenAI) | Azure.AI.Projects.OpenAI | 2.0.0-beta.1 |
| Protocol | MCP via ModelContextProtocol.AspNetCore | — |
| Logging | Serilog (structured) | — |
| Session Storage | In-memory (dev) → Redis (planned) | — |
| Profile Storage | In-memory (dev) / Azure Cosmos DB (optional) | — |
| Testing | xUnit, FluentAssertions, Moq | — |
| Packages | Centralized in `Directory.Packages.props` | — |

### New Packages Added in v0.3

| Package | Purpose |
|---------|---------|
| `Microsoft.Agents.AI.AzureAI` | Foundry agent integration — resolves remote agents by name |
| `Microsoft.Agents.AI.Hosting` | SDK's `AgentSessionStore` + `InMemoryAgentSessionStore` for session persistence |
| `Azure.AI.Projects` | Azure AI Foundry client SDK — project-level operations |
| `Azure.AI.Projects.OpenAI` | OpenAI integration for Foundry projects |

---

## Session Persistence Strategy

### Current State (v0.3 — SDK's AgentSessionStore)

Uses the SDK's `AgentSessionStore` abstract class and `InMemoryAgentSessionStore` from `Microsoft.Agents.AI.Hosting`. Messages are accessed via `TryGetInMemoryChatHistory()` / `SetInMemoryChatHistory()` extension methods (added in RC4), which access `StateBag["InMemoryChatHistoryProvider"]` directly. The previous `SerializedMessages` workaround has been eliminated. See [Journal 03 — The Ghost in the Session](../journal/03-%20The%20Ghost%20in%20the%20Session.md) for the history.

Agent factories use `ChatClientAgentOptions` with stable `Id` properties, which the SDK store uses for composite keys (`{agentId}:{conversationId}`).

### Planned Migration (Next Phases)

| Phase | Store | Messages | Notes |
|-------|-------|----------|-------|
| **v0.3 current** | SDK's `InMemoryAgentSessionStore` | `StateBag`-based via `TryGetInMemoryChatHistory()` | Zero custom store code |
| **v0.3.2: Redis** | `RedisAgentSessionStore : AgentSessionStore` | Same StateBag (inside session blob) | Durable session persistence with TTL-based expiry |

See [Spec 006 — Custom AgentSessionStore vs SDK](006-custom-agent-session-store-vs-framework/006-custom-agent-session-store-vs-framework.md) for the full analysis.

### Why Redis (Not PostgreSQL) for Sessions

Redis is the recommended session store because:
- **Sub-millisecond reads** for session blobs (10–50KB typically)
- **Natural TTL** for session expiry (no cleanup jobs)
- **Key-value pattern** matches the `AgentSessionStore` contract perfectly
- **No schema** — session blobs are opaque JSON, not relational data

PostgreSQL would be over-engineered for "store a JSON blob by key." It adds schema migrations, connection pooling, and higher latency for a use case that doesn't benefit from relational queries.

PostgreSQL is better suited for future needs like queryable investment strategies, compliance audit trails, or relational user data — not ephemeral session state.

---

## Environment Variables

### v0.2 (carried forward)

| Variable | Purpose |
|----------|---------|
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI service endpoint |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | Model deployment name (e.g., `gpt-4o-mini`) |
| `AZURE_OPENAI_API_KEY` | Azure OpenAI API key |

### New in v0.3

| Variable | Purpose |
|----------|---------|
| `STOCK_AGENT_PROJECT_ENDPOINT` | Azure AI Foundry project endpoint |
| `STOCK_AGENT_NAME` | Foundry agent name (e.g., `stock-specialized-investment-agent`) |
| `FINWISE_AZURE_TENANT_ID` | Azure AD tenant for Foundry auth |
| `FINWISE_AZURE_CLIENT_ID` | Service principal client ID |
| `FINWISE_AZURE_CLIENT_SECRET` | Service principal client secret |

---

## Testing

### Test Structure

| Project | Type | Count | What it tests |
|---------|------|-------|---------------|
| `FinWise.MultiAgentWorkflow.UnitTests` | Unit | 77 | Workflow logic, session management, reset evaluator, constants, AgentSessionManager |
| `FinWise.McpServer.IntegrationTests` | Integration (E2E) | 8 | Full MCP protocol: profile creation, session persistence, cross-session reuse, stock agent handoff, reset, tool discovery |
| `FinWise.StockAgent.IntegrationTests` | Integration | — | Standalone Foundry agent tests (independent of workflow) |

### Key Integration Test: `AggressiveShortTerm_ShouldHandoffToStockSpecializedAgent`

Validates the full end-to-end handoff chain:
1. Profile creation: email → risk (aggressive) → goals (stock trading) → timeframe (short-term)
2. All 4 profile fields validated in `PROFILE_READY:` marker
3. Stock query routed through orchestrator → stock-specialized-investment-agent (Foundry)
4. Response contains personalized stock recommendations grounded in annual report data

---

## What's NOT in v0.3 Yet (Deferred to v0.4+)

| Feature | Target Version |
|---------|---------------|
| Docker Compose deployment | v0.4 |
| Additional specialized agents (Real Estate, etc.) | v0.4+ |
| PostgreSQL for strategies/analytics | v0.4+ |
| Azure Container Apps deployment | v0.5 |
| Decoupling in-process agents into separate containers | v0.5+ |

> **Note on A2A / Remote Agents:** Remote agent communication is **already present in v0.3** via the Stock Specialized Agent hosted in Azure AI Foundry. It implements the same `AIAgent` abstract class and participates in handoff workflows identically to in-process agents. What v0.5+ addresses is decoupling our *custom* in-process agents (Profile, Advisor) into separate Docker containers — each running as an independent remote agent. Until then, custom agents remain in-process classes within the workflow.

---

## Key Design Decisions in v0.3

### 1. Code-Enforced Profile Gate (Not LLM-Dependent)

The orchestrator's prompt says "route to profile agent if no PROFILE_READY." But LLMs don't always follow instructions (especially `gpt-4o-mini`). The fix: **physically remove advisor/stock agents from the workflow** until `PROFILE_READY:` exists in message history. The wrong routing path is impossible, not just discouraged.

### 2. SDK's AgentSessionStore with StateBag-Based Message Access

Session persistence uses the SDK's `AgentSessionStore` abstract class and `InMemoryAgentSessionStore` directly from `Microsoft.Agents.AI.Hosting`. Messages are accessed via `TryGetInMemoryChatHistory()` / `SetInMemoryChatHistory()` extension methods, which read/write `StateBag["InMemoryChatHistoryProvider"]`. This replaced the earlier `SerializedMessages` workaround (see [Journal 03](../journal/03-%20The%20Ghost%20in%20the%20Session.md)). Agent factories use `ChatClientAgentOptions` with stable `Id` for the SDK store's composite key pattern.

### 3. Foundry Agent as `AIAgent` (Same Interface)

The stock agent from Azure AI Foundry implements the same `AIAgent` abstract class as in-process `ChatClientAgent` agents. This means:
- Same handoff mechanism (`AgentWorkflowBuilder.WithHandoffs`)
- Same workflow integration (`InProcessExecution.RunStreamingAsync`)
- Swap between local and remote agents without changing workflow code

### 4. Advisor Hands Off Specialized Questions

The advisor agent no longer tries to answer stock-specific, real estate, crypto, or commodity questions. It hands off to the orchestrator, which routes to the appropriate specialized agent or responds with "we don't currently support that specialization."

---

**Document End**
