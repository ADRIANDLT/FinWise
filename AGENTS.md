# FinWise — Agent Instructions

## Feature Toggles

| Feature | Status | When ON | When OFF |
|---------|--------|---------|----------|
| **Memory Bank** | ✅ ON | Load `.memory-bank/` files at session start | Skip `.memory-bank/` entirely |

## Architecture

MCP Client → **FinWise.McpServer** (thin server host) → **FinWise.MultiAgentWorkflow** (core library).
Hub-and-spoke: all agents route through Orchestrator. No direct agent-to-agent calls.
`PROFILE_READY:` marker from ProfileAgent gates advisor/stock access. MCP-Session-Id header = agentSessionId.

### Agents

| Agent | Role |
|-------|------|
| Orchestrator | Silent router — handoffs only, never outputs text to user |
| ProfileAgent | Collects user profile (email, risk, goals, timeframe) |
| AdvisorAgent | Investment recommendations (gated by `PROFILE_READY`) |
| StockAgent | Market analysis via Azure AI Foundry (optional, gated by `PROFILE_READY`) |

## Repository Structure

| Folder | Purpose |
|--------|---------|
| `src/FinWise.McpServer/` | Thin MCP server host (transport + composition root) |
| `src/FinWise.MultiAgentWorkflow/` | Core library: agents, workflow, session, storage |
| `tests/` | Unit, integration, E2E, and container tests |
| `specs/` | Feature specs and research |
| `docs/` | Setup guides and operational docs |

## Build, Test & Deploy

```powershell
# Build
dotnet build FinWise.slnx

# Unit tests (no infrastructure needed)
dotnet test tests/FinWise.MultiAgentWorkflow.UnitTests/
dotnet test tests/FinWise.McpServer.UnitTests/
```

### Mode A — Full Docker stack

> **Requires Docker Desktop 4.22+ / Docker Compose v2.20+** — the `include:` directive in `docker-compose.yml` is not supported by older versions.

```powershell
docker compose up -d
```

Starts CosmosDB emulator + Redis + FinWise MCP Server (port 5000).

### Mode B — Local .NET dev + Docker infrastructure

```powershell
# Start only CosmosDB + Redis
docker compose -f docker-compose.infra.yml up -d

# Run MCP server locally (requires infrastructure above)
dotnet run --project src/FinWise.McpServer/ --urls http://localhost:5000
```

### Integration tests (require infrastructure from Mode A or B + Azure env vars)

```powershell
dotnet test tests/FinWise.McpServer.IntegrationTests/    # Server running + Azure OpenAI env vars
dotnet test tests/FinWise.CosmosDb.IntegrationTests/     # CosmosDB emulator
dotnet test tests/FinWise.Redis.IntegrationTests/        # Redis
dotnet test tests/FinWise.StockAgent.IntegrationTests/   # Azure AI Foundry env vars
```

### Container tests (require Mode A — full Docker stack)

```powershell
docker compose up -d
dotnet test tests/FinWise.McpServer.ContainerTests/
```

## Design Rules

### MUST

- Hub-and-spoke handoffs — all agents route through Orchestrator
- Free-form profile fields — no validation/enum constraints
- Conventional Commits format
- Use env vars for secrets and credentials (Azure OpenAI, etc.)

### MUST NOT

- Never commit secrets or credentials
- Never commit `.env` files — only `.env.*.template` files are tracked in git.
  `.gitignore` uses `*.env` + `.env.*` + `!.env.*.template` to enforce this.
- Never bypass hub-and-spoke (no direct agent-to-agent calls)

## Versioning

Single source of truth: `Directory.Build.props` (`<FinWiseVersion>`). It propagates `<Version>` to all .NET projects automatically.

Bump these four files together on every version change:

| File | What to update | Why |
|---|---|---|
| `Directory.Build.props` | `<FinWiseVersion>` | Propagates `<Version>` to all .NET projects |
| `src/FinWise.McpServer/Program.cs` | `ServerInfo.Version` literal | Reported to MCP clients on `initialize` |
| `docker-compose.finwise.yml` | `image:` tag | Docker build + push tag |
| `README.md` | Docker Hub image tag in Azure Container Apps section | Documentation displayed to users |

### MUST NOT

- Do not add `<Version>` elements to individual `.csproj` files — they would override the centralized `FinWiseVersion`.

```powershell
# After bumping the 4 files above (e.g., to 1.0.2):
dotnet build FinWise.slnx
docker compose build finwise-mcp-server
docker push finwiseproject/finwise-mcp-server:1.0.2
```

## Project-Specific Instructions

Each project has its own `AGENTS.md` with technology-specific rules:

- [`src/FinWise.McpServer/AGENTS.md`](src/FinWise.McpServer/AGENTS.md)
- [`src/FinWise.MultiAgentWorkflow/AGENTS.md`](src/FinWise.MultiAgentWorkflow/AGENTS.md)

## Memory Bank

> Skip if Memory Bank = ❌ OFF above.

Read all `.memory-bank/` files at session start. Templates in [`.memory-bank/AGENTS.md`](.memory-bank/AGENTS.md).
Update after each completed step.
