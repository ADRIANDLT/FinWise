# FinWise
FinWise: A Multi-Agent Investment Assistant for Smarter Financial Decisions

## Quick Start

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) (for CosmosDB emulator)
- Azure OpenAI API credentials

### Running Locally

1. **Start the CosmosDB emulator** (optional - for persistent profile storage):
   ```powershell
   docker compose up -d
   ```

2. **Build the solution**:
   ```powershell
   dotnet build FinWise-orchestrator-mcp.sln
   ```

3. **Run the orchestrator**:
   ```powershell
   dotnet run --project src/FinWise.Orchestrator/ --urls http://127.0.0.1:3923
   ```

### Configuration

The application supports two storage modes for user profiles:

| Mode | Configuration | Use Case |
|------|---------------|----------|
| **In-Memory** | `CosmosDb.Enabled: false` | Quick testing, no persistence |
| **CosmosDB** | `CosmosDb.Enabled: true` | Persistent storage across restarts |

See [docs/COSMOSDB-SETUP.md](docs/COSMOSDB-SETUP.md) for detailed CosmosDB setup instructions.

## Documentation

- [CosmosDB Setup Guide](docs/COSMOSDB-SETUP.md) - Local development with CosmosDB emulator
- [Feature Specifications](specs/) - Detailed feature requirements and designs

## Testing

```powershell
# Run unit tests
dotnet test tests/FinWise.Orchestrator.Tests --filter "Category!=Integration&FullyQualifiedName!~EndToEndMcpTests"

# Run all tests (requires server and CosmosDB emulator)
dotnet test tests/FinWise.Orchestrator.Tests
```
