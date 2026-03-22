# FinWise — Agent Instructions

Multi-agent financial advisor exposing workflows via MCP (Model Context Protocol).

---

## 🔧 Feature Toggles

| Feature | Status | When ON | When OFF |
|---------|--------|---------|----------|
| **Memory Bank** | ✅ ON | Load `.memory-bank/` files at session start | Skip `.memory-bank/` entirely. Fresh start each session. |

---

## Architecture Overview

```
MCP Client (VS Code / Claude Desktop)
  │  HTTP + MCP-Session-Id header
  ▼
FinWise.McpServer (ASP.NET Core MCP Server)
  ├── Tools/FinWiseTools.cs (attribute-based, 2 tools)
  ├── McpSessionMapping (session → conversation mapping)
  └── Program.cs (composition root)
        │
        ▼
FinWise.MultiAgentWorkflow (Class Library)
  ├── Workflow/FinWiseWorkflowService (core orchestration)
  ├── Agents/ (OrchestratorAgent, ProfileAgent, AdvisorAgent)
  ├── Session/ (AgentSessionManager, SessionResetFlag, ConversationRunContext)
  └── Infrastructure/ (UserProfileStore)
```

**Key patterns:**
- Hub-and-spoke handoffs — all agents route through orchestrator, no direct agent-to-agent
- Stateless agents — `AIAgent` instances hold no state; all state lives in `AgentSession`
- `PROFILE_READY:` marker — signal from ProfileAgent that profile is complete; gates advisor access
- Session detection — `MCP-Session-Id` HTTP header distinguishes clients

## Repository Structure

```
├── src/FinWise.McpServer/                        # Thin MCP server host (transport + composition root)
├── src/FinWise.MultiAgentWorkflow/               # Class library: agents, workflow, session, services
│   ├── Agents/                                   # Agent factories (Orchestrator, Profile, Advisor)
│   ├── Workflow/                                  # Multi-agent handoff orchestration
│   ├── Session/                                   # Conversation state management
│   ├── DomainModel/                                  # Domain model (UserProfile)
│   └── Infrastructure/                               # UserProfileStore
├── tests/FinWise.MultiAgentWorkflow.UnitTests/   # Unit tests (xUnit + FluentAssertions + Moq)
├── tests/FinWise.McpServer.IntegrationTests/     # Integration / E2E tests
├── specs/                                         # Feature specs and research
└── samples/                                       # Reference implementations
```

## Technology Stack

| Category | Technology |
|----------|------------|
| Runtime | .NET 10, C# latest |
| AI | Microsoft.Extensions.AI + Azure OpenAI |
| Agent Framework | Microsoft.Agents.AI (NuGet preview) |
| Protocol | MCP via ModelContextProtocol.AspNetCore |
| Logging | Serilog (structured, `LogContext.PushProperty`) |
| Storage | In-memory (CosmosDB for profiles, more planned) |
| Testing | xUnit, FluentAssertions, Moq |
| Packages | Centralized in `Directory.Packages.props` |

## Design Rules

- **Namespace = folder path** — sub-folder names must NOT match class names (C# collision)
- **Manual DI** — no container; `Program.cs` is the composition root
- **Hub-and-spoke handoffs** — all agents route through orchestrator, no direct agent-to-agent
- **Packages** — centralized in `Directory.Packages.props`
- **Free-form profile fields** — no validation/enum constraints; advisor interprets

## Build & Test

```powershell
dotnet build FinWise.slnx
dotnet test tests/FinWise.MultiAgentWorkflow.UnitTests/
dotnet test tests/FinWise.McpServer.IntegrationTests/
dotnet run --project src/FinWise.McpServer/ --urls http://localhost:5000
```

## Development Rules

- **Minimal diffs** — only change what's necessary
- **TreatWarningsAsErrors** — zero warnings allowed
- **Conventional Commits** format
- **Never commit secrets** — use env vars for Azure OpenAI credentials
- **TDD** for new features (Red → Green → Refactor)
- **Free-form profile fields** — no validation/enum constraints; advisor interprets

## Project-Specific Instructions

| Project | Instructions |
|---------|--------------|
| MultiAgentWorkflow | `src/FinWise.MultiAgentWorkflow/AGENTS.md` |
| McpServer | `src/FinWise.McpServer/AGENTS.md` |

## Memory Bank

> Skip if Memory Bank = ❌ OFF above.

Read all `.memory-bank/` files at session start. Templates in [`.memory-bank/AGENTS.md`](.memory-bank/AGENTS.md).

| File | Purpose |
|------|---------|
| `activeContext.md` | Current task, recent changes, next steps |
| `learnings.md` | Technical patterns, code structure, decisions |
| `userDirectives.md` | User preferences and constraints |

**Workflow**: Load at session start → follow documented patterns → update after each completed step.
