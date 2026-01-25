# Global Repository - Agent Instructions

This repository contains the FinWise solution—a multi-agent financial advisor exposing workflows via MCP (Model Context Protocol).

---

## 🔧 Feature Toggles

> **🚨 MANDATORY - CHECK FIRST**: Before doing ANYTHING else, verify which features are enabled.
> **If a feature is OFF, you MUST NOT access, read, or reference any files related to that feature.**

| Feature | Status | When ON | When OFF |
|---------|--------|---------|----------|
| **Memory Bank** | ✅ ON | Load `.memory-bank/` files at session start, maintain context | **DO NOT read `.memory-bank/` files. DO NOT reference past context. Each session starts completely fresh. You have NO knowledge of previous work.** |

**How to Toggle:**

- Change `✅ ON` to `❌ OFF` (or vice versa) to enable/disable features
- **CRITICAL**: When a feature is OFF, you MUST skip ALL instructions AND files related to that feature
- Violating a toggle is a critical error - always check toggles BEFORE taking any action

---

## Repository Structure

```
├── src/
│   ├── FinWise.Orchestrator/     # Main MCP server with multi-agent workflow
│   ├── Microsoft.Agents.AI.*/    # Agent framework libraries (preview, will become NuGet)
│   ├── LegacySupport/            # Support for legacy integrations (preview from Agent framework, will become NuGet)
│   └── Shared/                   # Shared utilities (preview from Agent framework, will become NuGet)
├── tests/
│   └── FinWise.Orchestrator.Tests/  # Unit and E2E tests (xUnit)
├── specs/                        # Feature specifications and research
├── docs/                         # Documentation and guides
└── samples/                      # Example implementations
```

> **Note:** `Microsoft.Agents.AI.*` libraries are included as source because the Microsoft Agent Framework is in preview. Future versions will reference NuGet packages instead.

## Project-Specific Instructions

Each project has its own `AGENTS.md` with specialized guidance. When working in a subdirectory, the `chat.useNestedAgentsMdFiles` setting automatically loads the nearest `AGENTS.md`.

| Project | Instructions | Status |
|---------|--------------|--------|
| **Orchestrator** | `src/FinWise.Orchestrator/AGENTS.md` | ✅ Active |
| Tests | `tests/FinWise.Orchestrator.Tests/AGENTS.md` | *(to be created)* |

> **VS Code Users:** Enable the experimental `chat.useNestedAgentsMdFiles` setting to allow GitHub Copilot to automatically discover and use nested `AGENTS.md` files based on the files being edited.

## Build and Test Commands

### Build

```powershell
# From repo root
dotnet build FinWise-orchestrator-mcp.sln
```

### Run Unit Tests

```powershell
# All tests
dotnet test FinWise-orchestrator-mcp.sln

# Orchestrator tests only
dotnet test tests/FinWise.Orchestrator.Tests/
```

### Run E2E Tests (Requires Running Server)

E2E tests exercise the HTTP MCP endpoint and require:
- FinWise.Orchestrator running on `http://127.0.0.1:3923`
- Azure OpenAI credentials configured (env vars or appsettings)

**Option A: Two terminals (recommended)**

```powershell
# Terminal 1: Start server
dotnet run --project src/FinWise.Orchestrator/ --urls http://127.0.0.1:3923

# Terminal 2: Run E2E tests
dotnet test tests/FinWise.Orchestrator.Tests/ --filter "FullyQualifiedName~EndToEndMcpTests" --verbosity normal
```

**Option B: Single test**

```powershell
dotnet test tests/FinWise.Orchestrator.Tests/ --filter "FullyQualifiedName~TwoSessions_SameEmail" --verbosity normal
```

### Run Orchestrator

```powershell
dotnet run --project src/FinWise.Orchestrator/ --urls http://127.0.0.1:3923
```

## Technology Constraints

| Category | Technology |
|----------|------------|
| Runtime | .NET 10 |
| AI SDK | Microsoft.Extensions.AI, Azure OpenAI |
| Protocol | MCP (Model Context Protocol) with ASP.NET Core hosting |
| Testing | xUnit, FluentAssertions, Moq |
| Logging | Serilog with structured logging |
| Storage | In-memory (Cosmos DB planned for v0.3+) |

## Development Guidelines

- Prefer small, focused changes aligned with existing patterns
- Keep public APIs stable; prefer extending over breaking signatures
- Follow Microsoft Agent Framework patterns (see `https://learn.microsoft.com/en-us/agent-framework/overview/agent-framework-overview/`)
- Refer to feature specs in `specs/001-core-workflow/` for requirements
- Do NOT modify `Microsoft.Agents.AI.*` libraries unless explicitly requested

## Branch and PR Guidelines

- Commits: Use Conventional Commits format
- Propose minimal diffs that satisfy the request
- Suggest corresponding test updates for any behavior change
- Keep changes easy to rebase: small commits, clear intent
- Avoid generating large, intrusive refactors unless explicitly requested

## Security

- Never commit secrets or API keys
- Do not log secrets, paths that could be sensitive, or internal-only identifiers
- Use environment variables for Azure OpenAI credentials

