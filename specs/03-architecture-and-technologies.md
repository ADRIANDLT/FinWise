# FinWise: Architecture and Technology Stack

**Document Version:** 1.0  
**Date:** December 29, 2025  
**Status:** Draft

## Introduction & Objectives

This document defines the architectural approach and technology stack for FinWise, a multi-agent AI investment assistant system designed to provide personalized financial guidance to everyday retail investors. The architecture evolves through five incremental versions (v0.1 through v0.5), each adding capabilities while maintaining backward compatibility and leveraging proven patterns from production multi-agent systems deployed in 2024-2025.

### Document Purpose and Scope

This architecture document provides technical direction for implementing FinWise's multi-agent system across all planned versions. It addresses:

- **High-level architecture** for each version, showing components, interactions, and system boundaries
- **Technology stack selection** with rationale for each architectural variant
- **Deployment models** from local development (v0.1) through enterprise cloud scale (v0.5)
- **Integration patterns** for agents, data sources, and external systems

This document does NOT cover business requirements, functional specifications, or implementation details (code patterns, detailed API specifications, or database schemas). Those topics are addressed in companion documents: [01-idea-vision-scope.md](01-idea-vision-scope.md) for business requirements and [001-core-workflow/spec.md](001-core-workflow/spec.md) for detailed functional specifications.

### Architectural Principles

The FinWise architecture adheres to the following principles derived from successful production multi-agent systems:

1. **Standards-Based Integration**: Leverage Model Context Protocol (MCP) to prevent vendor lock-in and enable ecosystem interoperability
2. **Incremental Complexity**: Start simple (v0.1 local STDIO) and add complexity (MCP-HTTP-Streaming and Docker containers + Azure Container Apps as deployment targets) only when the initial POC versions are functional.
3. **Dual Role MCP Architecture**: FinWise operates AS an MCP server (exposing capabilities to end users through AI assistants) AND uses MCP clients (consuming external financial data and internal data stores to provide context to the agents)
4. **Cloud-Native goals**: Except in the first v0.1 with local deployment, as soon as possible we want to use containerization patterns that scale directly to the cloud on Azure Container Apps and potentially to Kubernetes in Azure (AKS).

**Note on Scope**: This architecture focuses on demonstrating multi-agent functionality in a proof-of-concept (POC) / minimum viable product (MVP) context. Advanced production concerns such as comprehensive security hardening, enterprise observability, event sourcing, and caching strategies are documented in the "Out of Scope" section at the end of this document for future consideration.

---

## High-Level Architecture

### Architecture Evolution: Five Incremental Versions

FinWise evolves through five architectural variants, each building on its predecessor:

| Version | Deployment | Agents | Databases | Target Audience |
|---------|-----------|--------|-----------|-----------------|
| **v0.1** | Local Windows | 3 agents in-process (.NET 10) | PostgreSQL (local) | Proof-of-Concept |
| **v0.2** | Docker Compose | 6 agents in-process (.NET 10) | PostgreSQL (2 DBs) - profiles + strategy summaries | Extended PoC |
| **v0.3** | Azure Container Apps | 6 agents in-process (.NET 10) | PostgreSQL (2 DBs) + Cosmos DB (RAG/chat/embeddings) | Cloud pilot |
| **v0.4** | Azure Container Apps | 7 distributed agents (.NET 10 + Python) | Same + enhanced Cosmos DB | Cloud pilot |
| **v0.5** | Azure Kubernetes Service (out of scope) | Full distributed agents (.NET 10 + Python) | PostgreSQL Hyperscale + Cosmos DB (multi-region) | Enterprise scale |

This staged approach enables learning and validation at each step. 

### v0.1: Local Development with MCP STDIO

**Deployment Context**: Developer workstation (Windows), accessed through Claude Desktop or similar MCP-compatible AI assistant.

#### Architecture Diagram

```
┌─────────────────────────────────────────────────────┐
│ User's AI Assistant (Claude, ChatGPT, GHCP, etc.)   │
│   - MCP Client built-in                             │
└──────────────────┬──────────────────────────────────┘
                   │ MCP STDIO
                   │ (stdin/stdout)
                   ▼
┌─────────────────────────────────────────────────────┐
│ FinWise MCP Server (Process)                        │
│ ┌─────────────────────────────────────────────────┐ │
│ │ Workflow with three agents:                     │ │
│ │   - Routes user queries                         │ │
│ │   - Manages agent handoffs                      │ │
│ │   - Retrieves context and passes to agents      │ │
│ │   - 1. Orchestrator Agent                       │ │
│ └──────────┬────────────────┬─────────────────────┘ │
│            │                │                       │
│ ┌──────────▼──────────┐  ┌─▼──────────────────────┐ │
│ │ 2.User Profile Agent│  │ 3.Global Advisor Agent │ │
│ │ (Simple - .NET)     │  │ (Hollow - .NET)        │ │
│ └──────────┬──────────┘  └────────────────────────┘ │
│            │                                        │
│            │ MCP Client                             │
│            │                                        │
│            ▼                                        │
│            │ MCP STDIO                              │
│            ▼                                        │
└─────────────────────────────────────────────────────┘
                        
┌─────────────────────────────────────────────────────┐
│ User Profile MCP Server (.NET Process)              │
│ - Separate process, STDIO transport                 │
│ - Exposes DB operations as tools                    │
│   (get/save user profiles only)                     │
└─────────────────────┬───────────────────────────────┘
                      │
                      │ Entity Framework Core
                      ▼
┌────────────────────────────────────────────────────┐
│ PostgreSQL (Local Container)                       │
│   - User profiles (risk tolerance, goals,          │
│     investment timeframes, questionnaire data)     │
└────────────────────────────────────────────────────┘
```

#### Components

