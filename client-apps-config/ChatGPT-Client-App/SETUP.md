# ChatGPT — MCP Server Configuration

## Overview

ChatGPT uses **Developer Mode** to connect to remote MCP servers. Unlike VS Code or Claude Desktop, ChatGPT does **not** use a local JSON config file. All MCP server connections are configured through the **ChatGPT web UI**.

> **Important**: ChatGPT only supports **remote MCP servers** over **HTTPS** (SSE and Streamable HTTP). It cannot connect to `localhost` or `http://` URLs. If you try to add `http://localhost:5000/mcp`, ChatGPT will reject it with: **"Error creating connector Unsafe URL"**.

---

## Connection Scenarios

### Scenario 1: Local Dev/Test — FinWise MCP Server on your machine

> **For developers** testing the FinWise MCP Server locally during development.

**Problem:** ChatGPT connects to MCP servers from **OpenAI's cloud infrastructure**, not from your browser or local machine. This means:
- `localhost` URLs are unreachable (OpenAI's servers can't reach your machine)
- `http://` URLs are rejected as "Unsafe URL" — ChatGPT requires HTTPS

**Solution:** Use a **tunnel** to expose your local server with a public HTTPS URL.

#### Option A — ngrok (Recommended for Dev)

1. Install ngrok: https://ngrok.com/download
2. Start the FinWise MCP Server locally:
   ```powershell
   dotnet run --project src/FinWise.McpServer/ --urls http://localhost:5000
   ```
3. Start the tunnel:
   ```bash
   ngrok http 5000
   ```
4. Copy the generated HTTPS URL (e.g., `https://a1b2c3d4.ngrok-free.app`)
5. In ChatGPT, create an app with the URL: `https://a1b2c3d4.ngrok-free.app/mcp`

> **Note:** The ngrok URL changes each time you restart the tunnel (unless you have a paid ngrok plan with a static domain). You'll need to update the ChatGPT app URL after each restart.

#### Option B — Cloudflare Tunnel

```bash
cloudflared tunnel --url http://localhost:5000
```

Copy the generated `https://` URL and use it in ChatGPT with `/mcp` appended.

#### Option C — VS Code Ports (Forward a Port)

VS Code can forward local ports and generate a public URL:
1. Open the **Ports** panel in VS Code (Terminal → Ports).
2. Forward port `5000`.
3. Set visibility to **Public**.
4. Use the generated `https://` URL with `/mcp` appended in ChatGPT.

---

### Scenario 2: End-User — FinWise MCP Server deployed to Azure

> **For end-users** connecting to a FinWise MCP Server that is publicly hosted on Azure (or any cloud provider).

**No tunnel needed.** ChatGPT connects directly to the public HTTPS endpoint.

1. Deploy FinWise MCP Server to Azure with a public HTTPS URL.
2. In ChatGPT, create an app with the URL (e.g., `https://finwise-mcp.azurewebsites.net/mcp`).

---

### Comparison

| | Scenario 1: Local Dev/Test | Scenario 2: End-User (Azure) |
|---|---|---|
| **Who** | Developers | End-users |
| **Server URL** | `https://xxxx.ngrok-free.app/mcp` (tunnel) | `https://finwise-mcp.azurewebsites.net/mcp` |
| **Tunnel required?** | ✅ Yes (ngrok, Cloudflare, etc.) | ❌ No |
| **HTTPS required?** | ✅ Yes (tunnel provides it) | ✅ Yes (Azure provides it) |
| **Connection origin** | OpenAI's cloud infrastructure | OpenAI's cloud infrastructure |

---

## Prerequisites

- **ChatGPT Plan**: Pro, Plus, Business, Enterprise, or Education.
- **Developer Mode** enabled (beta feature).
- **FinWise MCP Server** running and accessible via a **public HTTPS URL** (see scenarios above).

## Step-by-Step Setup

### 1. Enable Developer Mode

1. Go to [chatgpt.com](https://chatgpt.com/).
2. Navigate to **Settings → Apps**.
3. Click **Advanced settings**.
4. Enable **Developer mode**.

Direct link: [Settings → Apps → Advanced settings → Developer mode](https://chatgpt.com/#settings/Connectors/Advanced)

### 2. Create an App for FinWise MCP Server

1. Go to [ChatGPT Apps settings](https://chatgpt.com/#settings/Connectors).
2. Click **"Create app"** (only visible when Developer Mode is enabled).
3. Enter the following:
   - **Name**: `FinWise MCP Server`
   - **Server URL**: Your FinWise MCP server HTTPS URL (see [Connection Scenarios](#connection-scenarios) above)
   - **Authentication**: No Authentication (for local development)
4. Click **Add**. The app will appear under **"Drafts"**.

### 3. (Optional) Add Other MCP Servers

Repeat step 2 for any additional MCP servers:

| Server | URL | Auth |
|--------|-----|------|
| Microsoft Learn | `https://learn.microsoft.com/api/mcp` | No Auth |

> **Note**: The GitHub Copilot MCP (`https://api.githubcopilot.com/mcp`) is designed for GitHub Copilot clients and may not work directly from ChatGPT.

### 4. Use Apps in Conversations

1. Start a new chat on ChatGPT.
2. Click the **"+" menu** in the composer.
3. Select **Developer mode**.
4. Choose the FinWise MCP Server app.
5. Ask ChatGPT to use the FinWise tools (be explicit in your prompts).

### Prompting Tips

ChatGPT works best with explicit tool references:

```
Use the "FinWise MCP Server" tools to help me with investment advice.
Do not use built-in browsing or other tools; only use the FinWise connector.
```

### 5. Managing Tools

- Go to **Settings → Apps** and click on your FinWise app.
- Toggle individual tools on/off.
- Click **Refresh** to pull updated tool definitions from the MCP server.

## Security Considerations

- **Write actions require confirmation** by default. Review tool inputs before approving.
- ChatGPT respects the `readOnlyHint` tool annotation. Tools without this hint are treated as write actions.
- For production use, configure **OAuth authentication** on your MCP server.

## References

- [ChatGPT Developer Mode](https://developers.openai.com/api/docs/guides/developer-mode)
- [MCP and Connectors (OpenAI API)](https://developers.openai.com/api/docs/guides/tools-connectors-mcp)
- [MCP Risks and Safety](https://developers.openai.com/api/docs/mcp)
