---
name: microsoft-tech-deep-research
description: 'Deep research protocol for Microsoft technologies. Inspects repo package versions, queries Microsoft Learn MCP tools, fetches official docs, verifies Preview/GA status, and produces a Research Summary before any approach is proposed. Use whenever working with .NET, Azure, Semantic Kernel, Microsoft Agent Framework, MCP SDK, or any Microsoft SDK/framework/service — especially Preview packages.'
---

# Microsoft Deep Research Protocol

Structured research workflow for Microsoft technologies. Produces a **Research Summary** grounded in verified facts by leveraging the **Microsoft Learn MCP server** for high-quality documentation search.

> **Non-Microsoft technologies?** Use **tech-deep-research** instead — it covers Python, Node, Java, Go, Rust, and any non-Microsoft SDK/framework via web fetch.

---

## When to Use & Research Depth

**Mandatory** when the task involves any Microsoft SDK/framework/API/service — **in any language** (C#, Python, JS/TS, Java, Go) — or any Azure service, especially Preview packages.

| Depth | Steps | When |
|-------|-------|------|
| **Quick Check** | Step 1 (+ one Step 2 MCP query) | Known tech; confirm version or API |
| **Full Research** ⭐ default | Steps 1→2→3→4 | New packages, upgrades, preview SDKs, large features |
| **Spot Check** | Step 1 + targeted Step 2 | Review/audit — verify key claims only |

**Force Full Research** for: Agent development (Microsoft.Agents.AI, Semantic Kernel agents, AutoGen), Azure AI services, and any preview package. These areas change too fast for Quick Checks.

---

## Microsoft Technology Scope

Covers **all Microsoft technologies across all languages**. This list is illustrative — the key differentiator is **vendor (Microsoft)**, not language or product.

**The .NET ecosystem**: .NET/ASP.NET Core, C#/F#, NuGet, Microsoft.Extensions.AI, Microsoft.Agents.AI, ModelContextProtocol.AspNetCore, Aspire, Orleans, ML.NET, MAUI, Blazor.

**Azure SDKs (all languages)**: `Azure.*`/`Azure.AI.*` (NuGet), `azure-*`/`azure-ai-*` (PyPI), `@azure/*` (npm), `com.azure:*` (Maven), `github.com/Azure/azure-sdk-for-go`. Check the [Azure SDK releases page](https://azure.github.io/azure-sdk/releases/latest/) for new language support.

**Azure services**: Azure OpenAI, Azure AI Foundry, Azure AI Foundry Agent Service, Azure Functions, Container Apps, AKS, Cosmos DB, Azure AI Search, Event Grid/Service Bus, ACR.

**Protocols**: Model Context Protocol (MCP), Agent-to-Agent Protocol (A2A), OpenTelemetry, OpenAPI/Kiota.

**Identity**: Microsoft Entra, MSAL (all languages), Microsoft Graph.

### Agent Development (High-Volatility Area)

> **⚠️ Always Full Research for agent work.** This is Microsoft's most volatile area.

Multiple overlapping frameworks evolve independently:
- **Microsoft Agent Framework** (`Microsoft.Agents.AI`) — handoff/agent-as-tools orchestration (C#, Python)
- **Semantic Kernel** — agent orchestration, experimental stage (C#, Python, Java)
- **AutoGen** — MSR OSS, event-driven multi-agent (Python, emerging .NET)
- **Azure AI Foundry Agent Service** — managed cloud service
- **Microsoft.Extensions.AI** — lower-level AI model abstractions

**Why extra rigor**: overlapping frameworks with unclear boundaries, experimental APIs that change between previews, frequent service rebranding (e.g., "Azure AI Agent Service" → "Azure AI Foundry Agent Service (classic)"), orchestration pattern evolution. Always verify: which framework + exact version, API stability level (stable/preview/experimental), current recommended orchestration pattern, MCP/A2A support.

---

## Research Execution Steps

### Step 1 — Inspect the Repository

Check actual versions in the project's ecosystem:

**For .NET**: `Directory.Packages.props`, `global.json`, `.csproj`/`.fsproj` (TargetFramework, packages), `Directory.Build.props`

**For Python** (Azure/SK): `pyproject.toml`, `requirements.txt` / `uv.lock`, `poetry.lock`, `.python-version`

**For JS/TS** (Azure): `package.json` (`@azure/*`, `@microsoft/*`) / lock files, `.nvmrc`

**For Java** (Azure): `pom.xml`, `build.gradle(.kts)` / `gradle.lockfile`

**All projects**: `Dockerfile`, `docker-compose.yml`, Helm charts, `azure-pipelines.yml`, `bicep`/`arm` templates.

Flag pre-release packages: NuGet `-preview/-rc/-beta`, PyPI `a1/b1/rc1/dev`, npm `@next/@beta`, Maven `-SNAPSHOT`.

**Group related packages**: When multiple pre-release packages share a namespace (e.g., `Microsoft.Agents.AI`, `Microsoft.Agents.AI.Workflows`, `Microsoft.Agents.AI.Hosting`), research them as a **single SDK family**, not individually. They share a release cycle and version cadence.

> **Pre-release detected**: `Microsoft.Agents.AI` family at `1.0.0-rc4` (4 packages). Full Research required.

### Step 2 — Search Microsoft Learn

**Always try MCP tools first** — they provide structured, high-quality results.

1. **MCP tools** (`microsoft_docs_search`, `microsoft_docs_fetch`, `microsoft_code_sample_search`). Tool names may have a server prefix (e.g., `microsoft-learn-microsoft_docs_search`). Code sample search supports `language` filter: `csharp`, `python`, `javascript`, `typescript`, `java`, `go`.
2. **Check for `llms.txt`** at `https://[docs-site]/llms.txt` for AI-consumable docs ([spec](https://llmstxt.org/)).
3. **Web fetch fallback** (when MCP unavailable): `learn.microsoft.com/en-us/search/?terms=[query]`, `.NET API: /dotnet/api/[ns]`, Python: `/python/api/overview/azure/[svc]`, JS: `/javascript/api/overview/azure/[svc]`.

**Disambiguate search terms**: Microsoft uses "agents" in multiple contexts (Azure AI Search agentic retrieval, Microsoft Agent Framework, Azure AI Foundry Agent Service). Use specific package or framework names in MCP queries (e.g., `Microsoft.Agents.AI handoff` not just `agents orchestration`).

Focus on: setup/config, version-specific APIs, migration guides, auth patterns, breaking changes, recommended architecture.

### Step 3 — Web Research for Gaps

When Learn docs are insufficient (common with Preview SDKs):

- **GitHub repos**: README, CHANGELOG, issues, releases
- **Registries**: NuGet, PyPI, npm, Maven Central
- **NuGet version history** (fast): `api.nuget.org/v3-flatcontainer/[package-lowercase]/index.json` — returns all published versions as JSON, faster than HTML page
- **Azure SDK releases dashboard**: `azure.github.io/azure-sdk/releases/latest/`
- **DevBlogs**: `devblogs.microsoft.com/azure-sdk/`
- **Agent-specific**: Agent Framework docs (`learn.microsoft.com/en-us/microsoft/agents/`), Semantic Kernel (`learn.microsoft.com/en-us/semantic-kernel/`), AutoGen (`microsoft.github.io/autogen/stable/`), AI Foundry (`learn.microsoft.com/en-us/azure/ai-foundry/`)

### Step 4 — Research Summary

```
### Research Summary — [Technology/Package Name]

**Package version in repo**: X.Y.Z-preview
**Latest available**: X.Y.Z (via [source])
**Stability**: Preview / RC / GA
**Ecosystem**: NuGet / PyPI / npm / Maven
**Runtime/Language version**: .NET 10 / Python 3.12 / Node 22 / Java 21

**Key findings**:
- [Finding 1]
- [Finding 2]

**Version-sensitive warnings**:
- [API/pattern changes]

**Confidence**: High / Medium / Low
**Gaps**: [What couldn't be verified]
**Sources**: [URLs consulted]
```

**Do not present guesses as facts.** If research is incomplete, say so.

---

## Research Quality

### Source Priority

1. Repo code and config (ground truth) → 2. Microsoft Learn via MCP tools **first** → 3. Official Microsoft GitHub repos → 4. Registry metadata + Azure SDK release dashboard → 5. DevBlogs → 6. Ecosystem sources (gaps only)

### Credibility & Recency

- **< 3 months**: Trust directly.
- **3–12 months**: Cross-check against current Learn docs.
- **> 1 year**: Verify every claim — Microsoft renames services and changes APIs frequently.
- **DevBlogs about previews**: Check date — preview features may have changed or been cut before GA.
- **StackOverflow [azure] answers**: Many reference older SDK versions (v11 vs v12). Check dates.

### When Sources Contradict

1. Code/tests > docs. 2. Newer > older. 3. Learn docs > blog posts. 4. Azure SDK release dashboard > cached info. 5. **Always flag contradictions** — never silently pick one.

### Common Research Pitfalls

- **Don't assume features are pattern-exclusive.** HITL, checkpointing, and streaming are framework-level features available to ALL workflow types (handoff, graph, sequential). Don't recommend migrating patterns just to access a feature — check if it's already available in the current pattern.
- **Overview pages reference features documented elsewhere.** A page may list "checkpointing" as a bullet but the actual API is on a separate page. Follow the links.
- **Existing repo code is the best API documentation for preview packages.** The actual `HandoffBuilder`, `AgentWorkflowBuilder`, or `WorkflowBuilder` calls in the code reveal the real API surface — sometimes more accurately than the docs.

### Time-Boxing

| Quick Check | Spot Check | Full Research |
|-------------|-----------|---------------|
| ~2 min | ~5 min | ~10 min max |

Stop when: 3+ sources corroborate. Don't stop when: docs and code disagree, service may have been renamed, or package is Preview without issue check.

---

## Caution Zones

### Preview SDK Mode

For `-preview/-rc/-beta`, `a1/b1/rc1`, `@next`, `-SNAPSHOT`: pin exact versions, check GitHub issues, warn about production readiness, compare with previous previews, track GA timeline. Add banner: `> **Preview API**: Uses [package] [version]. May change.`

### Supply Chain Security

Check: preview deps of preview packages, lock file presence, `dependabot.yml`/NuGet audit config, Security tab advisories, Azure SDK release dashboard for known issues.

### Deprecation

Flag deprecated patterns. Prefer currently recommended APIs/hosting/auth. Distinguish "deprecated" from "superseded". Note migration paths even if not in current task scope.

---

## Approach Presentation

Always distinguish: **Official Microsoft recommendation** (Learn docs) vs **Repo constraint** (existing code) vs **Engineering judgment** (your recommendation and why).

---

## Environment & Tools

**Copilot CLI**: MCP tools built-in + `web_fetch`. **VS Code**: Needs `microsoft-learn` MCP server in `.vscode/mcp.json` (`{"type":"http","url":"https://learn.microsoft.com/api/mcp"}`). **Other environments**: Web fetch fallback. **No web access**: Complete Step 1, flag `Confidence: Low`.

Session reuse: don't re-research same package/version; state what was reused.

---

## Future-Proofing

This skill teaches **how** to research Microsoft tech, not **what** exists. Technology scope is illustrative — Step 1 (repo inspection) is the ground truth.

**Microsoft-specific trends**: multi-language SDK parity (don't assume C#-only), frequent service renaming, MCP/A2A protocol adoption, .NET annual LTS cycle, Aspire as cloud-native default, agent frameworks in rapid flux. New things we can't predict will appear — the 4-step protocol handles them all.
