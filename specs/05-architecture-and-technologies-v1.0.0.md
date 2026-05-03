# FinWise: Architecture and Technology Stack (v1.0.0)

**Document Version:** 1.0.0  
**Date:** April 17, 2026  
**Status:** Implemented  
**Previous Version:** [v0.3.1 Architecture](04-architecture-and-technologies-v0.3.1.md)  
**Key Specs:** [009 — Dockerized FinWise](009-dockerized-finwise/009-dockerized-finwise-plan.md), [011 — Azure Redis Support](011-azure-redis-support-plan/011-azure-redis-support-plan.md)  
**Journals:** [16 — ENV VAR Architecture](../journal/16-%20Externalizing%20Data%20Store%20Configuration%20and%20the%20ENV%20VAR%20Architecture.md), [17 — Azure Redis Odyssey](../journal/17-%20The%20Azure%20Redis%20Odyssey.md), [18 — Azure Cosmos DB in the Cloud](../journal/18-%20Azure%20Cosmos%20DB%20in%20the%20Cloud%20-%20From%20Emulator%20to%20Serverless%20Scale-Out.md)

---

## 1. Overview

**FinWise** is a multi-agent AI investment assistant that democratizes personalized financial guidance. Rather than using a single monolithic LLM, FinWise decomposes the advisory problem into four specialized AI agents — an orchestrator, a profile collector, a general financial advisor, and a stock market analyst — coordinated via a **hub-and-spoke handoff workflow** built on the **Microsoft Agent Framework (MAF) 1.0 GA**.

Users interact through any MCP-compatible client (VS Code with GitHub Copilot, Claude Desktop, Cursor, Windsurf). The system exposes three MCP tools (`run_finwise_workflow`, `reset_conversation`, `get_storage_info`) over the **Model Context Protocol (MCP)** Streamable HTTP transport. All conversation state lives in external stores (Redis for sessions, Azure Cosmos DB for user profiles), making the server fully stateless and horizontally scalable — validated with 5 container replicas in Azure Container Apps.

### Version History

| Version | Status | Milestone |
|---------|--------|-----------|
| v0.1 | Historical | Monolithic single-project POC with MAF built from source |
| v0.2 | Stable | "Great Decoupling" — library/host split, 3 in-process agents, 87 tests |
| v0.3 | Stable | Stock specialized agent (Azure AI Foundry), Redis session store, conditional agent gating |
| v0.3.1 | Stable | Docker container, full Docker Compose stack, MAF 1.0 GA upgrade, container tests |
| **v1.0.0** | **Implemented** | Azure cloud databases, externalized config, four deployment modes, Azure Container Apps, Docker Hub, stateless scale-out proven |

---

## 2. Architectural Principles

Six principles shaped every design decision in FinWise, from the v0.1 monolith through the v1.0.0 cloud deployment:

### 2.1 Separation of Concerns — Library vs. Host

The codebase is split into two projects:

- **`FinWise.MultiAgentWorkflow`** — a pure .NET class library containing all agent logic, workflow orchestration, session management, and storage abstractions. It has **zero MCP dependencies** and receives its LLM client via the `IChatClient` abstraction from `Microsoft.Extensions.AI`. This library can be hosted by any transport — MCP, REST, gRPC, or a console app.

- **`FinWise.McpServer`** — a thin ASP.NET Core host that wires transport (MCP Streamable HTTP), composition root (dependency injection factories), and infrastructure adapters (Azure OpenAI, Redis, CosmosDB). It delegates **all** business logic to the workflow library.

