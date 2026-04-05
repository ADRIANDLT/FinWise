# FinWise: Architecture and Technology Stack (v0.3.1)

**Document Version:** 3.1  
**Date:** April 5, 2026  
**Status:** Implemented  
**Previous Version:** [v0.3 Architecture](04-architecture-and-technologies-v0.3.md)  
**Spec:** [009 — Dockerized FinWise](009-dockerized-finwise/009-dockerized-finwise-plan.md)  
**Upgrade Spec:** [Microsoft Agent Framework RC4 → 1.0 GA](Technology-Upgrades/upgrade-to-microsoft-agent-framework-1.0-ga.md)

---

## Introduction

**FinWise** is a multi-agent investment assistant built on the **Microsoft Agent Framework (MAF)**. Users interact with it through any MCP-compatible AI assistant (VS Code with GitHub Copilot, Claude Desktop, etc.). The system exposes two MCP tools — `run_finwise_workflow` and `reset_conversation` — and internally orchestrates four specialized AI agents via a hub-and-spoke pattern.

### What's New in v0.3.1

- **Dockerized MCP Server** — The FinWise MCP Server now runs as a Docker container alongside Redis and the CosmosDB emulator, enabling full-stack one-command startup (`docker compose up`). Local `dotnet run` remains supported.
- **Microsoft Agent Framework 1.0 GA** — Upgraded from RC4 to 1.0 GA. The Foundry bridge package was renamed (`Microsoft.Agents.AI.AzureAI` → `Microsoft.Agents.AI.Foundry`), and the Stock agent resolution changed from a single-step to a two-step pattern returning `FoundryAgent`.
- **Companion SDK upgrades** — `Azure.AI.Projects` 2.0.0 GA, `Microsoft.Extensions.AI` 10.4.1.
- **Container integration tests** — 9 new tests (4 reused MCP protocol + 5 Docker-specific) with graceful skip when the Docker stack isn't running.

