---
name: microsoft-tech-deep-research
description: 'Deep research protocol for Microsoft technologies. Inspects repo package versions, queries Microsoft Learn MCP tools, fetches official docs, verifies Preview/GA status, and produces a Research Summary before any approach is proposed. Use whenever working with .NET, Azure, Semantic Kernel, Microsoft Agent Framework, MCP SDK, or any Microsoft SDK/framework/service — especially Preview packages.'
---

# Microsoft Deep Research Protocol

Structured research workflow for verifying current official guidance before proposing any approach involving Microsoft technologies. This skill produces a **Research Summary** that the calling agent uses to ground its proposals in verified facts rather than training-data assumptions.

## When This Skill Applies

This skill is **mandatory** when ANY of these are true:

- The task involves a Microsoft SDK, framework, API, or service
- Any NuGet package in use has a `-preview`, `-rc`, `-alpha`, or `-beta` suffix
- The task involves a version upgrade or migration
- The task depends on version-specific APIs or behaviors
- The task uses recently released or fast-moving tooling (e.g., Microsoft.Agents.AI, Semantic Kernel, Aspire)
- The task touches authentication, hosting, deployment, or cloud service integration
- The developer references a pattern that may be outdated
- You are uncertain whether your knowledge of the API is current

## Microsoft Technology Scope

This skill covers all Microsoft-related technologies, including but not limited to:

- .NET / ASP.NET Core
- Azure SDKs and services (Azure.AI.*, Azure.Identity, Azure.Cosmos, etc.)
- Microsoft Agent Framework (Microsoft.Agents.AI)
- Semantic Kernel
- Azure AI Foundry / Azure OpenAI / Azure AI services
- Model Context Protocol (ModelContextProtocol.AspNetCore)
- Microsoft.Extensions.AI
- C# / MSBuild / NuGet
- Azure Functions / Azure Container Apps / AKS / App Service
- Microsoft Aspire / Orleans
- Microsoft Graph / Entra / Identity
- ML.NET
- Visual Studio / VS Code tooling SDKs

---

## Research Execution Steps

Execute these steps **in order** before proposing any approach:

### Step 1 — Inspect the Repository

Check the actual versions and patterns currently in use:

- `Directory.Packages.props` — centralized NuGet package versions
- `global.json` — SDK version pinning
- `.csproj` / `.fsproj` files — `TargetFramework`, `LangVersion`, package references
- `Directory.Build.props` — shared build properties
- `packages.lock.json` — if present
- Existing code patterns for the technology in question

Report what you find. Flag any Preview packages explicitly:

> **Preview package detected**: `Microsoft.Agents.AI` version `0.x.y-preview`. APIs may differ from documentation. Research required.

### Step 2 — Search Microsoft Learn

Use the Microsoft Learn MCP tools to verify current guidance:

- **`microsoft_docs_search`** — find relevant documentation pages for the technology and version
- **`microsoft_code_sample_search`** — find official code examples (optionally filter by language: `csharp`)
- **`microsoft_docs_fetch`** — pull full content from the most relevant pages

Focus your searches on:
- Current setup and configuration guidance
- Version-specific API usage and patterns
- Migration guides (if upgrading)
- Authentication and identity patterns
- Service integration patterns
- Known breaking changes
- Recommended architecture guidance

### Step 3 — Web Research for Gaps

When Microsoft Learn doesn't cover the topic sufficiently (common with Preview SDKs):

- Use `web/fetch` to check official GitHub repos for the SDK (README, samples, changelogs)
- Check NuGet.org for the package's latest version and release notes
- Look for official Microsoft blog posts about the Preview release
- Check GitHub issues for known problems with the specific version

### Step 4 — Synthesize and Present the Research Summary

Before proposing any approach, present a **Research Summary** using this template:

```
### Research Summary — [Technology/Package Name]

**Package version in repo**: X.Y.Z-preview
**Latest available version**: X.Y.Z (checked via [source])
**Stability**: Preview / RC / GA
**Key findings**:
- [Finding 1 — what the current docs say]
- [Finding 2 — what changed recently]
- [Finding 3 — known issues or gaps]

**Version-sensitive warnings**:
- [API X was renamed/removed in version Y]
- [Pattern Z is deprecated in favor of W]

**Confidence level**: High / Medium / Low
**Gaps**: [What couldn't be verified]
```

