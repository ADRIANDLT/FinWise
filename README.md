# FinWise

A Multi-Agent Investment Assistant for Smarter Financial Decisions.

FinWise is an [MCP](https://modelcontextprotocol.io/) server with three AI agents that collaborate via hub-and-spoke handoffs:

- **OrchestratorAgent** — silent router that delegates to the right specialist
- **ProfileAgent** — collects/manages user profiles (`get_profile`, `set_profile`, `delete_profile`)
- **AdvisorAgent** — provides personalized investment advice once the profile is ready

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (only if using CosmosDB storage)
- Azure OpenAI credentials (endpoint, deployment name, API key)

### Running Locally

1. **Set Azure OpenAI environment variables** — the server **requires** these and will not start without them:
   ```powershell
   $env:AZURE_OPENAI_ENDPOINT = "https://<your-resource>.openai.azure.com/"
   $env:AZURE_OPENAI_DEPLOYMENT_NAME = "<your-deployment>"
   $env:AZURE_OPENAI_API_KEY = "<your-api-key>"
   ```

2. **Start the CosmosDB emulator** (optional — for persistent profile storage):
   ```powershell
   docker compose up -d
   ```
   > CosmosDB is **enabled by default** in `appsettings.json`. To skip Docker and use in-memory storage instead, set `$env:CosmosDb__Enabled = "false"`. See [docs/COSMOSDB-SETUP.md](docs/COSMOSDB-SETUP.md) for details.

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
   dotnet run --project src/FinWise.McpServer/ --urls http://localhost:6000
   ```

### Connect an MCP client

**VS Code** — The repo includes `.vscode/mcp.json` with a pre-configured `FinWise-Orchestrator-MCP` entry. Open the workspace and use it from Copilot Chat.

**Other clients** — Point any MCP client to `http://localhost:5000/mcp`.

## Testing

```powershell
# Unit tests (no external dependencies)
dotnet test tests/FinWise.McpServer.Tests/ --filter "Category!=Integration&FullyQualifiedName!~EndToEndMcpTests"

# Integration tests (requires CosmosDB emulator)
dotnet test tests/FinWise.McpServer.Tests/ --filter "Category=Integration"

# End-to-end tests (requires running server + Azure OpenAI credentials)
dotnet test tests/FinWise.McpServer.Tests/ --filter "FullyQualifiedName~EndToEndMcpTests"

# All tests (requires running server, CosmosDB emulator, and Azure OpenAI credentials)
dotnet test tests/FinWise.McpServer.Tests/
```

## Documentation

- [CosmosDB Setup Guide](docs/COSMOSDB-SETUP.md) — Local development with CosmosDB emulator
- [Feature Specifications](specs/) — Detailed feature requirements and designs
