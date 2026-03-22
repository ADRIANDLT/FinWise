# Dev Trio — VS Code vs Copilot CLI Compatibility Issues

Analysis of `co-dev.agent.md`, `coder.agent.md`, and `critic.agent.md` for
things that only work in VS Code and break or degrade in GitHub Copilot CLI.

---

## Summary

| Agent | `tools` field | Body references | Verdict |
|-------|---------------|-----------------|---------|
| **co-dev** | None (inherits all) | Clean | ✅ No issues |
| **coder** | 12 VS Code-only tools | 2 VS Code-only tool refs in body | ⚠️ Issues found |
| **critic** | None (inherits all) | Clean | ✅ No issues |

---

## Issue 1 — `coder.agent.md`: `tools` frontmatter whitelists VS Code-only tools

**Severity**: Low (works today, fragile)
**Location**: `.github/agents/coder.agent.md`, line 4

The `tools` field lists 12 VS Code Copilot extension tool names:

```yaml
tools: ['changes', 'search/codebase', 'edit/editFiles', 'runTests', 'problems',
        'githubRepo', 'runCommands', 'testFailure', 'usages', 'findTestFiles',
        'search/searchResults', 'runCommands/terminalLastCommand']
```

**CLI mapping** (none of these exist natively in CLI):

| VS Code tool | CLI equivalent | Available? |
|-------------|---------------|------------|
| `changes` | `powershell` → `git diff` | Indirect |
| `search/codebase` | `grep`, `glob` | Different name |
| `edit/editFiles` | `edit` | Different name |
| `runTests` | `powershell` → `dotnet test` | Indirect |
| `problems` | `ide-get_diagnostics` | Different name |
| `githubRepo` | GitHub MCP tools | Different name |
| `runCommands` | `powershell` | Different name |
| `testFailure` | `powershell` → `dotnet test` | Indirect |
| `usages` | `grep` | Different name |
| `findTestFiles` | `glob` → `**/*.Tests.*` | Different name |
| `search/searchResults` | `grep` | Different name |
| `runCommands/terminalLastCommand` | `read_powershell` | Different name |

**Current behavior**: CLI appears to ignore unrecognized tool names and grants
the default CLI toolset anyway — so the coder agent **works today**. However,
this is an implicit behavior, not a guaranteed contract.

**Risk**: If CLI behavior changes to enforce the whitelist (like it did for
`codebase-explainer`, which ended up with zero usable tools), the coder agent
would break.

**Recommendation**: Either remove the `tools` field (like critic and co-dev) or
maintain a parallel list of CLI-compatible tool names. Removing is simpler and
consistent with the other two agents.

---

## Issue 2 — `coder.agent.md`: Emergency Protocols reference VS Code-only tools

**Severity**: Low (guidance text, not functional)
**Location**: `.github/agents/coder.agent.md`, lines 338–339

The Emergency Protocols table instructs the agent to use tools that only exist
in VS Code:

```markdown
| **Compilation fails after edit** | Run `problems` tool to see all errors. |
| **Tests fail unexpectedly**      | Run `testFailure` tool for details.     |
```

**In CLI**:
- `problems` → doesn't exist. CLI equivalent: `ide-get_diagnostics` or
  `dotnet build` via `powershell`.
- `testFailure` → doesn't exist. CLI equivalent: `dotnet test` via `powershell`
  and reading the output.

**Impact**: When the coder agent encounters compilation or test failures in CLI,
these instructions point it to nonexistent tools. The agent likely works around
it (LLMs are adaptive), but the guidance is misleading.

**Recommendation**: Make the guidance environment-agnostic, e.g.:

```markdown
| **Compilation fails after edit** | Check for typos, missing imports, type mismatches. Run the build to see all errors. |
| **Tests fail unexpectedly**      | Run the tests and inspect failure output. Check if test assumptions changed.        |
```

---

## Non-Issues (Confirmed Working)

### co-dev.agent.md — ✅ Clean

- **No `tools` field** → inherits all available tools in any environment.
- **Agent delegation** uses names (`Coder`, `Critic`, `explore`, `task`) that
  map correctly in both VS Code and CLI.
- **Verification instructions** ("View the modified file(s)") work in both
  environments (`view` tool in CLI, editor in VS Code).
- All instructions are environment-agnostic.

### critic.agent.md — ✅ Clean

- **No `tools` field** → inherits all available tools in any environment.
- **No VS Code tool references** in the body text.
- Review instructions reference file reading generically, which works in both
  environments.

---

## Related — Already Fixed

### codebase-explainer.agent.md — Fixed this session

**Was**: `tools: ['codebase', 'search', 'usages', 'web/fetch', 'findTestFiles', 'githubRepo', 'problems']`
**Now**: No `tools` field (inherits all).

This agent was **completely broken** in CLI — the whitelist filtered out all CLI
tools, leaving it with only `skill` and `report_intent`. It couldn't read a
single file. The fix was removing the `tools` field entirely.

---

## Recommendation Summary

| Action | Agent | Change | Priority |
|--------|-------|--------|----------|
| Remove `tools` field | coder | Delete line 4's tools array from frontmatter | Medium — works today, fragile |
| Update Emergency Protocols | coder | Make tool references environment-agnostic | Low — guidance only |
| No change needed | co-dev | — | — |
| No change needed | critic | — | — |