This split emerged from the "Great Decoupling" (journal entry #01–02), which reduced `Program.cs` from 520 to 125 lines and enabled isolated unit testing of all agent logic without standing up an MCP server.

### 2.2 Hub-and-Spoke Agent Orchestration

All agent communication flows through the Orchestrator — there are no direct agent-to-agent calls. The Orchestrator is a **silent router** that only emits tool calls (handoff functions), never user-facing text. This constraint is enforced both in the system prompt and validated in code:

```
User → Orchestrator → Profile Agent → Orchestrator → Advisor Agent → Orchestrator → User
                                                   → Stock Agent  → Orchestrator → User
```

The hub-and-spoke pattern was chosen over mesh topologies because:
- **Traceability** — every handoff passes through a single checkpoint
- **Safety gating** — the Orchestrator enforces the `PROFILE_READY:` gate before routing to advisory agents
- **Debuggability** — failed handoffs are detectable (orchestrator emitting text = bug)

### 2.3 Architectural Enforcement over Prompt Engineering

A critical lesson from FinWise's development: **LLM instructions are guidelines, not guarantees**. When the system relied on prompt-based gating ("only route to advisor if profile is ready"), the Orchestrator would occasionally ignore the instruction and hand off anyway, creating infinite routing loops.

The fix was **architectural enforcement**: when `PROFILE_READY:` is absent from the conversation history, the advisor and stock agents are physically removed from the handoff target list. The Orchestrator literally cannot route to agents that don't exist in its workflow definition. This pattern — enforcing invariants in code rather than trusting LLM compliance — is applied consistently throughout:

| Invariant | Enforcement |
|-----------|-------------|
| Profile completion gates advisory access | Agents removed from workflow `availableAgents` array |
| Orchestrator must not produce user-facing text | Post-execution validation replaces leaked text with fallback |
| Session reset is deterministic | `SessionResetFlag` (AsyncLocal token) overrides any LLM response |
| Max handoff depth | Hard limit of 25 invocations with 60-second timeout in `CancellationTokenSource` |

### 2.4 Stateless Server, External State

Every component that holds conversation state is backed by an external store:

| State Concern | Store | Key Pattern |
|---------------|-------|-------------|
| Agent sessions (conversation history) | Redis or InMemory | `agentsession:{agentId}:{conversationId}` |
| MCP transport session (handshake params) | Redis or disabled | `mcpinit:{sessionId}` |
| User profiles | Azure Cosmos DB or InMemory | `userId` (email) as partition key |

Per-request state that does not survive across requests uses `AsyncLocal<T>` (inherently safe for scale-out):
- `SessionResetFlag` — signals the Orchestrator's `request_session_reset` tool result to the parent workflow
- `AgentSessionRunContext` — provides ambient access to in-flight message history during one execution

This separation means the `FinWiseWorkflowService` holds only injected references (`IChatClient`, `IUserProfileStore`, `AgentSessionStore`, `AIAgent?`). It creates no mutable state of its own.

### 2.5 Coexistence, Not Migration

Every infrastructure backing (InMemory, local Docker, Azure cloud) must coexist behind the same abstraction, toggled by configuration — never a hard migration. This principle governed every store implementation:

- `IUserProfileStore` → `InMemoryUserProfileStore` / `CosmosDbUserProfileStore`
- `AgentSessionStore` (SDK abstract) → `InMemoryAgentSessionStore` (SDK) / `RedisAgentSessionStore`
- `ISessionMigrationHandler` → `RedisSessionMigrationHandler` / disabled (no Redis)

The master toggle `FINWISE_FORCE_IN_MEMORY_DATA=true` short-circuits all individual flags, guaranteeing a zero-infrastructure developer experience from `dotnet run`.

### 2.6 Stable Agent Identity

The Microsoft Agent Framework keys session storage by `{agentId}:{conversationId}`. If agent IDs were random GUIDs generated per request, sessions would be lost between requests. FinWise uses **name-based, deterministic IDs** (`orchestrator_agent`, `profile_agent`, `advisor_agent`) ensuring stable session keys. Global uniqueness comes from the `conversationId` half — the MCP Session ID.

---

## 3. System Architecture

### 3.1 High-Level View

```
┌──────────────────────────────────────────────────────────────┐
│ User's AI Assistant (VS Code / Claude Desktop / etc.)        │
│   MCP Client built-in                                        │
└───────────────────┬──────────────────────────────────────────┘
                    │ MCP Streamable HTTP (HTTP + SSE)
                    │ MCP-Session-Id header = agent session key
                    ▼
┌══════════════════════════════════════════════════════════════┐
║ FinWise.McpServer (ASP.NET Core · Composition Root)          ║
║                                                              ║
║  Program.cs → Factories create infrastructure at startup     ║
║  Tools/FinWiseTools.cs → 3 MCP tools (auto-discovered)      ║
║  GET /health → "healthy" (Docker/ACA health probe)           ║
║                                                              ║
║  ┌──────────────────────────────────────────────────────┐    ║
║  │ FinWise.MultiAgentWorkflow (Class Library)           │    ║
║  │                                                      │    ║
║  │  Workflow/ → FinWiseWorkflowService (hub-and-spoke)  │    ║
║  │  Agents/  → 4 agent factories + system prompts       │    ║
║  │  Session/ → AgentSessionManager, reset signaling     │    ║
║  │  DomainModel/ → UserProfile (immutable record)       │    ║
║  │  Infrastructure/ → Store abstractions + impls        │    ║
║  └──────────────────────────────────────────────────────┘    ║
║                          │                                    ║
║      ┌───────────────────┼───────────────────┐               ║
║      ▼                   ▼                   ▼               ║
║  Redis             CosmosDB           Azure OpenAI           ║
║  (local/Azure)     (emulator/Azure)   (gpt-4o-mini)          ║
║                                                              ║
║                    (optional)                                ║
║                    Azure AI Foundry (stock agent)             ║
╚══════════════════════════════════════════════════════════════╝
```

### 3.2 Four Deployment Modes

FinWise supports a progression from zero-infrastructure local development to fully cloud-native:

| Mode | Server | Data Stores | Use Case |
|------|--------|-------------|----------|
| **A. Full Docker Stack** | Docker container (`docker compose up`) | Local Redis + CosmosDB emulator (Docker) | Full-stack local testing |
| **B. .NET Host + Docker Infra** | `dotnet run` (host process) | Local Redis + CosmosDB emulator (Docker) | Development with debugger |
| **C. Server Container → Azure DBs** | Docker container (`docker-compose.finwise.yml`) | Azure Managed Redis + Azure Cosmos DB Serverless | Pre-production validation |
| **D. Azure Container Apps** | Azure Container Apps (Docker Hub image) | Azure Managed Redis + Azure Cosmos DB Serverless | Production cloud deployment |

**Mode D** was validated with 5 container replicas — all 8 E2E tests passing with zero session affinity. The MCP endpoint (`https://finwise-mcp-server-container-app.whitesky-2a351661.eastus2.azurecontainerapps.io/mcp`) is directly usable from VS Code or Claude Desktop.

---

## 4. Agent Architecture

### 4.1 The Four Agents

| Agent | Type | Role | Tools | Prompt Source |
|-------|------|------|-------|---------------|
| **Orchestrator** | `ChatClientAgent` | Silent router — emits only tool calls (handoff functions), never text | `request_session_reset` | Embedded `.prompt.md` (113 lines) |
| **Profile Agent** | `ChatClientAgent` | Collects user profile: email → risk tolerance → investment goals → timeframe | `get_profile`, `set_profile`, `delete_profile` | Embedded `.prompt.md` |
| **Advisor Agent** | `ChatClientAgent` | General financial advice (retirement, bonds, budgeting) personalized to profile | None (prompt-only) | Embedded `.prompt.md` |
| **Stock Agent** | `FoundryAgent` | Stock market analysis with retrieval-augmented grounding on annual reports | Managed in Foundry | Azure AI Foundry (cloud-managed) |

Three agents (`ChatClientAgent`) run in-process against Azure OpenAI's `gpt-4o-mini`. The fourth (`FoundryAgent`) runs in Azure AI Foundry's managed infrastructure with grounding data (Apple, Microsoft, Tesla, Nvidia, Amazon annual reports).

### 4.2 The Profile Gate — Conditional Agent Availability

The workflow is rebuilt dynamically based on conversation state. On every request:

1. **First pass**: create workflow with `isProfileReady: false` → only `[profileAgent]` available
2. **Check message history** for `PROFILE_READY:` marker (emitted by Profile Agent after collecting all 4 fields)
3. **If found**: rebuild workflow with `isProfileReady: true` → `[profileAgent, advisorAgent, stockAgent]` available

```csharp
AIAgent[] availableAgents = isProfileReady
    ? _stockAgent is not null
        ? [profileAgent, advisorAgent, _stockAgent]
        : [profileAgent, advisorAgent]
    : [profileAgent];

AgentWorkflow workflow = AgentWorkflowBuilder
    .CreateHandoffBuilderWith(orchestratorAgent)
    .WithHandoffs(orchestratorAgent, availableAgents)
    .WithHandoffs(availableAgents, orchestratorAgent)
    .Build();
```

This is the **architectural enforcement** described in §2.3 — the LLM cannot route to agents that don't exist in the handoff topology.

### 4.3 Agent Factory Pattern

Each agent is created by a factory class (`OrchestratorAgentFactory`, `UserProfileAgentFactory`, etc.) that:

1. **Loads the system prompt** from an embedded `.prompt.md` resource file (not a string literal in code)
2. **Registers agent-specific tools** as `AIFunction` instances (e.g., `get_profile`, `set_profile` for Profile Agent)
3. **Creates a `ChatClientAgent`** with a stable, name-based ID (`orchestrator_agent`, `profile_agent`, etc.)
4. **Injects dependencies** only where needed (Profile Agent receives `IUserProfileStore`; Advisor Agent receives nothing)

The `StockSpecializedAgentFactory` follows a different pattern — it's an **async factory** that resolves a pre-existing agent from Azure AI Foundry by name using the Azure.AI.Projects SDK. The two-step resolution process:
1. `AgentAdministrationClient.GetAgentAsync(name)` → returns `ProjectsAgentRecord`
2. `AIProjectClient.AsAIAgent(record)` → adapts to `FoundryAgent`

The agent's prompt, grounding data, and tools are managed entirely in Azure AI Foundry, not in the codebase. This agent is optional — if environment variables are missing, the workflow runs without stock analysis.

### 4.4 Orchestrator Routing Decision Tree

The Orchestrator's system prompt (113 lines) defines a strict decision tree:

```
┌─ PROFILE_READY not found → ALWAYS route to profile_agent (zero exceptions)
│
├─ PROFILE_READY found + user wants reset → call request_session_reset → respond directly
│
└─ PROFILE_READY found → check intent:
    ├─ Stock-related (any mention of stocks, tickers, companies) → stock agent
    ├─ Unsupported specialization (real estate, crypto, forex) → respond directly with decline
    ├─ Profile management (show/update/delete) → profile agent
    └─ General finance (retirement, bonds, budgeting) → advisor agent
```

The "when in doubt, choose stock agent" bias is deliberate — stock queries are the most common advisory request and the stock agent has grounding data to provide specific, data-backed answers.

### 4.5 Profile Agent — Incremental Profile Building

The Profile Agent follows a **progressive data collection** pattern:

1. **Extract email** from the current message (looks for `@` character)
2. **Check existing profile** via `get_profile(email)` → returns `FOUND_COMPLETE`, `FOUND_PARTIAL`, or `NOT_FOUND`
3. **Collect missing fields** one at a time: Risk Tolerance → Investment Goals → Investment Timeframe
4. **Save incrementally** — calls `set_profile()` after each answer, not after all fields are collected
5. **Emit `PROFILE_READY:`** marker when all 4 fields are present

The incremental save pattern ensures no data loss if the user abandons the conversation mid-profile. The `UserProfile` record uses `WithUpdates()` (immutable record copy) to merge new fields with existing values.

All profile fields are **free-form text** — no validation, no enum constraints. The LLM interprets user intent ("I'm pretty cautious" → risk tolerance), and the exact phrasing is stored as-is.

---

## 5. Workflow Execution

### 5.1 Request Lifecycle

Every MCP tool call to `run_finwise_workflow` follows this lifecycle:

```
 1. Extract MCP-Session-Id from HTTP header (= agentSessionId)
 2. Create agents with isProfileReady=false (safe default)
 3. Restore AgentSession + message history from store
 4. Check PROFILE_READY in message history
 5. If found → rebuild agents with isProfileReady=true, re-restore session
 6. Add user message to history
 7. Push AsyncLocal scopes (AgentSessionRunContext, SessionResetFlag)
 8. Execute workflow (InProcessExecution.RunStreamingAsync)
    └─ Stream WorkflowEvents: ExecutorInvokedEvent, WorkflowOutputEvent, WorkflowErrorEvent
    └─ Max 25 agent invocations, 60-second timeout
 9. Deduplicate and append workflow output messages to history
10. Validate response (detect orchestrator text leaks → replace with fallback)
11. Check SessionResetFlag token (detect tool-triggered resets)
12. If reset → override response, clear session
13. If not reset → persist session + messages to store
14. Return WorkflowResponse (response text, session ID, wasReset flag)
```

### 5.2 Message Deduplication

The Microsoft Agent Framework's handoff workflow broadcasts messages across all agents for context synchronization. This means a single user message can appear multiple times in the output stream. `AppendUniqueMessages` deduplicates by building a signature from `{role}:{authorName}:{text}` and only adding messages with new signatures.

### 5.3 Orchestrator Text Leak Detection

The Orchestrator should **never** produce user-facing text — only tool calls (handoff functions). If it does emit text, this indicates a failed handoff (the LLM generated a response instead of calling a function). The workflow validates this post-execution:

- If the last responding agent is `orchestrator_agent` and `SessionResetFlag` is not set → **replace response** with fallback text
- If the last responding agent is `orchestrator_agent` and `SessionResetFlag` is set → **allow** (reset confirmation is expected)

### 5.4 Session Reset Signaling

The Orchestrator's `request_session_reset` tool uses `AsyncLocal<T>` to communicate across the async boundary:

1. **Before workflow**: parent initializes `SessionResetFlag.Initialize()` → creates mutable `SessionResetToken`
2. **During workflow**: if the Orchestrator calls `request_session_reset`, the tool function calls `SessionResetFlag.Current?.Request()` → sets `IsRequested = true` on the token
3. **After workflow**: parent checks `resetToken.IsRequested` → if true, overrides any LLM response with a deterministic reset message and clears the session

This works because `AsyncLocal` copies references (not objects) to child tasks. The parent and child share the same `SessionResetToken` instance, so mutations are visible across the `await` boundary.

---

## 6. Session and State Management

### 6.1 Session Architecture

The `AgentSessionManager` wraps the Microsoft Agent Framework's `AgentSessionStore` abstraction:

- **`GetOrCreateSessionAsync`** — restores an `AgentSession` and its associated message history from the store. Uses the SDK's `TryGetInMemoryChatHistory()` extension.
- **`PersistSessionAsync`** — writes messages via `SetInMemoryChatHistory()` then saves the session blob. Messages are stored independently because the SDK's internal chat history provider was unreliable during deserialization (journal #03 — "The Ghost in the Session").
- **`ClearSessionAsync`** — delegates to `IClearableSessionStore` if available (Redis supports explicit deletion; InMemory does not).

### 6.2 MCP Session Migration

For multi-instance scale-out, a second Redis namespace (`mcpinit:*`) stores MCP transport handshake parameters. When a client's request lands on a different server instance, `RedisSessionMigrationHandler` restores the MCP session from Redis — enabling transparent cross-instance migration. TTL is refreshed on every successful migration read (true sliding window).

### 6.3 The `PROFILE_READY:` Marker

The `PROFILE_READY:` string (emitted by the Profile Agent) serves as a **structural protocol marker**, not an LLM instruction. It is:

- **Detected programmatically** by `AgentSessionConstants.IsProfileReady()` — scans message history for assistant messages containing the marker
- **Parsed via regex** to extract the user's email: `email=([^\s]+)`
- **Used to gate agent availability** (§4.2) — this is a code-level check, not an LLM prompt

This design means the Profile Agent's output format is a **contract** between the LLM and the workflow engine.

---

## 7. Infrastructure and Configuration

### 7.1 Composition Root

`Program.cs` is the composition root. Infrastructure is created at startup via static factory methods:

| Factory | Creates | Fallback |
|---------|---------|----------|
| `AzureOpenAIChatClientFactory` | `IChatClient` (Azure OpenAI) | None (required) |
| `UserProfileStoreFactory` | `IUserProfileStore` | `InMemoryUserProfileStore` |
| `AgentSessionStoreFactory` | `AgentSessionStore` + `IConnectionMultiplexer?` | `InMemoryAgentSessionStore` |
| `StockAgentFactory` | `AIAgent?` (Azure AI Foundry) | `null` (workflow runs without stock analysis) |

Each factory reads from the ASP.NET Core `IConfiguration` pipeline and applies `FINWISE_*` environment variable overrides. The `ForceInMemoryData` master toggle short-circuits all individual store flags.

### 7.2 Configuration Precedence

```
appsettings.json                    ← Base (ForceInMemoryData=true, all stores disabled, localhost)
  ↓ overridden by
appsettings.{Environment}.json      ← Docker: ForceInMemoryData=false, Docker DNS hostnames, stores enabled
  ↓ overridden by
FINWISE_* environment variables     ← Runtime override — Azure endpoints, custom settings
  ↓ short-circuited by
FINWISE_FORCE_IN_MEMORY_DATA=true   ← Master kill switch — ignores all individual flags
```

Defaults are designed for the **zero-infrastructure case** — `dotnet run` works out of the box with no databases. The developer opts *into* infrastructure, never out of it.

### 7.3 Environment Variable Map

Ten `FINWISE_*` variables control data store behavior:

| Env Var | Maps to | Default |
|---------|---------|---------|
| `FINWISE_FORCE_IN_MEMORY_DATA` | `ForceInMemoryData` | `true` |
| `FINWISE_COSMOSDB_ENABLED` | `CosmosDb:Enabled` | `false` |
| `FINWISE_COSMOSDB_ENDPOINT` | `CosmosDb:Endpoint` | `https://localhost:8081/` |
| `FINWISE_COSMOSDB_KEY` | `CosmosDb:Key` | Emulator well-known key |
| `FINWISE_COSMOSDB_DATABASE_NAME` | `CosmosDb:DatabaseName` | `FinWise` |
| `FINWISE_COSMOSDB_CONTAINER_NAME` | `CosmosDb:ContainerName` | `UserProfiles` |
| `FINWISE_COSMOSDB_ALLOW_INSECURE_TLS` | `CosmosDb:AllowInsecureTls` | `true` |
| `FINWISE_REDIS_ENABLED` | `Redis:Enabled` | `false` |
| `FINWISE_REDIS_CONNECTION_STRING` | `Redis:ConnectionString` | `localhost:6379` |
| `FINWISE_REDIS_SESSION_TTL_MINUTES` | `Redis:SessionTtlMinutes` | `1440` |

### 7.4 Docker Compose — Three-File Architecture

| File | Purpose | Command |
|------|---------|---------|
| `docker-compose.finwise.yml` | **Server only** — single source of truth. No `depends_on` on infra. | `docker compose -f docker-compose.finwise.yml --env-file .env --env-file .env.azure up -d` |
| `docker-compose.infra.yml` | **Infrastructure only** — CosmosDB emulator + Redis | `docker compose -f docker-compose.infra.yml up -d` |
| `docker-compose.yml` | **Full local stack** — `extends:` server + `includes:` infra + `depends_on` | `docker compose up -d` |

The key constraint: `docker-compose.finwise.yml` must be standalone so it can run against Azure databases (Mode C) without starting local emulators.

### 7.5 Layered `.env` Architecture

| File | Tracked? | Purpose |
|------|----------|---------|
| `.env` | No (secrets) | Base config: Azure OpenAI keys + local data store defaults |
| `.env.azure` | No (secrets) | Override layer: Azure Redis/CosmosDB endpoints only |
| `.env.azure.template` | Yes | Placeholder values for Azure config |

Docker Compose v2.24+ supports `--env-file .env --env-file .env.azure` where later files override earlier ones. Variables not in `.env.azure` fall through to `.env`.

---

## 8. Scale-Out Architecture

### 8.1 Stateless Containers

FinWise containers are fully stateless. All persistent state lives in external stores:

- **Agent sessions** → Azure Managed Redis (`agentsession:*` keys, 24h sliding TTL)
- **MCP transport sessions** → Azure Managed Redis (`mcpinit:*` keys, same TTL)
- **User profiles** → Azure Cosmos DB Serverless (`/userId` partition key)

Sessions created on Replica 1 are correctly retrieved by Replica 3. Profiles persisted by one container are visible to all others. No session affinity, no sticky sessions, no inter-replica coordination.

### 8.2 Azure Container Apps Deployment

| Setting | Value |
|---------|-------|
| Container App | `finwise-mcp-server-container-app` |
| Environment | `whitesky-2a351661` |
| Region | East US 2 |
| Image Source | Docker Hub: `finwiseproject/finwise-mcp-server:1.0.0` |
| Ingress | HTTPS (external), auto-TLS |
| Replicas | 1–5 (validated with 5) |
| MCP Endpoint | `https://finwise-mcp-server-container-app.whitesky-2a351661.eastus2.azurecontainerapps.io/mcp` |
| Session Affinity | Disabled |

### 8.3 Azure Managed Redis

| Setting | Value |
|---------|-------|
| Resource | `finwise-managed-redis-poc` |
| Type | `Microsoft.Cache/redisEnterprise` |
| Region | East US |
| SKU | Balanced B0 (~$7/mo) |
| Protocol | Plaintext (POC — production path: TLS + Entra ID) |
| Clustering | OSSCluster (single B0 node) |
| Port | 10000 |

**Critical discovery**: StackExchange.Redis auto-enables SSL when it detects `*.redis.azure.net` in the hostname. For Plaintext instances, `ssl=False` must be explicitly set.

### 8.4 Azure Cosmos DB

| Setting | Value |
|---------|-------|
| Capacity | Serverless (pay-per-request) |
| API | NoSQL |
| Database | `FinWise` (auto-created) |
| Container | `UserProfiles` (auto-created, partition key: `/userId`) |
| Auth | Key-based (production path: Managed Identity) |

Database and container are created on first request — no Azure Portal setup needed beyond the account.

---

## 9. Security Considerations

| Concern | Mitigation |
|---------|-----------|
| **Secrets in image** | Keys passed via env vars at runtime, never baked into the Docker image |
| **Non-root execution** | `USER $APP_UID` in Dockerfile (UID 1654) |
| **`.env` files** | All `.env` / `.env.azure` in `.gitignore`; only `.env.*.template` tracked |
| **CosmosDB emulator TLS** | `AllowInsecureTls` + `DangerousAcceptAnyServerCertificateValidator` — gated by config flag, dev only |
| **Azure Redis (POC)** | Plaintext + Access Key — production evolution: TLS → Entra ID |
| **Azure Cosmos DB** | Key-based auth — production evolution: Managed Identity |
| **Multi-stage Docker build** | No SDK, source code, or build artifacts in runtime image |
| **`.dockerignore`** | Excludes `.git/`, `bin/`, `obj/`, `.memory-bank/`, specs, docs |

---

# Part II — Technology and Stack

## 10. Technology Stack

### 10.1 Core Framework

| Category | Technology | Version | Role |
|----------|------------|---------|------|
| Runtime | .NET 10, C# latest | — | Application runtime |
| AI Abstraction | Microsoft.Extensions.AI | 10.4.1 | LLM-provider-agnostic `IChatClient` interface |
| LLM Provider | Azure.AI.OpenAI | 2.1.0 | Azure OpenAI `gpt-4o-mini` chat completions |
| Agent Framework | Microsoft.Agents.AI | 1.0.0 GA | `AIAgent`, `ChatClientAgent` abstractions |
| Agent Framework (Abstractions) | Microsoft.Agents.AI.Abstractions | 1.0.0 GA | Core agent interfaces |
| Agent Framework (Workflows) | Microsoft.Agents.AI.Workflows | 1.0.0 GA | `AgentWorkflowBuilder`, handoff orchestration |
| Agent Framework (Hosting) | Microsoft.Agents.AI.Hosting | 1.0.0-preview.260402.1 | `AgentSessionStore`, `InMemoryAgentSessionStore` |
| Agent Framework (Foundry) | Microsoft.Agents.AI.Foundry | 1.0.0 GA | `FoundryAgent` bridge to Azure AI Foundry |
| Protocol | ModelContextProtocol.AspNetCore | 1.1.0 | MCP Streamable HTTP transport |

### 10.2 Azure Services

| Service | SDK | Version | Role |
|---------|-----|---------|------|
| Azure OpenAI | Azure.AI.OpenAI | 2.1.0 | LLM inference (gpt-4o-mini) |
| Azure AI Foundry | Azure.AI.Projects | 2.0.0 GA | Stock agent resolution + execution |
| Azure AI Foundry (OpenAI) | Azure.AI.Projects.OpenAI | 2.0.0-beta.1 | Foundry agent companion package |
| Azure Cosmos DB | Microsoft.Azure.Cosmos | 3.46.1 | User profile persistence (NoSQL) |
| Azure Identity | Azure.Identity | 1.20.0 | Entra ID / service principal credentials |
| Azure Managed Redis | StackExchange.Redis | 2.12.4 | Agent session persistence |

### 10.3 Infrastructure

| Category | Technology | Version/Details |
|----------|------------|-----------------|
| Container Runtime | Docker (multi-stage build) | `sdk:10.0` → `aspnet:10.0` |
| Container Orchestration | Docker Compose | v2.24+ (multi `--env-file` support) |
| Cloud Hosting | Azure Container Apps | East US 2, HTTPS ingress |
| Container Registry | Docker Hub | `finwiseproject/finwise-mcp-server` |
| Local Redis | redis:7.4-alpine | volatile-lru, RDB persistence, 256MB limit |
| Local CosmosDB | CosmosDB Linux Emulator | HTTPS :8081, data persistence volume |
| Logging | Serilog + Serilog.AspNetCore | 4.2.0 / 8.0.3 |
| Telemetry | OpenTelemetry.Api | 1.12.0 |

### 10.4 Testing

| Package | Version | Role |
|---------|---------|------|
| xUnit | 2.9.2 | Test framework |
| FluentAssertions | 6.12.2 | Assertion library |
| Moq | 4.20.72 | Mocking framework |
| Xunit.SkippableFact | 1.5.61 | Graceful test skipping when infra is down |
| Microsoft.NET.Test.Sdk | 17.12.0 | Test runner |

### 10.5 Package Management

All NuGet versions are centralized in `Directory.Packages.props`. Global assembly versioning is controlled by `FinWiseVersion` in `Directory.Build.props`. Experimental API diagnostics (`MAAIW001`, `OPENAI001`) are suppressed globally.

---

## 11. Domain Model

### 11.1 UserProfile

```csharp
public record UserProfile(
    string UserId,              // Email address
    string? RiskTolerance,      // Free-form text ("Conservative", "I'm cautious", etc.)
    string? InvestmentGoals,    // Free-form text
    string? InvestmentTimeframe // Free-form text
)
```

Immutable record — updates create new instances via `WithUpdates()`. `IsComplete` gates advisory access. No enums, no validation — the LLM interprets user intent.

### 11.2 WorkflowResponse

```csharp
public record WorkflowResponse(string Response, string AgentSessionId, bool WasReset);
```

Returned by `FinWiseWorkflowService.ProcessMessageAsync()`. The `WasReset` flag indicates whether the session was cleared during this request.

---

## 12. MCP Tools

| Tool | Description | Behavior |
|------|-------------|----------|
| `run_finwise_workflow` | Send user messages to the financial advisor | Extracts MCP-Session-Id, calls `ProcessMessageAsync`, returns response text |
| `reset_conversation` | Clear conversation history | Calls `ResetSessionAsync`, user profiles are retained |
| `get_storage_info` | Report active storage configuration | Reads config flags, reports which stores are active (InMemory/CosmosDB/Redis) |

Tools are thin adapters: resolve service from DI → call workflow → return string. Auto-discovered via `[McpServerTool]` attributes and `WithToolsFromAssembly()`.

---

# Part III — Appendices

## Appendix A: Full Change Delta from v0.3.1

| Aspect | v0.3.1 | v1.0.0 |
|--------|--------|--------|
| **Deployment modes** | 2 (full Docker, .NET host) | **4** — added server → Azure DBs + Azure Container Apps |
| **Docker Compose files** | 2 | **3** — added `docker-compose.finwise.yml` (server-only) |
| **Docker Compose composition** | `include:` for infra | `extends:` for server + `include:` for infra |
| **MCP tools** | 2 | **3** — added `get_storage_info` |
| **Data store config** | Hardcoded in appsettings | **Externalized** — 10 `FINWISE_*` env vars + `ForceInMemoryData` |
| **`.env` architecture** | Single `.env` | **Layered** — `.env` (base) + `.env.azure` (overrides) |
| **Redis support** | Local Docker only | + **Azure Managed Redis** (`*.redis.azure.net:10000`) |
| **CosmosDB support** | Local emulator only | + **Azure Cosmos DB Serverless** |
| **CosmosDB throughput** | `throughput: 400` | **Removed** — Serverless compatibility |
| **Scale-out** | Single instance | **5 replicas in Azure Container Apps** |
| **Docker Hub** | N/A | Published to `finwiseproject/finwise-mcp-server` |
| **Azure Container Apps** | Planned | **Deployed and validated** — HTTPS, 5 replicas |
| **Test traits** | None | **xUnit Trait** — `Unit`/`Integration`/`Container` |
| **Integration tests** | Hardcoded emulator | **Target-agnostic** — env-var-driven (emulator or Azure) |
| **E2E assertions** | Rigid LLM wording | **Resilient** — structural markers only |

---

## Appendix B: Test Architecture

### B.1 Test Pyramid

```
                 ┌─────────────────────────┐
                 │ E2E Container (11)      │  4 reused MCP + 5 Docker + 2 env var
                 ├─────────────────────────┤
                 │ E2E Local (8)           │  Full MCP protocol against dotnet run
            ┌────┴─────────────────────────┴────┐
            │ Integration (per-component)        │  Redis (12), CosmosDB (13), Stock (4)
       ┌────┴───────────────────────────────────┴────┐
       │ Unit Tests (fast, isolated)                  │  MultiAgentWorkflow + McpServer
       └─────────────────────────────────────────────┘
```

### B.2 Trait Categorization

All 20 test classes use `[Trait("Category", "...")]` for selective execution:

```powershell
dotnet test --filter "Category=Unit"         # No infrastructure needed
dotnet test --filter "Category=Integration"  # Needs Docker infra or Azure credentials
dotnet test --filter "Category=Container"    # Needs full Docker stack
```

### B.3 Target-Agnostic Integration Tests

CosmosDB and Redis integration tests support both emulator and Azure targets via environment variables with emulator fallback. Availability probes use `ReadAccountAsync()` (authenticated SDK call) instead of HTTP pings.

### B.4 Resilient E2E Assertions

E2E tests no longer assert on intermediate LLM wording. Instead, they drive through profile setup steps and assert on **structural markers** (`PROFILE_READY:`) and final outcomes — making tests deterministic despite non-deterministic LLM behavior.

### B.5 Container Test Categories

| Category | Tests | What it validates |
|----------|-------|-------------------|
| `DockerizedMcpTests` | 4× `[SkippableFact]` | MCP protocol through Docker |
| `DockerContainerSpecificTests` | 5× `[SkippableFact]` | Dockerfile, networking, env var injection, startup time |
| `DockerEnvVarConfigTests` | 2× `[SkippableFact]` | `FINWISE_*` env var pipeline end-to-end |

---

## Appendix C: Docker Infrastructure

### C.1 Dockerfile — Multi-Stage Build

```
Stage 1: BUILD (sdk:10.0)              Stage 2: RUNTIME (aspnet:10.0)
─────────────────────────               ──────────────────────────────
 COPY project files + props              COPY --from=build /app
 dotnet restore (cached layer)           apt-get install curl (health check)
 COPY src/ (source code)                 USER $APP_UID (non-root, UID 1654)
 dotnet publish -c Release               EXPOSE 5000
                                         ENTRYPOINT ["dotnet", "FinWise.McpServer.dll"]
 ✘ SDK discarded                         ✘ No SDK, source, or build artifacts
```

### C.2 Container Networking

| From → To | Hostname | Port | Protocol |
|-----------|----------|------|----------|
| Host → MCP server | `localhost` | 5000 | HTTP |
| MCP server → Redis (local) | `finwise-redis` | 6379 | Redis |
| MCP server → Redis (Azure) | `*.redis.azure.net` | 10000 | Redis (plaintext) |
| MCP server → CosmosDB (local) | `finwise-cosmosdb-emulator` | 8081 | HTTPS (insecure) |
| MCP server → CosmosDB (Azure) | `*.documents.azure.com` | 443 | HTTPS |
| MCP server → Azure OpenAI | External URL | 443 | HTTPS |

---

## Appendix D: Repository Structure

```
├── .github/
│   └── workflows/
│       └── ci.yml                                  # CI pipeline (GitHub Actions)
├── src/
│   ├── FinWise.McpServer/                          # Thin MCP host (composition root)
│   │   ├── Dockerfile                              # Multi-stage Docker build
│   │   ├── Program.cs                              # Startup + DI + /health
│   │   ├── Tools/FinWiseTools.cs                   # 3 MCP tools
│   │   ├── appsettings.json                        # Base config (zero-infra defaults)
│   │   ├── appsettings.Docker.json                 # Container overrides
│   │   └── Infrastructure/                         # Factories + adapters
│   └── FinWise.MultiAgentWorkflow/                 # Core library (zero MCP deps)
│       ├── Agents/{Orchestrator,Profile,Advisor,Stock}Agent/
│       ├── Workflow/FinWiseWorkflowService.cs
│       ├── Session/{Manager,Constants,ResetFlag,RunContext}
│       ├── DomainModel/UserProfile.cs
│       └── Infrastructure/{AgentSessionStores,UserProfileStores}/
├── tests/                                          # 8 test projects + shared base
├── docker-compose.yml                              # Full local stack
├── docker-compose.finwise.yml                      # Server only (Azure-ready)
├── docker-compose.infra.yml                        # Infrastructure only
├── Directory.Build.props                           # Global version (1.0.1)
├── Directory.Packages.props                        # Centralized NuGet versions
└── FinWise.slnx
```

---

## Appendix E: Implementation Learnings

### Azure Managed Redis — SSL Auto-Detection
StackExchange.Redis auto-enables SSL for `*.redis.azure.net` hostnames. Fix: `ssl=False` explicit in connection string.

### Cosmos DB Serverless — No Throughput Parameter
`CreateDatabaseIfNotExistsAsync(throughput: 400)` fails on Serverless. Omitting throughput works for both Serverless and emulator.

### CosmosDB Emulator — LimitToEndpoint
`LimitToEndpoint = true` disables endpoint discovery, preventing routing to unreachable Docker-internal IPs. Only needed for the emulator; Azure Cosmos DB works with standard endpoint discovery.

### LLM Non-Determinism in Tests
Never assert on intermediate LLM wording. Assert on `PROFILE_READY:` markers and final outcomes.

### AsyncLocal for Cross-Boundary Communication
`AsyncLocal` copies references, not objects. The parent and child share the same mutable token, enabling workflow tools to signal state (like reset) back to the calling code.

### SDK Session Deserialization
The MAF RC4 SDK silently broke `InMemoryChatHistoryProvider` deserialization. Fix: serialize messages independently alongside the `AgentSession` blob, under FinWise's control. Lesson: never depend on SDK internal session service mechanisms.

### Pre-Release SDK Consumption
FinWise rode 5 MAF milestones (preview → RC4 → GA). Each brought silent breaking changes — API names change visibly, but API *behavior* changes invisibly. The RC4→GA package rename (`AzureAI` → `Foundry`) would have caused a confusing `dotnet restore` failure without pre-research via NuGet API queries.

---

## Appendix F: Commands Reference

```powershell
# Build
docker build -t finwiseproject/finwise-mcp-server:1.0.0 -f src/FinWise.McpServer/Dockerfile .
dotnet build FinWise.slnx

# Option A: Full local Docker stack
docker compose up -d --build

# Option B: .NET host + Docker infra
docker compose -f docker-compose.infra.yml up -d
dotnet run --project src/FinWise.McpServer/ --urls http://localhost:5000

# Option C: Server container → Azure databases
docker compose -f docker-compose.finwise.yml --env-file .env --env-file .env.azure up -d --build

# Option D: Publish to Docker Hub → Azure Container Apps
docker push finwiseproject/finwise-mcp-server:1.0.0

# Tests
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration" --settings test.docker-local.runsettings
dotnet test tests/FinWise.McpServer.ContainerTests/

# Stop
docker compose down
docker compose down -v   # Remove volumes
```

---

## Appendix G: CI Pipeline (GitHub Actions)

Automated CI via `.github/workflows/ci.yml`, triggered on push/PR to `main` and manual dispatch. Reuses the same Docker Compose stack as local development — no GitHub Actions service containers.

### Dual-Mode Execution

Controlled by `FINWISE_FORCE_IN_MEMORY_DATA` in the `finwise-ci-testing` GitHub Environment:

| Mode | Value | Jobs | Duration |
|------|-------|------|----------|
| **Full** | `false` (default) | Unit + Integration + E2E (real databases) | ~12 min |
| **Fast** | `true` | Unit + E2E only (in-memory stores) | ~7 min |

### Job Graph

```
resolve-mode ──────────┐
                       ├──→ e2e-and-container-tests (always, adapts to mode)
build-and-unit-tests ──┘
                       └──→ integration-tests (full mode only)
```

`resolve-mode` and `build-and-unit-tests` run in parallel. Both must complete before the downstream jobs start. `e2e-and-container-tests` and `integration-tests` also run in parallel (in full mode).

| Job | Infrastructure | Mode |
|-----|---------------|------|
| `resolve-mode` | None (reads env var) | Always |
| `build-and-unit-tests` | None | Always |
| `e2e-and-container-tests` | Full Docker stack (full) or server-only container (fast) | Always (adapts) |
| `integration-tests` | `docker-compose.infra.yml` (CosmosDB + Redis) | Full only |

### Workflow Hardening

- `permissions: contents: read` — least-privilege `GITHUB_TOKEN`
- `concurrency: cancel-in-progress: true` — cancels stale runs
- Job-level timeouts: 2 / 15 / 25 / 20 minutes
- Docker Compose version check ≥ v2.24 (fail-fast)
- Secrets validation gate before expensive Docker operations
- Case-insensitive mode normalization via `tr '[:upper:]' '[:lower:]'`
- `.env` cleanup and Docker teardown in `if: always()` steps
- Container logs dumped on failure for diagnosis
- `.trx` test results uploaded as artifacts with `if-no-files-found: ignore`

### GitHub Environment

A GitHub Environment named `finwise-ci-testing` provides variables and secrets to Jobs 2–4. Only `FINWISE_AZURE_CLIENT_SECRET` is stored as a secret (masked in logs). All other Azure config is stored as environment variables (visible in the GitHub UI for debugging).

> **Design spec**: [Spec 013 — CI GitHub Actions Workflow](013-ci-github-actions-workflow/013-ci-github-actions-workflow.md)

---

## Appendix H: What's NOT in v1.0.0 (Deferred)

| Feature | Target |
|---------|--------|
| Entra ID auth (Redis + CosmosDB) | v1.1 |
| Azure Managed Redis TLS | v1.1 |
| Docker image to ACR | v1.1+ |
| Chiseled/distroless images | v1.2 |
| Multi-architecture builds (ARM64) | v1.2+ |
| Testcontainers | v1.2+ |
| Decoupling agents into separate containers | v2.0+ |

---

**Document End**
