# FinWise: Architecture and Technology Stack (v0.2)

**Document Version:** 2.0  
**Date:** March 1, 2026  
**Status:** Current

## Introduction & Objectives

FinWise is a multi-agent AI investment assistant that provides personalized financial guidance to retail investors. The system exposes its capabilities via Model Context Protocol (MCP), allowing users to interact through any MCP-compatible AI assistant (VS Code with GitHub Copilot, Claude Desktop, etc.).

This document describes the **current v0.2 architecture** and the planned evolution through v0.5. It is standalone and supersedes the v0.1 architecture document.

### Document Purpose and Scope

This architecture document provides technical direction for implementing FinWise's multi-agent system across all planned versions. It addresses:

- **High-level architecture** for each version, showing components, interactions, and system boundaries
- **Technology stack selection** with rationale for each architectural variant
- **Deployment models** from local development (v0.2) through distributed cloud scale (v0.5)
- **Integration patterns** for agents, data sources, and external systems

This document does NOT cover business requirements, functional specifications, or implementation details. Those topics are addressed in companion documents: [01-idea-vision-scope.md](01-idea-vision-scope.md) for business requirements and [001-core-workflow/spec.md](001-core-workflow/spec.md) for detailed functional specifications.

### Architectural Principles

1. **Standards-Based Integration**: Leverage Model Context Protocol (MCP) to prevent vendor lock-in and enable ecosystem interoperability
2. **Incremental Complexity**: Start simple (v0.2 local HTTP) and add complexity (Docker containers, Azure Container Apps) only when earlier versions are functional
3. **Clean Separation of Concerns**: MCP transport and composition are isolated from workflow logic — the multi-agent workflow is a reusable class library with zero MCP dependencies
4. **LLM-Provider Agnosticism**: The workflow library receives `IChatClient` (a pure abstraction). Azure OpenAI is a composition-root decision, not a domain dependency
5. **Cloud-Native Goals**: Containerization patterns that scale directly from Docker Compose to Azure Container Apps and potentially to Kubernetes

**Note on Scope**: This architecture focuses on demonstrating multi-agent functionality in a proof-of-concept (POC) / minimum viable product (MVP) context. Advanced production concerns such as comprehensive security hardening, enterprise observability, event sourcing, and caching strategies are documented in the "Out of Scope" section at the end of this document.

---

## High-Level Architecture

### Architecture Evolution: Five Incremental Versions

FinWise evolves through five architectural variants, each building on its predecessor:

| Version | Deployment | Agents | Storage | Goal |
|---------|-----------|--------|---------|-----------------|
| **v0.1** | Local Windows (monolithic) | 3 agents in-process (.NET 10) |  In-memory + optional CosmosDB| POC (historical) |
| **v0.2** | Local Windows (decoupled projects) | 3 agents in-process (.NET 10) | In-memory + optional CosmosDB (profiles) | **Current state** |
| **v0.3** | Docker Compose | 6 agents in-process (.NET 10) | PostgreSQL (2 DBs) | Extended PoC |
| **v0.4** | Azure Container Apps | 6 agents in-process (.NET 10) | PostgreSQL (2 DBs) + Cosmos DB (RAG/chat/embeddings) | Cloud pilot |
| **v0.5** | Azure Container Apps | 7 distributed agents (.NET 10 + Python) | Same + enhanced Cosmos DB | Cloud pilot |

This staged approach enables learning and validation at each step. Azure Kubernetes Service (AKS) remains a viable future path if scale demands exceed Container Apps limits but is not planned for implementation.

### v0.1 → v0.2: What Changed

v0.1 was a monolithic architecture where a single `Program.cs` (~520 lines) mixed MCP transport, multi-agent workflow orchestration, and session/conversation management. While functional as a proof-of-concept, this coupling meant the workflow couldn't be tested without the MCP server, couldn't be reused by a different host, and was difficult to navigate.

v0.2 decoupled the system into two projects via a structural refactoring (see [003-decoupling-refactoring-tech-specs-plan](003-decoupling-refactoring-tech-specs-plan/003-decoupling-refactoring-tech-specs-plan-v2.md) for the detailed plan):