## Test-Driven Development

Follow the **Red-Green-Refactor** cycle when implementing new functionality:

1. **Red**: Write a failing test that defines the expected behavior
2. **Green**: Write the minimum code to make the test pass
3. **Refactor**: Clean up both test and production code while keeping tests green

### When to Apply TDD

| Scenario | Approach |
|----------|----------|
| New feature or behavior | Write test first, then implement |
| Bug fix | Write a failing test that reproduces the bug, then fix |
| Refactoring existing code | Ensure tests exist before refactoring; add if missing |
| Exploratory/spike work | Test-after is acceptable; convert to TDD for production code |
| Non-code changes (docs, config) | No tests required |

### TDD Best Practices

- **Start with a test list**: Before coding, outline the test cases you'll need
- **One behavior per test**: Each test should verify a single, specific behavior
- **Test behavior, not implementation**: Focus on what the code does, not how it does it
- **Run tests frequently**: After each small change, verify all tests still pass
- **Don't skip refactoring**: The third step is critical—clean code prevents technical debt

> **Note**: Tests use xUnit + FluentAssertions + Moq. See `tests/FinWise.Orchestrator.Tests/` for examples.

## Memory Bank

> **⚠️ Skip this entire section if Memory Bank = ❌ OFF in Feature Toggles above.**
>
> **Detailed memory file templates:** See [`.memory-bank/AGENTS.md`](.memory-bank/AGENTS.md)

### Why This Matters

AI agents are stateless—each conversation starts from zero context. The Memory Bank
creates persistent, structured context that survives across sessions. Without it,
you waste time re-explaining decisions, repeating patterns, and rediscovering issues.
Think of Memory Bank as the agent's project journal: with it, the agent becomes a
knowledgeable team member who remembers your project's unique context and decisions.

### Files

At the start of each session, read all files in `.memory-bank/`:

| File | Purpose | Update Frequency |
|------|---------|------------------|
| `activeContext.md` | Current task, recent changes, next steps, active decisions | After each step |
| `learnings.md` | Technical patterns, known issues, code structure | When patterns discovered |
| `userDirectives.md` | User preferences, style rules, boundaries | Rarely (user-driven) |

### Evidence-Based Documentation

**CRITICAL:** Memory Bank must be 100% accurate. Inaccurate documentation is worse than none.

| Requirement | How to Apply |
|-------------|--------------|
| **File references** | Every pattern/decision must reference source file: `(see src/Example.cs)` |
| **Verify before documenting** | Use `grep`, `find`, or read files to confirm patterns exist |
| **Mark uncertainty** | If unverified, prefix with `⚠️ Unverified:` |

**Example:**
```markdown
## Pattern: Repository Layer
Base class: `BaseRepository<T>` (see `src/example-core-common-library/Repositories/`)
Timeout: ⚠️ Unverified - assumed 30s based on docs
```

### Workflow

#### 1. Starting New Chat Sessions

<constraints>
**MANDATORY**: Before answering ANY user request or performing ANY work:

1. Check if `.memory-bank/` directory exists with ALL three required files:
   - `activeContext.md`
   - `learnings.md`
   - `userDirectives.md`
2. If ANY file is missing, **STOP and create it** using templates in [`.memory-bank/AGENTS.md`](.memory-bank/AGENTS.md)
3. Do NOT proceed with the user's request until Memory Bank is initialized
</constraints>

- Read ALL Memory Bank files to initialize your understanding
- If required files are missing, **STOP and create them** using templates in [`.memory-bank/AGENTS.md`](.memory-bank/AGENTS.md)
- If the current work focus has changed, clear `activeContext.md` before continuing
- Confirm Memory Bank is loaded: "📚 Memory Bank loaded — current focus: [topic]"

#### 2. During Development

- Follow patterns, decisions, and context documented in the Memory Bank
- **IMPORTANT:** When using tools (writing files, executing commands), preface the action
  description with `MEMORY BANK ACTIVE: ` to signal you are operating based on established context

**Mandatory Step Completion Tracking:**

- **IMMEDIATELY** after completing ANY step from "Next Steps" in `activeContext.md`,
  update the file to mark that step as completed (change ☐ to ✅)
- This update must happen **BEFORE** proceeding to the next step
- **Do NOT batch multiple step completions**—update after each individual step
- Also update the "Current State" section to reflect progress

**Context Update Guidelines:**

- Read the current `activeContext.md` before making updates to avoid corruption
- **If `activeContext.md` becomes malformed, STOP and completely rewrite it** with current accurate state
- Update `learnings.md` when discovering new patterns, issues, or technical decisions

#### 3. Priority Rules

- When Memory Bank conflicts with general knowledge, **always prioritize Memory Bank**
- Your ability to function effectively depends on the accuracy of the Memory Bank—maintain it diligently

---

> **⚠️ REMINDER:** Memory Bank updates are MANDATORY after each completed step.
> Missing updates cause context rot and repeated work.

> **⚠️ REMINDER:** If Memory Bank conflicts with general knowledge,
> ALWAYS prefer Memory Bank content. It contains project-specific context.
