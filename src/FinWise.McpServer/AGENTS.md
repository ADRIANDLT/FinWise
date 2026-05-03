# FinWise.McpServer

Thin MCP server host — transport + composition root. Delegates all logic to `FinWise.MultiAgentWorkflow`.

## Technology

.NET 10, C# latest, ASP.NET Core, MCP via ModelContextProtocol.AspNetCore, Serilog, Azure AI Foundry (via `AIProjectClient` + Responses-backed `IChatClient`).
Packages centralized in `Directory.Packages.props`.

## MUST

- `Program.cs` is the composition root — no 3rd-party DI container (ASP.NET built-in DI is fine)
- MCP tools in `Tools/` — attribute-based (`[McpServerTool]`), auto-discovered
- Tools are thin adapters: resolve service → call workflow → return result
- MCP-Session-Id header = agentSessionId (no mapping layer)
- TreatWarningsAsErrors — zero warnings

## MUST NOT

- Never inspect conversation content or detect `PROFILE_READY:` — that's workflow logic
- Never put business/agent logic here — belongs in MultiAgentWorkflow
