# CLI - Agent Instructions

> **⚠️ IMPORTANT: READ ROOT INSTRUCTIONS FIRST**
>
> Before following these project-specific instructions, you **MUST** read the root
> [`/AGENTS.md`](../../AGENTS.md) file for repo-wide constraints that apply to ALL code in this repository.

---

The CLI is an **ultra-thin layer** over Core workflows. It parses arguments, passes the credential, displays output, and saves results to a file. No business logic lives here.

## Architecture Rules

> **⛔ CRITICAL: No business logic in the CLI project.**

- Do NOT add agent creation, Azure SDK calls, or research logic here — delegate to `AIResearchAgents.Core`
- Do NOT instantiate `AIProjectClient` or any Azure SDK client directly — use `AIToolsResearchAgentRunner.CreateFromEnvironment()`
- Console output is a CLI concern — the core library never writes to the console
- **File I/O is a CLI-only concern** — the core library returns `ResearchResult` with `FormattedOutput`; the CLI decides whether/where to save it

## Credential Rule

- The CLI passes `new AzureCliCredential()` to `CreateFromEnvironment()` for fast local dev
- This is the only place in the solution where a concrete credential type is chosen
- If a different host (MCP Server) needs a different credential, it passes its own — the core library doesn't care

## Backward Compatibility

- Respect existing command and option names; changes can break pipelines

## Key Files

| File | Purpose |
|------|---------|
| `Program.cs` | Top-level statements: arg parsing → runner creation → `RunAsync()` → display + save |

## Commands

| Usage | Description |
|-------|-------------|
| `dotnet run` | Run research with default topic |
| `dotnet run -- --new-agent-version-in-foundry` | Force-create a new agent version before running |
| `dotnet run -- "custom topic"` | Run research with a custom topic (positional args joined) |
| `dotnet run -- --no-links` | Strip citation links from output (both console and file) |

## Testing

Run tests: `dotnet test ../../tests/AIResearch.CLI.Tests/`
