# Memory Bank Templates

This directory contains persistent context for AI agents across chat sessions.
For operational workflow, see [root AGENTS.md](../AGENTS.md#memory-bank).

Files in this folder (except this AGENTS.md) are git-ignored — each developer
maintains their own local context.

## Required Files

**If any file is missing, create it using the templates below.**

| File | Purpose | Update Frequency |
|------|---------|------------------|
| `activeContext.md` | Current task, recent changes, next steps, active decisions | After each step |
| `learnings.md` | Technical patterns, known issues, code structure | When patterns discovered |
| `userDirectives.md` | User preferences, style rules, boundaries | Rarely (user-driven) |

## File Templates

If any file is missing, create it using the templates below.

### `activeContext.md`

```markdown
# Active Context

## Current Work Focus
[Description of current task/feature being worked on]

## Recent Changes
- ✅ Completed items with checkmark
- ☐ Pending items with empty checkbox

## Active Decisions
[Key architectural or implementation decisions made during this work]

## Next Steps
1. ☐ Step description
2. ☐ Step description

## Current State
[Summary of where we are in the implementation]
```

### `learnings.md`

```markdown
# Project Learnings

## Code Structure

### [Your Main Module/Layer]
- [Document the primary architectural component]
- [Key responsibilities and boundaries]
- [Important patterns used]

### [Secondary Module/Layer]
- [Document supporting components]
- [Integration points and dependencies]

### CLI / API Surface
- [Document the public interface layer]
- [Key entry points and their purpose]

## Known Issues
- [Document any known bugs, quirks, or workarounds]

## Patterns Discovered
- [Document patterns learned during development]

## Technical Decisions
- [Document significant architectural decisions and rationale]
```

### `userDirectives.md`

```markdown
# User Directives

## Response Style
- [Preferred tone, verbosity, format preferences]

## Behavioral Boundaries
- [Things the agent should never do]

## Priorities
- [What matters most: speed vs quality, minimal vs comprehensive, etc.]

## Project-Specific Rules
- [Any additional rules specific to how you want to work]
```

## Workflow

> **⚠️ First check Feature Toggles** in [root AGENTS.md](../AGENTS.md#-feature-toggles) to verify Memory Bank is enabled.

See [root AGENTS.md](../AGENTS.md#memory-bank) for operational workflow instructions.
