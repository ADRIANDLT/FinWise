# FinWise MCP Server — Client App Configurations

This folder contains MCP client configurations for connecting various AI apps to the FinWise MCP Server and other MCP servers used in this project.

## MCP Servers Configured

| Server | URL | Transport |
|--------|-----|-----------|
| **FinWise MCP Server** | `http://localhost:5000/mcp` | HTTP (Streamable HTTP) |
| **GitHub Copilot** | `https://api.githubcopilot.com/mcp` | HTTP |
| **Microsoft Learn** | `https://learn.microsoft.com/api/mcp` | HTTP |
| **Perplexity Ask** | Docker stdio | stdio |

## Client App Configurations

| Folder | App | Config Format | Notes |
|--------|-----|---------------|-------|
| [`Claude-Client-App/`](Claude-Client-App/) | Claude Desktop | `claude_desktop_config.json` | stdio-only config; uses [`mcp-remote`](https://github.com/geelen/mcp-remote) bridge for HTTP servers |
| [`ChatGPT-Client-App/`](ChatGPT-Client-App/) | ChatGPT (Web) | UI-based (no config file) | Requires Developer Mode; instructions only |
| [`Cursor-Client-App/`](Cursor-Client-App/) | Cursor IDE | `mcp.json` | Similar to VS Code format |
| [`Windsurf-Client-App/`](Windsurf-Client-App/) | Windsurf IDE | `mcp.json` | Codeium's agentic IDE |

> The **VS Code** configuration lives in the repo at [`.vscode/mcp.json`](../.vscode/mcp.json).

## Prerequisites

Before any client can use the FinWise MCP Server:

1. **Start the FinWise MCP Server** locally:
   ```powershell
   dotnet run --project src/FinWise.McpServer/ --urls http://localhost:5000
   ```
   Or via Docker Compose:
   ```powershell
   docker compose up -d
   ```

2. **Docker Desktop** must be running if using the Perplexity MCP server.

## Other Notable MCP Clients (Not Configured)

Based on research of the [MCP Clients ecosystem](https://modelcontextprotocol.io/clients) (111+ clients as of April 2026), these are additional clients worth considering:

| Client | Type | MCP Support | Why Consider |
|--------|------|-------------|--------------|
| **Claude Code** | CLI | Full (resources, prompts, tools, roots, elicitation) | Anthropic's CLI coding agent |
| **Cline** | VS Code Extension | Resources, tools, discovery | Popular open-source autonomous coding agent |
| **Amazon Q CLI** | CLI | Prompts, tools | AWS-integrated terminal AI assistant |
| **JetBrains AI Assistant** | IDE Plugin | Tools | For IntelliJ/Rider users |
| **Mistral Le Chat** | Web | Tools (remote) | Alternative AI assistant with MCP |
| **LM Studio** | Desktop App | Tools | For running local models with MCP tools |
| **Goose** | Desktop/CLI | Full | Open-source AI agent by Block |

### Clients That Do NOT Support MCP

- **Perplexity** (the chat app) — Not listed as an MCP client. Perplexity _provides_ an MCP server (perplexity-ask) but does not _consume_ MCP servers as a client.
- **Google Gemini** (web app) — No MCP client support (though Gemini CLI does).
- **Microsoft Copilot** (consumer) — No direct MCP client support (Copilot Studio does for enterprise).
