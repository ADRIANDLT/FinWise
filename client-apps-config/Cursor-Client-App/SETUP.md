# Cursor — MCP Server Configuration

## Overview

[Cursor](https://cursor.com) is an AI code editor with built-in MCP support. It uses a `mcp.json` configuration file similar to VS Code. Cursor supports tools, prompts, roots, elicitation, and both stdio and HTTP (SSE/Streamable HTTP) transports.

## Config File Location

Cursor supports **two levels** of MCP configuration:

| Scope | Path | Description |
|-------|------|-------------|
| **Project-level** | `<project-root>/.cursor/mcp.json` | Specific to the current project |
| **Global** | `~/.cursor/mcp.json` | Applies to all projects |

### Windows Paths

| Scope | Path |
|-------|------|
| **Project-level** | `<project-root>\.cursor\mcp.json` |
| **Global** | `C:\Users\<YourUsername>\.cursor\mcp.json` |

## How to Install

### Option A — Project-Level (Recommended)

1. Create a `.cursor/` folder in the FinWise project root (if it doesn't exist).
2. Copy the provided `mcp.json` file to `.cursor/mcp.json`.
3. Restart Cursor or reload the window.

```powershell
# From the FinWise project root:
mkdir .cursor -Force
Copy-Item config\Cursor-Client-App\mcp.json .cursor\mcp.json
```

### Option B — Global Configuration

1. Copy `mcp.json` to your home directory's `.cursor/` folder:

```powershell
# Windows
mkdir "$HOME\.cursor" -Force
Copy-Item config\Cursor-Client-App\mcp.json "$HOME\.cursor\mcp.json"
```

```bash
# macOS/Linux
mkdir -p ~/.cursor
cp config/Cursor-Client-App/mcp.json ~/.cursor/mcp.json
```

## Configuration Notes

| Server | Transport | Notes |
|--------|-----------|-------|
| `FinWise-MCP-Server` | HTTP | Must be running locally on port 5000 |
| `github` | HTTP | GitHub Copilot MCP endpoint |
| `microsoft-learn` | HTTP | Microsoft Learn docs |
| `perplexity-ask` | stdio (Docker) | Replace `YOUR_PERPLEXITY_API_KEY_HERE` with your key |

## Verification

1. Open Cursor and load the FinWise project.
2. Open **Cursor Composer** (Cmd+I / Ctrl+I).
3. MCP tools should be available in agent mode.
4. Check **Settings → MCP** to see connected servers and their status.

## Troubleshooting

- Ensure the JSON file is valid (no trailing commas, no comments).
- If servers don't appear, restart Cursor.
- Check that the FinWise server is running: `curl http://localhost:5000/mcp`

## References

- [Cursor MCP Documentation](https://docs.cursor.com/context/model-context-protocol)
- [MCP Protocol Specification](https://modelcontextprotocol.io/docs/concepts/transports)
