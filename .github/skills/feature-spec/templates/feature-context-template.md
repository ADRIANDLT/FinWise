# Feature Context Template

> Use this template to provide upfront context for complex features.
> For simple features, just include context in your initial prompt instead.

## How to Use

1. **Copy this file** to your feature's specs folder:
   ```
   /specs/feature-<name>/feature-context.md
   ```
   Example: `/specs/feature-user-auth/feature-context.md`

2. **Fill in the sections** below with your feature-specific context

3. **Start the spec workflow** by attaching the file or referencing it:
   ```
   Create a feature spec for [name] using the context in /specs/feature-user-auth/feature-context.md
   ```

4. **The agent will generate** the final spec file in the same folder:
   ```
   /specs/feature-<name>/specs-funct-and-tech.md
   ```

---

## Feature Overview

**Feature name:** [Short name]

**Problem:** [What problem does this solve?]

**Scope:** [What's included? What's explicitly out of scope?]

---

## Requirements & Constraints

- [Specific requirement or constraint]
- [Business rule or edge case to handle]
- [Performance, security, or compliance requirement]

---

## Implementation Guidance

**Use:**
- [Framework, library, or pattern to use]
- [Existing code/patterns to follow]

**Avoid:**
- [Technology or approach to avoid]
- [Anti-pattern or deprecated approach]

---

## Additional Context

[Any other context that would help the agent understand the feature]
