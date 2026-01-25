# GitHub Copilot Instructions

> **Primary instructions live in [`AGENTS.md`](../AGENTS.md).**
>
> This file exists for GitHub Copilot discovery. All detailed guidance—build
> commands, technology constraints, directory rules, and project-specific
> instructions—is maintained in the hierarchical `AGENTS.md` files throughout
> the repository.

## Required Reading (Every Session)

**At the START of each new chat session**, complete these steps in order:

### 1. Check Feature Toggles (CRITICAL - DO THIS FIRST)

Read the **Feature Toggles** section at the top of [`AGENTS.md`](../AGENTS.md#-feature-toggles) to determine which features are enabled.

### 2. Memory Bank (Only if Memory Bank = ✅ ON)

> **Skip this section if Memory Bank = ❌ OFF in AGENTS.md Feature Toggles.**

Follow the Memory Bank workflow defined in [`AGENTS.md` → Memory Bank section](../AGENTS.md#memory-bank).

### 3. Project Instructions

The `chat.useNestedAgentsMdFiles` setting automatically discovers and loads the appropriate
`AGENTS.md` files based on the files being edited (local + parent hierarchy, not siblings).

See the root [`AGENTS.md`](../AGENTS.md#project-specific-instructions) for the full list of project-specific instruction files.
