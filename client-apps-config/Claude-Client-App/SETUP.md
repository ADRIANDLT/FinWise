# Claude Desktop — MCP Server Configuration

## Overview

Claude Desktop supports MCP servers via a JSON config file (`claude_desktop_config.json`). It **only supports stdio transport** in the config file — servers must be launched as subprocesses using `command` + `args`.

For **local HTTP MCP servers** (like FinWise), you must use the [`mcp-remote`](https://www.npmjs.com/package/mcp-remote) npm package as a stdio-to-HTTP bridge. The `url` property is **not supported** in `claude_desktop_config.json`.

For **remote MCP servers** accessible over the internet (not localhost), Claude also supports adding them through the UI as **Custom Connectors** (Settings → Connectors → Add custom connector). Note that Custom Connectors route through Anthropic's cloud infrastructure, so the server must be publicly reachable.

---

## Connection Scenarios

### Scenario 1: Local Dev/Test — FinWise MCP Server on your machine

> **For developers** running the FinWise MCP Server locally during development and testing.

**Problem:** Claude Desktop's config file only supports stdio transport, but FinWise runs as an HTTP server on `localhost:5000`.

**Solution:** Use the `mcp-remote` bridge — a stdio subprocess that forwards JSON-RPC messages to the local HTTP server.

```
Claude Desktop  ←stdio→  mcp-remote (bridge)  ←HTTP→  FinWise MCP Server
                          npx subprocess              http://localhost:5000/mcp
```

**Config** (`claude_desktop_config.json`):

```json
{
    "mcpServers": {
        "FinWise-MCP-Server-Local": {
            "command": "npx",
            "args": [
                "-y",
                "mcp-remote",
                "http://localhost:5000/mcp",
                "--allow-http"
            ]
        }
    }
}
```

**Prerequisites:**
- Node.js 18+ installed (`npx` on PATH)
- FinWise MCP Server running locally:
  ```powershell
  dotnet run --project src/FinWise.McpServer/ --urls http://localhost:5000
  ```
  Or via Docker Compose:
  ```powershell
  docker compose up -d
  ```

**Why `--allow-http`?** By default, `mcp-remote` blocks non-HTTPS connections for security. Since local dev uses `http://`, the `--allow-http` flag is required. It must come **after** the URL.

#### Why is the `mcp-remote` Bridge Needed?

The MCP specification defines two standard transport mechanisms:

1. **stdio** — The client launches the MCP server as a subprocess and communicates via `stdin`/`stdout` using JSON-RPC messages.
2. **Streamable HTTP** — The server runs as an independent HTTP process. The client sends JSON-RPC messages via HTTP POST requests, and the server can optionally stream responses using Server-Sent Events (SSE).

**Claude Desktop's `claude_desktop_config.json` only supports the stdio transport.** Each server entry must specify a `command` (an executable to launch as a subprocess) and `args`. There is no `url`, `type: "http"`, or equivalent property — the config parser simply doesn't recognize them. If you add a `url` property, Claude will reject the entry with the error: _"Some MCP servers could not be loaded... are not valid MCP server configurations and were skipped."_

However, FinWise MCP Server (and many modern MCP servers) uses **Streamable HTTP transport** — it runs as a standalone HTTP server at `http://localhost:5000/mcp`, not as a subprocess that reads from stdin. This creates a transport mismatch that `mcp-remote` solves by acting as a protocol bridge.

The [`mcp-remote`](https://github.com/geelen/mcp-remote) package (1.4k+ GitHub stars, 300k+ weekly npm downloads) is launched by Claude Desktop as a stdio subprocess, and it translates the JSON-RPC messages over the wire to HTTP requests against the real MCP server. It also handles OAuth flows for authenticated remote servers, though FinWise doesn't require this.

#### Why doesn't Claude Desktop support HTTP directly?

As stated in the `mcp-remote` README: _"Most [MCP clients] are stdio-only, and those that do support HTTP+SSE don't yet support the OAuth flows required."_ Claude Desktop was originally designed to launch local MCP servers as subprocesses for a simple trust model — both client and server run under the user's permissions, secrets stay in environment variables, and `npx`/`uvx` avoid explicit install steps. Support for HTTP connections in the config file has not been added as of April 2026. Remote servers are instead handled through the **Custom Connectors** UI (which routes through Anthropic's infrastructure and therefore cannot reach `localhost`).

#### Key points

| What | Works? | Why |
|------|--------|-----|
| `"command": "npx", "args": ["mcp-remote", ...]` | ✅ | stdio transport — Claude launches subprocess |
| `"url": "http://localhost:5000/mcp"` | ❌ | `url` property not recognized by Claude Desktop's config parser |
| Custom Connectors UI (Settings → Connectors) | ❌ for localhost | Routes through Anthropic's cloud — can't reach `localhost` |

#### Other clients that DO support HTTP directly

Not all MCP clients have this limitation. VS Code, Cursor, and Windsurf all support the `url` property (or `type: "http"`) natively in their config files, connecting directly to HTTP MCP servers without a bridge.

---

### Scenario 2: Azure via `mcp-remote` Bridge — FinWise MCP Server in Azure Container Apps

> **For developers** using Claude Desktop to connect to the FinWise MCP Server deployed in Azure Container Apps.

**Problem:** Same as Scenario 1 — Claude Desktop's config file only supports stdio transport. But the server is running in Azure, not locally.

**Solution:** Use the `mcp-remote` bridge pointing to the Azure HTTPS endpoint. No `--allow-http` needed since Azure uses HTTPS.

```
Claude Desktop  ←stdio→  mcp-remote (bridge)  ←HTTPS→  FinWise MCP Server (Azure)
                          npx subprocess                https://finwise-mcp-server-container-app.....azurecontainerapps.io/mcp
```

**Config** (`claude_desktop_config.json`):

```json
{
    "mcpServers": {
        "FinWise-MCP-Server-Azure": {
            "command": "npx",
            "args": [
                "-y",
                "mcp-remote",
                "https://finwise-mcp-server-container-app.whitesky-2a351661.eastus2.azurecontainerapps.io/mcp"
            ]
        }
    }
}
```

**Prerequisites:**
- Node.js 18+ installed (`npx` on PATH)
- FinWise MCP Server deployed and running in Azure Container Apps

**Note:** No `--allow-http` flag needed — the Azure endpoint uses HTTPS.

---

### Scenario 3: Azure via Custom Connectors UI — End-Users

> **For end-users** connecting to a FinWise MCP Server that is publicly hosted on Azure (or any cloud provider).

**No bridge needed.** Claude connects directly to the public HTTPS endpoint using **Custom Connectors** — a built-in Claude feature for remote MCP servers.

```
Claude  ←Streamable HTTP→  FinWise MCP Server
  (via Anthropic's cloud)    https://finwise-mcp.azurewebsites.net/mcp
```

**How to set it up:**

1. Deploy FinWise MCP Server to Azure with a public HTTPS URL (e.g., Azure App Service, Azure Container Apps).
2. In Claude, navigate to **Customize → Connectors** (or **Organization settings → Connectors** for Team/Enterprise).
3. Click **Add custom connector** and enter the public URL (e.g., `https://finwise-mcp.azurewebsites.net/mcp`).
4. If your server requires authentication, configure OAuth Client ID and Client Secret in **Advanced settings**.
5. The connector is now available in your conversations via the **"+" → Connectors** menu.

**Important:** Custom Connector connections originate from **Anthropic's cloud infrastructure**, not from the user's machine. This is true across all Claude clients — claude.ai, Claude Desktop, Cowork, and mobile apps. This is why Custom Connectors can reach a public Azure endpoint but cannot reach `localhost`.

**Requirements for the Azure-deployed server:**

| Requirement | Details |
|-------------|---------|
| **Public HTTPS URL** | Must be reachable over the public internet from Anthropic's IP ranges |
| **HTTPS (not HTTP)** | Custom Connectors require HTTPS. Azure App Service provides this by default |
| **Anthropic IP allowlist** | If behind a firewall, allowlist [Anthropic's IP addresses](https://platform.claude.com/docs/en/api/ip-addresses) |
| **Auth (optional)** | Claude supports authless servers and OAuth (DCR, static client credentials). OAuth callback URL: `https://claude.ai/api/mcp/auth_callback` |
| **Plan** | Available on Free (1 connector limit), Pro, Max, Team, and Enterprise plans (beta) |

---

### Comparison

| | Scenario 1: Local Dev/Test | Scenario 2: Azure via Bridge | Scenario 3: End-User (Connectors) |
|---|---|---|---|
| **Who** | Developers | Developers | End-users |
| **Server location** | `http://localhost:5000/mcp` | Azure Container Apps (HTTPS) | Azure (HTTPS) |
| **Config method** | `claude_desktop_config.json` | `claude_desktop_config.json` | Claude UI (Connectors) |
| **Transport** | stdio → HTTP bridge | stdio → HTTPS bridge | Direct Streamable HTTP |
| **`mcp-remote` needed?** | ✅ Yes | ✅ Yes | ❌ No |
| **Node.js required?** | ✅ (for npx) | ✅ (for npx) | ❌ |
| **`--allow-http` flag?** | ✅ (localhost HTTP) | ❌ (Azure HTTPS) | N/A |
| **Connection origin** | Your local machine | Your local machine | Anthropic's cloud |

### References

- [MCP Transports Specification](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports) — Official spec for stdio and Streamable HTTP transports
- [`mcp-remote` GitHub](https://github.com/geelen/mcp-remote) — The bridge package README with detailed rationale
- [`mcp-remote` on npm](https://www.npmjs.com/package/mcp-remote) — Package details (v0.1.38, MIT license)
- [Get started with Custom Connectors](https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp) — Claude's remote MCP server UI
- [Build custom connectors via remote MCP servers](https://support.claude.com/en/articles/11503834-build-custom-connectors-via-remote-mcp-servers) — Technical requirements for hosted servers

## Config File Location

| OS | Path |
|----|------|
| **Windows** | `%APPDATA%\Claude\claude_desktop_config.json` |
| **macOS** | `~/Library/Application Support/Claude/claude_desktop_config.json` |

### Windows Full Path Example

```
C:\Users\<YourUsername>\AppData\Roaming\Claude\claude_desktop_config.json
```

## How to Install

### Option A — Via Claude Desktop Settings UI

1. Open **Claude Desktop**.
2. Click the **Claude menu** in the system menu bar (not the in-window settings).
3. Select **Settings…**
4. Navigate to the **Developer** tab in the left sidebar.
5. Click **Edit Config** to open the config file.
6. Paste the contents of the provided `claude_desktop_config.json` file.
7. Save the file and **restart Claude Desktop**.

### Option B — Manual File Copy

1. Copy the `claude_desktop_config.json` file from this folder.
2. Place it at the path shown above for your OS.
   - On Windows: `%APPDATA%\Claude\claude_desktop_config.json`
   - On macOS: `~/Library/Application Support/Claude/claude_desktop_config.json`
3. If the file already exists, **merge** the `mcpServers` entries rather than overwriting.
4. **Restart Claude Desktop** completely (quit and relaunch).

## Configuration Explained

| Server | Transport | Description |
|--------|-----------|-------------|
| `FinWise-MCP-Server-Azure` | stdio via `mcp-remote` bridge | FinWise MCP Server in Azure Container Apps. Uses `mcp-remote` to bridge stdio→HTTPS |
| `DISABLED-FinWise-MCP-Server-Local` | stdio via `mcp-remote` bridge | FinWise MCP Server running locally on port 5000. Disabled by prefix — rename to enable |

> **Switching between servers:** Claude Desktop's JSON doesn't support comments. To switch, rename the entries — add `DISABLED-` prefix to disable, remove it to enable. Only have one active FinWise server at a time to avoid routing conflicts.

### Important Notes

- **Node.js is required** — `npx` must be available on your PATH for the `mcp-remote` bridge to work.
- **Only one FinWise server at a time** — If both local and Azure entries are active, Claude may route to either. Disable one by adding the `DISABLED-` prefix.
- **For local dev**, FinWise MCP Server must be running before Claude Desktop can use it. Start it with:
  ```powershell
  dotnet run --project src/FinWise.McpServer/ --urls http://localhost:5000
  ```
  Or via Docker Compose:
  ```powershell
  docker compose up -d
  ```
- **`--allow-http`** flag is required because FinWise runs on `http://` (not `https://`). The flag must come **after** the URL.
- **`url` property does not work** in `claude_desktop_config.json`. Claude Desktop only supports stdio transport in the config file. Use `mcp-remote` as the bridge.

## Verification

After restarting Claude Desktop:
1. Look for the **MCP server indicator** (hammer icon) in the bottom-right corner of the chat input box.
2. Click it to see the available tools from connected MCP servers.
3. If a server doesn't appear, check **Settings → Developer** for error logs.

## Troubleshooting

- **Server not showing up**: Ensure the config JSON is valid (no trailing commas, correct syntax). Restart Claude Desktop completely (quit from system tray, not just close the window).
- **"Not valid MCP server configurations"**: You likely have a `url` property in the config. Claude Desktop only supports stdio (`command` + `args`). Use `mcp-remote` as described above.
- **`ERR_INVALID_URL` with `--allow-http`**: The `--allow-http` flag must come **after** the URL in args, not before it. `mcp-remote` treats the first non-flag argument as the URL.
- **Connection refused for FinWise**: Make sure the FinWise MCP Server is running on `http://localhost:5000`.
- **Node.js / npx not found**: Claude Desktop needs Node.js 18+ on your system PATH. Verify with `node --version`.
- **Stale credentials**: If `mcp-remote` has persistent auth issues, clear its cache: `rm -rf ~/.mcp-auth` (or `Remove-Item -Recurse ~\.mcp-auth` on PowerShell).

## References

- [Claude Desktop MCP Setup Guide](https://modelcontextprotocol.io/quickstart/user)
- [Local MCP Servers on Claude Desktop](https://support.claude.com/en/articles/10949351-getting-started-with-local-mcp-servers-on-claude-desktop)
- [Remote MCP Servers (Custom Connectors)](https://support.claude.com/en/articles/11175166-get-started-with-custom-connectors-using-remote-mcp)
- [MCP Transports Specification](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports)
- [`mcp-remote` GitHub](https://github.com/geelen/mcp-remote)
