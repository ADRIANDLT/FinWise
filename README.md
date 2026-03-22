# FinWise

A Multi-Agent Investment Assistant for Smarter Financial Decisions.

FinWise is an [MCP](https://modelcontextprotocol.io/) server with four AI agents that collaborate via hub-and-spoke handoffs:

- **OrchestratorAgent** — silent router that delegates to the right specialist
- **ProfileAgent** — collects/manages user profiles (`get_profile`, `set_profile`, `delete_profile`)
- **AdvisorAgent** — provides personalized investment advice once the profile is ready
- **StockSpecializedAgent** — delegates to an Azure AI Foundry Agent for real-time stock research and analysis

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (only if using CosmosDB storage)
- Azure OpenAI credentials (endpoint, deployment name, API key)
- Azure AI Foundry project with a deployed stock agent (for the StockSpecializedAgent)
- An Azure AD App Registration (service principal) with access to the Foundry project

### Environment Variables

The server reads all configuration from environment variables. Set them as **system** or **user** environment variables (restart VS Code / terminal after setting).

#### Required — Azure OpenAI (orchestrator, profile, and advisor agents)

| Variable | Description | Example |
|----------|-------------|----------|
| `AZURE_OPENAI_ENDPOINT` | Azure OpenAI service endpoint | `https://<resource>.openai.azure.com/` |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | Model deployment name | `gpt-4o` |
| `AZURE_OPENAI_API_KEY` | API key for Azure OpenAI | |

#### Required — FinWise Service Principal (shared Azure identity)

Used by the stock agent today, but serves as the application's identity for accessing any Azure resource (Foundry, Cognitive Services, etc.).

| Variable | Description |
|----------|-------------|
| `FINWISE_AZURE_TENANT_ID` | Azure AD tenant ID |
| `FINWISE_AZURE_CLIENT_ID` | Azure AD application (client) ID |
| `FINWISE_AZURE_CLIENT_SECRET` | Azure AD client secret |

> Uses `ClientSecretCredential` so your app's users don't need individual Azure Entra accounts.

#### Required — Azure AI Foundry (stock specialized agent)

| Variable | Description | Example |
|----------|-------------|----------|
| `STOCK_AGENT_PROJECT_ENDPOINT` | Foundry project endpoint | `https://<resource>.services.ai.azure.com/api/projects/<project>` |
| `STOCK_AGENT_NAME` | Name of the stock agent in Foundry | `stock-specialized-investment-agent` |

#### Optional

| Variable | Default | Description |
|----------|---------|-------------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Controls which `appsettings.{env}.json` loads |
| `CosmosDb__Enabled` | `true` | Toggle CosmosDB vs in-memory profile store |
| `CosmosDb__Endpoint` | `https://localhost:8081/` | CosmosDB endpoint |
| `CosmosDb__Key` | Emulator key | CosmosDB account key |
| `CosmosDb__DatabaseName` | `FinWise` | Database name |
| `CosmosDb__ContainerName` | `UserProfiles` | Container name |

### Running Locally

1. **Set required environment variables** (see tables above).

2. **Start the CosmosDB emulator** (optional — for persistent profile storage):
   ```powershell
   docker compose up -d
   ```
   > CosmosDB is **enabled by default** in `appsettings.json`. To skip Docker and use in-memory storage instead, set `CosmosDb__Enabled` to `false`. See [docs/COSMOSDB-SETUP.md](docs/COSMOSDB-SETUP.md) for details.

   | Mode | Configuration | Use Case |
   |------|---------------|----------|
   | **In-Memory** | `CosmosDb.Enabled: false` | Quick testing, no persistence |
   | **CosmosDB** | `CosmosDb.Enabled: true` (default) | Persistent storage across restarts |

3. **Build and run the orchestrator**:
   ```powershell
   dotnet build FinWise.slnx
   dotnet run --project src/FinWise.McpServer/
   ```
   The MCP server starts at **http://localhost:5000/mcp** (hardcoded default). To use a different URL:
   ```powershell
   dotnet run --project src/FinWise.McpServer/ --urls http://localhost:5000
   ```

### Connect an MCP client

**VS Code** — The repo includes `.vscode/mcp.json` with a pre-configured `FinWise-Orchestrator-MCP` entry. Open the workspace and use it from Copilot Chat.

**Other clients** — Point any MCP client to `http://localhost:5000/mcp`.

## Testing

```powershell
# Unit tests (no external dependencies)
dotnet test tests/FinWise.MultiAgentWorkflow.UnitTests/

# MCP server integration tests (requires CosmosDB emulator + Azure OpenAI credentials)
dotnet test tests/FinWise.McpServer.IntegrationTests/

# Stock agent integration tests (requires Foundry credentials)
dotnet test tests/FinWise.StockAgent.IntegrationTests/

# CosmosDB integration tests (requires CosmosDB emulator)
dotnet test tests/FinWise.CosmosDb.IntegrationTests/
```

## Logging

Serilog writes structured logs to both **console** and a **rolling file**:

```
%LOCALAPPDATA%\FinWise-Orchestrator-MCP\Logs\finwise-{date}.log
```

## Documentation

- [CosmosDB Setup Guide](docs/COSMOSDB-SETUP.md) — Local development with CosmosDB emulator
- [Feature Specifications](specs/) — Detailed feature requirements and designs
