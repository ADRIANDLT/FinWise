# FinWise.McpServer — Project Instructions

Thin MCP server host. Transport + composition root.
Delegates all workflow logic to `FinWise.MultiAgentWorkflow`.

## Enforcements

- Program.cs is the composition root — manual DI, registers services for attribute-based tool injection
- MCP tools live in `Tools/` folder — attribute-based (`[McpServerToolType]`), auto-discovered via `WithToolsFromAssembly()`
- Tools are thin adapters: resolve services from `IServiceProvider` → call workflow service → return response
- `Infrastructure.cs` owns Azure OpenAI client creation (deployment/config decision, not domain logic)
- Session-to-conversation mapping (`McpSessionMapping`) stays here — MCP transport concern
- Never inspect PROFILE_READY markers or conversation content — that's workflow logic
- Store creation (CosmosDB config binding, session store creation) stays here — host concern. Pass `IUserProfileStore` and `IAgentSessionStore` to workflow.
- TreatWarningsAsErrors — zero warnings allowed

## Files

| File | Purpose |
|------|---------|
| `Program.cs` | Composition root: config, store creation, DI registration, `WithToolsFromAssembly()` |
| `Infrastructure.cs` | Azure OpenAI client factory, Serilog setup, error logging helper |
| `McpSessionMapping.cs` | Session-to-conversation mapping (wraps ConcurrentDictionary + session ID extraction) |
| `Tools/FinWiseTools.cs` | Attribute-based MCP tools: `run_finwise_workflow`, `reset_conversation` |