| Concern | v0.1 | v0.2 |
|---------|------|------|
| Project structure | Single project (~520 line Program.cs) | Two projects: MCP server host + class library |
| MCP tools | 3 closure-based tools in Program.cs | 2 attribute-based tools in `Tools/FinWiseTools.cs` |
| Agent creation | Inline in Program.cs | Factory pattern (folder-per-agent with `.prompt.md` files) |
| Workflow logic | Mixed into MCP tool closures | Dedicated `FinWiseWorkflowService` class |
| Session mapping | Loose dict + local functions | `McpSessionMapping` helper class |
| MCP dependency | Everywhere | Isolated to McpServer project only |
| LLM provider | Coupled (Azure OpenAI throughout) | Agnostic (`IChatClient` abstraction in workflow lib) |
| Testability | Required MCP server for all tests | Unit tests for workflow, integration tests for MCP |

---

### v0.2: Decoupled Two-Project Architecture (Current)

**Deployment Context**: Developer workstation (Windows), accessed through any MCP-compatible AI assistant via Streamable HTTP.

#### Architecture Diagram

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
│ │   - Azure OpenAI client creation                     │ │
│ │   - Store creation (InMemory / CosmosDB)             │ │
│ │   - DI registration + MCP tool auto-discovery        │ │
│ ├──────────────────────────────────────────────────────┤ │
│ │ Tools/FinWiseTools.cs (2 attribute-based MCP tools)  │ │
│ │   🔧 run_finwise_workflow (query → advice/profile)   │ │
│ │   🔧 reset_conversation  (clear session)             │ │
│ ├──────────────────────────────────────────────────────┤ │
│ │ McpSessionMapping.cs                                 │ │
│ │   MCP-Session-Id → agentSessionId mapping            │ │
│ ├──────────────────────────────────────────────────────┤ │
│ │ Infrastructure.cs                                    │ │
│ │   Azure OpenAI factory, Serilog setup                │ │
│ └──────────────────────────────────────────────────────┘ │
│                        │ IChatClient + IUserProfileStore  │
│                        │ + IAgentSessionStore (injected)  │
└────────────────────────┼─────────────────────────────────┘
                         ▼
┌──────────────────────────────────────────────────────────┐
│ FinWise.MultiAgentWorkflow (Class Library — No MCP)      │
│ ┌──────────────────────────────────────────────────────┐ │
│ │ Workflow/                                            │ │
│ │   FinWiseWorkflowService (core orchestration)        │ │
│ │   WorkflowResponse (result DTO)                      │ │
│ ├──────────────────────────────────────────────────────┤ │
│ │ Agents/ (folder-per-agent with .prompt.md files)     │ │
│ │   🤖 OrchestratorAgent  (silent router, tool calls)  │ │
│ │   🤖 UserProfileAgent   (get/set/delete profile)     │ │
│ │   🤖 AdvisorAgent        (investment advice)          │ │
│ │   Handoff: hub-and-spoke via OrchestratorAgent       │ │
│ ├──────────────────────────────────────────────────────┤ │
│ │ Session/                                             │ │
│ │   AgentSessionManager, AgentSessionResetEvaluator,   │ │
│ │   AgentSessionRunContext, AgentSessionConstants       │ │
│ ├──────────────────────────────────────────────────────┤ │
│ │ Infrastructure/                                      │ │
│ │   IAgentSessionStore → InMemoryAgentSessionStore     │ │
│ │   IUserProfileStore → InMemoryUserProfileStore       │ │
│ │                     → CosmosDbUserProfileStore        │ │
│ ├──────────────────────────────────────────────────────┤ │
│ │ DomainModel/                                         │ │
│ │   UserProfile (immutable record)                     │ │
│ └──────────────────────────────────────────────────────┘ │
└──────────────────────────┬───────────────────────────────┘
                           │
              ┌────────────┴────────────┐
              ▼                         ▼
