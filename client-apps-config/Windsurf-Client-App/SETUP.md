# Windsurf — MCP Server Configuration

## Overview

[Windsurf](https://codeium.com/windsurf) (by Codeium) is an agentic AI IDE with MCP support for tools and tool discovery. It supports both stdio and HTTP transports. Windsurf uses a `mcp.json` configuration file.

## Config File Location

| Scope | Path |
|-------|------|
| **Global** | `~/.codeium/windsurf/mcp_config.json` |

### Windows

```
C:\Users\<YourUsername>\.codeium\windsurf\mcp_config.json
```

### macOS

```
~/.codeium/windsurf/mcp_config.json
```

> **Note**: Windsurf may also support project-level `.windsurf/mcp.json` files. Check the latest [Windsurf MCP docs](https://docs.windsurf.com/windsurf/cascade/mcp) for updates.

## How to Install

### Option A — Via Windsurf Settings UI

1. Open **Windsurf**.
2. Open the Command Palette (Ctrl+Shift+P / Cmd+Shift+P).
3. Search for **"MCP"** or navigate to **Settings → MCP**.
4. Add each MCP server via the UI.

### Option B — Manual File Copy

1. Create the config directory if it doesn't exist:

```powershell
# Windows
mkdir "$HOME\.codeium\windsurf" -Force
Copy-Item config\Windsurf-Client-App\mcp.json "$HOME\.codeium\windsurf\mcp_config.json"
```

```bash
# macOS/Linux
mkdir -p ~/.codeium/windsurf
cp config/Windsurf-Client-App/mcp.json ~/.codeium/windsurf/mcp_config.json
```

2. Restart Windsurf to load the new configuration.

## Configuration Notes

| Server | Transport | Notes |
|--------|-----------|-------|
| `FinWise-MCP-Server` | HTTP | Must be running locally on port 5000 |
| `github` | HTTP | GitHub Copilot MCP endpoint |
| `microsoft-learn` | HTTP | Microsoft Learn docs |
| `perplexity-ask` | stdio (Docker) | Replace `YOUR_PERPLEXITY_API_KEY_HERE` with your key |

## Verification

1. Open Windsurf and check that MCP servers appear in the Cascade panel.
2. MCP tools should be available during AI-assisted coding.
3. Verify FinWise tools are listed and accessible.

## Troubleshooting

- Ensure the JSON is valid and the file is in the correct location.
- If using Windsurf's Cascade agent, MCP tools are available in agent mode.
- Restart Windsurf after any config changes.
- Ensure Docker is running for the perplexity-ask server.

## References

- [Windsurf MCP Guide](https://docs.windsurf.com/windsurf/cascade/mcp)
- [Windsurf MCP Video Tutorial](https://windsurf.com/university/tutorials/configuring-first-mcp-server)
