# 🤖 Agentic Trends Guru — MCP Server

> **Your AI-powered scout for the latest viral AI development tool trends — delivered through the Model Context Protocol.**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=.net)](https://dotnet.microsoft.com/)
[![MCP](https://img.shields.io/badge/MCP-1.0-00ADD8?logo=protocol)](https://modelcontextprotocol.io/)
[![Azure](https://img.shields.io/badge/Azure-Foundry-0078D4?logo=microsoftazure)](https://azure.microsoft.com/)

---

## 🌟 What is this?

The **Agentic Trends Guru MCP Server** wraps the `AIResearchAgents.Core` library as a Model Context Protocol (MCP) server, exposing an **Azure Foundry Bing Grounded Search agent** as an MCP tool. This agent delivers curated weekly digests of viral AI development tool trends — from AI coding assistants and AI IDEs to emerging workflows like vibe coding and parallel code generation.

**Consumable from:**
- 🎨 **Claude Desktop** (Anthropic)
- 💬 **ChatGPT** (OpenAI)
- 🐙 **GitHub Copilot** (VS Code extension)
- 🖥️ **GitHub Copilot CLI** (command line)

The MCP Server acts as a bridge between your favorite AI assistant and the Azure Foundry agent, which performs real-time web research using Bing Grounded Search to surface the freshest AI dev tool news and trends from the last 7 days.

---

## 🚀 Quick Start

### Prerequisites

Before you begin, ensure you have:

- ✅ **Azure subscription** with an Azure AI Foundry project configured
- ✅ **.NET 10 SDK** installed ([Download](https://dotnet.microsoft.com/download))
- ✅ **Environment variables** configured (see [Configuration](#-configuration) below)
- ✅ **Azure authentication** set up (uses `DefaultAzureCredential`)

### 1. Configure Environment Variables

Set the following environment variables (required by `AIResearchAgents.Core` at runtime):

```powershell
# PowerShell example
$env:PROJECT_ENDPOINT = "https://your-project.openai.azure.com"
$env:MODEL_DEPLOYMENT_NAME = "your-gpt-4o-model-deployment-name"
$env:BING_CONNECTION_NAME = "your-bing-grounded-search"
```

> 💡 **Tip**: These can be set at the system, user, or process level. The Core library reads them in that order.

### 2. Start the MCP Server

From the repository root, run:

```bash
dotnet run --project src/AIResearchAgents.MCPServer
```

**Expected output:**

```
[INF] Agentic Trends Guru MCP Server ready — endpoint: http://127.0.0.1:3001/mcp
```

The server is now listening on `http://127.0.0.1:3001/mcp` and ready to accept MCP client connections.

### 3. Connect from VS Code (GitHub Copilot)

Add this configuration to your `.vscode/mcp.json`:

```json
{
    "servers": {
        "agentic-trends-guru": {
            "type": "http",
            "url": "http://127.0.0.1:3001/mcp"
        }
    }
}
```

> ⚠️ **Important**: The server uses **HTTP transport**, so you must start it manually before connecting your MCP client. The client won't auto-start the server process.

---

## 🔧 Configuration

### Environment Variables

The MCP Server delegates to the `AIResearchAgents.Core` library, which reads these environment variables at runtime:

| Variable | Description | Example |
|----------|-------------|---------|
| `PROJECT_ENDPOINT` | Azure AI Foundry project endpoint URL | `https://my-project.openai.azure.com` |
| `MODEL_DEPLOYMENT_NAME` | Azure OpenAI model deployment name | `gpt-4o` |
| `BING_CONNECTION_NAME` | Bing connection name configured in Azure AI Foundry | `bing-grounded-search` |

These configure the Azure Foundry connection within the MCP Server process. The server passes these through to the Core library, which uses them to connect to your Azure Foundry agent.

### Port Configuration

The default port is **3001**, configurable in `appsettings.json`:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://127.0.0.1:3001"
      }
    }
  }
}
```

### Citation Links

By default, Bing Grounded Search citation URLs are **stripped** from the MCP tool response. The URLs point to real pages but the AI-generated bullet content may not accurately reflect what's on the linked page. To include citation links (with a disclaimer), set `IncludeCitationLinks` to `true` in `appsettings.json`:

```json
{
  "AgenticTrends": {
    "IncludeCitationLinks": true
  }
}
```

| Value | Behavior |
|-------|----------|
| `false` (default) | Citation links and stray brackets stripped. Clean text-only output. |
| `true` | Citation links included as `[Title](url)` markdown. A disclaimer is appended noting URLs are from Bing and content is AI-generated. |

---

## 🛠️ MCP Tools

The server exposes the following MCP tools:

### `get_agentic_trends`

**Researches the latest viral AI development tool trends from the past 7 days.**

| Aspect | Details |
|--------|---------|
| **Description** | Returns a curated digest of product announcements, releases, and emerging workflows for AI coding assistants, AI IDEs, code generation tools, and AI-powered development practices. |
| **Parameters** | • `query` (optional string): Focused research query. When omitted, returns a comprehensive weekly digest. When provided, focuses on the specified topic (must be AI dev tools related). |
| **Response Format** | Rich **Markdown** with emojis, sections, bullet points, and bold text. Citation links are included or stripped based on the `AgenticTrends:IncludeCitationLinks` setting (default: off). Designed to be presented directly to the user without reformatting. |
| **Typical Response Time** | **15-30 seconds** (performs live web research via Bing Grounded Search) |
| **Output Structure** | Two sections: **🔥 Product News & Viral Trends** (5-6 items) and **🔄 Emerging Processes & Workflows** (5-6 items). Each item includes the tool/trend name, what happened, and the exact date. Citation links are included when `IncludeCitationLinks` is enabled. |

**Example usage (from Claude/Copilot):**

```
What are the latest AI dev tool trends?
```

The MCP client invokes `get_agentic_trends` with no query, and the agent returns a comprehensive weekly digest.

**Example focused query:**

```
What's new with GitHub Copilot this week?
```

The MCP client invokes `get_agentic_trends` with `query="GitHub Copilot"`, and the agent returns focused research on that topic.

---

## 🚀 Deploying Agent Updates

When you modify agent instructions or prompts in the code (e.g., changing the default research topic or agent instructions in `AIToolsResearchAgentConfig.cs`), you need to deploy a new agent version to Azure Foundry.

### When to Use

- After changing agent instructions/prompts in `AIToolsResearchAgentConfig.cs`
- After modifying the default research topic
- When you want to create a new versioned agent in Azure Foundry

### How to Deploy

**Option 1: Via CLI flag**

```bash
dotnet run --project src/AIResearchAgents.MCPServer -- --deploy-agent
```

**Option 2: Via environment variable**

```powershell
$env:DEPLOY_AGENTS = "true"
dotnet run --project src/AIResearchAgents.MCPServer
```

**Option 3: Via direct executable**

```bash
AIResearchAgents.MCPServer.exe --deploy-agent
```

### What It Does

1. **Creates a new agent version** in Azure Foundry at startup
2. **Logs the deployment**: `🚀 Deploy mode: Creating new agent version in Azure Foundry...`
3. **Serves MCP requests normally** after deployment completes

> 💡 **Tip**: After deploying, the server continues running and serves MCP requests as usual. You don't need to restart it again.

---

## 📡 MCP Client Configuration

The MCP Server uses **HTTP transport** and requires manual startup. Here's how to configure different clients:

### VS Code / GitHub Copilot

Add to `.vscode/mcp.json`:

```json
{
    "servers": {
        "agentic-trends-guru": {
            "type": "http",
            "url": "http://127.0.0.1:3001/mcp"
        }
    }
}
```

### Claude Desktop

Add to `~/Library/Application Support/Claude/claude_desktop_config.json` (macOS) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows):

```json
{
    "mcpServers": {
        "agentic-trends-guru": {
            "type": "http",
            "url": "http://127.0.0.1:3001/mcp"
        }
    }
}
```

### ChatGPT / GitHub Copilot CLI

Configuration varies by client. Consult the client's MCP integration documentation for HTTP transport setup.

> ⚠️ **Important**: The server must be started manually before connecting your MCP client. HTTP transport doesn't support auto-start.

---

## 🏗️ Architecture

The MCP Server acts as a protocol adapter between MCP clients and the Azure Foundry agent:

```
┌─────────────────────────────────────────┐
│  MCP Client                             │
│  (Claude / Copilot / ChatGPT)           │
└────────────────┬────────────────────────┘
                 │
                 │ MCP Protocol (HTTP)
                 ▼
┌─────────────────────────────────────────┐
│  AIResearchAgents.MCPServer             │
│  (this project)                         │
│  • HTTP transport                       │
│  • Tool discovery                       │
│  • Request routing                      │
└────────────────┬────────────────────────┘
                 │
                 │ delegates to
                 ▼
┌─────────────────────────────────────────┐
│  AIResearchAgents.Core                  │
│  • Agent orchestration                  │
│  • Configuration management             │
└────────────────┬────────────────────────┘
                 │
                 │ Azure SDK
                 ▼
┌─────────────────────────────────────────┐
│  Azure Foundry Agent                    │
│  (Bing Grounded Search)                 │
│  • Live web research                    │
│  • Trend analysis                       │
└─────────────────────────────────────────┘
```

**Key Components:**

- **MCP Server**: Protocol adapter exposing tools via MCP HTTP transport
- **Core Library**: Business logic for agent orchestration and configuration
- **Azure Foundry**: Managed agent runtime with Bing Grounded Search integration

---

## 📝 Logging

The MCP Server uses **Serilog** for structured logging. Logs are written to:

| Destination | Path / Details |
|-------------|----------------|
| **File** | `%LOCALAPPDATA%\AIResearchAgents-MCPServer\Logs\log.txt` |
| **Console** | Stdout (visible when running `dotnet run`) |
| **Rolling Policy** | Daily rolling, retained for 30 days |

**Example log entry:**

```
[INF] Agentic Trends Guru MCP Server ready — endpoint: http://127.0.0.1:3001/mcp
[INF] MCP tool invoked: get_agentic_trends (query: null)
[INF] Azure Foundry agent completed in 18.3s
```

> 💡 **Tip**: Check logs if the server fails to start or if tool invocations return errors.

---

## 🔮 Roadmap

### Phase 1a (Current) ✅
- ✅ MCP Server wrapping Azure Foundry agent
- ✅ HTTP transport with auto-discovery
- ✅ `get_agentic_trends` tool
- ✅ Agent deployment via CLI flag

### Phase 1b (Coming Soon) 🚧
- 🔜 **Microsoft Agent Framework orchestration** — multi-agent workflow directly in this project
- 🔜 Additional research agents (frameworks, hardware, runtimes)
- 🔜 Agent-to-agent orchestration for complex queries

### Phase 2 (Future) 🔮
- 🔮 SSE transport for streaming responses
- 🔮 Caching layer for faster repeat queries
- 🔮 Custom agent instructions per MCP client

---

## 🤝 Contributing

Contributions are welcome! Please:

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

---

## 📄 License

This project is licensed under the MIT License. See the [LICENSE](../../LICENSE) file for details.

---

## 🙏 Acknowledgments

- **Model Context Protocol** by Anthropic
- **Azure AI Foundry** for managed agent runtime
- **Bing Grounded Search** for real-time web research
- **.NET 10** and **Serilog** for robust infrastructure

---

**Built with ❤️ for AI developers by AI developers.**