┌──────────────────────┐  ┌──────────────────────────────┐
│ Azure OpenAI Service │  │ Azure Cosmos DB (Optional)   │
│   (LLM inference)    │  │   (User profile persistence) │
└──────────────────────┘  └──────────────────────────────┘
```

#### Agent Workflow & Session Lifecycle (Zoom)

```
                         ┌──────────────────────────────────────────┐
                         │         FinWiseWorkflowService           │
                         │  ProcessMessageAsync(agentSessionId, q)  │
                         └────────────┬─────────────────────────────┘
                                      │
                    ┌─────────────────┼──────────────────────┐
                    ▼                 ▼                      ▼
          ┌─────────────────┐ ┌──────────────┐  ┌───────────────────┐
          │ AgentSession    │ │ AgentSession  │  │ AgentSessionRun   │
          │ ResetEvaluator  │ │ Manager       │  │ Context           │
          │                 │ │               │  │                   │
          │ "start new      │ │ Restore or    │  │ AsyncLocal scope  │
          │  session" →     │ │ create session│  │ for in-flight     │
          │  reset if       │ │ (serialize /  │  │ session data      │
          │  PROFILE_READY  │ │  deserialize) │  │                   │
          │  exists         │ │       │       │  └───────────────────┘
          └─────────────────┘ │       ▼       │
                              │  IAgentSession│
                              │  Store        │
                              └───────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────┐
