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
| **Session persistence** | In-memory only, custom `IAgentSessionStore` | SDK's `AgentSessionStore` + `InMemoryAgentSessionStore` (dev) + `RedisAgentSessionStore` (prod) via StackExchange.Redis |
| **Message persistence** | Via `InMemoryChatHistoryProvider.GetService<T>()` | SDK's `TryGetInMemoryChatHistory()` / `SetInMemoryChatHistory()` via `StateBag` |
| **Profile gate** | Prompt-only (orchestrator instructions) | Code-enforced: workflow conditionally registers agents based on `PROFILE_READY:` presence |
| **Workflow safety** | No guard against infinite loops | 60s CancellationToken timeout + 25-invocation max guard + `SessionResetFlag` (mutable AsyncLocal token) |
| **Orchestrator routing** | Stock queries → advisor agent | Stock queries → stock-specialized-investment-agent; unsupported specializations → graceful refusal |
| **Advisor behavior** | Answers all questions | Hands off specialized investment questions (stocks, real estate, crypto) to orchestrator |

### Version Context

| Version | Status | Description |
|---------|--------|-------------|
| v0.1 | Historical | Monolithic single-project POC |
| v0.2 | Stable | Decoupled two-project architecture, 3 in-process agents |
| **v0.3** | **In Progress** | Stock specialized agent (Foundry), SDK upgrade, session fixes, Redis session store, Docker Compose |
| v0.4 | Planned | Additional agents, Azure Container Apps prep |
| v0.5 | Planned | Azure Container Apps, decoupling in-process agents into separate containers |

---

## High-Level Architecture

### v0.3: Stock Specialized Agent + Foundry Integration

**Deployment Context**: Developer workstation (Windows) with Docker Compose for infrastructure (Redis + CosmosDB emulator), accessed through any MCP-compatible AI assistant via Streamable HTTP. Stock Specialized Agent runs in Azure AI Foundry (cloud-hosted).

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
│ │   - Calls Infrastructure/ factories for dependencies  │ │
│ │   - DI registration + MCP tool auto-discovery        │ │
│ ├──────────────────────────────────────────────────────┤ │
│ │ Tools/FinWiseTools.cs (2 attribute-based MCP tools)  │ │
│ │   🔧 run_finwise_workflow (query → advice/profile)   │ │
│ │   🔧 reset_conversation  (clear session)             │ │
│ ├──────────────────────────────────────────────────────┤ │
│ │ Infrastructure/ (SRP factory classes)                │ │
│ │   McpSession/McpSessionAccessor.cs                   │ │
│ │     Extracts MCP-Session-Id → used as agentSessionId │ │
│ │   McpSession/Redis/RedisSessionMigrationHandler.cs   │ │
│ │     ISessionMigrationHandler for cross-instance scale│ │
│ │   AgentSessionStorage/AgentSessionStoreFactory.cs    │ │
│ │   UserProfileStorage/UserProfileStoreFactory.cs      │ │
│ │   AzureOpenAI/AzureOpenAIChatClientFactory.cs        │ │
│ │   AzureAIFoundry/StockAgentFactory.cs                │ │
│ │   Logging/LoggingSetup.cs                            │ │
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
│ │   SessionResetFlag / SessionResetToken               │ │
│ │   AgentSessionRunContext, AgentSessionConstants       │ │
│ │     ↳ IsProfileReady() helper for code-level gate    │ │
│ ├──────────────────────────────────────────────────────┤ │
│ │ Infrastructure/                                      │ │
│ │   AgentSessionStore → InMemoryAgentSessionStore    │ │
│ │   (SDK: Microsoft.Agents.AI.Hosting)               │ │
│ │                      → RedisAgentSessionStore        │ │
│ │   (StackExchange.Redis, TTL-based, sliding expiry)  │ │
│ │   IClearableSessionStore (explicit key deletion)    │ │
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
  │Redis 7.4 (Docker)   │    │ └───────────────────┘ │  │
  │ Session persistence │    └───────────────────────┘  │
  │ TTL-based sliding   │                               │
  │ expiry (24h default)│                               │
  │ StackExchange.Redis │                               │
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
│    • SessionResetFlag: tool-triggered reset via AsyncLocal token     │
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

