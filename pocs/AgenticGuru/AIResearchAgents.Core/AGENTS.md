# Core Library - Agent Instructions

> **⚠️ IMPORTANT: READ ROOT INSTRUCTIONS FIRST**
>
> Before following these project-specific instructions, you **MUST** read the root
> [`/AGENTS.md`](../../AGENTS.md) file for repo-wide constraints that apply to ALL code in this repository.

---

This library implements **AIResearchAgents.Core** — the agent orchestration core that runs AI research queries and returns in-memory results. It is consumed by the CLI and will be consumed by a future MCP Server.

## Architecture Rules

> **⛔ CRITICAL: This library is pure in-memory.**

- **No file I/O** — the library never reads or writes files. Consumers (CLI, MCP Server) decide how to persist output.
- **No console output** — no `Console.Write*` calls. The library returns structured data via `ResearchResult`.
- **No credential selection** — the library accepts `TokenCredential` and never instantiates a concrete credential (e.g., `AzureCliCredential`, `ManagedIdentityCredential`). The consumer chooses.

## Credential Pattern

**Key principle: The DLL never chooses the credential. The consumer does.**

- `AIToolsResearchAgentRunner` constructor accepts `TokenCredential` (from `Azure.Core`)
- `CreateFromEnvironment(customTopic?, credential?)` defaults to `DefaultAzureCredential` when no credential is supplied
- Each consumer passes the credential appropriate for its hosting environment:
  - **CLI (local dev):** `new AzureCliCredential()` — fastest, no credential chain probing
  - **MCP Server / ASP.NET Core (production):** `new ManagedIdentityCredential()` — secure, no secrets
  - **Portable / CI:** `new DefaultAzureCredential()` — probes environment, then Managed Identity, then CLI, etc.

## Folder-per-Agent Convention

Each agent lives in its own folder under `Agents/`:

```
Agents/
  AIToolsResearchAgent/
    AIToolsResearchAgentConfig.cs   ← sealed record, FromEnvironment()
    AIToolsResearchAgentRunner.cs   ← sealed class, RunAsync()
  [FutureAgent]/
    [FutureAgent]Config.cs
    [FutureAgent]Runner.cs
```

- Config is a `sealed record` with `FromEnvironment()` that reads env vars
- Config has an `internal` overload `FromEnvironment(Func<string, string?>)` for deterministic unit tests
- Runner is a `sealed class` that accepts config + `TokenCredential` in constructor

## Agent Lifecycle Pattern

- Agents persist across runs — no create/delete overhead per run
- **Get-or-create pattern:** `GetAgentAsync` → catch `ClientResultException` (404) → `CreateAgentVersionAsync`
- **Runtime rule:** The production runner (`AIToolsResearchAgentRunner` and other non-test callers) must not delete agents after each run; they are treated as long-lived resources for reuse and performance.
- **Test/ops exception:** Integration tests and operational maintenance scripts **may (and in tests, should)** delete agents and related cloud resources as part of explicit cleanup — see `tests/AGENTS.md` for test cleanup requirements.

## Azure SDK Specifics

- `#pragma warning disable OPENAI001` is required — OpenAI Responses API is still experimental
- Key dependencies: `Azure.AI.Projects`, `Azure.AI.Projects.OpenAI`, `Azure.Identity`
- `InternalsVisibleTo` is set for both test projects

## Citation Extraction Rules

- **Never use `GetOutputText()`** for display — it returns fabricated/hallucinated URLs
- Use `ExtractAnnotatedText()` which processes `UriCitationMessageAnnotation` from response annotations
- Only process the **last** `MessageResponseItem` from `OutputItems` — intermediate messages (before Bing search) cause content duplication
- Process annotations in **reverse order** (descending `StartIndex`) to avoid index shifting
- All response types (`ResponseResult`, `MessageResponseItem`, `UriCitationMessageAnnotation`) come via `Azure.AI.Projects.OpenAI` — never add a direct `OpenAI` package reference

## Key Types

| Type | Description |
|------|-------------|
| `ResearchResult` | Sealed record DTO — `IsSuccess`, `Summary`, `ErrorMessage`, `ElapsedSeconds`, `FormattedOutput`. Static utility `StripMarkdownLinks()` strips `[Title](url)` → `Title` and Bing citation brackets |
| `AIToolsResearchAgentConfig` | Sealed record — config from env vars (`ProjectEndpoint`, `ModelDeploymentName`, `BingConnectionName`, `AgentName`, `ResearchTopic`, `AgentInstructions`) |
| `AIToolsResearchAgentRunner` | Sealed class — `CreateFromEnvironment()` factory + `RunAsync()` |

## Testing

Run tests: `dotnet test ../../tests/AIResearchAgents.Core.Tests/`

