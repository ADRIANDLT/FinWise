---
name: pm-spec
description: 'AI-assisted product specification creation for Product Managers. Use when asked to "create a spec", "write a specification", "spec a feature", "feature spec", or "product specification". Guides PMs through context gathering, approach selection (Feature Spec or SpecKit), and structured spec generation with review. Keywords: specification, product management, feature spec, feature planning, PM workflow.'
---

# PM Spec Skill: AI-Assisted Product Specification Creation

A structured workflow for Product Managers to create professional product specifications with AI assistance. This is the entry-point skill — it orchestrates four specialized skills to gather context, select an approach, generate the spec, and review it.

## Core Principles

1. **Right-size the spec** — Match specification depth to feature complexity
2. **No assumptions** — Gather context before writing; use [TBD] for unknowns
3. **Quality through review** — Every spec goes through a write-review cycle
4. **PM-friendly** — No coding required; conversational workflow

---

## When to Use

- You need to create a product specification for a new feature
- You want AI to draft the spec while you provide domain expertise
- User asks to "create a spec", "write a specification", "spec a feature", "feature spec"

---

## Skills

This workflow is powered by four modular skills. Load each skill by reading its SKILL.md file when entering that phase.

| Phase | Skill | Path | Purpose |
|-------|-------|------|---------|
| 1 | Context Gathering | `.github/skills/context-gathering/SKILL.md` | Gather feature context, discovery questions, engineering opt-in |
| 2 | Approach Selection | `.github/skills/approach-selection/SKILL.md` | Feature Spec vs SpecKit decision, announcement |
| 3 | Spec Writing | `.github/skills/spec-writing/SKILL.md` | Generate the specification following templates |
| 4 | Spec Critique | `.github/skills/spec-critique/SKILL.md` | Review for completeness, clarity, consistency |

---

## Workflow

### Phase 1: Context Gathering

Load `.github/skills/context-gathering/SKILL.md` and follow its instructions.

Context sources (in priority order):
1. `instructions.md` file (if provided by PM)
2. Conversational discovery (one question at a time)
3. Tool-based research (explore/search tools)

### Phase 2: Approach Selection

Load `.github/skills/approach-selection/SKILL.md` and follow its instructions.

Two approaches available:
| Approach | When to Use | Template |
|----------|-------------|----------|
| **Feature Spec** (default) | All features — from focused enhancements to multi-component work | `templates/feature-spec-template.md` |
| **SpecKit** (rare) | Platform-level, 5+ components, 4+ teams, months+ | SpecKit structure |

**Decision criteria details**: See `docs/decision-framework.md`

### Phase 3: Spec Generation

Load `.github/skills/spec-writing/SKILL.md` and follow its instructions.

Provide:
- All gathered context from Phase 1
- Selected approach and template reference from Phase 2
- Core vs full section scope (from engineering opt-in)
- File path for the output spec

### Phase 4: Review

> ⚠️ Before loading the critique skill, clear your writing persona. Evaluate the spec as if written by a different author.

Load `.github/skills/spec-critique/SKILL.md` and follow its instructions.

### Phase 5: Refinement

Address review findings by severity:
- **Critical findings** — Must fix before spec is usable
- **Important findings** — Should address for quality
- **Suggestions** — Note for PM consideration

Maximum 3 review cycles. If not converging, escalate to PM.

---

## Output

Save the generated specification to a location agreed with the PM. Default conventions:
- Feature spec: `my-specs/[feature-name]-spec.md`
- SpecKit: `my-specs/[feature-name]/` (multiple files per SpecKit structure)

---

## Templates Reference

| Template | Purpose | Location |
|----------|---------|----------|
| Feature Spec Template | All features (default) | `templates/feature-spec-template.md` |
| Decision Framework | Approach selection guidance | `docs/decision-framework.md` |

---

## Example Specifications

For reference on what good specs look like:

| Example | Location |
|---------|----------|
| Dependency Update Bot | `examples/specs/automated-dependency-update-bot-spec.md` |
| AI Code Review Agent | `examples/specs/ai-code-review-agent-spec.md` |

---

## Useful Commands

| Command | Purpose |
|---------|---------|
| `Create a spec for [feature]` | Start Feature Spec workflow |
| `Create a feature spec for [feature]` | Start Feature Spec workflow |
| `Here's my instructions.md — create a spec` | Start from existing context file |
| `Review this spec for completeness` | Trigger review cycle |
| `What approach should I use for [feature]?` | Get approach recommendation |