If research is incomplete, say so explicitly. **Do not present guesses as facts.**

---

## Research Priority Order

When researching Microsoft technologies, use this strict priority order:

1. **Current repository configuration and actual code** — what's really installed and used
2. **Microsoft Learn MCP tools** — `microsoft_docs_search`, `microsoft_docs_fetch`, `microsoft_code_sample_search` to verify current guidance
3. **Official Microsoft samples and repos** — GitHub repos from Microsoft orgs
4. **First-party release notes / migration guides / changelogs** — NuGet release notes, GitHub releases
5. **Official Microsoft blog posts** — devblogs.microsoft.com for Preview announcements
6. **Reputable ecosystem sources** — only for gaps not covered by official sources

**Never rely on training data alone** for version-specific behavior. Always verify.

---

## Version Verification Requirements

Before the calling agent proposes an important implementation step for a Microsoft SDK, framework, or API, this skill must verify:

- Package name(s) and exact installed version(s) in the repo
- Whether the package is Preview, RC, or GA
- Target runtime / language version (.NET version, C# version)
- Breaking changes between the installed version and latest
- Deprecated APIs or patterns that should be avoided
- Current recommended patterns from official docs
- Whether code samples found online are version-aligned with the repo

If verification is incomplete, say so explicitly. **Do not present guesses as facts.**

---

## Preview SDK Caution Mode

When working with Preview packages, activate **heightened caution**:

1. **Expect breaking changes** — Preview APIs can change between releases without notice
2. **Pin versions explicitly** — never recommend floating version ranges for Preview packages
3. **Check GitHub issues** — look for known issues with the specific Preview version in the repo
4. **Warn about production readiness** — clearly state when a Preview package is not recommended for production
5. **Track the upgrade path** — note what's expected to change when the package reaches GA
6. **Compare with previous previews** — if the repo uses an older preview, check what changed
7. **Test more aggressively** — Preview APIs may have subtle behavioral differences from documentation

When proposing code that uses Preview APIs, add a warning banner:

> **Preview API**: This code uses `PackageName` version `X.Y.Z-preview`. The API may change in future releases. Verified against current documentation as of [date].

---

## Deprecation Awareness

- Flag when older patterns exist in the codebase but are no longer recommended by Microsoft
- Prefer currently recommended APIs, hosting models, and authentication patterns
- When a migration path exists, mention it even if it's not part of the current task
- Distinguish between "deprecated" (will be removed) and "superseded" (still works, newer option available)

---

## Approach Presentation (Microsoft-Specific)

When the calling agent presents approaches for Microsoft technology, it **must** explicitly distinguish between:

- **Official Microsoft recommendation** — what Microsoft Learn / official docs say to do today
- **Repository-specific constraint** — what this particular codebase already does or requires
- **Engineering judgment** — what the agent recommends regardless of official guidance

Use this format:

> **Official recommendation**: Microsoft docs recommend using `X` pattern with `Y` API.
> **This repo currently**: Uses `Z` pattern (introduced in version W).
> **My recommendation**: [Align with official / Deviate because...] — here's why: ...

---

## When Understanding Isn't Landing

If the developer is struggling with a Microsoft-specific concept:

1. Use `microsoft_docs_search` to find the relevant documentation page
2. Use `microsoft_docs_fetch` to pull the full content and walk through it together
3. Use `microsoft_code_sample_search` to find official code examples that demonstrate the pattern
4. If needed, use `web/fetch` to find the official GitHub sample repo and trace through a real example

Don't just explain from memory — **show the official source** and walk through it together.

---

## MCP Server Requirement

This skill requires the **microsoft-learn** MCP server to be configured. If the Microsoft Learn MCP tools (`microsoft_docs_search`, `microsoft_docs_fetch`, `microsoft_code_sample_search`) are not available in the environment, say so clearly and suggest adding:

```json
"microsoft-learn": {
  "type": "http",
  "url": "https://learn.microsoft.com/api/mcp"
}
```

to the developer's `.vscode/mcp.json` configuration.