- **Program.cs** is a pure composition root — calls Infrastructure/ factory classes for all external dependencies. Infrastructure responsibilities decomposed via SRP: `AzureOpenAIChatClientFactory`, `AgentSessionStoreFactory`, `UserProfileStoreFactory`, `StockAgentFactory`, `LoggingSetup`.
- **MCP Session ID** is used directly as the agent session identifier (no mapping layer). `McpSessionAccessor` extracts it from the `MCP-Session-Id` HTTP header.
- **`RedisSessionMigrationHandler`** implements `ISessionMigrationHandler` (MCP SDK 1.1.0) to persist MCP initialize handshake params in Redis (`mcpinit:*` keys), enabling cross-instance session migration without sticky sessions.
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
- `ClearSessionAsync` uses `IClearableSessionStore` pattern: Redis performs explicit key delete, in-memory is a no-op

**New: RedisAgentSessionStore**
- Extends SDK's `AgentSessionStore` abstract class + implements `IClearableSessionStore`
- Uses `StackExchange.Redis` with `IConnectionMultiplexer`
- Key format: `agentsession:{agentId}:{conversationId}`
- TTL-based sliding expiration (default 24h)
- Corrupt data handling: catches `JsonException`, deletes key, creates fresh session
- Configured via `RedisOptions` bound from `appsettings.json` `Redis` section

**New: IClearableSessionStore**
- Optional capability interface for session stores that support explicit session deletion
- Not part of the SDK's `AgentSessionStore` contract
- Implemented by `RedisAgentSessionStore`; `InMemoryAgentSessionStore` (SDK) does not implement it

**New: SessionResetFlag / SessionResetToken**
- Replaced `AgentSessionResetEvaluator` — reset is now tool-triggered, not history-evaluated
- `SessionResetToken` is a mutable token set by the orchestrator's `request_session_reset` tool during workflow execution
- `SessionResetFlag` provides ambient `AsyncLocal` access — parent creates token before workflow, tools mutate it, parent reads after workflow completes

**Updated: AgentSessionConstants**
- Added `IsProfileReady(List<ChatMessage>)` — code-level check for profile gate

**Deleted: IAgentSessionStore, AgentSessionData, InMemoryAgentSessionStore (custom), AgentSessionResetEvaluator**
- Custom store interface, metadata record, and in-memory implementation replaced by SDK's `AgentSessionStore` + `InMemoryAgentSessionStore`
- `AgentSessionResetEvaluator` replaced by `SessionResetFlag` / `SessionResetToken`

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
| Protocol | MCP via ModelContextProtocol.AspNetCore | 1.1.0 |
| Logging | Serilog (structured) | — |
| Session Storage | In-memory (dev) / Redis via StackExchange.Redis (prod) | — |
| Profile Storage | In-memory (dev) / Azure Cosmos DB (optional) | — |
| Testing | xUnit, FluentAssertions, Moq | — |
| Packages | Centralized in `Directory.Packages.props` | — |

### New Packages Added in v0.3

| Package | Purpose |
|---------|---------|
| `Microsoft.Agents.AI.AzureAI` | Foundry agent integration — resolves remote agents by name |
| `Microsoft.Agents.AI.Hosting` | SDK's `AgentSessionStore` + `InMemoryAgentSessionStore` for session persistence |
| `StackExchange.Redis` | Redis client for durable session storage with TTL-based expiry |
| `Azure.AI.Projects` | Azure AI Foundry client SDK — project-level operations |
| `Azure.AI.Projects.OpenAI` | OpenAI integration for Foundry projects |

---

## Session Persistence Strategy

### Current State (v0.3 — SDK's AgentSessionStore + Redis)

Uses the SDK's `AgentSessionStore` abstract class with two implementations:
- **`InMemoryAgentSessionStore`** (from `Microsoft.Agents.AI.Hosting`) — for local development without Docker
- **`RedisAgentSessionStore`** (custom, extends `AgentSessionStore`) — for durable session persistence

Messages are accessed via `TryGetInMemoryChatHistory()` / `SetInMemoryChatHistory()` extension methods (added in RC4), which access `StateBag["InMemoryChatHistoryProvider"]` directly. The previous `SerializedMessages` workaround has been eliminated. See [Journal 03 — The Ghost in the Session](../journal/03-%20The%20Ghost%20in%20the%20Session.md) for the history.

Agent factories use `ChatClientAgentOptions` with stable `Id` properties, which the SDK store uses for composite keys (`{agentId}:{conversationId}`).

### Redis Implementation Details