**FinWise multi-agent workflow in a MCP Server** (C#/.NET 9):
- Exposes FinWise AS an MCP server using Microsoft ModelContextProtocol SDK
- Implements `McpServerTool` attributes on agent operations:
  - `get_investment_recommendations` → routes to Global Advisor Agent (retrieves user profile internally)
  - `update_user_profile` → routes to User Profile Agent
- Runs as a local process started by Claude Desktop via stdio transport
- Zero custom UI required—users access through existing AI assistant

**Agents** (Microsoft Agent Framework):
- **Orchestrator Agent**: 
  - Implements sequential and handoff orchestration patterns from Agent Framework
  - Routes queries using LLM-driven intent classification
  - **Retrieves user profile** via User Profile MCP Server or User Profile Agent
  - **Passes context to specialized agents** during handoffs (e.g., provides user profile when handing off to Global Advisor)
- **User Profile Agent** (Hollow): 
  - Collects basic user information (age, investment goals, time horizon) through conversation
  - Stores profiles via User Profile MCP Server
  - Acts as MCP client to the User Profile MCP Server
- **Global Advisor Agent** (Hollow): 
  - Provides generic stock vs. real estate guidance
  - **Receives user profile as context** from orchestrator during handoff (doesn't fetch data itself)
  - Focuses purely on investment strategy based on provided context

**User Profile MCP Server** (.NET - Separate Process):
- Separate .NET process communicating with main FinWise process via STDIO
- Exposes user profile operations as MCP tools: `get_user_profile`, `save_user_profile`, `update_user_profile`
- **Scope**: ONLY handles user profile data - risk tolerance, investment goals, timeframes, questionnaire responses
- **Accessed by**: User Profile Agent (for data storage/retrieval) and Orchestrator (for context retrieval when handing off to other agents)
- **NOT accessed by**: Global Advisor Agent (receives data as context during handoff, doesn't fetch directly)
- Uses Entity Framework Core internally to access PostgreSQL
- **Note**: Conversation history and investment recommendations will be added in v0.2+ via separate MCP servers/databases (likely NoSQL)
- **Benefits**: 
  - Decouples agents from database schema
  - Consistent MCP pattern for all integrations (same pattern as external MCP servers)
  - **Clear separation of concerns**: data management (User Profile Agent) vs. business logic (Global Advisor Agent)
  - Easier testing (specialized agents work with provided context, not database queries)
  - Process isolation improves fault tolerance

**Data Layer**:
- **PostgreSQL** (DB in a Docker container): Stores ONLY user profiles (risk tolerance, investment goals, time horizons, questionnaire responses, user demographics)
- **Schema**: To be determined in feature's specs doc.
- **Note**: Conversation history and strategy summaries will use separate databases in later v0.2+ versions (likely NoSQL for flexibility)
- **Data Access Pattern**: Database MCP Server abstracts database access, agents use MCP tools instead of direct SQL/ORM queries

**Development Tools**:
- VS Code with C# Dev Kit
- Docker Desktop for Windows (Only for PostgreSQL database container)
- Azure OpenAI Service (cloud API calls for LLM inference)
- Process management: FinWise process communicated with the User Profile MCP Server via STDIO

#### Key Characteristics

- **Transport**: MCP STDIO (stdin/stdout) between the user's MCP client (i.e. Claude or GHCP) and FinWise process, and between FinWise and User Profile MCP Server
- **User Interface**: Claude Desktop (or ChatGPT, GitHub Copilot—any MCP client)
- **Persistence**: Local PostgreSQL container
- **Deployment**: Two .NET processes (FinWise + User Profile MCP Server) + PostgreSQL container via Docker Desktop
- **Scalability**: Single user (developer/tester/demo), proof-of-concept scale

#### Technology Choices: Rationale

**Why .NET/C# and Microsoft Agent Framework?**
- Unified platform combining the former Semantic Kernel's enterprise features (telemetry, type safety, Azure integration) with AutoGen's multi-agent orchestration patterns
- Native MCP support through official ModelContextProtocol NuGet package
- Strong Azure ecosystem integration for future cloud deployment
- Excellent tooling in VSCode and Visual Studio for development/debugging

**Why MCP STDIO for v0.1?**
- Simplest transport mechanism (no network configuration)
- Native support in Claude Desktop, ChatGPT, and other AI assistants
- Enables immediate user testing without building custom UI
- Proven pattern: Block, Bloomberg, and many others use MCP servers

**Why PostgreSQL over SQL Server or Cosmos DB?**
- Open source, runs light as Linux containers in Docker desktop for Windows
- Sufficient for v0.1 scale (limited data)
- Lightweight compared to SQL Server Developer Edition or other databases

**Why Azure OpenAI Service (cloud) for v0.1 local deployment?**
- Eliminates need to run local LLM models (heavy GPU requirements)
- Access to latest reasoning models (GPT-4, o1) for quality agent responses
- Per-token pricing manageable for development/testing
- Same API used in production, simplifying transition

**Why separate User Profile MCP Server process in v0.1?**
- **Agent-native pattern**: LLM-powered agents naturally work with tools ("get user profile") rather than coupled code (SQL/LINQ queries) in the agents
- **Consistent integration**: Same MCP client pattern for database (internal) and market data (external) - agents don't need to distinguish
- **Process isolation**: MCP architecture principle - servers run in separate processes, even locally with STDIO
- **Decoupling**: Change database schema without modifying agent prompts or logic
- **Natural language interface**: Agents request data semantically ("Get investment goals for this user") instead of programmatically
- **Demonstrates core MCP value**: Shows MCP as universal integration layer from day 1, not just for external APIs

---

### v0.2: Dockerized Multi-Agent System with Streamable HTTP Transport

**Deployment Context**: Local development machine (Docker Linux containers), accessed via Streamable HTTP for multi-client testing.

#### Architecture Diagram

```
┌───────────────────────────────────────────────────────────────┐
│ User's AI Assistants (Multiple Clients)                       │
│   - Claude Desktop, ChatGPT, Custom UI                        │
└────────────────┬──────────────────────────────────────────────┘
                 │ MCP Streamable HTTP
                 │ (localhost:8080)
                 ▼
┌─────────────────────────────────────────────────────────────────┐
│ Docker Compose Environment (Internal FinWise Infrastructure)    │
│ ┌─────────────────────────────────────────────────────────────┐ │
│ │ FinWise Orchestrator Container (.NET)                       │ │
│ │   - Exposes MCP server via HTTP/SSE (port 8080)             │ │
│ │   - ALL AGENTS IN-PROCESS:                                  │ │
│ │     • Orchestrator Agent                                    │ │
│ │     • User Profile Agent                                    │ │
│ │     • Global Advisor Agent                                  │ │
│ │     • Stock Fundamentals Agent (NEW in v0.2)                │ │
│ │     • Real Estate Agent (NEW in v0.2)                       │ │
│ │     • Investment Strategy Summarization Agent (NEW v0.2)    │ │
│ │   - Acts as MCP CLIENT to consume MCP servers               │ │
│ └────┬────────────────────────────────────────────────────────┘ │
│      │                                                          │
│      │ MCP HTTP calls to internal MCP servers                   │
│      │                                                          │
│ ┌────▼─────────────────────────────┬────────────────────────┐   │
│ │ User Profile MCP Server (.NET)   │ Investment Strategy    │   │
│ │   - Separate container           │ MCP Server (.NET)      │   │
│ │   - User profile management      │   - Separate container │   │
│ │   - Risk assessment              │   - Strategy summaries │   │
│ │                                  │   (NEW in v0.2)        │   │
│ └──────┬───────────────────────────┴──────┬─────────────────┘   │
│        │                                  │                     │
│ ┌──────▼──────────────────────┐   ┌──────▼───────────────────┐  │
│ │ PostgreSQL DB #1            │   │ PostgreSQL DB #2         │  │
│ │   - User profiles           │   │   - Investment strategies│  │
│ │                             │   │   - Investment recomend  │  │
│ └─────────────────────────────┘   └──────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                      │
                      │ MCP HTTP calls over internet
                      │ (to 3rd party services - TBD)
                      │
┌─────────────────────▼─────────────────────────────────────────┐
│ External MCP Servers (Remote 3rd Party - Internet) - TBD      │
│ ┌──────────────────────────────────────────────────────────┐ │
│ │ Stock Market Data MCP Server (e.g., Alpha Vantage)       │ │
│ │   - Real-time quotes, historical data, fundamentals      │ │
│ └──────────────────────────────────────────────────────────┘ │
│ ┌──────────────────────────────────────────────────────────┐ │
│ │ Real Estate Data MCP Server (e.g., Zillow, Redfin)       │ │
│ │   - Property valuations, market trends, listings         │ │
│ └──────────────────────────────────────────────────────────┘ │
│ ┌──────────────────────────────────────────────────────────┐ │
│ │ Financial News MCP Server (e.g., Perplexity, NewsAPI)    │ │
│ │   - Market news, sentiment analysis, research reports    │ │
│ └──────────────────────────────────────────────────────────┘ │
└────────────────────────────────────────────────────────────────┘

External Dependencies (Cloud APIs):
  - Azure OpenAI Service (LLM inference)
  - Market data APIs (if not using MCP servers)
```

#### Components

**FinWise Orchestrator Container** (.NET):
- Exposes FinWise AS an MCP server (Streamable HTTP on port 8080)
- Contains ALL AGENTS running in-process:
  - **Orchestrator Agent**: Routes queries, manages handoffs, coordinates workflow
  - **User Profile Agent**: Collects user information, stores via User Profile MCP Server
  - **Global Advisor Agent**: High-level investment guidance across asset classes
  - **Stock Fundamentals Agent** (NEW in v0.2): Analyzes individual stocks using external market data MCP servers
  - **Real Estate Agent** (NEW in v0.2): Residential real estate guidance using external property data MCP servers
  - **Investment Strategy Summarization Agent** (NEW in v0.2): Synthesizes recommendations into persistent strategies
- Acts as MCP CLIENT to consume internal MCP servers (User Profile, Investment Strategy) and external 3rd-party MCP servers
- Uses ModelContextProtocol SDK for both server and client roles
- Implements handoff and concurrent orchestration patterns via Microsoft Agent Framework
- Authentication: Basic API key for local testing

**Internal MCP Servers** (Separate Containers for Data Access):
- **User Profile MCP Server** (.NET): Manages user profiles, risk assessment questionnaires (PostgreSQL DB #1)
- **Investment Strategy MCP Server** (NEW in v0.2, .NET): Manages investment strategy summaries/recommendations (PostgreSQL DB #2, FR-6)
- Each runs as separate container, exposes data operations as MCP tools
- Accessed by agents running in Orchestrator container

**External MCP Servers** (3rd Party Internet Services - TBD):
- **Stock Market Data MCP Server**: Real-time quotes, fundamentals, historical pricing (e.g., Alpha Vantage, Yahoo Finance)
- **Real Estate Data MCP Server**: Property valuations, market trends, listings (e.g., Zillow, Redfin)
- **Financial News MCP Server**: Market intelligence, sentiment analysis, research reports (e.g., Perplexity, NewsAPI)
- Consumed by in-process agents via MCP client connections over internet
- Specific providers to be determined during implementation

**PostgreSQL Databases** (Microservices Pattern - Separate Databases):

**PostgreSQL DB #1** (User Profile MCP Server):
- User profiles only (from v0.1)
- Risk tolerance, investment goals, timeframes, questionnaire responses
- Schema: `user_profiles` table

**PostgreSQL DB #2** (Investment Strategy MCP Server - NEW in v0.2):
- **Investment strategy summaries/recommendations** (FR-6): Synthesized final recommendations from multi-agent conversations with date-title identifiers
- Schema: `strategy_summaries` table
- **Note**: Conversation sessions, chat history, vector embeddings, and semantic search are deferred to v0.3+ (Cosmos DB for RAG/chat/embeddings)

**Agent Access Patterns to Internal MCP Servers**:
- **User Profile MCP Server**: Accessed by User Profile Agent (write), Orchestrator (read for context passing)
- **Investment Strategy MCP Server** (NEW in v0.2): 
  - **Written by**: Investment Strategy Summarization Agent (creates/saves summaries)
  - **Read by**: Global Advisor Agent (retrieves past recommendations), Orchestrator (context passing)

**External MCP Servers** (agents act as MCP clients):
- Market Data MCP Server: Real-time quotes, fundamentals, historical pricing
- Financial News MCP Server: Market intelligence, sentiment analysis
- Orchestrator agents connect to these as MCP clients, no custom integration code required

#### Key Characteristics

- **Transport**: MCP Streamable HTTP for remote access and multi-client support
- **User Interface**: Multiple AI assistants can connect simultaneously
- **Authentication to MCP servers**: Basic API keys
- **Persistence**: 2 PostgreSQL databases 
- **Deployment**: Docker Compose (5 containers: orchestrator with all agents in-process, 2 internal MCP servers, 2 PostgreSQL databases)
- **Agent Architecture**: All 6 agents run in-process within orchestrator container (monolithic agent runtime)
- **Scalability**: Multiple local users, small team testing
- **Language**: All agents in .NET (polyglot architecture demonstrated in v0.4+ with distributed agents)
- **Testing**: Internal MCP servers can be tested independently via ports

#### Technology Choices: Rationale

**Why Streamable HTTP Transport over STDIO?**
- Enables multiple clients to connect simultaneously (team testing)
- Prepares for cloud deployment (same transport used in v0.3+)
- Supports server-sent events (SSE) for long-running operations (progress updates, streaming responses)
- Single endpoint handles both POST (client requests) and GET (SSE stream)

**Why Docker Linux Containers?**
- Consistent environment across development machines (no "works on my machine")
- Direct path to Azure Container Apps and AKS (same container images)
- Ecosystem maturity for multi-container orchestration

**Why Docker Linux Containers for v0.2+?**
- Prepares for polyglot architecture (Python Risk Management agent added in v0.4)
- Consistent environment across development machines (no "works on my machine")
- Direct path to Azure Container Apps and AKS (same container images)
- Ecosystem maturity for multi-container orchestration

---

### v0.3: Azure Container Apps Cloud Deployment

**Deployment Context**: Azure cloud (serverless containers), accessible over public internet with authentication.

**Note**: v0.3 uses the **same agent architecture as v0.2** (all agents in-process within orchestrator container). The primary difference is deployment target: local Docker Compose (v0.2) → Azure Container Apps (v0.3). Agent distribution into separate containers via A2A protocol is introduced in v0.4+.

#### Architecture Diagram

BAsically, al agents are still in-process within the Finwise workflow process, but:
- We add additonal in-process agents (All agents are .NET classes)
- We deploy the containers to the cloud in Azure Container Apps.

```
┌──────────────────────────────────────────────────────────────┐
│ Internet Users (AI Assistants)                                │
│   - Claude Desktop, ChatGPT, Mobile apps                      │
└─────────────┬────────────────────────────────────────────────┘
              │ HTTPS (TLS 1.3)
              │ Direct to Container Apps ingress
              ▼
┌──────────────────────────────────────────────────────────────┐
│ Azure Container Apps Environment                              │
│ ┌──────────────────────────────────────────────────────────┐ │
│ │ FinWise Orchestrator App                                 │ │
│ │   - MCP HTTP/SSE endpoint (public ingress)               │ │
│ │   - Managed identity for Azure resource access           │ │
│ │   - Auto-scaling (0-10 replicas based on HTTP requests)  │ │
│ └────────┬─────────────────────────────────────────────────┘ │
│          │ Internal HTTP (Container Apps VNET)                │
│          │                                                     │
│ ┌────────▼───────────┬──────────────┬────────────────────┐   │
│ │ Profile MCP Server │ Strategy MCP │ User Context MCP   │   │
│ │                    │              │                    │   │
│ │                    │              │                    │   │
│ └────────────────────┴──────────────┴────────────────────┘   │
└────────────┬──────────────────────────┬──────────────────────┘
             │                          │
             │ Connection strings       │ Connection strings
             ▼                          ▼
┌────────────────────────────┐  ┌─────────────────────────────┐
│ Azure Database for         │  │ (NEW) Azure Cosmos DB NoSQL │
│ PostgreSQL - Flexible      │  │   - Vector search enabled   │
│   - User profiles          │  │   - RAG document store      │
│   - Transactions           │  │   - Session state           │
│   - Relational data        │  │   - Conversation history    │
└────────────────────────────┘  └─────────────────────────────┘

External Services:
┌─────────────────────────────────────────────────────────────┐
│ Azure OpenAI Service                                         │
│   - GPT-4, o1 for reasoning                                 │
│   - text-embedding-ada-002 for vectorization                │
└─────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────┐
│ External MCP Servers (consumed as clients)                   │
│   - Market Data (Alpha Vantage, Yahoo Finance)              │
│   - Financial News (Perplexity, NewsAPI)                    │
└─────────────────────────────────────────────────────────────┘

```

#### Components

**Note**: This architecture uses the simplest Azure components for POC/MVP demonstration. Production features like Azure Front Door, WAF, and advanced networking are intentionally excluded and documented in the Out of Scope section.

**Azure Container Apps Environment**:
- Serverless container hosting with scale-to-zero capability
- Built-in ingress (HTTPS) with automatic TLS certificate management—no separate gateway needed
- Internal VNET for agent-to-agent communication (secure by default)
- KEDA event-driven autoscaling (HTTP requests, queue depth)

**FinWise Orchestrator Container App**:
- Exposes public MCP Streamable HTTP endpoint for user AI assistants
- Basic API key authentication for POC (production authentication in Out of Scope section)
- Uses connection strings and API keys for Azure OpenAI, databases (stored in environment variables)
- Scales 0-10 replicas based on HTTP request rate (KEDA HTTP add-on scaler)

**Internal MCP Servers** (NEW User Context MCP Server in v0.3):
- **User Profile MCP Server** (.NET): Manages user profiles, risk assessment questionnaires (PostgreSQL DB #1)
- **Investment Strategy MCP Server** (.NET): Manages investment strategy summaries/recommendations (PostgreSQL DB #2, FR-6)
- **User Context MCP Server** (NEW in v0.3, .NET): Manages conversation history, session state, RAG document retrieval (Cosmos DB)
  - Provides conversation history retrieval for context-aware recommendations
  - Manages session state across replicas for stateless orchestrator scaling
  - Performs semantic search over RAG documents (market research, financial articles, compliance docs)
  - Stores and retrieves vector embeddings for similarity search
  - Accessed by Orchestrator (session management, context retrieval) and agents (RAG enrichment)
- Each runs as separate container, exposes data operations as MCP tools
- Internal ingress only (not publicly accessible)

**Specialized Agents in Azure Container Apps** (internal ingress only):
- User Profile, Stock Analysis, Real Estate, Investment Strategy Summarization (from v0.2)
- All agents implemented in .NET (polyglot architecture with Python introduced in v0.4)
- Each app auto-scales independently based on workload
- Communicate with orchestrator through Container Apps VNET (no public internet exposure)
- Docker images stored in Azure Container Registry (ACR)

**Azure Database for PostgreSQL - Flexible Server**:
- Managed PostgreSQL service with built-in high availability
- **PostgreSQL DB #1**: User profiles (risk tolerance, goals, timeframes)
- **PostgreSQL DB #2**: Investment strategy summaries (FR-6), structured financial data
- pgvector extension enabled for hybrid search on both databases
- Geo-redundant backups, automatic patching
- **Rationale**: Strategy summaries are structured financial documents with predictable schema, benefit from ACID guarantees and relational integrity

**Azure Cosmos DB for NoSQL** (NEW in v0.3):
- Can use the emulator in Docker compose, but use native Azure Cosmos DB in Azure.
- **RAG Document Store**: Market research documents, financial articles, compliance docs with vector embeddings
- **Conversation History**: High-volume chat messages, semi-structured multi-turn conversations
- **Session State**: Ephemeral key-value data for active user sessions across replicas
- **Market Sentiment Data**: High-throughput time-series data from news feeds and social media
- Vector search for semantic retrieval (DiskANN index) over RAG documents and conversations
- Multi-region replication (optional for v0.3)
- Change feed for real-time event processing (future: trigger alerts on market changes)
- **Accessed via**: User Context MCP Server (NEW in v0.3) - agents do not query Cosmos DB directly
- **Rationale**: Cosmos DB excels at unstructured/semi-structured data, high-throughput writes, and vector search at scale—perfect for RAG and conversations, but overkill for structured strategy summaries

#### Deployment Process

1. **Build container images** using Azure DevOps or GitHub Actions with workload identity federation
2. **Push images to Azure Container Registry** with managed identity authentication
3. **Deploy using Azure CLI or Bicep templates**:
   ```bash
   az containerapp create \
     --name finwise-orchestrator \
     --resource-group finwise-prod \
     --environment finwise-env \
     --image finwiseacr.azurecr.io/orchestrator:v0.3 \
     --target-port 8080 \
     --ingress external \
     --min-replicas 0 \
     --max-replicas 10 \
     --cpu 1.0 --memory 2.0Gi \
     --env-vars AZURE_OPENAI_ENDPOINT=secretref:openai-endpoint \
     --registry-identity system
   ```
4. **Configure connection strings** for database and Azure OpenAI access via environment variables

#### Key Characteristics

- **Transport**: MCP Streamable HTTP (public HTTPS endpoint)
- **User Interface**: Any MCP-compatible client over internet
- **Authentication**: Basic API keys for POC (production authentication detailed in Out of Scope section)
- **Persistence**: Azure Database for PostgreSQL (2 DBs) + Azure Cosmos DB (NEW)
- **Internal MCP Servers**: 3 total (User Profile, Investment Strategy, User Context - NEW in v0.3)
- **Deployment**: Azure Container Apps (6-8 apps in single environment)
- **Scalability**: 100s of concurrent users, automatic scaling
- **Cost**: Pay-per-use (scale-to-zero when idle), ~$200-500/month for pilot

#### Technology Choices: Rationale

**Why Azure Container Apps over AKS for v0.3?**
- Serverless abstraction eliminates Kubernetes operational overhead (no nodes to patch, monitor)
- Scale-to-zero capability reduces costs during low-traffic periods (nights, weekends)
- Built-in HTTPS ingress and certificate management (no manual Let's Encrypt setup)
- Sufficient scale for pilot deployment (100s of users, not 1000s)
- Developer-friendly: uses same Docker Compose patterns from v0.2

**Why Azure Cosmos DB in Addition to PostgreSQL?**
- **PostgreSQL DB #1**: User profiles (structured, relational, ACID-critical)
- **PostgreSQL DB #2**: Investment strategy summaries (structured financial documents, relational integrity, ACID guarantees)
- **Cosmos DB**: RAG embeddings, conversation history, session state, market sentiment (unstructured/semi-structured, high-throughput, vector search)
- Polyglot persistence: right database for right workload (structured financial data in PostgreSQL, unstructured RAG/sessions in Cosmos DB)
- Cosmos DB change feed enables event-driven patterns (trigger agent when new market data arrives)
- **Cost-Effective**: Keeping structured strategy summaries in PostgreSQL avoids unnecessary Cosmos DB costs for data that doesn't need NoSQL flexibility

---

### v0.4: Distributed Agent Architecture with A2A Protocol

**Deployment Context**: Production Azure environment with distributed agents, A2A protocol, and additional regulatory features.

#### Architecture Highlights

v0.4 introduces a **fundamental architectural shift** from in-process agents (v0.1-v0.3) to **distributed agents** running in separate containers communicating via **A2A (Agent-to-Agent) protocol**.

**Key Architectural Change - Distributed Agents**:
- **Agents move from in-process to separate containers**:
  - Each agent (User Profile, Global Advisor, Stock Fundamentals, Real Estate, Investment Strategy Summarization, Risk Management) runs in its own container
  - Agents communicate via A2A protocol (JSON-RPC over HTTP)
  - **Orchestrator still coordinates all handoffs** (same pattern as v0.1-v0.3, just distributed)
  - Independent scaling: Scale individual agents based on workload
  - Service resilience: Agent failures isolated, system continues with reduced capabilities

**New Components**:
- **Risk Management Agent (Python + pandas/scipy)**: Evaluates portfolio composition, alerts on concentration risk, validates recommendations against risk tolerance (FR-7 from vision doc)
  - **Why Python**: Leverages Python's statistical and quantitative analysis libraries (pandas, NumPy, SciPy) for portfolio risk calculations, Monte Carlo simulations, and correlation analysis
  - **Polyglot Architecture**: Demonstrates A2A as language-agnostic protocol—Python agent seamlessly integrates with .NET orchestrator and agents
- **Compliance Checker Service** (.NET): Validates all investment recommendations against regulatory constraints (internal policy enforcement)

**Data Layer Enhancements**:
- **Cosmos DB**: Expanded vector index for regulatory documents, compliance rules, market sentiment analysis
- **PostgreSQL**: Additional tables for recommendation tracking

**Deployment**:
- Same Azure Container Apps environment, additional container app for Risk Management agent
- Multi-region read replicas for Cosmos DB (latency optimization for global users—if applicable)

#### Key Characteristics

- **Distributed Agent Architecture**: Agents run in separate containers (vs in-process in v0.1-v0.3)
- **A2A Protocol**: Agent-to-agent communication via JSON-RPC over HTTP
- **Orchestrator Coordination**: Orchestrator still manages all handoffs (same pattern as before, just distributed)
- **Polyglot Architecture**: Python Risk Management agent demonstrates multi-language integration via A2A
- **Independent Scaling**: Each agent container scales based on workload (e.g., scale Stock Fundamentals agent separately)
- **Risk Management**: Automated portfolio risk assessment and alerts using Python statistical libraries
- **Scalability**: 1000s of concurrent users (Container Apps scaling to 20 replicas per agent)

---

### v0.5: Enterprise-Scale Deployment on Azure Kubernetes Service

**Deployment Context**: Enterprise production (10,000+ users), multi-tenant, highly regulated environment.

While v0.5 is not planned for implementation in this project, the Docker container-based architecture established in v0.2-v0.4 provides a clear migration path to Azure Kubernetes Service (AKS) if FinWise requires enterprise-scale deployment. The same container images built for Azure Container Apps can be deployed to AKS with minimal modifications, primarily involving Kubernetes manifests (Deployments, Services, Ingress) and configuration management.

**Key advantages of AKS migration** include unlimited horizontal scaling (5,000+ nodes vs Container Apps' limits), advanced features like GPU node pools for custom model inference, fine-grained multi-tenancy through namespace isolation, and access to the full Kubernetes ecosystem (Helm charts, custom operators, service meshes). The transition would involve moving from Container Apps' serverless abstraction to managing Kubernetes infrastructure, requiring specialized DevOps expertise but offering greater control over networking, resource allocation, and deployment strategies.

**When to consider v0.5**: Migration to AKS becomes viable when concurrent user load exceeds 1,000+ users (Container Apps strain), GPU workloads are required for local model hosting, strict multi-tenant isolation is mandated by compliance requirements, or advanced observability/traffic management capabilities (service mesh, canary deployments) justify the operational complexity. Until then, Azure Container Apps (v0.3-v0.4) provides superior developer productivity and cost-effectiveness for POC/MVP and early production deployments.

---

## Technologies, Stack & Tools

### Core Technology Stack Summary

| Version | Languages | Deployment | Agents | MCP | Databases |
|---------|-----------|------------|--------|-----|-----------|
| **v0.1** | .NET 10 (C#) | Local process | Microsoft Agent Framework<br>3 agents in-process | STDIO transport<br>ModelContextProtocol SDK | PostgreSQL 15 (local)<br>User profiles only |
| **v0.2** | .NET 10 (C#) | Docker Compose | Same<br>6 agents in-process | Streamable HTTP<br>Same SDK | PostgreSQL 15 (2 DBs)<br>Profiles + strategy summaries |
| **v0.3** | .NET 10 (C#) | Azure Container Apps | Same<br>6 agents in-process | Streamable HTTP<br>Same SDK | Azure PostgreSQL (2 DBs)<br>+ **Cosmos DB (RAG/chat/embeddings)** |
| **v0.4** | .NET 10 (C#)<br>+ Python | Azure Container Apps | Same<br>7 distributed agents via A2A | Streamable HTTP<br>Same SDK | Same as v0.3<br>Enhanced Cosmos DB |
| **v0.5** <br> Viable, but out of scope| .NET 10 (C#)<br>+ Python | Azure Kubernetes Service | Same<br>Full distributed agents | Streamable HTTP<br>Same SDK | PostgreSQL Hyperscale<br>Cosmos DB (multi-region) |

**Note**: All versions use Azure OpenAI Service for LLM inference.

### Detailed Technology Justification

#### Microsoft Agent Framework

**What it is**: Unified platform combining Semantic Kernel (enterprise features) and AutoGen (multi-agent orchestration patterns).

**Why chosen**:
- **Native .NET Integration**: First-class support for C#, ASP.NET Core, Azure services
- **Multi-Agent Orchestration**: Sequential, concurrent, handoff, group chat, Magentic patterns built-in
- **MCP Support**: Official integration via ModelContextProtocol SDK
- **Production Maturity**: Used by Microsoft internal teams, Azure AI Foundry

**Alternatives Considered**:
- **LangChain (Python)**: More mature ecosystem, but weaker .NET support, forces Python dependency
- **CrewAI (Python)**: Lightweight, fast, but Python-only, less enterprise tooling
- **Custom Framework**: Full control, but reinventing orchestration patterns is high risk

**Decision**: Microsoft Agent Framework for .NET agents (all agents in v0.1-v0.3), with Python Risk Management agent added in v0.4 via MCP to demonstrate polyglot architecture and leverage Python's statistical libraries for quantitative risk analysis.

#### Model Context Protocol (MCP)

**What it is**: Open standard for connecting AI agents to external tools and data sources (think "USB-C for AI").

**Why chosen**:
- **Dual Role Architecture**: FinWise operates AS MCP server (user interface) AND uses MCP clients (data integration)
- **Ecosystem**: 16,000+ MCP servers, universal support (Microsoft, Anthropic, OpenAI, Replit)
- **Developer Productivity**: 2-3x faster than building custom REST APIs per integration
- **Standardization**: Language-agnostic (C#, Python, JavaScript agents all interoperate)
- **Transport Flexibility**: STDIO (v0.1 local) → Streamable HTTP (v0.2+ distributed)

**Alternatives Considered**:
- **REST APIs**: Widely understood, but no standard for agent tool discovery, context management
- **GraphQL**: Flexible queries, but limited action/tool semantics, complex to implement
- **gRPC**: High performance, but less web-friendly, requires Protocol Buffers

**Decision**: MCP as primary integration layer for all agent and data access integrations.

#### Azure OpenAI Service

**What it is**: Managed API access to OpenAI models (GPT-4, o1, o3) with enterprise SLAs and Azure compliance.

**Why chosen**:
- **Reasoning Models**: o1, o3 demonstrate 90%+ improvement in multi-step planning vs GPT-4
- **Enterprise Features**: Private network access, managed identity authentication, RBAC, audit logs
- **Azure Integration**: Same identity model as other Azure services (no separate API key management)
- **SLA**: 99.9% uptime guarantee (vs no SLA for OpenAI public API)

**Alternatives Considered**:
- **OpenAI Public API**: Cheaper for low volume, but no enterprise SLA, separate credential management
- **Self-Hosted LLMs (Llama, Mistral)**: Full control, but requires GPU infrastructure, lower quality on complex reasoning
- **Anthropic Claude**: Excellent quality, but requires separate vendor relationship, less Azure integration

**Decision**: Azure OpenAI for production (compliance, SLA), with option to add Anthropic Claude via MCP for specific use cases.

#### PostgreSQL with pgvector

**What it is**: Open-source relational database with pgvector extension for vector similarity search.

**Why chosen**:
- **Relational + Vector**: Hybrid storage eliminates need for separate databases
- **Operational Familiarity**: Most teams already run PostgreSQL (zero learning curve)
- **Azure Integration**: Azure Database for PostgreSQL supports pgvector, managed backups, HA

**Alternatives Considered**:
- **SQL Server**: Better Windows integration, but higher Azure costs, weaker vector support
- **MongoDB**: Flexible schema, but less mature vector search, learning curve for relational teams
- **Pinecone/Weaviate**: Purpose-built vector DBs, but adds operational complexity, separate from relational data

**Decision**: PostgreSQL for v0.1-v0.4, upgrade to Hyperscale in v0.5 for sharding. Add Cosmos DB in v0.3 for high-throughput RAG workloads (specialized use case).

#### Azure Cosmos DB for NoSQL

**What it is**: Globally distributed, multi-model NoSQL database with native vector search.

**Why chosen** (v0.3+):
- **Vector Search at Scale**: Handles millions of embeddings with <100ms latency
- **RAG Optimization**: DiskANN index + hybrid search (vector + keyword) in single query
- **Global Distribution**: Multi-region writes for low-latency worldwide access
- **Change Feed**: Real-time event processing (trigger agents on data changes)
- **Serverless Option**: Pay-per-operation for bursty workloads (matches agent patterns)

**When NOT to Use**:
- v0.1-v0.2: PostgreSQL sufficient for small scale, Cosmos DB adds complexity/cost without benefit
- Relational data: Keep in PostgreSQL (user profiles, transactions) vs Cosmos DB (vectors, sessions)

**Decision**: Add Cosmos DB in v0.3 for RAG workloads, expand to multi-region in v0.5. PostgreSQL remains primary for relational data (polyglot persistence).

#### Azure Container Apps vs Azure Kubernetes Service

**Container Apps** (v0.3-v0.4):
- **When to Use**: Serverless priority, scale-to-zero important, team lacks Kubernetes expertise, <1000 concurrent users
- **Strengths**: Zero operational overhead, consumption pricing, Docker Compose compatibility
- **Limitations**: Max scale ~10K pods, limited network control, no direct Kubernetes API access

**AKS** (v0.5):
- **When to Use**: Enterprise scale (10K+ users), GPU requirements, service mesh needs, multi-tenancy, compliance demands
- **Strengths**: Unlimited scale, full Kubernetes ecosystem, GPU support, Istio, advanced networking
- **Limitations**: Higher operational complexity, minimum node costs, requires Kubernetes expertise

**Decision**: Container Apps for v0.3-v0.4 (faster to market, lower ops burden), migrate to AKS in v0.5 when scale/features justify complexity.

#### Observability Stack

**v0.1-v0.2**: Console/Docker logs (sufficient for development)

**v0.3-v0.5**: Azure Monitor + Application Insights
- **Why**: Native Azure integration, unified logs and basic metrics
- **Features**: Basic error tracking and performance monitoring

---

## Alternative Integration Patterns (Beyond MCP)

While MCP is the primary integration layer, certain scenarios benefit from alternative patterns:

### REST APIs

**When to Use**:
- External services that don't support MCP (legacy systems, third-party APIs without MCP wrappers)
- Simple CRUD operations where MCP overhead not justified

**Implementation**:
- Wrap REST endpoints in lightweight MCP server (convert REST → MCP tools)
- Example: Alpha Vantage stock data API → MCP server exposes `get_stock_quote(symbol)` tool

### GraphQL

**When to Use**:
- Flexible queries over large data graphs where clients need varied subsets of data
- Example: Portfolio management UI fetching user profile + holdings + recent transactions

**Implementation**:
- GraphQL for UI data fetching, MCP for agent tool invocation
- Avoid mixing: GraphQL optimizes reads, MCP optimizes agent actions

---

## Deployment Architecture Differentiators

### v0.1 vs v0.2: Local Process → Docker Containers

**Key Differentiator**: Transition from single .NET process to multi-container orchestration.

**Benefits**:
- **Environment Consistency**: Same containers run on any developer's machine (eliminates "works on my machine")
- **Polyglot Enablement**: Python real estate agent demonstrates multi-language architecture
- **Cloud-Ready**: Docker images deploy unchanged to Azure Container Apps, AKS

**Trade-offs**:
- **Complexity**: Docker Compose adds orchestration overhead vs single process
- **Resource Usage**: Multiple containers consume more memory than single process
- **Debugging**: Distributed debugging harder than single-process debugging in Visual Studio

**Decision Point**: Transition to v0.2 when adding Stock/Real Estate agents (hollow agents in v0.1 don't justify Docker overhead).

### v0.2 vs v0.3: Docker Compose → Azure Container Apps

**Key Differentiator**: Transition from local orchestration to cloud serverless containers.

**Benefits**:
- **Global Access**: Public HTTPS endpoint enables internet users (vs localhost)
- **Managed Services**: Azure Database for PostgreSQL, Cosmos DB eliminate database operations
- **Auto-Scaling**: KEDA scales agents based on load (vs fixed local resources)
- **Network Isolation**: Internal VNET for agent-to-agent communication

**Trade-offs**:
- **Cost**: ~$200-500/month vs $0 for local (Azure resource costs)
- **Latency**: Network hops to Azure databases vs local PostgreSQL container
- **Development Velocity**: Debugging in cloud harder than local (though can still test locally)

**Decision Point**: Transition to v0.3 when pilot users require access outside local network OR when database size exceeds local machine capacity.

### v0.3/v0.4 vs v0.5: Container Apps → AKS

**Key Differentiator**: Serverless simplicity → Enterprise control and scale.

**Benefits**:
- **Unlimited Scale**: AKS supports 5K nodes vs Container Apps limits
- **Advanced Features**: Istio service mesh, GPU node pools, custom Kubernetes operators
- **Multi-Tenancy**: Namespace isolation, network policies per tenant
- **Cost Optimization at Scale**: Reserved instances, spot VMs (Container Apps less flexible)

**Trade-offs**:
- **Operational Complexity**: Requires Kubernetes expertise (cluster upgrades, node management, pod scheduling)
- **Cost Floor**: Minimum node costs (~$200/month) vs Container Apps scale-to-zero
- **Development Overhead**: Helm charts, Kubernetes manifests vs simple Container Apps configuration

**Decision Point**: Transition to v0.5 when:
- Concurrent users exceed 1000 (Container Apps strain)
- GPU workloads required (local model inference)
- Multi-tenancy isolation critical (regulatory compliance)
- Service mesh benefits justify operational cost (observability, traffic management)

---

### Key Success Factors

1. **Start Simple**: Resist temptation to build v0.5 architecture immediately. v0.1 validates core concepts without cloud costs.
2. **Embrace Standards**: MCP prevents lock-in and enables ecosystem integration.
3. **Incremental Investment**: Each version adds features only when business value justifies operational complexity.
4. **Test Production Patterns Locally**: v0.2 Docker Compose mirrors v0.3 Container Apps architecture (smooth transition).

### Risk Mitigation

**Technical Risks**:
- **Agent Quality**: Hollow agents in v0.1 may not represent production complexity → Iteratively enhance with real financial logic in v0.2+
- **MCP Ecosystem Maturity**: Protocol is new (Nov 2024) → Monitor ecosystem, maintain REST API fallback option

---

## Out of Scope (Advanced Production Features)

The following topics represent advanced production capabilities that are **not included in the POC/MVP scope** but are documented here for future consideration when transitioning from proof-of-concept to production-ready deployment.

### Advanced Observability

**Azure Monitor + Application Insights**:
- Structured logging and metrics collection for production troubleshooting
- Error tracking and performance monitoring dashboards
- Custom metrics for agent performance and financial KPIs
- Log Analytics queries for debugging multi-agent workflows
- Availability testing and synthetic monitoring
- **Why Out of Scope**: POC/MVP uses console logging (v0.1-v0.2) and basic Docker logs for debugging. Production observability adds operational overhead without immediate POC value and can be added incrementally based on operational needs.

**OpenTelemetry Integration**:
- Distributed tracing across all agent interactions
- Automatic instrumentation through Microsoft Agent Framework
- Visualize agent handoffs, identify bottlenecks in multi-agent workflows
- Custom spans for financial domain operations (portfolio analysis, risk assessment)

**Advanced Metrics Collection**:
- **Managed Prometheus** (v0.5): Kubernetes-native metrics collection, PromQL queries
- **Managed Grafana**: Pre-built dashboards for AKS, agent performance, financial KPIs
- Custom metrics: Recommendation accuracy, user satisfaction scores, agent response times
- Anomaly detection and intelligent alerting

**Why Out of Scope**: 
- POC/MVP focuses on validating multi-agent functionality, not production monitoring
- Basic Azure Monitor logging sufficient for troubleshooting during development
- OpenTelemetry adds complexity without immediate POC value
- Production observability can be added incrementally based on operational needs

### Security & Authentication

**OAuth 2.1 + Azure AD B2C**:
- End-user authentication with social identity providers (Google, Microsoft)
- Multi-factor authentication (MFA)
- Token-based authentication with automatic rotation
- Role-based access control (RBAC) for different user tiers

**Managed Identity & Workload Identity**:
- Zero credential management (no secrets in environment variables or code)
- Azure resources authenticate using managed identity (Container Apps) or Workload Identity (AKS)
- Automatic token rotation handled by Azure platform
- Fine-grained RBAC permissions (least-privilege access)
- Audit trail through Azure AD sign-in logs

**Advanced Security Features**:
- **Human-in-the-Loop Gates**: High-value recommendations (>$10K) require explicit human approval
- **Rate Limiting**: Per-user API rate limits via Azure API Management
- **PII Redaction**: Automatic masking of sensitive data in logs (Application Insights custom processors)
- **Network Policies** (AKS): Zero-trust pod-to-pod communication (deny all, explicitly allow)
- **Container Image Scanning**: Vulnerability detection with Trivy or Azure Defender
- **Azure Policy for AKS**: Enforce security standards across cluster

**Why Out of Scope**:
- POC/MVP uses basic API keys for simplicity (no user registration flow needed)
- Security hardening addresses production threats, not POC functionality demonstration
- OAuth 2.1 and managed identity add development overhead without POC benefit
- Can be retrofitted before production launch based on compliance requirements

### Caching & Performance Optimization

**Azure Cache for Redis**:
- **LLM Response Caching**: Cache identical queries to reduce Azure OpenAI costs (30-50% reduction)
- **Session State**: Distributed session storage across multiple orchestrator replicas
- **Semantic Caching**: Cache semantically similar queries (not just exact matches)
- **Performance**: Sub-millisecond latency for cached reads
- **High Availability**: Automatic failover, managed patching, backups

**Cost Optimization Strategies**:
- Context compression techniques to reduce token usage
- Strategic model selection (GPT-4o-mini for routine tasks, GPT-4 for complex reasoning)
- Prompt caching for repeated system prompts
- Parallel agent execution to reduce total latency

**Why Out of Scope**:
- POC focuses on functional correctness, not cost optimization
- Caching adds architectural complexity (cache invalidation, consistency)
- Azure OpenAI costs manageable at POC scale (limited users, low query volume)
- Can be added in production based on actual usage patterns and cost data

### Event Sourcing & Audit Trails

**Event Sourcing with Azure Service Bus**:
- All agent actions recorded as immutable events
- Complete audit trail for regulatory compliance (financial services requirement)
- Event replay for debugging and alternative scenario exploration
- CQRS pattern: Separate command (write) and query (read) models

**Implementation Components**:
- **Azure Service Bus**: Message queue for async agent communication
- **Event Store**: Cosmos DB change feed for event persistence
- **KEDA Autoscaling**: Scale agents based on Service Bus queue depth
- **Event-Driven Workflows**: Market alert → queue message → portfolio rebalancing agent triggered

**Compliance & Audit Benefits**:
- Reconstruct any investment recommendation's decision trail
- Regulatory reporting (SEC, FINRA compliance)
- Failure recovery through event replay
- Time-travel debugging (replay events to specific point in time)

**Why Out of Scope**:
- Event sourcing is architectural pattern for compliance, not POC functionality
- Adds significant complexity (event store, replay logic, schema versioning)
- POC can use simple database logging for basic audit needs
- Financial services compliance required only for production deployment

### Service Mesh (Istio)

**Istio Service Mesh** (AKS v0.5):
- **Security**: Automatic mTLS encryption between all services (zero-trust networking)
- **Observability**: Distributed tracing without code changes (automatic span generation)
- **Reliability**: Circuit breaking, retries, timeouts configured declaratively
- **Traffic Management**: Canary deployments, A/B testing for gradual agent rollouts
- **Policy Enforcement**: Centralized policy management for service-to-service communication

**Why Out of Scope**:
- Service mesh addresses production concerns (multi-service security, advanced traffic control)
- Adds Kubernetes operational complexity (Istio control plane, sidecar proxies)
- POC runs minimal services where direct communication sufficient
- Can be evaluated for v0.5 enterprise deployment based on scale requirements

### Production Compliance & Governance

**Full Audit Trails**:
- PostgreSQL audit tables with immutable records
- Every investment recommendation linked to input data, agent reasoning, user profile
- Retention policies (7 years for financial records)

**Regulatory Compliance**:
- SEC, FINRA compliance for investment advisory
- GDPR data privacy (user data deletion, export)
- SOC 2 Type II certification requirements

**Multi-Tenancy & Isolation**:
- Namespace-based tenant isolation in AKS
- Cosmos DB partition keys by `tenant_id`
- PostgreSQL Hyperscale sharding by tenant
- Resource quotas and network policies per tenant

**Why Out of Scope**:
- POC demonstrates technical feasibility, not regulatory compliance
- Compliance requirements vary by jurisdiction and business model
- Audit trails and governance can be designed based on actual regulatory guidance
- Legal and compliance teams should define requirements before implementation

---

**Document End**

