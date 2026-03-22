---
name: explain-codebase
description: 'Explain an existing codebase: architecture, design patterns, dependency choices, and system structure. Use when asked to "explain this codebase", "how does this system work", "walk me through the architecture", "what dependencies does this use", "trace this flow", "how is this designed", or "map this project". Produces System Maps, Dependency Reports, and Flow Traces.'
---

# Explain Codebase

Produce clear, structured explanations of an existing codebase — architecture, design patterns, dependency choices, and data/control flows. All output is explanatory; no code generation or modification.

## When to Use

- Onboarding to an unfamiliar codebase
- Understanding how a feature or workflow is implemented end-to-end
- Inventorying and assessing external dependencies (frameworks, SDKs, APIs, cloud services)
- Investigating why a component was designed a certain way
- Preparing for a refactor, migration, or architecture review by first understanding the current state

---

## Process

### Phase 1: Orient — Map the Landscape

1. **Scan project structure** — directories, solution/project files, build configuration
2. **Identify architectural layers** — entry points, core logic, infrastructure, tests
3. **Read configuration files** — dependency manifests (`*.csproj`, `package.json`, `Directory.Packages.props`, `docker-compose.yml`, etc.)
4. **Detect boundaries** — what are the deployable units, libraries, and shared contracts
5. **Check for documentation** — ADRs, specs, journals, README files, `AGENTS.md` files that reveal design intent

Deliver a **System Map** (see output template below).

### Phase 2: Zoom In — Explain Architecture

For each major component or area requested:

1. **Purpose** — what problem does this component solve?
2. **Pattern** — what design pattern or architectural style does it follow? (e.g., hub-and-spoke, CQRS, repository, composition root, factory, strategy, anti-corruption layer)
3. **Relationships** — what does it depend on? What depends on it? How tightly coupled?
4. **Trade-offs** — what does this design make easy? What does it make hard? What alternatives exist?
5. **Conventions** — what naming, folder, or structural conventions does this area follow?

### Phase 3: Dependencies Deep-Dive

For frameworks, SDKs, APIs, and cloud services:

1. **Inventory** — list all external dependencies with their versions and categories (framework, SDK, protocol, cloud service, testing, logging, etc.)
2. **Role** — what role does each dependency play in the architecture? Why was it likely chosen over alternatives?
3. **Coupling** — how deeply is the codebase coupled to this dependency? Is it abstracted behind an interface or used directly?
4. **Version & maturity** — is this a stable release, preview/beta, or deprecated? Flag any risks
5. **Configuration** — how is the dependency configured? (env vars, appsettings, code-based setup)

Deliver a **Dependency Report** (see output template below).

### Phase 4: Flow Tracing

When asked about a specific workflow or feature:

1. **Entry point** — where does the request/event enter the system?
2. **Step-by-step path** — trace through each layer, naming the actual classes/methods/files involved
3. **State changes** — what data is read, written, or transformed at each step?
4. **Branching** — where are the decision points? What determines which path is taken?
5. **Exit** — what is the final output or side effect?

Deliver a **Flow Trace** (see output template below).

---

## Routing — What to Do When

| User says | Execute |
|-----------|---------|
| "Explain this codebase" / "Walk me through this project" | Phase 1 → present System Map → ask which area to zoom into |
| Points to a specific file or folder | Phase 2 for that component + Phase 3 for its dependencies |
| "How does X work?" / "Trace the flow for X" | Phase 4 for that feature or workflow |
| "What dependencies does this use?" | Phase 3 for the relevant scope |
| "Why was X designed this way?" | Phase 2 (trade-offs) + evidence from code, ADRs, specs, or journals |

---

## Explanation Guidelines

### Use real names
Reference actual types, files, folders, and namespaces from the codebase — not generic placeholders.

### Name the pattern
When you spot a known pattern (factory, mediator, composition root, anti-corruption layer, hub-and-spoke), name it explicitly and explain how it's applied here.

### Quantify
Say "3 agents", "2 NuGet packages", "1 entry point" — not "several" or "multiple".

### Diagrams
Use ASCII art, Mermaid, or structured tables to show relationships when helpful.

### Big picture first
Lead with the high-level overview, then drill into details only when asked.

### Evidence over speculation
Always read actual code before explaining. If unsure about intent, say so and offer hypotheses with code evidence. If the repo has ADRs, specs, or journal entries, reference them.

---

## Output Templates

### System Map

```
## System Map: {Project Name}

Architecture style: {e.g., Layered, Hexagonal, MCP server + class library}
Deployable units: {list}
Entry points: {list with purpose}

┌─────────────────────┐
│   {Layer/Component}  │──depends on──▶ {Other Component}
│   Purpose: ...       │
│   Pattern: ...       │
└─────────────────────┘
         │
         ▼
   {Next layer...}

Key conventions:
- {Convention 1}
- {Convention 2}
```

### Dependency Report

```
## Dependencies: {Scope}

| Dependency | Version | Category | Role | Coupling | Risk |
|------------|---------|----------|------|----------|------|
| ...        | ...     | ...      | ...  | ...      | ...  |

Notable:
- {Insight about a key dependency}
- {Insight about version/maturity risks}
```

### Flow Trace

```
## Flow: {Feature/Workflow Name}

Entry: {File + method}

1. {Step} — {File}:{Type}.{Method}
   → {What happens, what data moves}
2. {Step} — ...
   → ...

Decision points:
- At step N: {condition} determines {path A vs path B}

Final output: {what the caller/user receives}
```