- **Key format**: `agentsession:{agentId}:{conversationId}` — namespaced to separate from other Redis stores
- **TTL**: Sliding expiration (default 24 hours, configurable via `SessionTtlMinutes`)
- **Serialization**: Uses SDK's `agent.SerializeSessionAsync()` / `agent.DeserializeSessionAsync()` — session blobs stored as JSON strings
- **Corrupt data handling**: Catches `JsonException`, deletes corrupt key, creates fresh session
- **Session clearing**: Implements `IClearableSessionStore` for explicit key deletion on reset (in-memory is a no-op)
- **Config**: Toggled via `Redis:Enabled` in `appsettings.json`; when `false`, falls back to `InMemoryAgentSessionStore`
- **Infrastructure**: Redis 7.4-alpine via Docker Compose

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
| `FinWise.MultiAgentWorkflow.UnitTests` | Unit | ~66 | Workflow logic, session management, `SessionResetFlag`, `AgentSessionConstants`, `AgentSessionManager`, `AgentSessionRunContext`, `RedisAgentSessionStore` (unit), `StockSpecializedAgentFactory`, `CosmosDbUserProfileStore`, message deduplication |
| `FinWise.McpServer.UnitTests` | Unit | ~9 | `McpSessionAccessor`, `RedisSessionMigrationHandler` |
| `FinWise.McpServer.IntegrationTests` | Integration (E2E) | 8 | Full MCP protocol: profile creation, session persistence, cross-session reuse, stock agent handoff, reset, tool discovery |
| `FinWise.Redis.IntegrationTests` | Integration | ~12 | `RedisAgentSessionStore` + `RedisSessionMigrationHandler` against live Redis (requires Docker) |
| `FinWise.CosmosDb.IntegrationTests` | Integration | ~10 | `CosmosDbUserProfileStore` against CosmosDB emulator |
| `FinWise.StockAgent.IntegrationTests` | Integration | ~4 | Standalone Foundry agent tests (independent of workflow) |

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
| Additional specialized agents (Real Estate, etc.) | v0.4+ |
| PostgreSQL for strategies/analytics | v0.4+ |
| Azure Container Apps deployment | v0.5 |
| Decoupling in-process agents into separate containers | v0.5+ |

> **Note on A2A / Remote Agents:** Remote agent communication is **already present in v0.3** via the Stock Specialized Agent hosted in Azure AI Foundry. It implements the same `AIAgent` abstract class and participates in handoff workflows identically to in-process agents. What v0.5+ addresses is decoupling our *custom* in-process agents (Profile, Advisor) into separate Docker containers — each running as an independent remote agent. Until then, custom agents remain in-process classes within the workflow.

---

## Key Design Decisions in v0.3

### 1. Code-Enforced Profile Gate (Not LLM-Dependent)

The orchestrator's prompt says "route to profile agent if no PROFILE_READY." But LLMs don't always follow instructions (especially `gpt-4o-mini`). The fix: **physically remove advisor/stock agents from the workflow** until `PROFILE_READY:` exists in message history. The wrong routing path is impossible, not just discouraged.

### 2. SDK's AgentSessionStore with Redis + StateBag-Based Message Access

Session persistence uses the SDK's `AgentSessionStore` abstract class with two implementations: `InMemoryAgentSessionStore` (SDK, for dev) and `RedisAgentSessionStore` (custom, for durable persistence). Redis is the default in `appsettings.json`; toggled via `Redis:Enabled`. Messages are accessed via `TryGetInMemoryChatHistory()` / `SetInMemoryChatHistory()` extension methods, which read/write `StateBag["InMemoryChatHistoryProvider"]`. This replaced the earlier `SerializedMessages` workaround (see [Journal 03](../journal/03-%20The%20Ghost%20in%20the%20Session.md)). Agent factories use `ChatClientAgentOptions` with stable `Id` for the SDK store's composite key pattern. Redis uses `StackExchange.Redis` with sliding TTL and handles corrupt data gracefully.

### 3. Foundry Agent as `AIAgent` (Same Interface)

The stock agent from Azure AI Foundry implements the same `AIAgent` abstract class as in-process `ChatClientAgent` agents. This means:
- Same handoff mechanism (`AgentWorkflowBuilder.WithHandoffs`)
- Same workflow integration (`InProcessExecution.RunStreamingAsync`)
- Swap between local and remote agents without changing workflow code

### 4. Advisor Hands Off Specialized Questions

The advisor agent no longer tries to answer stock-specific, real estate, crypto, or commodity questions. It hands off to the orchestrator, which routes to the appropriate specialized agent or responds with "we don't currently support that specialization."

---

**Document End**
