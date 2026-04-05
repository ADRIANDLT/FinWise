# Agent Skills

This directory contains skills — structured prompts that give Copilot domain-specific expertise for common engineering tasks. Skills are activated automatically: just ask Copilot in Chat using natural language and it will pick the right skill based on your request. No setup required.

> **Quick start:** Open Copilot Chat and type something like _"refactor this function"_ or _"find tech debt"_. Copilot matches your intent to a skill and follows its workflow.

## Available Skills

### Code Quality & Refactoring

| Skill                                                       | What it does                                                                                         | Try saying                                                                   |
| ----------------------------------------------------------- | ---------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------- |
| [**refactor**](refactor/SKILL.md)                           | Systematically refactor code to improve maintainability without changing behavior                    | _"refactor this"_, _"clean up this code"_, _"simplify this"_                 |
| [**test-quality-analysis**](test-quality-analysis/SKILL.md) | Score tests 1–5 on real value — detect coverage-only tests, test smells, and low-value assertions    | _"analyze test quality"_, _"find test smells"_, _"are these tests valuable"_ |
| [**tech-debt-discovery**](tech-debt-discovery/SKILL.md)     | Inventory and prioritize technical debt from code markers, git history, and dependency health        | _"find tech debt"_, _"how healthy is this codebase"_, _"audit code quality"_ |
| [**log-pattern-analyzer**](log-pattern-analyzer/SKILL.md)   | Audit logging in code — find gaps, inconsistencies, sensitive data exposure, and missing correlation | _"analyze logging"_, _"find log gaps"_, _"audit logs in code"_               |

### Development Workflow

| Skill                                                                   | What it does                                                                               | Try saying                                                                 |
| ----------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------- |
| [**git-commit**](git-commit/SKILL.md)                                   | Create well-structured commits following the Conventional Commits specification            | _"commit changes"_, _"stage and commit"_, _"/commit"_                      |
| [**feature-spec**](feature-spec/SKILL.md)                               | Plan features with lightweight specs, approval gates, and step-by-step progress tracking   | _"create a feature spec"_, _"spec out a feature"_                          |
| [**scaffolding-generator**](scaffolding-generator/SKILL.md)             | Detect existing code conventions and generate new components that match the codebase style | _"scaffold a component"_, _"generate boilerplate"_, _"add a new endpoint"_ |
| [**hypothesis-driven-debugging**](hypothesis-driven-debugging/SKILL.md) | Investigate failures through systematic minimal reproduction and multi-hypothesis testing  | _"debug"_, _"find root cause"_, _"troubleshoot"_                           |
| [**release-notes**](release-notes/SKILL.md)                             | Generate changelog entries from git history in Keep a Changelog format                     | _"write release notes"_, _"update the changelog"_                          |

### Documentation & Knowledge

| Skill                                                   | What it does                                                                                     | Try saying                                                               |
| ------------------------------------------------------- | ------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------ |
| [**doc-coauthoring**](doc-coauthoring/SKILL.md)         | Co-author technical docs through context gathering, iterative refinement, and reader testing     | _"write a design doc"_, _"create an RFC"_, _"draft a proposal"_          |
| [**postmortem**](postmortem/SKILL.md)                   | Write a blameless postmortem for a production incident with root-cause analysis and action items | _"write a postmortem"_, _"root cause analysis"_                          |
| [**stories-journal**](stories-journal/SKILL.md)         | Chronicle the project journey — capture decisions, dialogue, and progress as narrative entries   | _"write a story"_, _"create a journal entry"_, _"chronicle our session"_ |

### Analysis & Security

| Skill                                                                         | What it does                                                                                      | Try saying                                                                      |
| ----------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------- |
| [**semantic-codebase-intelligence**](semantic-codebase-intelligence/SKILL.md) | Map dependencies, detect architecture boundaries, score coupling/cohesion, and discover dead code | _"analyze codebase structure"_, _"map dependencies"_, _"find dead code"_        |
| [**security-threat-modeler**](security-threat-modeler/SKILL.md)               | Produce a STRIDE-based threat model with data-flow diagrams, trust boundaries, and mitigations    | _"threat model"_, _"security analysis"_, _"STRIDE analysis"_                    |
| [**agentic-eval**](agentic-eval/SKILL.md)                                     | Evaluate and improve AI agent outputs using self-critique loops and LLM-as-judge patterns         | _"build an evaluator-optimizer pipeline"_, _"create a rubric-based evaluation"_ |

### Technology Research

| Skill                                                                         | What it does                                                                                                               | Try saying                                                                                 |
| ----------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------ |
| [**microsoft-tech-deep-research**](microsoft-tech-deep-research/SKILL.md)     | Deep research for Microsoft technologies using Microsoft Learn MCP tools — .NET, Azure, Semantic Kernel, Agent Framework    | _"research this Microsoft SDK"_, _"verify Azure API"_, _"check .NET preview package"_      |
| [**tech-deep-research**](tech-deep-research/SKILL.md)                         | Deep research for any non-Microsoft technology — Python, Node, Java, Go, Rust, or any SDK/framework via web fetch          | _"research this Python library"_, _"verify Node package"_, _"investigate this Go module"_  |

### Template

| Skill                                                   | What it does                                                                  | Try saying                               |
| ------------------------------------------------------- | ----------------------------------------------------------------------------- | ---------------------------------------- |
| [**make-skill-template**](make-skill-template/SKILL.md) | Scaffold a new skill folder with a SKILL.md and optional resource directories | _"create a skill"_, _"make a new skill"_ |

---

## For Contributors

### How Skills Work

Each skill is a folder containing a `SKILL.md` file with YAML frontmatter (`name` + `description`) and structured instructions. Copilot automatically discovers skills and activates them when the user's request matches the description keywords.

```
.github/skills/
└── my-skill/
    ├── SKILL.md          # Required — frontmatter + instructions
    ├── scripts/          # Optional — helper scripts
    ├── references/       # Optional — reference docs
    └── assets/           # Optional — templates, examples
```

### Adding a New Skill

1. Check existing skills to avoid duplication
2. Create a new folder under `.github/skills/` with a kebab-case name
3. Add a `SKILL.md` with YAML frontmatter:
   ```yaml
   ---
   name: my-skill
   description: "Short description with trigger keywords. Use when asked to ..."
   ---
   ```
4. Write the skill instructions below the frontmatter
5. Optionally add `scripts/`, `references/`, or `assets/` subdirectories

> **Tip**: Use the `make-skill-template` skill — just ask Copilot to _"create a new skill"_ and it will scaffold everything for you.

### Updating an Existing Skill

1. Edit the `SKILL.md` file in the skill's folder
2. Update the `description` in the frontmatter if trigger keywords change
3. Keep instructions concise — prefer constraints over verbose instructions
