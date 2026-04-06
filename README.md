# 💰 FinWise

**A Multi-Agent Investment Assistant for Smarter Financial Decisions**

FinWise is an [MCP](https://modelcontextprotocol.io/) server built with .NET 10 and the [Microsoft Agent Framework](https://learn.microsoft.com/en-us/microsoft/agents/) that orchestrates four AI agents via hub-and-spoke handoffs:

| Agent | Role |
|-------|------|
| 🎯 **OrchestratorAgent** | Silent router — delegates to the right specialist |
| 👤 **ProfileAgent** | Collects and manages user investment profiles |
| 📊 **AdvisorAgent** | Personalized investment advice (once profile is ready) |
| 📈 **StockSpecializedAgent** | Real-time stock research via Azure AI Foundry |

```
                    ┌──────────────┐
                    │  MCP Client  │  (VS Code, Claude Desktop, etc.)
                    └──────┬───────┘
                           │ HTTP POST /mcp
                           ▼
                    ┌──────────────┐
                    │ Orchestrator │  🎯 routes every request
                    └──┬───┬───┬───┘
                       │   │   │
              ┌────────┘   │   └────────┐
              ▼            ▼            ▼
        ┌──────────┐ ┌──────────┐ ┌──────────┐
        │ Profile  │ │ Advisor  │ │  Stock   │
        │  Agent   │ │  Agent   │ │  Agent   │
        │   👤     │ │   📊    │ │   📈     │
        └────┬─────┘ └──────────┘ └────┬─────┘
             │                         │
             ▼                         ▼
        CosmosDB                  Azure AI Foundry
        (profiles)                (stock research)
```

### 🛠️ Tech Stack

| | Technology |
|---|---|
| **Runtime** | .NET 10, C# latest |
| **AI** | Microsoft.Extensions.AI + Azure OpenAI |
| **Agents** | Microsoft.Agents.AI (NuGet preview) |
| **Protocol** | MCP via ModelContextProtocol.AspNetCore |
| **Storage** | Azure CosmosDB (profiles), Redis (sessions) |
| **Logging** | Serilog (structured) |
| **Testing** | xUnit, FluentAssertions, Moq |

---

## 🚀 Quick Start

### Prerequisites

- 🐳 [Docker Desktop 4.22+](https://www.docker.com/products/docker-desktop/) (Docker Compose v2.20+) — required for both options. The `include:` directive in `docker-compose.yml` requires Compose v2.20 or later.
- 🔧 [.NET 10 SDK](https://dotnet.microsoft.com/download) — only for Option B and running tests
- 🔑 Azure OpenAI credentials (endpoint, deployment name, API key)

### Running the MCP Server

There are two ways to run FinWise — choose the one that fits your workflow:

| | Option | Docker runs | MCP Server runs as |
|---|--------|--------------------|--------------------|
| 🐳 | **Option A: Full Docker Stack** | CosmosDB + Redis + **FinWise** | Docker container |
| 🔧 | **Option B: .NET Process** | CosmosDB + Redis only | `dotnet run` on host |

Both options connect to the same CosmosDB emulator and Redis — the only difference is where the MCP server process runs.

---

#### 🐳 Option A: Full Docker Stack (recommended for quick start)

Everything runs in Docker — one command, no .NET SDK required on the host.

1. **Create a `.env` file** from the template (secrets are never committed):
   ```powershell
   Copy-Item .env.template .env
   # Edit .env with your Azure OpenAI credentials
   ```

   ⚠️ **Why is `.env` needed?** Docker Compose does **not** inherit Windows/OS system environment variables. Even if you already have `AZURE_OPENAI_API_KEY` etc. set at the Machine level, you must provide them in `.env` for Docker. Option B (`dotnet run`) doesn't need `.env` because the .NET process inherits system env vars directly.

2. **Start the full stack**:
   ```powershell
   docker compose up --build
   ```
   This starts three containers:
   | Container | Service | Purpose |
   |-----------|---------|---------|
   | `finwise-cosmosdb-emulator` | Azure CosmosDB Linux emulator | User profiles |
   | `finwise-redis` | Redis 7.4 | Agent sessions |
   | `finwise-mcp-server` | FinWise MCP Server | Waits for both ↑ to be healthy |

3. **Verify all services are healthy**:
   ```powershell
   docker compose ps
   ```

4. **View logs**:
   ```powershell
   docker compose logs -f finwise-mcp-server
   ```

The MCP server is available at **http://localhost:5000/mcp**.

> ⏹️ **Stop**: `docker compose down` · **Stop + clear data**: `docker compose down -v`

---

#### 🔧 Option B: .NET Process (recommended for development/debugging)

Run infrastructure in Docker but the MCP server as a local .NET process — enables debugging, hot reload, and faster iteration.

1. **Set required environment variables** as system or user env vars (see [Environment Variables](#-environment-variables) below).

2. **Start infrastructure only** (without the MCP server container):
   ```powershell
   docker compose -f docker-compose.infra.yml up
   ```

3. **Build and run the MCP server**:
   ```powershell
   dotnet build FinWise.slnx
   dotnet run --project src/FinWise.McpServer/
   ```

The MCP server starts at **http://localhost:5000/mcp**.

> 💡 Both CosmosDB and Redis are **enabled by default**. To run without any Docker infrastructure, set `CosmosDb__Enabled` and/or `Redis__Enabled` to `false` (falls back to in-memory stores).

---

### 🔌 Connect an MCP Client

| Client | Setup |
|--------|-------|
| **VS Code** | The repo includes `.vscode/mcp.json` — open the workspace and use `FinWise-Orchestrator-MCP` from Copilot Chat |
| **Claude Desktop** | Add `http://localhost:5000/mcp` as an MCP server |
| **Other clients** | Point any MCP client to `http://localhost:5000/mcp` |

---

## 🧪 Testing

```powershell
# Unit tests (no external dependencies)
dotnet test tests/FinWise.MultiAgentWorkflow.UnitTests/

# CosmosDB integration tests (requires CosmosDB emulator running)
dotnet test tests/FinWise.CosmosDb.IntegrationTests/

# MCP server E2E tests (requires Option B running + Azure OpenAI credentials)
dotnet test tests/FinWise.McpServer.IntegrationTests/

# Stock agent tests (requires Azure AI Foundry credentials)
dotnet test tests/FinWise.StockAgent.IntegrationTests/

# Container tests (requires full Docker stack — Option A)
dotnet test tests/FinWise.McpServer.ContainerTests/
```

> 💡 Container tests use `[SkippableFact]` — they skip gracefully when Docker is not running.

---

## 📁 Project Structure

```
├── src/
│   ├── FinWise.McpServer/                  # MCP server host (transport + composition root)
│   │   └── Dockerfile                      # Multi-stage Docker build for this service
│   └── FinWise.MultiAgentWorkflow/         # Agent orchestration, session, domain model
├── tests/
│   ├── FinWise.MultiAgentWorkflow.UnitTests/
│   ├── FinWise.McpServer.IntegrationTests/ # E2E tests against local server
│   ├── FinWise.McpServer.ContainerTests/   # E2E tests against Docker stack
│   ├── FinWise.McpServer.E2ETestBase/      # Shared MCP protocol test helpers
│   ├── FinWise.CosmosDb.IntegrationTests/
│   └── FinWise.StockAgent.IntegrationTests/
├── specs/                                   # Feature specifications
├── journal/                                 # Project narrative chronicles
├── docker-compose.yml                       # Full stack: CosmosDB + Redis + FinWise
└── .env.template                            # Template for secrets (copy to .env)
```

---

## 🗄️ Storage Options

FinWise uses two separate stores — one for **user profiles** and one for **agent conversation sessions**. Each can run in-memory (no Docker needed) or with a persistent backend (requires Docker).

| What | In-Memory (no Docker) | Persistent (Docker) |
|------|----------------------|---------------------|
| 👤 **User Profiles** — investment goals, risk tolerance, email | `InMemoryProfileStore` — data lost on restart | `CosmosProfileStore` — survives restarts, shared across instances |
| 💬 **Agent Sessions** — conversation history, handoff state | `InMemoryAgentSessionStore` — single instance only | `RedisAgentSessionStore` — survives restarts, enables scale-out |

### 👤 Profile Storage (`CosmosDb__Enabled`)

User profiles are the persistent data that the ProfileAgent collects (name, email, risk tolerance, investment goals). They're stored per-user and retrieved across conversations.

| Store | Config | Behavior |
|-------|--------|----------|
| **InMemoryProfileStore** | `CosmosDb__Enabled: false` | Profiles live in process memory — lost when the server stops. Good for quick testing without Docker. |
| **CosmosProfileStore** | `CosmosDb__Enabled: true` (default) | Profiles persisted in CosmosDB — survive restarts, shareable across server instances. Requires `finwise-cosmosdb-emulator` running in Docker. |

> 📖 See [docs/COSMOSDB-SETUP.md](docs/COSMOSDB-SETUP.md) for CosmosDB emulator setup details.

### 💬 Session Storage (`Redis__Enabled`)

Agent sessions hold the ongoing conversation state — which agent is active, what the orchestrator has decided, and the full chat history for each MCP session. They're ephemeral by design (24 h TTL).

| Store | Config | Behavior |
|-------|--------|----------|
| **InMemoryAgentSessionStore** | `Redis__Enabled: false` | Sessions live in process memory — lost on restart, can't share across instances. Good for single-process development. |
| **RedisAgentSessionStore** | `Redis__Enabled: true` (default) | Sessions persisted in Redis — survive restarts, can be shared across multiple server instances (scale-out). Requires `finwise-redis` running in Docker. |

> Redis sessions have a 24 h sliding TTL and the container uses a `volatile-lru` eviction policy.

---

## 🔑 Environment Variables

The server reads configuration from environment variables. Set them as **system** or **user** environment variables (restart VS Code / terminal after setting).

### Required — Azure OpenAI

Used by the orchestrator, profile, and advisor agents.

| Variable | Description | Example |
|----------|-------------|----------|
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI service endpoint | `https://<resource>.openai.azure.com/` |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | Model deployment name | `gpt-4o` |
| `AZURE_OPENAI_API_KEY` | API key for Azure OpenAI | |

### Optional — Stock Agent

Only required if you want to use the `StockSpecializedAgent` for real-time stock research.

| Variable | Description | Example |
|----------|-------------|----------|
| `STOCK_AGENT_PROJECT_ENDPOINT` | Azure AI Foundry project endpoint | `https://<resource>.services.ai.azure.com/api/projects/<project>` |
| `STOCK_AGENT_NAME` | Name of the stock agent in Foundry | `stock-specialized-investment-agent` |
| `FINWISE_AZURE_TENANT_ID` | Azure AD tenant ID | |
| `FINWISE_AZURE_CLIENT_ID` | Azure AD application (client) ID | |
| `FINWISE_AZURE_CLIENT_SECRET` | Azure AD client secret | |

> Uses `ClientSecretCredential` so your app's users don't need individual Azure Entra accounts.

### Optional — Storage & Runtime

| Variable | Default | Description |
|----------|---------|-------------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Controls which `appsettings.{env}.json` loads |
| `CosmosDb__Enabled` | `true` | `false` → in-memory profile store |
| `CosmosDb__Endpoint` | `https://localhost:8081/` | CosmosDB endpoint |
| `CosmosDb__Key` | Emulator key | CosmosDB account key |
| `CosmosDb__DatabaseName` | `FinWise` | Database name |
| `CosmosDb__ContainerName` | `UserProfiles` | Container name |
| `Redis__Enabled` | `true` | `false` → in-memory session store |
| `Redis__ConnectionString` | `localhost:6379` | Redis connection string |
| `Redis__SessionTtlMinutes` | `1440` | Session TTL in minutes (24 h) |

---

## 📋 Logging

Serilog writes structured logs to both **console** and a **rolling file**:

```
%LOCALAPPDATA%\FinWise-Orchestrator-MCP\Logs\finwise-{date}.log
```

> 🐳 **In Docker**: File logging is unavailable (non-root user). Use `docker compose logs -f finwise-mcp-server` for console output.

---

## 📚 Documentation

| Resource | Description |
|----------|-------------|
| 📖 [CosmosDB Setup Guide](docs/COSMOSDB-SETUP.md) | Local development with CosmosDB emulator |
| 📋 [Feature Specifications](specs/) | Detailed feature requirements and designs |
| 📓 [Project Journal](journal/) | Narrative chronicles of the development journey |
