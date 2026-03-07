# MCP Server - Agent Instructions

> **⚠️ IMPORTANT: READ ROOT INSTRUCTIONS FIRST**
>
> Before following these project-specific instructions, you **MUST** read the root
> [`/AGENTS.md`](../../AGENTS.md) file for repo-wide constraints that apply to ALL code in this repository.

---

The MCP Server exposes AI research capabilities as MCP tools via HTTP Streaming transport. Currently a **thin wrapper** over `AIResearchAgents.Core` (Phase 1a). In Phase 1b, it will host the Microsoft Agent Framework orchestrator and local agent definitions directly.

## Architecture Rules

> **Phase 1a (current):** Thin wrapper — delegates all research logic to `AIResearchAgents.Core`.
>
> **Phase 1b (upcoming):** Will add Agent Framework orchestrator, local agent definitions, and hand-off workflows directly in this project. Foundry-level research logic stays in Core; orchestration logic lives here.

- Foundry agent creation, Azure SDK calls, and research execution belong in `AIResearchAgents.Core` — not here
- Do NOT instantiate `AIProjectClient` or Azure SDK clients directly — use `AIToolsResearchAgentRunner.CreateFromEnvironment()`
- **File I/O is NOT a concern for the MCP Server** — the MCP protocol returns `FormattedOutput` directly to the client
- No `forceNewVersion` flag — always uses existing agent (or creates on first use)
- Add new MCP tools by creating new `[McpServerToolType]` classes in the `Tools/` folder — do NOT modify `Program.cs`

## Credential Rule

- The MCP Server registers `DefaultAzureCredential` as a singleton `TokenCredential` in DI
- The tool class resolves `TokenCredential` from DI and passes it to `CreateFromEnvironment()`
- Runner is created per-request because `customTopic` varies per invocation

## MCP SDK Conventions

- Tool registration: Attribute-based with `[McpServerToolType]` on the class, `[McpServerTool]` on methods, `[Description]` for metadata
- Auto-discovery: `.WithToolsFromAssembly()` in Program.cs — no manual tool registration needed
- DI injection: The MCP SDK injects `IServiceProvider` as the first method parameter

## Output Formatting Rules

- Tool output is prefixed with `[INSTRUCTIONS FOR AI ASSISTANT]` + `---` separator — tells consuming LLMs to display the research verbatim
- Citation links toggle: `AgenticTrends:IncludeCitationLinks` in `appsettings.json` — when off, `StripMarkdownLinks()` removes URLs; when on, a URL disclaimer is appended
- Do NOT change the instruction prefix without testing across Claude, Copilot, and ChatGPT clients

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point: DI setup, MCP server config with HTTP transport |
| `Infrastructure.cs` | Serilog logging config (console + file), exception categorization |
| `Tools/AgenticTrendsTools.cs` | `[McpServerToolType]` with `get_agentic_trends` tool |
| `appsettings.json` | Logging levels and Kestrel URL (port 3001) |

## Commands

| Usage | Description |
|-------|-------------|
| `dotnet run --project src/AIResearchAgents.MCPServer` | Start the MCP server |

**Environment variables required** (read by `AIResearchAgents.Core` at runtime — must be set in whatever process hosts the server):

| Variable | Description | Example |
|----------|-------------|---------|
| `PROJECT_ENDPOINT` | Azure AI Foundry project endpoint URL | `https://my-project.openai.azure.com` |
| `MODEL_DEPLOYMENT_NAME` | Azure OpenAI model deployment name | `my-name-gpt-4o-model` |
| `BING_CONNECTION_NAME` | Bing connection name in Azure AI Foundry | `my-name-bing-grounded-search` |

## Testing

Run MCP Server tests only:

```bash
dotnet test tests/AIResearchAgents.MCPServer.Tests/
```

> **⚠️ IMPORTANT: When developing the MCP Server, only run MCP Server and Core tests.**
> Do NOT run CLI tests (`tests/AIResearch.CLI.Tests/`) — the CLI integration tests execute the actual CLI binary, which triggers real Azure agent calls and generates `research_summary_*.md` report files in the working directory.
>
> **Correct test scope for MCP Server development:**
> ```bash
> dotnet test tests/AIResearchAgents.MCPServer.Tests/ && dotnet test tests/AIResearchAgents.Core.Tests/
> ```
> **Do NOT use** `dotnet test AIResearch.slnx` — that includes CLI tests.