│                    Hub-and-Spoke Agent Workflow                      │
│                                                                     │
│                    ┌──────────────────────┐                          │
│          ┌────────▶│  🤖 OrchestratorAgent │◀────────┐               │
│          │         │   (Silent Router)     │         │               │
│          │         │   Tool calls only     │         │               │
│          │         └───┬──────────────┬────┘         │               │
│          │             │              │              │               │
│          │   handoff_to│    handoff_to│              │               │
│          │   _profile  │    _advisor  │              │               │
│          │             ▼              ▼              │               │
│          │  ┌─────────────────┐  ┌─────────────────┐│               │
│          │  │🤖 UserProfile   │  │🤖 AdvisorAgent  ││               │
│          │  │   Agent         │  │                 ││               │
│  handoff │  │                 │  │ Reads profile   ││  handoff      │
│  _to_    │  │ Tools:          │  │ from PROFILE_   ││  _to_         │
│  _orch   │  │  get_profile()  │  │ READY marker    ││  _orch        │
│          │  │  set_profile()  │  │ in history      ││               │
│          │  │  delete_profile()  │                 ││               │
│          │  │                 │  │ Personalized    ││               │
│          │  │ Emits           │  │ investment      ││               │
│          │  │ PROFILE_READY:  │  │ advice          ││               │
│          │  │ when complete   │  │                 ││               │
│          │  │       │         │  └────────┬────────┘│               │
│          └──┤       │         │           └─────────┘               │
│             └───┬───┘─────────┘                                     │
│                 │                                                    │
│                 ▼                                                    │
│        IUserProfileStore                                            │
│                                                                     │
├─────────────────────────────────────────────────────────────────────┤
│  Routing Rules:                                                     │
│    • No PROFILE_READY in history → ALWAYS route to ProfileAgent     │
│    • PROFILE_READY exists + profile intent → route to ProfileAgent  │
│    • PROFILE_READY exists + advice intent  → route to AdvisorAgent  │
│    • AdvisorAgent: if no PROFILE_READY → handoff back to Orch      │
└─────────────────────────────────────────────────────────────────────┘
```

#### Components

**FinWise.McpServer** (ASP.NET Core MCP Server Host):
- **Composition root** (`Program.cs`): Creates Azure OpenAI client, stores, and `FinWiseWorkflowService`. Registers services for attribute-based tool injection. ~125 lines.
- **MCP tools** (`Tools/FinWiseTools.cs`): Two attribute-based tools auto-discovered via `WithToolsFromAssembly()`. Tools are thin adapters that resolve services from `IServiceProvider`, call the workflow service, and return responses.
  - `run_finwise_workflow` — Single entry point for all user messages. The internal multi-agent workflow handles routing: profile collection, profile management, and financial advice.
  - `reset_conversation` — Explicit session reset (clears history, keeps profiles). Provides a deterministic reset for MCP clients.
- **Session mapping** (`McpSessionMapping.cs`): Thread-safe `ConcurrentDictionary` mapping `MCP-Session-Id` HTTP headers to agent session IDs. This is an MCP transport concern — only plain `agentSessionId` strings cross the project boundary.
- **Infrastructure** (`Infrastructure.cs`): Azure OpenAI client factory (reads environment variables), Serilog configuration, error logging helper.

**FinWise.MultiAgentWorkflow** (Class Library — Zero MCP Dependencies):
- **Workflow** (`FinWiseWorkflowService`): Core orchestration class. Public API: `ProcessMessageAsync(agentSessionId, query)` and `ResetSessionAsync(agentSessionId)`. Internally creates agents, builds handoff workflow, manages session lifecycle, and returns `WorkflowResponse`.
- **Agents** (folder-per-agent layout with externalized `.prompt.md` files):
  - **OrchestratorAgent** — Silent router. Only emits tool calls (`handoff_to_profile_agent`, `handoff_to_advisor_agent`), never text. Routes to ProfileAgent when no `PROFILE_READY` marker exists; routes based on intent after profile completion.
  - **UserProfileAgent** — Collects user profile (email, risk tolerance, investment goals, timeframe) through conversation. Has three tools: `GetProfile`, `SetProfile`, `DeleteProfile`. Saves incrementally after each field. Emits `PROFILE_READY:` marker when profile is complete.
  - **AdvisorAgent** — Provides personalized investment recommendations based on the user's profile data from the `PROFILE_READY` marker in conversation history. No tools — reads context from history.
- **Session** — Conversation state management:
  - `AgentSessionManager`: Serialize/deserialize Microsoft Agent Framework `AgentSession` objects. Supports session timeout logic (configurable per profile state).
  - `AgentSessionResetEvaluator`: Detects natural-language reset phrases (e.g., "start new session", "switch user"). Only triggers when `PROFILE_READY` marker exists in history.
  - `AgentSessionRunContext`: `AsyncLocal<T>` ambient context for in-flight session data.
  - `AgentSessionConstants`: Shared constants (`PROFILE_READY:` marker, response prefixes) and `ExtractUserIdFromMessageHistory()` utility.
- **Infrastructure** — Storage abstractions with pluggable implementations:
  - `IAgentSessionStore` → `InMemoryAgentSessionStore` (development). Future: Redis.
  - `IUserProfileStore` → `InMemoryUserProfileStore` (development) or `CosmosDbUserProfileStore` (persistent profiles).
- **DomainModel** — `UserProfile` immutable record with `IsComplete` property and `WithUpdates()` for incremental field updates. All fields are free-form text — no validation or enum constraints.

**Key Design Patterns**:
- **Hub-and-spoke handoffs** — All agents route through the Orchestrator. No direct agent-to-agent communication.
- **Stateless agents** — `AIAgent` instances hold no state. All conversation state lives in Microsoft Agent Framework `AgentSession`, serialized/deserialized between requests.
- **`PROFILE_READY:` marker** — Signal from ProfileAgent that the user's profile is complete. Gates access to the AdvisorAgent. Contains email, risk, goals, and timeframe.
- **Manual DI** — No DI container for agents. `Program.cs` is the explicit composition root.
- **Embedded prompts** — Agent system prompts stored as `.prompt.md` files (embedded resources), enabling clean git diffs separate from C# logic.

#### Key Characteristics

- **Transport**: MCP Streamable HTTP (HTTP + SSE, `MCP-Session-Id` header)
- **User Interface**: Any MCP-compatible client (VS Code, Claude Desktop, etc.)
- **Persistence**: In-memory by default; optional Azure Cosmos DB for user profiles
- **Deployment**: Single .NET process (FinWise.McpServer), no containers required
- **Scalability**: Single user (developer/tester/demo), proof-of-concept scale
- **Testing**: Unit tests for workflow library (no MCP server needed), integration tests for MCP protocol

#### Technology Choices: Rationale

**Why two projects instead of one?**
- The workflow library can be tested without MCP infrastructure
- The workflow can be reused by different hosts (REST API, WebJob, CLI) without modification
- Package dependencies are cleanly separated: MCP packages in the host, Agent Framework in the library
- The class library is LLM-provider-agnostic — swap Azure OpenAI for Ollama by changing only `Program.cs`

**Why Streamable HTTP instead of STDIO?**
- Multi-client support from day one (multiple AI assistants can connect)
- Session identification via `MCP-Session-Id` header (STDIO has no session concept)
- Prepares for cloud deployment (same transport in v0.3+)
- Supports SSE for streaming responses

**Why in-memory stores for v0.2?**
- Simplest possible storage for POC validation
- Zero infrastructure setup (no database containers)
- Optional CosmosDB for profile persistence when needed
- Storage abstractions (`IAgentSessionStore`, `IUserProfileStore`) enable plugging in any provider later

**Why attribute-based MCP tools?**
- Auto-discovered via `WithToolsFromAssembly()` — no manual registration in `Program.cs`
- Tools are regular static methods, easy to read and test
- Service injection via `IServiceProvider` — clean dependency resolution

---

### v0.3: Dockerized Multi-Agent System

**Deployment Context**: Local development machine (Docker Linux containers), accessed via Streamable HTTP for multi-client testing.

#### Architecture Highlights

v0.3 introduces **containerization**, **persistent storage**, and **three additional agents**. The two-project architecture from v0.2 is preserved — all agents remain in-process within the orchestrator container.

**Key Changes from v0.2**:
- All components run in Docker Compose (5 containers: orchestrator + 2 internal MCP servers + 2 PostgreSQL databases)
- Profile storage moves from in-memory/CosmosDB to PostgreSQL via a dedicated **User Profile MCP Server** (separate container)
- New **Investment Strategy MCP Server** container for strategy summaries
- Three new agents added in-process: **Stock Fundamentals Agent**, **Real Estate Agent**, **Investment Strategy Summarization Agent**

**Components**:

- **FinWise Orchestrator Container** (.NET): Contains all 6 agents running in-process. Exposes MCP Streamable HTTP on port 8080. Acts as MCP client to internal MCP servers and external 3rd-party MCP servers.
- **User Profile MCP Server** (.NET, separate container): Manages user profiles via PostgreSQL. Exposes CRUD operations as MCP tools.
- **Investment Strategy MCP Server** (.NET, separate container): Manages investment strategy summaries and recommendations via PostgreSQL.
- **PostgreSQL DB #1**: User profiles (risk tolerance, goals, timeframes)
- **PostgreSQL DB #2**: Investment strategy summaries and recommendations
- **External MCP Servers** (3rd party, TBD): Stock market data, real estate data, financial news — consumed by agents via MCP client connections

**Key Characteristics**:
- **Transport**: MCP Streamable HTTP (same as v0.2)
- **Persistence**: 2 PostgreSQL databases (replaces in-memory stores)
- **Deployment**: Docker Compose (5 containers)
- **Scalability**: Multiple local users, small team testing

---

### v0.4: Azure Container Apps Cloud Deployment

**Deployment Context**: Azure cloud (serverless containers), accessible over public internet with authentication.

#### Architecture Highlights

v0.4 uses the **same agent architecture as v0.3** (all agents in-process). The primary change is deployment target: local Docker Compose → Azure Container Apps.

**Key Changes from v0.3**:
- Azure Container Apps Environment with serverless scaling (0–10 replicas)
- Built-in HTTPS ingress with automatic TLS certificate management
- **Azure Cosmos DB for NoSQL** added for RAG documents, conversation history, session state, and vector search
- Azure Database for PostgreSQL - Flexible Server replaces local PostgreSQL
- Internal VNET for container-to-container communication
- New **User Context MCP Server** container for conversation history and RAG document retrieval via Cosmos DB

**Components**:
- **FinWise Orchestrator Container App**: Public MCP endpoint, managed identity, auto-scaling
- **Internal MCP Servers**: User Profile, Investment Strategy, User Context (NEW — Cosmos DB)
- **Azure Database for PostgreSQL**: 2 databases (profiles + strategies)
- **Azure Cosmos DB for NoSQL**: RAG documents, conversation history, session state, vector embeddings

**Key Characteristics**:
- **Transport**: MCP Streamable HTTP (public HTTPS endpoint)
- **Authentication**: Basic API keys for POC
- **Persistence**: Azure PostgreSQL (2 DBs) + Azure Cosmos DB
- **Deployment**: Azure Container Apps (6–8 apps in single environment)
- **Scalability**: 100s of concurrent users, automatic scaling
- **Cost**: Pay-per-use (scale-to-zero), ~$200–500/month for pilot

---

### v0.5: Distributed Agent Architecture with A2A Protocol

**Deployment Context**: Production Azure environment with distributed agents and A2A protocol.

#### Architecture Highlights

v0.5 introduces a **fundamental architectural shift** from in-process agents (v0.2–v0.4) to **distributed agents** running in separate containers communicating via **A2A (Agent-to-Agent) protocol** (JSON-RPC over HTTP).

**Key Changes from v0.4**:
- Agents move from in-process to separate containers with independent scaling
- A2A protocol for agent-to-agent communication (language-agnostic)
- New **Risk Management Agent** (Python + pandas/scipy) — demonstrates polyglot architecture
- **Compliance Checker Service** (.NET) for regulatory validation

**Key Characteristics**:
- **Distributed Architecture**: Each agent runs in its own container
- **A2A Protocol**: JSON-RPC over HTTP for agent-to-agent communication
- **Polyglot**: Python Risk Management agent integrates with .NET orchestrator via A2A
- **Independent Scaling**: Each agent container scales based on workload
- **Scalability**: 1000s of concurrent users

**AKS Path**: Azure Kubernetes Service remains a viable migration path if scale demands exceed Container Apps limits (5,000+ nodes, GPU workloads, multi-tenant namespace isolation), but is not planned for implementation.

---

## Technologies, Stack & Tools

### Core Technology Stack Summary

| Version | Languages | Deployment | Agents | MCP | Storage |
|---------|-----------|------------|--------|-----|---------|
| **v0.1** | .NET 10 (C#) | Local process (monolithic) | 3 agents in-process | Streamable HTTP | In-memory |
| **v0.2** | .NET 10 (C#) | Local process (decoupled) | 3 agents in-process | Streamable HTTP | In-memory + optional CosmosDB |
| **v0.3** | .NET 10 (C#) | Docker Compose | 6 agents in-process | Streamable HTTP | PostgreSQL (2 DBs) |
| **v0.4** | .NET 10 (C#) | Azure Container Apps | 6 agents in-process | Streamable HTTP | Azure PostgreSQL + Cosmos DB |
| **v0.5** | .NET 10 + Python | Azure Container Apps | 7 distributed agents (A2A) | Streamable HTTP | Same + enhanced Cosmos DB |

**Note**: All versions use Azure OpenAI Service for LLM inference.

### Key Technology Justification

#### Microsoft Agent Framework

Unified platform combining Semantic Kernel's enterprise features with AutoGen's multi-agent orchestration patterns. Chosen for native .NET integration, multi-agent orchestration patterns (sequential, concurrent, handoff), MCP support via ModelContextProtocol SDK, and production maturity.

**Alternatives Considered**: LangChain (weaker .NET support), CrewAI (Python-only), custom framework (high risk reinventing orchestration patterns).

#### Model Context Protocol (MCP)

Open standard for connecting AI agents to external tools and data sources. FinWise operates AS an MCP server (user interface) AND will use MCP clients (data integration in v0.3+). Chosen for ecosystem breadth (16,000+ MCP servers), developer productivity (2–3x faster than custom REST APIs), standardization (language-agnostic), and transport flexibility (Streamable HTTP).

**Alternatives Considered**: REST APIs (no standard tool discovery), GraphQL (limited action semantics), gRPC (less web-friendly).

#### Azure OpenAI Service

Managed API access to OpenAI models with enterprise SLAs and Azure compliance. Chosen for reasoning model access (GPT-4, o1, o3), enterprise features (private network, managed identity, RBAC), Azure integration (same identity model), and 99.9% uptime SLA.

The workflow library is LLM-provider-agnostic — Azure OpenAI is a deployment decision made in `Program.cs`, not a domain dependency.

#### PostgreSQL (v0.3+)

Open-source relational database with pgvector extension for vector similarity search. Chosen for hybrid relational + vector storage, operational familiarity, Azure managed service support, and lightweight local containers.

#### Azure Cosmos DB for NoSQL (v0.4+)

Globally distributed NoSQL database with native vector search. Added for RAG document store, conversation history, and session state — workloads that benefit from flexible schema, high-throughput writes, and vector search at scale. User profiles may optionally use CosmosDB from v0.2 for persistent storage.

#### Container Deployment (v0.3+)

- **Docker Compose** (v0.3): Consistent dev environments, direct path to cloud
- **Azure Container Apps** (v0.4–v0.5): Serverless scaling, scale-to-zero, built-in HTTPS ingress, KEDA autoscaling
- **AKS migration path**: Available when scale exceeds 1,000+ concurrent users, GPU workloads required, or multi-tenant isolation mandated

---

## Deployment Architecture Differentiators

### v0.2 vs v0.3: Local Process → Docker Containers

**Benefits**: Environment consistency ("works on my machine" elimination), polyglot enablement for future Python agents, cloud-ready container images.

**Trade-offs**: Docker Compose adds orchestration overhead, higher memory usage, distributed debugging complexity.

**Decision Point**: Transition to v0.3 when adding Stock/Real Estate agents or when persistent storage is required.

### v0.3 vs v0.4: Docker Compose → Azure Container Apps

**Benefits**: Global access (public HTTPS), managed databases, auto-scaling, network isolation.

**Trade-offs**: ~$200–500/month Azure costs, network latency to cloud databases, harder debugging.

**Decision Point**: Transition when pilot users require access outside local network.

### v0.4/v0.5 vs AKS: Container Apps → Kubernetes

**Benefits**: Unlimited scale (5K+ nodes), GPU support, service mesh, multi-tenancy.

**Trade-offs**: Significant operational complexity, minimum node costs, requires Kubernetes expertise.

**Decision Point**: Concurrent users exceed 1,000, GPU workloads required, or multi-tenant isolation mandated.

---

## Alternative Integration Patterns

While MCP is the primary integration layer, certain scenarios benefit from alternatives:

- **REST APIs**: For external services without MCP support. Strategy: wrap REST endpoints in lightweight MCP servers.
- **GraphQL**: For flexible UI data fetching over large data graphs. Keep separate from agent tool invocation (GraphQL for reads, MCP for agent actions).

---

## Out of Scope (Advanced Production Features)

The following topics represent advanced production capabilities not included in the POC/MVP scope:

| Topic | Rationale for Deferral |
|-------|----------------------|
| **Advanced Observability** (OpenTelemetry, Prometheus, Grafana) | Console/file logging sufficient for POC; observability can be added incrementally |
| **Security & Authentication** (OAuth 2.1, Azure AD B2C, managed identity) | Basic API keys sufficient for POC; security hardening before production |
| **Caching** (Azure Cache for Redis, semantic caching) | Functional correctness over cost optimization; caching adds invalidation complexity |
| **Event Sourcing** (Azure Service Bus, CQRS) | Architectural pattern for compliance, not POC functionality |
| **Service Mesh** (Istio) | Production concern for multi-service security and traffic control |
| **Compliance & Governance** (audit trails, GDPR, multi-tenancy) | POC demonstrates technical feasibility, not regulatory compliance |

---

### Key Success Factors

1. **Start Simple**: v0.2 validates core agent orchestration without cloud costs or infrastructure complexity.
2. **Clean Boundaries**: The two-project architecture ensures the workflow library can evolve independently of the MCP host.
3. **Embrace Standards**: MCP prevents lock-in and enables ecosystem integration.
4. **Incremental Investment**: Each version adds features only when business value justifies operational complexity.
5. **Test Production Patterns Locally**: v0.3 Docker Compose mirrors v0.4 Container Apps architecture (smooth transition).

---

## AgentSession Definition & Naming Conventions

### What Is an AgentSession?

> An AgentSession is a stateful container that holds the complete message history and context for one interaction thread between a user and the multi-agent workflow. All agents (orchestrator, profile, advisor) share the same AgentSession — it's scoped to the user's interaction, not to an individual agent. Identified by `agentSessionId` (GUID string — called `conversationId` in the SDK's `AgentSessionStore`), persisted between requests via `IAgentSessionStore`, and reset when the user explicitly requests it or starts a new MCP session.

All agents in the handoff workflow share a single AgentSession per user interaction. The session is created from the orchestrator agent but stores messages from all agents.

### Naming Convention

> **No bare "Session" in type names.** Always prefix:
> - `AgentSession*` — for types in the workflow/Agent Framework layer (e.g., `AgentSessionManager`, `AgentSessionResetEvaluator`, `IAgentSessionStore`)
> - `McpSession*` — for types in the MCP transport layer (e.g., `McpSessionMapping`)

This convention disambiguates between MCP transport sessions (`MCP-Session-Id` header) and Agent Framework sessions (stateful conversation containers). Both protocols use "session" to mean different things — the prefix eliminates confusion.

### `agentSessionId` vs SDK's `conversationId`

The SDK's `AgentSessionStore` (in `Microsoft.Agents.AI.Hosting`) uses `conversationId` as the storage key. We use `agentSessionId` for the same concept — a unique identifier for one interaction thread. Our name avoids confusion with MCP session IDs and reflects the FinWise-managed lifecycle (create → use → reset → new ID). See [004-session-conversation-refactoring-analysis](004-session-conversation-refactoring/004-session-conversation-refactoring-analysis.md) for the full rationale.

---

**Document End**
