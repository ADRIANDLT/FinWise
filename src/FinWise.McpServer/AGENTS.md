# FinWise.McpServer — Project Instructions

Thin MCP server host. Transport + composition root.
Delegates all workflow logic to `FinWise.MultiAgentWorkflow`.

## Enforcements

- `Program.cs` is the composition root — manual DI
- MCP tools in `Tools/` — attribute-based, auto-discovered
- Tools are thin adapters: resolve services → call workflow → return response
- Never inspect conversation content or PROFILE_READY markers — that's workflow logic
- Session-to-conversation mapping is an MCP transport concern — stays here
- TreatWarningsAsErrors — zero warnings allowed