See [Appendix A](#appendix-a-full-change-delta-from-v03) for the complete before/after delta.

### Version Context

| Version | Status | Description |
|---------|--------|-------------|
| v0.1 | Historical | Monolithic single-project POC |
| v0.2 | Stable | Decoupled two-project architecture, 3 in-process agents |
| v0.3 | Stable | Stock specialized agent (Foundry), SDK upgrade, session fixes, Redis session store |
| **v0.3.1** | **Implemented** | Docker container, full Docker Compose stack, MAF 1.0 GA, container tests |
| v0.4 | Planned | Additional agents, Azure Container Apps prep |
| v0.5 | Planned | Azure Container Apps, decoupling in-process agents into separate containers |

---

## System Architecture

### High-Level View

Two deployment modes are supported — both expose the same MCP endpoint at `http://localhost:5000/mcp`:

```
┌──────────────────────────────────────────────────────────┐
│ User's AI Assistant (VS Code / Claude Desktop / etc.)    │
│   - MCP Client built-in                                  │
└───────────────────┬──────────────────────────────────────┘
                    │ MCP Streamable HTTP
                    │ (localhost:5000, MCP-Session-Id header)
                    ▼
┌══════════════════════════════════════════════════════════┐
║ DOCKER COMPOSE NETWORK (finwise)                         ║
║                                                          ║
║ ┌──────────────────────────────────────────────────────┐ ║
║ │ finwise-mcp-server (ASP.NET Core — Docker Container) │ ║
║ │                                                      │ ║
║ │ Base: mcr.microsoft.com/dotnet/aspnet:10.0           │ ║
║ │ User: app (non-root, UID 1654)                       │ ║
║ │ Kestrel: 0.0.0.0:5000                                │ ║
║ │ ASPNETCORE_ENVIRONMENT=Docker                         │ ║
║ │                                                      │ ║
║ │ ┌──────────────────────────────────────────────────┐ │ ║
║ │ │ Program.cs (Composition Root)                    │ │ ║
║ │ │   - Loads appsettings.Docker.json (overrides)    │ │ ║
║ │ │   - DI registration + MCP tool auto-discovery    │ │ ║
║ │ ├──────────────────────────────────────────────────┤ │ ║
║ │ │ Tools/FinWiseTools.cs (2 attribute-based MCP)    │ │ ║
║ │ │   🔧 run_finwise_workflow (query → advice)       │ │ ║
║ │ │   🔧 reset_conversation  (clear session)         │ │ ║
║ │ ├──────────────────────────────────────────────────┤ │ ║
║ │ │ GET /health → "healthy" (Docker health probe)    │ │ ║
║ │ └──────────────────────────────────────────────────┘ │ ║
║ │                        │                              │ ║
║ │      IChatClient + IUserProfileStore + AgentSession-  │ ║
║ │      Store (SDK) + FoundryAgent (stock) injected       │ ║
║ └────────────────────────┼──────────────────────────────┘ ║
║                          │                                ║
║       ┌──────────────────┼──────────────────┐             ║
║       ▼                  ▼                  ▼             ║
║ ┌───────────┐  ┌──────────────┐  ┌───────────────────┐   ║
║ │ redis     │  │ cosmosdb-    │  │ Azure OpenAI      │   ║
║ │ (7.4)     │  │ emulator     │  │ (external)        │   ║
║ │ :6379     │  │ :8081        │  │ via env vars      │   ║
║ │           │  │ HTTPS+TLS    │  │ from .env file    │   ║
║ └───────────┘  └──────────────┘  └───────────────────┘   ║
║                                                          ║
║       ▲                  ▲          (optional)           ║
║       │                  │          ┌───────────────────┐ ║
║       │                  │          │ Azure AI Foundry  │ ║
║       │                  │          │ (stock agent)     │ ║
║       │                  │          │ via env vars      │ ║
║       │                  │          └───────────────────┘ ║
╚═══════╪══════════════════╪══════════════════════════════╝
        │                  │
   localhost:6379     localhost:8081
   (host access)     (host access)
        │                  │
  ┌─────┴──────┐    ┌─────┴──────┐
  │ Dev tools  │    │ Dev tools  │
  │ RedisInsight│    │ Data       │
  │ redis-cli  │    │ Explorer   │
  └────────────┘    └────────────┘
```

### Two Deployment Modes

```
  Option A: Full Docker Stack                 Option B: .NET Process (dev/debug)
  ─────────────────────────────               ──────────────────────────────────
  docker compose up -d --build                docker compose up -d finwise-redis
                                                finwise-cosmosdb-emulator
  ┌──────────┐                                ┌──────────┐
  │ finwise- │ ← container                    │ dotnet   │ ← host process
  │ mcp-     │   (aspnet:10.0)                │ run      │   (localhost:5000)
  │ server   │   (0.0.0.0:5000)               │          │
  └────┬─────┘                                └────┬─────┘
       │ Docker DNS                                │ localhost
       ├── redis:6379                              ├── localhost:6379
       ├── cosmosdb-emulator:8081                  ├── localhost:8081
       └── Azure OpenAI (external)                 └── Azure OpenAI (env vars)
```

---

## Components

### FinWise.McpServer (ASP.NET Core MCP Server Host)

Thin MCP server host — transport + composition root. Delegates all logic to `FinWise.MultiAgentWorkflow`. Two MCP tools are auto-discovered via `[McpServerTool]` attributes. A `GET /health` endpoint serves Docker health probes.

In Docker mode, `appsettings.Docker.json` overrides Kestrel binding (`0.0.0.0:5000`), Redis connection (`redis:6379`), and CosmosDB endpoint (`cosmosdb-emulator:8081`). The Dockerfile uses a multi-stage build (`sdk:10.0` → `aspnet:10.0`) producing a minimal runtime image with no SDK, source code, or build artifacts.

### FinWise.MultiAgentWorkflow (Class Library)

Core library: multi-agent orchestration, session management, profile storage. Zero MCP dependencies. Receives `IChatClient` via DI (LLM-provider-agnostic).

The MAF 1.0 GA upgrade introduced changes in the `StockSpecializedAgentFactory`:

- **Foundry bridge package renamed** — `Microsoft.Agents.AI.AzureAI` → `Microsoft.Agents.AI.Foundry`.
- **Two-step agent resolution** — The factory now fetches a `ProjectsAgentRecord` via `AgentAdministrationClient.GetAgentAsync()`, then adapts it into a `FoundryAgent` via `AsAIAgent()`. The previous single-step `GetAIAgentAsync()` was removed in 1.0 GA.
- **Return type** — `StockSpecializedAgentFactory.CreateAgentAsync()` now returns `FoundryAgent` (which implements `AIAgent`).

See [Upgrade Spec](Technology-Upgrades/upgrade-to-microsoft-agent-framework-1.0-ga.md) for full breaking change details.

### Redis (Agent Session Storage)

Agent session state is persisted in Redis via `RedisAgentSessionStore` (extends the SDK's `AgentSessionStore`). In Docker mode, the connection switches from `localhost:6379` to `redis:6379` via config override — no code changes required.

### CosmosDB Emulator (User Profile Storage)

User profiles are stored in the CosmosDB emulator via `CosmosDbUserProfileStore`. A **dual-access pattern** enables both the host (`localhost:8081`) and containers (`cosmosdb-emulator:8081`) to reach the same emulator simultaneously by setting `LimitToEndpoint = true` (disables endpoint discovery, which otherwise routes to unreachable Docker-internal IPs).

---

## Docker Compose Stack

Three services with health-checked dependency ordering:

| Service | Image | Port | Role |
|---------|-------|------|------|
| `finwise-cosmosdb-emulator` | CosmosDB Linux emulator | 8081 | User profile persistence |
| `finwise-redis` | Redis 7.4 Alpine | 6379 | Agent session persistence |
| `finwise-mcp-server` | Custom multi-stage build | 5000 | MCP Server host |

The MCP server waits for both Redis and CosmosDB to report healthy before starting. Both compose files (`docker-compose.yml` and `docker-compose.infra.yml`) declare `name: finwise` to share the same Docker volumes — without this, Compose uses the directory name as project prefix, silently creating separate volumes and causing data loss when switching between deployment modes.

Azure OpenAI credentials flow from a `.env` file (git-ignored) through Docker Compose interpolation into the container as environment variables — secrets are never baked into the image.

See [Appendix B](#appendix-b-docker-infrastructure-details) for Dockerfile stages, configuration layering, environment variable reference, and container networking details.

---

## Security Considerations

| Concern | Mitigation |
|---------|-----------|
| **Secrets in image** | Azure OpenAI keys passed via env vars at runtime, never baked into the image |
| **Non-root execution** | `USER $APP_UID` in Dockerfile (default non-root user in `aspnet:10.0`, UID 1654) |
| **`.env` file** | Added to `.gitignore`; `.env.template` committed without real values |
| **CosmosDB emulator TLS** | `AllowInsecureTls=true` + `DangerousAcceptAnyServerCertificateValidator` — dev/emulator only, gated by config flag |
| **Image layers** | Multi-stage build ensures SDK, source code, and intermediate build artifacts are not in the final image |
| **Base image updates** | Use `aspnet:10.0` floating tag for dev; pin to digest in production/CI |
| **`.dockerignore`** | Excludes `.git/`, `bin/`, `obj/`, `.memory-bank/`, specs, docs from build context |

---

## Technology Stack

| Category | Technology | Version |
|----------|------------|---------|
| Runtime | .NET 10, C# latest | — |
| AI (LLM) | Microsoft.Extensions.AI + Azure OpenAI | 10.4.1 / 2.1.0 |
| Agent Framework | Microsoft.Agents.AI | **1.0.0** |
| Agent Framework (Abstractions) | Microsoft.Agents.AI.Abstractions | **1.0.0** |
| Agent Framework (Workflows) | Microsoft.Agents.AI.Workflows | **1.0.0** |
| Agent Framework (Hosting) | Microsoft.Agents.AI.Hosting | 1.0.0-preview.260402.1 |
| Agent Framework (Foundry) | **Microsoft.Agents.AI.Foundry** | **1.0.0** |
| Foundry SDK | Azure.AI.Projects | **2.0.0** |
| Foundry SDK (OpenAI) | Azure.AI.Projects.OpenAI | 2.0.0-beta.1 |
| Protocol | MCP via ModelContextProtocol.AspNetCore | 1.1.0 |
| Logging | Serilog (structured) | — |
| Session Storage | In-memory (dev) / Redis via StackExchange.Redis (prod) | — |
| Profile Storage | In-memory (dev) / Azure Cosmos DB (optional) | — |
| Container Runtime | Docker (multi-stage build) | — |
| Container Orchestration | Docker Compose | — |
| Infrastructure | Redis 7.4-alpine, CosmosDB Linux Emulator | — |
| Testing | xUnit, FluentAssertions, Moq, Xunit.SkippableFact | — |
| Packages | Centralized in `Directory.Packages.props` | — |

---

## What's NOT in v0.3.1 Yet (Deferred)

| Feature | Target Version |
|---------|---------------|
| Push Docker image to ACR | v0.4+ |
| GitHub Actions CI (build + container tests) | v0.4+ |
| Chiseled/distroless image (`aspnet:10.0-noble-chiseled`) | v0.4+ |
| Multi-architecture builds (ARM64) | v0.4+ |
| Testcontainers for programmatic container lifecycle | v0.4+ |
| Additional specialized agents | v0.4+ |
| Azure Container Apps deployment | v0.5 |
| Decoupling in-process agents into separate containers | v0.5+ |

---
---

# Appendices

## Appendix A: Full Change Delta from v0.3

| Aspect | v0.3 | v0.3.1 |
|--------|------|--------|
| **MCP Server deployment** | `dotnet run` on host only | Docker container **or** `dotnet run` (both supported) |
| **Dockerfile** | N/A | Multi-stage build: `sdk:10.0` (build) → `aspnet:10.0` (runtime) |
| **Docker Compose** | 2 services (Redis, CosmosDB emulator) | 3 services: Redis + CosmosDB emulator + **`finwise-mcp-server`** |
| **Configuration** | `appsettings.json` only | + `appsettings.Docker.json` (Kestrel `0.0.0.0`, Docker DNS hostnames) |
| **Health check** | No `/health` endpoint | Dedicated `GET /health` → `"healthy"` for Docker health probes |
| **Environment variables** | Host-level env vars | `.env` file → Docker Compose interpolation (secrets never in image) |
| **Container networking** | N/A | Docker bridge network; services resolve by hostname (`redis:6379`, `cosmosdb-emulator:8081`) |
| **Test architecture** | Monolithic `EndToEndMcpTests` | Shared `McpEndToEndTestBase` (class library) + `IntegrationTests` + **`ContainerTests`** |
| **Container tests** | N/A | `FinWise.McpServer.ContainerTests` — 9 tests (4 reused MCP protocol + 5 Docker-specific) with `[SkippableFact]` |
| **Security** | N/A | Non-root user (`$APP_UID`), multi-stage build (no SDK/source in runtime image), `.env` in `.gitignore` |
| **CosmosDB emulator networking** | `AZURE_COSMOS_EMULATOR_IP_ADDRESS_OVERRIDE=127.0.0.1` | Removed override + `LimitToEndpoint = true` (dual-access: host + container) |
| **MCP client SSE handling** | `ReadAsStringAsync()` | `HttpCompletionOption.ResponseHeadersRead` + line-by-line SSE parsing |
| **Microsoft Agent Framework** | `1.0.0-rc4` (all packages) | **`1.0.0` GA** (core packages); `Microsoft.Agents.AI.Hosting` remains preview |
| **Foundry bridge package** | `Microsoft.Agents.AI.AzureAI` (rc4) | **Renamed** to `Microsoft.Agents.AI.Foundry` (1.0.0 GA) |
| **Azure.AI.Projects** | `2.0.0-beta.1` | **`2.0.0` GA** — agent resolution API restructured (two-step pattern) |
| **Microsoft.Extensions.AI** | `10.3.0` | `10.4.1` |
| **Foundry agent resolution** | Single-step `GetAIAgentAsync()` → `AIAgent` | Two-step: `AgentAdministrationClient.GetAgentAsync()` → `AsAIAgent()` → `FoundryAgent` |
| **Experimental diagnostics** | N/A | `MAAIW001;OPENAI001` suppressed globally (handoff + Foundry APIs marked `[Experimental]`) |
| **Global app version** | N/A | `FinWiseVersion` property in `Directory.Build.props` stamps all assemblies |
| **Docker Compose project naming** | N/A (infra compose had `name: finwise`) | `name: finwise` added to `docker-compose.yml` — prevents volume divergence between compose files |
| **CosmosDB persistence tests** | N/A | 3 new integration tests verifying data survives across store instances and emulator restarts |

---

## Appendix B: Docker Infrastructure Details

### Dockerfile — Multi-Stage Build

The Dockerfile is placed in `src/FinWise.McpServer/` with the build context set to the repo root (so it can access both `src/FinWise.McpServer/` and `src/FinWise.MultiAgentWorkflow/`).

```
Stage 1: BUILD (sdk:10.0 ~900 MB)         Stage 2: RUNTIME (aspnet:10.0 ~220 MB)
──────────────────────────────────         ────────────────────────────────────────
 COPY project files first                   COPY --from=build /app
   ├── Directory.Build.props                 ├── FinWise.McpServer.dll
   ├── Directory.Packages.props              ├── FinWise.MultiAgentWorkflow.dll
   ├── McpServer.csproj                      ├── appsettings.json
   └── MultiAgentWorkflow.csproj             ├── appsettings.Docker.json
                                             └── (NuGet dependencies)
 dotnet restore  ← cached layer
                                            apt-get install -y curl  ← health check
 COPY src/  ← source code
                                            USER $APP_UID  ← non-root (UID 1654)
 dotnet publish -c Release -o /app          EXPOSE 5000
                                            ENTRYPOINT ["dotnet", "FinWise.McpServer.dll"]

 ✘ SDK discarded                            ✘ No SDK  ✘ No source  ✘ No build artifacts
```

**Key decisions:**

1. **Cannot use `FinWise.slnx` for restore** — the solution references test projects not in the Docker context. Individual `.csproj` restore is used instead.
2. **`curl` installed before `USER` switch** — the non-root user cannot install packages.
3. **`Directory.Build.props` and `Directory.Packages.props` copied** — required for centralized NuGet package management.
4. **Port 5000** — explicit Kestrel configuration in `appsettings.Docker.json` overrides the `aspnet:10.0` default of 8080.

### Configuration Layering

ASP.NET Core's configuration precedence (later overrides earlier) is used for container-specific settings:

```
  appsettings.json (base)           appsettings.Docker.json (overrides)
  ───────────────────────           ────────────────────────────────────
  Kestrel: localhost:5000    →      Kestrel: 0.0.0.0:5000
  Redis: localhost:6379      →      Redis: redis:6379
  CosmosDb: localhost:8081   →      CosmosDb: cosmosdb-emulator:8081
```

The `ASPNETCORE_ENVIRONMENT=Docker` env var in `docker-compose.yml` triggers loading `appsettings.Docker.json` via the standard `AddJsonFile($"appsettings.{env}.json")` pattern in `Program.cs`. Only the three values above change; all other configuration (CosmosDB credentials, Redis TTL, logging levels) is inherited from the base `appsettings.json`.

### Environment Variables and Secrets

Docker Compose does **not** inherit Windows/OS system environment variables. Secrets are managed via a `.env` file (git-ignored) and interpolated by Compose:

```
.env (secrets, git-ignored)            docker-compose.yml
────────────────────────               ───────────────────
AZURE_OPENAI_ENDPOINT=...      ──→    environment:
AZURE_OPENAI_API_KEY=...       ──→      - AZURE_OPENAI_API_KEY=${AZURE_OPENAI_API_KEY}
```

A `.env.template` is committed with placeholder values. Users copy it to `.env` and fill in real credentials.

| Variable | Required | Source | Purpose |
|----------|----------|--------|---------|
| `ASPNETCORE_ENVIRONMENT` | Yes (compose) | `docker-compose.yml` | Set to `Docker` to load `appsettings.Docker.json` |
| `AZURE_OPENAI_ENDPOINT` | Yes | `.env` | Azure OpenAI service URL |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | Yes | `.env` | Model deployment name |
| `AZURE_OPENAI_API_KEY` | Yes | `.env` | Azure OpenAI API key |
| `STOCK_AGENT_PROJECT_ENDPOINT` | No | `.env` | Azure AI Foundry endpoint |
| `STOCK_AGENT_NAME` | No | `.env` | Stock agent name |
| `FINWISE_AZURE_TENANT_ID` | No | `.env` | Azure AD tenant |
| `FINWISE_AZURE_CLIENT_ID` | No | `.env` | Azure AD client |
| `FINWISE_AZURE_CLIENT_SECRET` | No | `.env` | Azure AD secret |
| `FINWISE_MCP_URL` | No | Host env (tests) | Override test target URL (default: `http://localhost:5000`) |

### Container Networking

All three containers share a Docker bridge network. Service names resolve as DNS hostnames within the network:

| From → To | Hostname | Port | Protocol | Config Source |
|-----------|----------|------|----------|---------------|
| Host → finwise-mcp-server | `localhost` | `5000:5000` | HTTP | Port mapping |
| Host → redis | `localhost` | `6379:6379` | Redis | Port mapping |
| Host → cosmosdb-emulator | `localhost` | `8081:8081` | HTTPS | Port mapping |
| finwise-mcp-server → redis | `redis` | `6379` | Redis | `appsettings.Docker.json` |
| finwise-mcp-server → cosmosdb-emulator | `cosmosdb-emulator` | `8081` | HTTPS (insecure TLS) | `appsettings.Docker.json` |
| finwise-mcp-server → Azure OpenAI | External URL | `443` | HTTPS | `.env` env vars |

### Health Check Strategy

Three layers of health checking ensure reliable startup and test orchestration:

| Level | Method | Purpose |
|-------|--------|--------|
| **Docker health check** | HTTP GET to `/health` endpoint inside container | Compose reports service health; `depends_on` gates |
| **Test health check** | `ContainerHealthCheck.IsServerReachableAsync()` | Tests wait for server before executing |
| **Startup probe** | `start_period: 15s` in compose | Grace period for .NET app startup |

A dedicated `/health` endpoint (returning HTTP 200) was added because the MCP `/mcp` endpoint returns 405 for GET requests, making it unsuitable for health probes.

---

## Appendix C: Test Architecture

### Shared Base Approach

The existing `EndToEndMcpTests` and the new Docker container tests make **identical HTTP calls** to the same `localhost:5000/mcp` URL. To avoid duplication, MCP protocol helpers are extracted into a shared base class library:

```
┌──────────────────────────────────────────────────────────┐
│                   TEST PROJECTS                          │
│                                                          │
│  ┌──────────────────────────┐  ┌────────────────────────┐│
│  │ IntegrationTests         │  │ ContainerTests         ││
│  │ ────────────────────── │  │ ──────────────────── ││
│  │ EndToEndMcpTests         │  │ DockerizedMcpTests     ││
│  │   6× [Fact]               │  │   4× [SkippableFact]   ││
│  │                          │  │                        ││
│  │ Server: dotnet run       │  │ DockerContainer-       ││
│  │ Fails if server down     │  │   SpecificTests        ││
│  │                          │  │   5× [SkippableFact]   ││
│  │                          │  │                        ││
│  │                          │  │ Server: docker compose  ││
│  │                          │  │ Skips if container down ││
│  └────────────┬─────────────┘  └───────────┬────────────┘│
│               │               inherits     │             │
│               └──────────┬─────────────────┘             │
│                          ▼                               │
│           ┌──────────────────────────────┐               │
│           │ E2ETestBase (class library)  │               │
│           │ ────────────────────────── │               │
│           │ McpEndToEndTestBase          │               │
│           │   InitializeMcpSession()     │               │
│           │   InitializeNewMcpSession()  │               │
│           │   CallFinancialAdviceTool()  │               │
│           │   CallResetSessionTool()     │               │
│           │   SetupTestProfile()         │               │
│           │   SetupTestProfileWithEmail()│               │
│           │   SSE / JSON-RPC parsing     │               │
│           │   XunitLoggerProvider        │               │
│           │   McpBaseUrl (env override)  │               │
│           └──────────────────────────────┘               │
│                                                          │
└──────────────────────────────────────────────────────────┘
```

**Key design:**

- `McpEndToEndTestBase` is a **plain class library** (not a test project) containing HTTP client setup, MCP protocol helpers, and common test-profile setup utilities.
- The base URL is configurable via environment variable (default: `http://localhost:5000`), enabling the same test logic to target different server instances.
- SSE (Server-Sent Events) responses are handled with streaming reads to avoid blocking on open connections — a pattern required for MCP Streamable HTTP transport.

### Container Test Categories

**Reused MCP Protocol Tests (`DockerizedMcpTests`)** — Re-exercise existing E2E scenarios against the containerized server:

| Test | What it validates |
|------|-------------------|
| `Container_McpInitialize_ShouldReturnSessionId` | MCP handshake works through Docker |
| `Container_ToolDiscovery_ShouldExposeTools` | Tool registration survives containerization |
| `Container_FinancialAdvice_ShouldAskForEmail` | Full agent workflow works in container |
| `Container_ResetConversation_ShouldClear` | Session reset works through Docker/Redis |

**Docker-Specific Tests (`DockerContainerSpecificTests`)** — Validate concerns only exercisable against a container:

| Test | What it validates | Why local E2E can't cover it |
|------|-------------------|------------------------------|
| `Container_ShouldBeReachableAndHealthy` | Dockerfile builds, Kestrel binds `0.0.0.0:5000`, port mapping works | Local binds `localhost:5000` directly |
| `Container_RedisConnectivity_ShouldWorkOverDockerNetwork` | `appsettings.Docker.json` Redis override, Docker DNS resolution (`redis:6379`) | Local uses `localhost:6379` |
| `Container_AzureOpenAIEnvVars_ShouldBeInjected` | `.env` → docker-compose → container env var injection chain | Local inherits host env vars directly |
| `Container_CosmosDbConnectivity_ShouldWorkOverDockerNetwork` | `appsettings.Docker.json` CosmosDB override, cross-container TLS (`cosmosdb-emulator:8081`) | Local uses `localhost:8081` |
| `Container_StartupTime_ShouldBeReasonable` | No SDK in runtime image, published assemblies complete (<10 s) | Local `dotnet run` always recompiles |

All container tests use `[SkippableFact]` — if the Docker stack isn't running, tests are **skipped** (not failed).

### Test Pyramid (v0.3.1)

```
                    ┌────────────────────────┐
                    │ E2E Container (9)      │  ← NEW in v0.3.1
                    │ 4 reused + 5 Docker    │
                    ├────────────────────────┤
                    │ E2E Local (6)          │  ← Existing (refactored)
               ┌────┴────────────────────────┴────┐
               │ Integration (per-component)       │  ← Existing
               │ Redis, CosmosDB, Stock             │
          ┌────┴──────────────────────────────────┴────┐
          │ Unit Tests (fast, isolated)                │  ← Existing
          │ MultiAgentWorkflow (~66), McpServer (~9)    │
          └────────────────────────────────────────────┘
```

### Test Coverage Matrix

| Concern | Unit | Local E2E | Container (reused) | Container (Docker-specific) |
|---------|:----:|:---------:|:------------------:|:---------------------------:|
| Business logic | ✅ | ✅ | ✅ | — |
| MCP protocol | — | ✅ | ✅ | — |
| Dockerfile correctness | — | — | ✅ (implicit) | ✅ |
| `appsettings.Docker.json` | — | — | ✅ (implicit) | ✅ |
| Container networking (Redis) | — | — | — | ✅ |
| Container networking (CosmosDB) | — | — | — | ✅ |
| Port binding (`0.0.0.0` vs `localhost`) | — | — | ✅ (implicit) | ✅ |
| Env var injection (`.env` → container) | — | — | — | ✅ |
| Startup time regression | — | — | — | ✅ |

---

## Appendix D: Repository Structure

```
├── src/
│   ├── FinWise.McpServer/
│   │   ├── Dockerfile                          # Multi-stage Docker build
│   │   ├── appsettings.json                    # Base config (localhost endpoints)
│   │   ├── appsettings.Docker.json             # Container overrides (0.0.0.0, Docker DNS)
│   │   ├── Program.cs                          # Composition root + GET /health
│   │   ├── Tools/FinWiseTools.cs
│   │   └── Infrastructure/
│   └── FinWise.MultiAgentWorkflow/             # Core library (MAF 1.0 GA)
├── tests/
│   ├── FinWise.McpServer.E2ETestBase/          # Shared test base class library
│   ├── FinWise.McpServer.IntegrationTests/     # E2E against dotnet run
│   ├── FinWise.McpServer.ContainerTests/       # E2E against Docker container
│   ├── FinWise.MultiAgentWorkflow.UnitTests/
│   ├── FinWise.McpServer.UnitTests/
│   ├── FinWise.CosmosDb.IntegrationTests/
│   └── FinWise.StockAgent.IntegrationTests/
├── docker-compose.yml                          # 3 services (MCP server + Redis + CosmosDB)
├── docker-compose.infra.yml                    # 2 services (Redis + CosmosDB only)
├── .dockerignore
├── .env.template                               # Template for Docker secrets
├── Directory.Build.props                       # Global version (0.3.1) + experimental suppressions
├── Directory.Packages.props                    # Centralized NuGet versions
└── FinWise.slnx
```

---

## Appendix E: Implementation Learnings & Critical Fixes

### CosmosDB Emulator Docker Networking

The CosmosDB Linux emulator reports Docker-internal IP addresses in its account metadata. The .NET Cosmos SDK's endpoint discovery feature routes requests to those reported addresses, which are unreachable depending on the access path.

No single value for `AZURE_COSMOS_EMULATOR_IP_ADDRESS_OVERRIDE` works for both host and container access simultaneously. The architectural solution is to **disable endpoint discovery** (`LimitToEndpoint = true`) so the SDK always uses the connection string endpoint, enabling the dual-access pattern described in the Components section.

### SSE Streaming for MCP Streamable HTTP

MCP servers using Streamable HTTP transport return Server-Sent Events (SSE) responses. Any HTTP client (including test clients) must use **streaming reads** rather than buffered reads, because buffered reads block indefinitely on open SSE connections. This is a fundamental protocol concern for all MCP Streamable HTTP consumers.

### Docker Compose Volume Divergence

When `docker-compose.yml` uses `include: docker-compose.infra.yml`, the `name:` property from the included file is **not inherited**. Compose falls back to the directory name as project prefix, silently creating separate volumes. Both compose files must explicitly declare the same `name: finwise` to share volumes. Without this fix, data written via one compose file is invisible to the other.

### Dockerfile Design Decisions

1. **Solution file exclusion** — The `.slnx` references test projects outside the Docker context; the Dockerfile restores individual project files instead.
2. **Runtime dependency verification** — NuGet packages with `PrivateAssets="all"` are excluded from publish output. Runtime-needed packages (e.g., `Newtonsoft.Json` for CosmosDB) must not have this flag.
3. **Health check tooling** — The `aspnet:10.0` base image lacks `curl`; it must be installed before switching to the non-root user.
4. **Console-based logging** — Non-root container user cannot write to the app directory. The logging strategy shifts from file sinks to stdout (consumed via `docker compose logs`).
5. **Dedicated health endpoint** — The MCP protocol endpoint returns 405 for GET; a separate `/health` route is required for Docker health probes.

### Windows Environment Variables

Docker Compose `.env` files don't inherit Windows system environment variables. Even if credentials are set at the Machine level, they must be explicitly provided in `.env` for Docker. The local `dotnet run` mode inherits them automatically — this is a key difference between the two deployment modes.

---

## Appendix F: Commands Reference

```powershell
# Build image only
docker build -t finwise-mcp-server -f src/FinWise.McpServer/Dockerfile .

# Start full stack (build + run)
docker compose up -d --build

# View logs
docker compose logs -f finwise-mcp-server

# Check health
docker compose ps

# Run container tests
dotnet test tests/FinWise.McpServer.ContainerTests/

# Run all tests (unit + integration + container)
dotnet test FinWise.slnx

# Stop everything
docker compose down

# Stop and remove volumes (clean slate)
docker compose down -v
```

---

**Document End**
