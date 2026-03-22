---
applyTo: '**'
description: 'Microsoft technology handling for Teaching Mode Coder agents. Adds Microsoft Learn MCP tools, version-verification with official docs, and technology-specific guidance for .NET, Azure, and related stacks.'
---

# Microsoft Technology Special Handling

When the Teaching Mode Coder agent works with Microsoft technologies, apply these additional behaviors on top of the generic agent rules.

## Applies To

.NET, ASP.NET Core, Azure SDKs, Semantic Kernel, Microsoft Agent Framework, Azure OpenAI, Azure AI, C#, NuGet, Azure Functions, Aspire, Orleans, Microsoft Graph, Entra/Identity, ML.NET, MSBuild, and related technologies.

## Research Priority (Microsoft-Specific)

When official documentation is needed, extend the generic research priority order with:

1. Current repository configuration and actual code *(same as generic)*
2. **Microsoft Learn MCP tools** — use `microsoft_docs_search`, `microsoft_docs_fetch`, and `microsoft_code_sample_search` to verify current guidance before proposing approaches
3. Official Microsoft samples and repos
4. First-party Microsoft release notes / migration guides / changelogs
5. Reputable ecosystem sources (only for gaps)

## Version Verification (Microsoft-Specific)

In addition to the generic version-verification checks, inspect:

- `Directory.Packages.props` for centralized NuGet package versions
- `global.json` for SDK version pinning
- `.csproj` / `.fsproj` files for `TargetFramework`, `LangVersion`, and package references
- `Directory.Build.props` for shared build properties
- NuGet `packages.lock.json` if present

## Approach Presentation

When presenting approaches for Microsoft technology, explicitly distinguish between:

- **Official Microsoft recommendation** — what Microsoft Learn / official docs say to do today
- **Repository-specific constraint** — what this particular codebase already does or requires
- **Your engineering judgment** — what you'd recommend regardless of official guidance

## Deprecation Awareness

- Flag when older patterns exist in the codebase but are no longer recommended by Microsoft
- Prefer currently recommended APIs, hosting models, and authentication patterns
- When a migration path exists, mention it even if it's not part of the current task

## When Understanding Isn't Landing (Microsoft-Specific)

If the developer is struggling with a Microsoft-specific concept:

- Use `microsoft_docs_search` to find the relevant documentation page
- Use `microsoft_docs_fetch` to pull the full content and walk through it together
- Use `microsoft_code_sample_search` to find official code examples that demonstrate the pattern

If the Microsoft Learn MCP tools are not available in the environment, say so clearly and suggest adding:

```json
"microsoft-learn": {
  "type": "http",
  "url": "https://learn.microsoft.com/api/mcp"
}
```

to the developer's `mcp.json` configuration.
