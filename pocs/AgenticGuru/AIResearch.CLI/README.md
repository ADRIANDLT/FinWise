# 🤖 Agentic Trends Guru — CLI

> **Your AI-powered scout for the latest viral AI development tool trends — delivered straight to your terminal.**

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=.net)](https://dotnet.microsoft.com/)
[![Azure](https://img.shields.io/badge/Azure-Foundry-0078D4?logo=microsoftazure)](https://azure.microsoft.com/)

---

## 🌟 What is this?

The **Agentic Trends Guru CLI** is a terminal interface to the `AIResearchAgents.Core` library, providing instant access to an **Azure Foundry Bing Grounded Search agent** that delivers curated weekly digests of viral AI development tool trends — from AI coding assistants and AI IDEs to emerging workflows like vibe coding and parallel code generation.

**What it does:**
- 🔍 **Researches AI dev tool trends** from the last 7 days using live web search
- 💻 **Displays rich Markdown** directly in your terminal with emojis, sections, and clickable links
- 💾 **Saves results to a file** (`research_summary_YYYY-MM-DD.md`) for easy sharing and reference
- ⚡ **Runs in seconds** — get your weekly digest in 15-30 seconds
- 🎯 **Supports custom queries** — focus on specific tools or trends

This CLI is perfect for quick terminal research sessions, CI/CD integrations, or scripted weekly trend reports.

---

## 🚀 Quick Start

### Prerequisites

Before you begin, ensure you have:

- ✅ **Azure subscription** with an Azure AI Foundry project configured
- ✅ **.NET 10 SDK** installed ([Download](https://dotnet.microsoft.com/download))
- ✅ **Environment variables** configured (see [Configuration](#-configuration) below)
- ✅ **Azure CLI authenticated** (`az login` — required for `AzureCliCredential`)

### 1. Configure Environment Variables

Set the following environment variables (required by `AIResearchAgents.Core` at runtime):

```powershell
# PowerShell example
$env:PROJECT_ENDPOINT = "https://your-project.openai.azure.com"
$env:MODEL_DEPLOYMENT_NAME = "your-gpt-4o-model-deployment-name"
$env:BING_CONNECTION_NAME = "your-bing-grounded-search"
```

> 💡 **Tip**: These can be set at the system, user, or process level. The Core library reads them in that order.

### 2. Authenticate with Azure CLI

The CLI uses **`AzureCliCredential`** for fast local development. Ensure you're logged in:

```bash
az login
```

> ⚠️ **Important**: You must be authenticated with Azure CLI before running the CLI. This is the fastest auth method for local dev.

### 3. Run the CLI

From the repository root:

```bash
dotnet run --project src/AIResearch.CLI
```

**What happens:**

1. 🔄 **Spinner shows progress**: `⠋ Researching agentic trends... 12.3s`
2. ✅ **Completion message**: `✓ Research completed in 18.0s`
3. 📄 **Console output**: Full rich Markdown digest with emojis, sections, and links
4. 💾 **File saved**: `research_summary_2026-02-12.md` in the current directory

### 4. Try a Custom Query

Focus on a specific tool or trend:

```bash
dotnet run --project src/AIResearch.CLI -- "cursor AI latest features"
```

The agent will research Cursor AI specifically instead of providing a broad weekly digest.

---

## 🔧 Configuration

### Environment Variables

The CLI delegates to the `AIResearchAgents.Core` library, which reads these environment variables at runtime:

| Variable | Description | Example |
|----------|-------------|---------|
| `PROJECT_ENDPOINT` | Azure AI Foundry project endpoint URL | `https://my-project.openai.azure.com` |
| `MODEL_DEPLOYMENT_NAME` | Azure OpenAI model deployment name | `gpt-4o` |
| `BING_CONNECTION_NAME` | Bing connection name configured in Azure AI Foundry | `bing-grounded-search` |

These configure the Azure Foundry connection within the CLI process. The CLI passes these through to the Core library, which uses them to connect to your Azure Foundry agent.

### Authentication

The CLI uses **`AzureCliCredential`** from Azure SDK, which:

- ✅ **Fast** — no interactive browser prompts
- ✅ **Local dev optimized** — assumes you're already logged in with `az login`
- ✅ **No secrets in code** — leverages Azure CLI token cache

> 💡 **Comparison**: The MCP Server uses `DefaultAzureCredential` (which tries multiple auth methods), but the CLI uses `AzureCliCredential` exclusively for speed and simplicity in terminal workflows.

---

## 💻 Usage

### Default Weekly Digest

Run without arguments to get a comprehensive weekly digest:

```bash
dotnet run --project src/AIResearch.CLI
```

**Output:**
- 🔥 **Product News & Viral Trends** (5-6 items)
- 🔄 **Emerging Processes & Workflows** (5-6 items)

### Custom Topic Research

Focus on a specific tool, trend, or category:

```bash
dotnet run --project src/AIResearch.CLI -- "GitHub Copilot updates"
```

```bash
dotnet run --project src/AIResearch.CLI -- "vibe coding workflow trends"
```

> 💡 **Tip**: The agent still focuses on **AI dev tools only** — it will refuse off-topic queries and suggest rephrasing.

### Deploy New Agent Version

When you modify agent instructions or prompts in the code, deploy a new agent version to Azure Foundry:

```bash
dotnet run --project src/AIResearch.CLI -- --new-agent-version-in-foundry
```

**What it does:**
1. Creates a new agent version in Azure Foundry
2. Logs: `Creating new agent version...`
3. Runs the research as usual after deployment

### Strip Citation Links

Remove Bing citation links from both console output and saved file for cleaner output:

```bash
dotnet run --project src/AIResearch.CLI -- --no-links
```

**What it does:**
- Strips `[Title](url)` markdown links, leaving just the title text
- Removes stray Bing citation bracket markers (`【】`)
- Applies to both console display and the saved `.md` file

> 💡 **Tip**: Without `--no-links`, the output includes Bing Grounded Search citation URLs. These URLs point to real pages but the AI-generated bullet content may not exactly match the linked article. Use `--no-links` for cleaner reports.

### Combined Flags

Flags can be combined with each other and with custom queries:

```bash
# Deploy new agent + custom query
dotnet run --project src/AIResearch.CLI -- --new-agent-version-in-foundry "cursor AI features"

# Deploy new agent + strip links
dotnet run --project src/AIResearch.CLI -- --new-agent-version-in-foundry --no-links

# All flags + custom query
dotnet run --project src/AIResearch.CLI -- --new-agent-version-in-foundry --no-links "cursor AI features"
```

### Cancellation

Press **Ctrl+C** at any time to cancel the research operation. The CLI handles graceful shutdown.

---

## 📊 Output

The CLI produces **two outputs**: one for your terminal, and one saved to a file.

### Console Output

After the research completes, the CLI displays:

1. **Completion message**: `✓ Research completed in 18.0s`
2. **Full Markdown digest**: Rich formatted output with emojis, headers, and bullets. Includes citation links by default (use `--no-links` to strip them).
3. **File path**: `📄 Full summary saved to: research_summary_2026-02-12.md`

### File Output

The CLI automatically saves the Markdown digest to the current directory:

- **Filename format**: `research_summary_YYYY-MM-DD.md`
- **If file exists**: Appends time suffix `_HHmm` (e.g., `research_summary_2026-02-12_1430.md`)
- **Location**: Current working directory (wherever you ran `dotnet run` from)

### Output Structure

Both console and file output follow this structure:

```markdown
# 🤖 Agentic Dev Tools — Weekly Trends Digest

**Generated by**: Agentic Trends Guru Agent (Powered by Azure Foundry Agent Bing Grounded Search)

📅 Week of February 12, 2026  |  ⏱️ Generated in 18.0 seconds

### 🔥 Product News & Viral Trends
- **Tool Name**: Description of what happened, including specifics (Date)
- **Another Tool**: Another announcement or release (Date)
- ... (5-6 items when sufficient relevant news exists)

### �� Emerging Processes & Workflows
- **Workflow Name**: Description of the emerging trend or practice (Date)
- **Another Workflow**: New development practice or pattern (Date)
- ... (5-6 items when sufficient relevant trends exist)
```

### Sample Output Excerpt

Here's what the actual output looks like (from a real CLI run):

```markdown
### 🔥 Product News & Viral Trends
- **OpenAI Codex GPT-5.3**: OpenAI launched an upgraded agentic coding model, 
  improving speed by 25% and enabling tasks such as building complex games and 
  apps autonomously (February 5, 2026)
- **Apple Xcode 26.3**: Apple added agentic coding support, integrating OpenAI's 
  Codex and Anthropic's Claude Agent for app development within its IDE, 
  streamlining the process of building, testing, and iterating applications 
  (February 4, 2026)

### 🔄 Emerging Processes & Workflows
- **Vibe Coding**: The trend continues to grow, with Apple demonstrating 
  developers prompting agents for "autonomous creative coding," rapidly 
  iterating through builds (February 3, 2026)
- **Parallel Test-Driven Development with Agents**: Semantic Kernel 2.0 
  highlights workflows where agents independently develop and test modular 
  application components in sync, reducing bottlenecks (February 6, 2026)
```

> 💡 **Tip**: The saved file is ready to commit to a repo, share in Slack/Teams, or publish to a blog.

---

## ��️ Architecture

The CLI is a thin terminal wrapper around the Core library:

```
Terminal / Shell
    │ CLI args (query, flags)
    ▼
AIResearch.CLI (this project)
    │ • Argument parsing
    │ • Spinner UI
    │ • File I/O
    ▼
AIResearchAgents.Core
    │ • Agent orchestration
    │ • Configuration management
    │ • Azure SDK (AzureCliCredential)
    ▼
Azure Foundry Agent
    │ (Bing Grounded Search)
    │ • Live web research
    │ • Trend analysis
    ▼
    Rich Markdown Output
```

**Key Components:**

- **CLI Layer**: Argument parsing, terminal UI (spinner), file output
- **Core Library**: Business logic for agent orchestration and configuration
- **Azure Foundry**: Managed agent runtime with Bing Grounded Search integration

**Design Philosophy:**

The CLI keeps **all UI concerns** (spinner, file I/O) out of the Core library. The Core library focuses purely on agent orchestration and returns structured results. This keeps the Core library reusable across different frontends (CLI, MCP Server, web API, etc.).

---

## 🆚 CLI vs MCP Server

Both projects wrap the same `AIResearchAgents.Core` library, but serve different use cases:

| Feature | CLI | MCP Server |
|---------|-----|------------|
| **Interface** | Terminal / Shell | MCP Protocol (HTTP) |
| **Authentication** | `AzureCliCredential` (fast for local dev) | `DefaultAzureCredential` (production-ready) |
| **Output** | Console + Markdown file | MCP protocol response (JSON/text) |
| **Use Case** | Quick terminal research, CI/CD scripts, weekly reports | Integration with AI assistants (Claude, Copilot) |
| **Startup** | Run once, exit | Long-running server process |
| **Logging** | Console output only | Serilog (file + console) |
| **Best For** | Developers who want instant terminal results | AI assistants that need research capabilities |

> 💡 **When to use the CLI**: You want quick terminal-based research, scripted reports, or CI/CD integrations.

> 💡 **When to use the MCP Server**: You want to integrate the research agent with Claude Desktop, GitHub Copilot, or other MCP clients.

---

## 📝 Logging

The CLI uses **console output only** — no structured logging framework like the MCP Server's Serilog.

**What you see:**

```
⠋ Researching agentic trends... 12.3s
✓ Research completed in 18.0s

# 🤖 Agentic Dev Tools — Weekly Trends Digest
...

📄 Full summary saved to: research_summary_2026-02-12.md
```

**Error output** goes to `stderr`:

```
ERROR: Required environment variable 'PROJECT_ENDPOINT' is not set or is empty.
```

> 💡 **Comparison**: The MCP Server uses Serilog for structured logs (file + console), but the CLI keeps it simple with direct console writes for fast feedback.

---

## 🎯 Use Cases

### 1. Daily Standup Brief

Run the CLI every morning to get the latest AI dev tool trends:

```bash
dotnet run --project src/AIResearch.CLI
```

Read the console output or the saved Markdown file during your team standup.

### 2. Weekly Trend Report

Schedule the CLI to run weekly (via cron or Task Scheduler) and commit the output to a Git repo:

```bash
#!/bin/bash
cd /path/to/repo
dotnet run --project src/AIResearch.CLI
git add research_summary_*.md
git commit -m "Weekly AI dev tools digest"
git push
```

### 3. Custom Research on Demand

Quickly research a specific tool or trend:

```bash
dotnet run --project src/AIResearch.CLI -- "GitHub Copilot Workspace features"
```

Perfect for preparing for meetings, demos, or blog posts.

### 4. CI/CD Integration

Add the CLI to your CI/CD pipeline to generate trend reports automatically:

```yaml
# GitHub Actions example
- name: Generate AI Trends Report
  run: dotnet run --project src/AIResearch.CLI
- name: Upload Report
  uses: actions/upload-artifact@v2
  with:
    name: trends-report
    path: research_summary_*.md
```

---

## 🚨 Troubleshooting

### Error: Required environment variable not set

```
ERROR: Required environment variable 'PROJECT_ENDPOINT' is not set or is empty.
```

**Solution**: Set the required environment variables (`PROJECT_ENDPOINT`, `MODEL_DEPLOYMENT_NAME`, `BING_CONNECTION_NAME`). See [Configuration](#-configuration).

---

### Error: Azure CLI authentication failed

```
ERROR: AzureCliCredential authentication failed.
```

**Solution**: Ensure you're logged in with Azure CLI:

```bash
az login
```

Check your authentication status:

```bash
az account show
```

---

### Slow research times (>60 seconds)

**Possible causes:**
- Network latency to Azure Foundry
- Bing Grounded Search performing extensive web research
- Large result set

**Solution**: Research typically completes in 15-30 seconds. If it consistently takes longer, check your network connection and Azure Foundry region.

---

## 🔮 Roadmap

### Phase 1 (Current) ✅
- ✅ CLI wrapping Azure Foundry agent
- ✅ Rich Markdown output (console + file)
- ✅ Custom query support
- ✅ Agent deployment via CLI flag
- ✅ `AzureCliCredential` for fast local auth

### Phase 2 (Coming Soon) 🚧
- 🔜 **Watch mode** — continuous research with file watching
- 🔜 **Multiple output formats** — JSON, HTML, plain text
- 🔜 **Configurable output directory** — save files to a specific location
- �� **Interactive mode** — prompt for custom queries in a loop

### Phase 3 (Future) 🔮
- 🔮 **Caching layer** — faster repeat queries
- 🔮 **Historical trend analysis** — compare weekly digests
- 🔮 **Scheduled runs** — built-in cron-like scheduling

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

- **Azure AI Foundry** for managed agent runtime
- **Bing Grounded Search** for real-time web research
- **.NET 10** for high-performance terminal applications
- **Azure CLI** for fast local authentication

---

**Built with ❤️ for AI developers by AI developers.**
