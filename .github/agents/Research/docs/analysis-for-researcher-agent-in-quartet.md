# Analysis: Adding a Researcher Agent to the Dev Quartet

*From Trio (co-dev → coder → critic) to Quartet (co-dev → researcher → coder → critic)*

> **Status**: Analysis / Proposal
> **Date**: 2026-03-28
> **Scope**: `.github/agents/` — agent workflow architecture

---

## Why This Document Exists

The current dev agent workflow — **co-dev** (orchestrator), **coder** (implementer), **critic** (reviewer) — has a structural blind spot: **no agent verifies technology assumptions against current documentation before implementation begins**.

This analysis evaluates adding a dedicated **researcher agent** to the workflow, leveraging the existing `tech-deep-research` and `microsoft-tech-deep-research` skills. The conclusion: the Researcher should **always be part of the quartet** — it costs zero tokens when not invoked, and co-dev's per-task triage decides when to actually call it.

---

## Part 1 — The Current Dev Trio: Deep Analysis

### 1.1 co-dev.agent.md — The Orchestrator

**Role**: Collaborative development lead — **pure coordinator, writes zero code**.

**Core workflow (Multi-Pass, the default)**:

```
Decompose → Delegate to Coder (Draft) → Verify → Delegate to Critic (Correctness lens)
         → Delegate to Coder (Refine 1) → Verify → Delegate to Critic (Clarity lens)
         → Delegate to Coder (Refine 2) → Verify → Delegate to Critic (Edge Cases lens)
         → Delegate to Coder (Refine 3) → Verify → Delegate to Critic (Full lens)
         → Report Complete
```

**Key characteristics**:

- Decomposes work into independent tracks with dependency analysis
- Drives a structured 4-pass refinement loop (Draft → Correctness → Clarity → Edge Cases → Excellence)
- Triages critic findings by severity and confidence levels
- Escalates architectural decisions to human developers
- Manages three-layer context sharing protocol:
  - **Layer 1**: Project standards (always included)
  - **Layer 2**: Task context (included when relevant)
  - **Layer 3**: Exclusions (never passed to sub-agents)
- Supports parallel track execution when tasks are independent

**Agent delegation table** (from `co-dev.agent.md`):

| Operation | Agent | Notes |
|-----------|-------|-------|
| Code implementation | **Coder** | Always — never use built-in agents for coding |
| Code review | **Critic** | Always — never skip review |
| Test execution | **task** | Builds, tests, lints |
| Codebase exploration | **explore** | Finding files, searching code |
| Git write commands | **NEVER** | Prohibited for all agents |

**Critical observation**: co-dev has **no mechanism to trigger research before implementation**. The agent table has no research entry. When co-dev receives a task, it decomposes and immediately starts the code+critique loop. There is no "Step 0: Do we understand the technology well enough to implement correctly?" gate.

---

### 1.2 coder.agent.md — The Implementer

**Role**: Expert software engineer — **writes all the code**.

**Key characteristics**:

- 20+ years experience persona with production-grade quality standards
- Follows Multi-Pass lens discipline when directed by co-dev
- Zero-confirmation execution for routine patterns matching existing codebase conventions
- Escalates architectural decisions, approach selection, security implementations, and ambiguity to CoDev
- Reports discoveries back to CoDev (reference examples, patterns learned, potential pitfalls)
- Quality lenses: Draft → Correctness → Clarity → Edge Cases → Excellence

**Research-adjacent capability** (the only one):

> "Verify SDK/NuGet APIs exist: Before using any SDK method, enum, or property, check the package version in .csproj and search for existing usage in the codebase. If an API doesn't compile, search for alternatives rather than guessing at the correct name."

**Critical observation**: The Coder's "research" is purely **defensive and reactive** — it checks that an API exists *in the codebase* before calling it. This is not proactive technology investigation. When the Coder encounters an unfamiliar API, a preview package with changed signatures, or a new SDK not yet used in the repo, it must either:

1. Guess from training data (potentially stale for preview packages)
2. Escalate to CoDev — who also has no research tools
3. Attempt to compile and iterate on errors

None of these produce high-quality first-pass implementations for novel technology.

---

### 1.3 critic.agent.md — The Reviewer

**Role**: Expert code reviewer — **reads code, never writes it**.

**Key characteristics**:

- Lens-focused reviews matching Multi-Pass passes (Correctness → Clarity → Edge Cases → Full)
- Confidence scoring with explicit percentages (e.g., "Confidence: 92%")
- Severity classification: Critical (blocks merge) → Important (should fix) → Suggestion (note for future)
- Pattern verification against existing codebase conventions
- Consolidation principle for systemic issues (report root cause once, list all locations)
- Security exception: always reported regardless of current review lens

**Critical observation**: The Critic reviews what was **already written**. It evaluates code against its own knowledge and codebase patterns, but it **cannot verify correctness against current official documentation**. If the Coder uses a deprecated API based on stale training data, the Critic would only catch it if it independently knew the API was deprecated — it cannot look up the current docs to verify.

This creates a systemic risk: **both the Coder and the Critic share the same knowledge blindspot** — neither has access to current, authoritative documentation during the implementation/review loop.

---

## Part 2 — The Two Research Skills: Deep Analysis

### 2.1 tech-deep-research (Non-Microsoft Technologies)

**Purpose**: Structured 4-step research protocol for **any non-Microsoft technology** — Python, JavaScript/Node, Java, Go, Rust, Ruby, PHP, and any SDK/framework/library not from Microsoft.

**The 4-step protocol**:

| Step | Action | Output |
|------|--------|--------|
| **1. Inspect Repository** | Read manifest files, flag pre-release packages, identify ecosystem, group related packages | Ground truth: what's actually in use |
| **2. Search Official Docs** | Fetch vendor docs, check `llms.txt`, use web tools | Authoritative API/pattern guidance |
| **3. Web Research for Gaps** | GitHub repos, registries, changelogs, security advisories | Fill holes in official docs |
| **4. Research Summary** | Structured output with versions, stability, findings, confidence, gaps, sources | Decision-ready artifact |

**Depth levels**:

| Depth | Time | When |
|-------|------|------|
| Quick Check | ~2 min | Known tech; confirm version or API |
| Spot Check | ~5 min | Review/audit — verify key claims only |
| Full Research ⭐ default | ~10 min | New packages, upgrades, pre-release, large features |

**Source priority**: Repo code > Official vendor docs > Official samples > Registry metadata > Release notes > Ecosystem sources.

**Key design strength**: The protocol teaches *how* to research, not *what* to research. It's future-proof — when new ecosystems appear, the same 4 steps apply.

---

### 2.2 microsoft-tech-deep-research (Microsoft Technologies)

**Purpose**: Same 4-step protocol, specialized for **Microsoft technologies across all languages** (C#, Python, JS/TS, Java, Go).

**What makes it different from the non-Microsoft skill**:

| Aspect | tech-deep-research | microsoft-tech-deep-research |
|--------|-------------------|------------------------------|
| **Step 2 tools** | Web fetch (generic) | Microsoft Learn MCP tools (structured, authoritative) |
| **Doc sources** | Vendor sites, readthedocs, pkg docs | `learn.microsoft.com` via MCP |
| **Code samples** | GitHub, vendor examples | `microsoft_code_sample_search` with language filter |
| **Special concerns** | General pre-release handling | Agent framework volatility, service renaming, Preview SDK mode |
| **Disambiguation** | Straightforward naming | "agents" means 5 different things in Microsoft context |

**MCP tools available**:

- `microsoft_docs_search` — targeted search against Microsoft Learn
- `microsoft_docs_fetch` — full-page retrieval of documentation
- `microsoft_code_sample_search` — official code snippets filtered by language

**High-volatility areas requiring Full Research**: Microsoft Agent Framework (`Microsoft.Agents.AI`), Semantic Kernel, AutoGen, Azure AI Foundry Agent Service, `Microsoft.Extensions.AI`.

**Key design strength**: MCP tools provide **structured, authoritative results** — significantly higher quality than generic web fetch for Microsoft technology. The skill also handles the uniquely Microsoft challenge of overlapping frameworks with unclear boundaries.

---

### 2.3 How the Two Skills Relate

They are **complementary halves** of a unified research capability:

- ✅ Same 4-step structure
- ✅ Same depth levels (Quick/Spot/Full)
- ✅ Same quality standards and source priority rules
- ✅ Same Research Summary output format
- ✅ Same time-boxing guidelines
- ✅ Explicit vendor identification rules to pick the correct skill
- ✅ Cross-reference each other: "Mixed vendor task → run **both** in parallel"
- ❌ Different tool chains (web fetch vs Microsoft Learn MCP)
- ❌ Different caution zones (general pre-release vs Microsoft Preview SDK mode)

---

## Part 3 — The Gap: Why This Improvement Matters

### 3.1 The Structural Blind Spot

The current trio workflow has a fundamental gap in its information flow:

```
Task arrives → co-dev decomposes → Coder implements (from training data) → Critic reviews (from training data)
                                    ↑                                       ↑
                                    No current doc verification              No current doc verification
```

**Both the Coder and the Critic operate from the same potentially stale knowledge base.** When the technology is stable and well-known, this works fine. When the technology is pre-release, fast-moving, or recently updated, this creates compounding errors:

1. Coder implements using a deprecated/changed API
2. Critic doesn't catch it (same stale knowledge)
3. The code compiles but uses suboptimal or incorrect patterns
4. Issues surface later in integration testing or production

### 3.2 Concrete Scenarios Where Research Would Prevent Failures

| Scenario | Current Behavior | With Researcher |
|----------|-----------------|-----------------|
| Task uses preview SDK (`Microsoft.Agents.AI` RC) | Coder implements from training data; may use deprecated/changed APIs that existed in earlier previews | Researcher verifies current API surface against docs; Coder receives accurate API guidance |
| Task requires new NuGet/npm package | Coder guesses at API patterns from training data | Researcher inspects registry, fetches docs, produces verified API summary |
| Task involves version upgrade | No one checks migration guides or breaking changes | Researcher checks CHANGELOG, migration guides, flags breaking changes |
| Mixed vendor stack (Azure OpenAI + LangChain) | No cross-vendor compatibility verification | Researcher runs both skills in parallel, cross-references version compatibility |
| Critic reviews code using preview SDK | Reviews against training knowledge; can't verify current API surface | Researcher's summary is available as reference; co-dev can share with Critic too |
| Technology recently renamed/reorganized | Coder uses old names/paths from training data | Researcher discovers current naming via live docs |

### 3.3 Where Research Already Exists (But Outside the Trio)

The **teaching-mode agents** (`Microsoft-teaching-mode-coder.agent.md`, `teaching-mode-coder.agent.md`) already embed research:

- Research Triage system (Skip / Quick Check / Full Research)
- Direct reference to their correlated research skills
- Research *before* implementing

But these agents are **standalone workflows** — they operate independently and do not participate in the co-dev orchestration loop. The co-dev agent has no mechanism to invoke them as part of its coordination workflow. This means:

- The research capability **exists** in the repository
- The research capability is **inaccessible** from the primary quality-focused workflow (co-dev trio)
- Users must manually choose between "quality workflow (trio) without research" or "research workflow (teaching-mode) without structured critique"

A researcher agent would bridge this gap — bringing research into the quality-focused workflow.

---

## Part 4 — Critical Analysis: Should We Create researcher.agent.md?

### 4.1 Arguments FOR a Researcher Agent

**1. Separation of concerns**

Research is a cognitively distinct task from implementation and code review. It involves:
- Loading and parsing documentation pages
- Comparing package versions across registries
- Cross-referencing multiple sources for consistency
- Assessing stability levels and migration paths

A dedicated agent can be optimized for this specific task without compromising the Coder's implementation focus or the Critic's review discipline.

**2. Context window efficiency**

Research requires loading docs, comparing versions, cross-referencing sources — all of which consume context tokens. Keeping research in a separate agent means:
- The Researcher's context is dedicated to doc analysis (not cluttered with code)
- The Coder receives a clean, structured summary (not raw research artifacts)
- The Critic isn't burdened with research context during reviews

**3. Conditional execution**

Not every task needs research. A dedicated agent allows co-dev to make a clear decision:
- "This is established patterns → skip researcher, go directly to Coder"
- "This uses preview SDK → run Researcher first, then inject findings into Coder context"

This conditionality is cleaner with a separate agent than with embedded research logic.

**4. The skills already exist and are battle-tested**

`tech-deep-research` and `microsoft-tech-deep-research` are comprehensive, well-structured protocols. The researcher agent would **orchestrate** them — it wouldn't need to reinvent research methodology. This dramatically reduces the effort to create the agent.

**5. Fills a real, demonstrated gap**

The trio currently has a blind spot for technology correctness that neither the Coder (defensive checks only) nor the Critic (reviews from training knowledge only) can fill. This isn't theoretical — it's the kind of gap that produces subtle bugs that pass all review passes.

### 4.2 Arguments AGAINST a Researcher Agent

**1. Latency tax per invocation**

Invoking the Researcher before a Coder delegation adds execution time. If co-dev calls the Researcher before every Coder delegation, even trivial tasks (e.g., adding a config value) become slower.

*Mitigation*: This is a per-task decision, not a per-codebase decision. Co-dev's triage should skip the Researcher for ~60-70% of tasks. The Researcher costs zero tokens when not invoked — the latency tax only applies to tasks where co-dev explicitly triggers research.

**2. Orchestration complexity**

co-dev already manages 4 Multi-Pass iterations × 2 agents (Coder + Critic). Adding a third specialist means:
- New decision tree: "Does this need research? Microsoft or non-Microsoft or both?"
- New handoff format: Researcher → co-dev → Coder (research findings must be translated into implementation context)
- New failure modes: What if the Researcher can't reach docs? What if findings contradict the Coder's assumptions mid-implementation?

*Mitigation*: The Researcher's output format is already defined by the skills. Failure produces a partial summary with `Confidence: Low`, never blocking the pipeline.

**3. Teaching-mode agents already solve this**

`opus-msft-teaching-mode-coder.agent.md` has research triage built in. Instead of adding a 4th agent, co-dev could delegate to a teaching-mode coder *instead of* the base coder when research is needed.

*Counter-argument*: Teaching-mode agents combine research + implementation in one context window, which is less efficient for large research tasks. They also don't support the structured Multi-Pass refinement loop that co-dev drives.

**4. The Coder could just invoke the skills directly**

Skills are invokable by any agent. The Coder could be instructed: "Before implementing, if the task involves preview/unfamiliar tech, invoke the appropriate research skill."

*Counter-argument*: This bloats the Coder's context with research artifacts, mixes concerns (research + implementation in one agent), and makes it harder for co-dev to manage the workflow.

**5. Research output is perishable**

In a 4-pass Multi-Pass workflow, research done before the Draft pass may be stale by the time Refine 3 runs (though this is unlikely within a single session).

*Counter-argument*: Research output is consumed once (before Draft) and injected as Layer 2 context. It doesn't need to be "fresh" for refinement passes — the API facts don't change within a session.

**6. The real problem may be co-dev's triage**

co-dev doesn't assess technology novelty before starting. Adding a triage step to co-dev ("Is this task using known or novel tech?") might solve 80% of the problem without a new agent.

*Counter-argument*: Triage alone tells co-dev *that* research is needed, but doesn't *do* the research. Something still has to execute the protocol. A dedicated agent is the cleanest executor.

### 4.3 Alternative Approaches Considered

| Alternative | Description | Pros | Cons |
|-------------|-------------|------|------|
| **A: Enhance co-dev's triage** | Add "Step 0: Technology Assessment" to co-dev; use `explore` agent to check versions | Minimal change, no new agent | co-dev can't execute research protocol; explore agent isn't designed for it |
| **B: Enhance Coder with research** | Add conditional research skill invocation to Coder's Step 1 | No new agent; research happens where it's needed | Bloats Coder's context; mixes concerns; harder for co-dev to manage |
| **C: Researcher agent** ⭐ | Dedicated agent with both skills; co-dev orchestrates conditionally; always available, zero cost when not invoked | Cleanest separation; optimal context usage; zero overhead for stable-tech tasks | 42-line overhead in always-loaded agents; new agent file to maintain |
| **D: Teaching-mode Coder swap** | co-dev delegates to teaching-mode coder for novel tasks | No new agent; research is embedded | Loses Multi-Pass refinement; teaching-mode workflow is different |

---

## Part 5 — Verdict and Recommendation

### Assessment: **Always include the Researcher in the quartet — it costs nothing when unused**

The key insight from implementation testing (Section 7.3) is that having the Researcher available and actually invoking it are **two separate decisions with very different costs**:

| Decision | Cost | Who decides |
|----------|------|-------------|
| **Having** the Researcher in the quartet | 42 lines (~3.4%) of conditional content in co-dev/coder/critic. Zero tokens for the Researcher agent itself when not invoked. | You (once, at setup) |
| **Invoking** the Researcher for a specific task | Real token + latency cost — justified per-task by co-dev's triage. | co-dev (per-task) |

The cost of having the Researcher available is the cost of a fire extinguisher on the wall — negligible to keep, expensive to not have when you need it. Technology evolves fast; a "stable" package today can release breaking changes tomorrow. The Researcher should **always be available**; co-dev decides **per-task** whether to invoke it.

This means the old question was wrong:
- **Wrong question**: "Should we add a Researcher agent to this codebase?" (conditional on codebase profile)
- **Right question**: "Should co-dev invoke the Researcher for this specific task?" (conditional on task novelty)

The quartet is the standard architecture. Co-dev's triage is the throttle.

### Design Constraints (Non-Negotiable)

1. **Must be truly optional per-task** — co-dev must have a fast triage (< 30 seconds) to decide "skip researcher for this task." The Researcher is always *available* but not always *invoked*. Most tasks (~60-70%) will skip it.

2. **Must be lean** — The researcher produces a structured Research Summary (exactly the format the skills already define) and nothing more. No implementation advice. No code suggestions. Pure facts + confidence levels.

3. **Output designed for co-dev consumption** — co-dev translates research findings into Layer 2 context for the Coder. The handoff protocol matters more than the agent itself.

4. **Does NOT participate in the Multi-Pass loop** — Research happens once, before Draft. Not re-invoked for refinement passes (unless co-dev decides mid-implementation that research was insufficient).

5. **Failure never blocks** — If research fails (no web access, docs unavailable), the researcher produces a partial summary with `Confidence: Low` and `Gaps: [what couldn't be verified]`. The pipeline continues.

### When co-dev Should Invoke Research (Per-Task Triage)

| Codebase Characteristic | Research Invocation Rate | co-dev Triage Guidance |
|------------------------|------------------------|-----------------------|
| Uses many preview/pre-release packages | ~30–40% of tasks | Invoke for any task touching preview packages |
| Mixed vendor stack (Microsoft + OSS) | ~20–30% of tasks | Invoke when task crosses vendor boundaries |
| Fast-moving AI/agent frameworks | ~30–40% of tasks | Invoke for any task touching agent/AI packages |
| 1–2 fast-moving deps, rest stable | ~10–15% of tasks | Invoke only for tasks touching those specific deps |
| Stable, well-known tech only | ~5% of tasks | Invoke for version upgrades, new packages, security concerns |

**Note**: Even "stable" codebases benefit from having the Researcher available. Packages release breaking changes, deprecate APIs, and publish security advisories. The cost of having the Researcher ready (42 lines, 3.4% overhead) is trivially small compared to the cost of implementing against stale API knowledge even once.

### Recommended Architecture

```
co-dev receives task
  │
  ├─ Triage: "Does this need research?"
  │   ├─ NO (established patterns, trivial change) → Skip to Coder
  │   └─ YES (new/preview/unfamiliar tech, version upgrade, new package)
  │       ├─ Microsoft tech?     → Researcher invokes microsoft-tech-deep-research
  │       ├─ Non-Microsoft tech? → Researcher invokes tech-deep-research
  │       └─ Mixed vendor?       → Researcher runs BOTH skills in parallel
  │
  ├─ Researcher produces Research Summary
  │   └─ co-dev injects summary into Coder's Layer 2 context
  │
  └─ Normal Multi-Pass loop: Coder (Draft) → Critic → Coder (Refine) → Critic → ...
```

### Updated Agent Table for co-dev

| Operation | Agent | Notes |
|-----------|-------|-------|
| Technology research | **Researcher** | Optional — when task involves novel/preview/unfamiliar tech |
| Code implementation | **Coder** | Always — never use built-in agents for coding |
| Code review | **Critic** | Always — never skip review |
| Test execution | **task** | Builds, tests, lints |
| Codebase exploration | **explore** | Finding files, searching code |
| Git write commands | **NEVER** | Prohibited for all agents |

---

## Part 6 — Implementation Design Decisions

If proceeding with the researcher agent, these decisions should be made:

### 6.1 Naming

`researcher.agent.md` — clear, consistent with the existing trio naming convention (`co-dev`, `coder`, `critic`, `researcher`).

### 6.2 Skill Selection Logic

The researcher should contain the vendor identification rules from both skills:

| Signal | Route to |
|--------|----------|
| `Azure.*`, `Microsoft.*`, `@azure/`, `com.azure:`, `azure-*` namespaces | `microsoft-tech-deep-research` |
| Documented on `learn.microsoft.com` | `microsoft-tech-deep-research` |
| Everything else | `tech-deep-research` |
| Mixed vendor task | Both skills in parallel |

### 6.3 Triage Ownership

**co-dev decides** whether research is needed. The researcher should always do real research when invoked — don't add triage logic in two places (that creates conflicting decisions).

### 6.4 Output Contract

The Research Summary format is already well-defined by both skills. The researcher outputs exactly that format, making it easy for co-dev to parse and inject into the Coder's Layer 2 context:

```
### Research Summary — [Technology/Package Name]

**Package version in repo**: X.Y.Z
**Latest available**: X.Y.Z (via [source])
**Stability**: Preview / Stable / GA
**Key findings**: [...]
**Version-sensitive warnings**: [...]
**Confidence**: High / Medium / Low
**Gaps**: [What couldn't be verified]
**Sources**: [URLs consulted]
```

### 6.5 Failure Mode

If research fails (no web access, docs unavailable, MCP tools unreachable):
- Produce a partial summary with `Confidence: Low`
- Document `Gaps: [what couldn't be verified]`
- **Never block the pipeline** — co-dev proceeds with what's available
- co-dev may note the low confidence for human attention

### 6.6 Re-invocation Protocol

The researcher runs **once before Draft** by default. co-dev may re-invoke if:
- The Coder discovers mid-implementation that research was insufficient
- The Coder escalates with "I can't find this API — is it correct?"
- A new technology concern emerges from Critic findings

---

## Summary

The dev trio (co-dev → coder → critic) delivers **excellent code quality through structured refinement**, but it has no mechanism to verify technology assumptions against current documentation. This blind spot exists regardless of how "stable" the tech stack appears — technology evolves fast, and what's stable today can change tomorrow.

A dedicated **researcher agent**, orchestrated conditionally by co-dev and leveraging the two existing research skills, fills this gap with minimal disruption to the proven Multi-Pass workflow. The key is keeping it **always available** (zero cost when not invoked), **lean** (structured summary, no code), and **non-blocking** (graceful degradation on failure).

The quartet — **co-dev → researcher (always available, invoked per-task) → coder → critic** — is the standard architecture. Co-dev's per-task triage is the throttle that prevents unnecessary research overhead.

---

## Part 7 — Implementation Insights & Refinements

*Added after building and testing the quartet.*

### 7.1 Clean Orchestrator Pattern — Zero Researcher Coupling

During implementation, an important architectural insight emerged: **Coder and Critic should have zero knowledge that a Researcher agent exists.**

**Problem discovered**: Initial implementation had Coder and Critic system-reminders saying "Researcher is your upstream partner... All research requests go through CoDev, never directly to Researcher." This created a leaky abstraction — sub-agents knew about implementation details of the orchestration.

**Solution applied**: All references to "Researcher" were removed from Coder and Critic. They now only know about:
- **CoDev** (their orchestrator)
- **Research Findings** (a type of verified context CoDev may provide)
- **Each other** (Coder ↔ Critic as partners)

The communication architecture is now a clean hub-and-spoke:

```
                    co-dev (knows all 3 agents)
                   /         |         \
            Researcher     Coder      Critic
            (knows CoDev)  (knows     (knows
                            CoDev +    CoDev +
                            Critic)    Coder)
```

**Why this is better**: The "never directly to Researcher" guardrail was a rule for a coupling that shouldn't exist. By removing the coupling itself, the guardrail becomes unnecessary. Coder and Critic can't contact an agent they don't know about.

### 7.2 Researcher Agent — Lean by Design

The Researcher agent was trimmed from 407 lines to 359 lines (12% reduction) by removing content that duplicated the skills or other sections within the same file:

| Section Removed/Compressed | Lines Saved | Reason |
|---------------------------|-------------|--------|
| **Guiding Principles** (6 expanded subsections → 1 paragraph) | ~17 | All 6 principles were already in the system-reminder or Skill Reference Banner |
| **Research Self-Check** (8-item table → 2-line note) | ~15 | Every check restated the Output Contract, Confidence Scoring, or anti-patterns |
| **LLM Operational Constraints** (2 subsections → 1) | ~11 | Removed generic "token management" advice; kept researcher-specific MCP fallback and error recovery |
| **Experienced Behaviors** (10 rows → 4 rows) | ~9 | Removed behaviors already covered by anti-patterns, skills, or output contract fields |

**Principle applied**: Every line must earn its place. If it duplicates the skills (which define methodology) or another section in the same file, it goes.

### 7.3 Context Window Efficiency

A critical concern: does the quartet impose context window overhead for simple, stable-tech tasks?

**Files that cost ZERO tokens when research is skipped:**

| File | Lines | Loaded When |
|------|-------|-------------|
| `researcher.agent.md` | 359 | Only when co-dev invokes Researcher |
| `tech-deep-research/SKILL.md` | 135 | Only when Researcher reads it |
| `microsoft-tech-deep-research/SKILL.md` | 118 | Only when Researcher reads it |
| **Total deferred** | **612** | **Zero cost for stable tech** |

**Residual overhead in always-loaded agents:**

| File | Dead-weight lines | % of file | Content |
|------|-------------------|-----------|---------|
| co-dev | 32 | ~8.6% | "After Research Completes", delegation template, re-invocation logic |
| coder | 4 | ~1.5% | Conditional "If CoDev included Research Findings..." |
| critic | 6 | ~1.5% | Conditional "When CoDev includes Research Findings..." |
| **Total** | **42** | **~3.4%** | Self-gating conditional content |

**Conclusion**: 612 lines of research infrastructure cost exactly zero tokens for stable-tech tasks. The 42-line overhead (3.4%) is the minimum needed for co-dev's triage logic and conditional awareness in Coder/Critic. All conditional content uses "if/when" guards that agents mentally skip when no research context is present.

### 7.4 Research Findings Flow — The Delegation Template Mechanism

Research findings reach Coder and Critic through a structured injection pipeline, not through direct agent communication:

```
Researcher                    Co-dev                        Coder / Critic
    │                            │                               │
    ├─ Research Summary ────────▶│                               │
    │  + "For CoDev" notes       │                               │
    │                            ├─ Extract key findings         │
    │                            │                               │
    │                            ├─ "### Research Findings" ────▶│ Coder: primary API ref
    │                            ├─ "### Research Findings" ────▶│ Critic: verification baseline
    │                            │                               │
    │                            │◀── Escalation (if needed) ───┤
    │◀── Re-research (if needed)─┤                               │
```

Co-dev physically includes findings as a named `### Research Findings` section in every delegation to Coder and Critic. They don't need to "fetch" research — it arrives in their task context. This keeps the architecture stateless and explicit.

### 7.5 Re-Research Safety Limit

A maximum of **2 re-research attempts** per task was added to co-dev. If research still doesn't resolve the issue after 2 rounds, co-dev escalates to human — the documentation may be genuinely incomplete or the API may have undocumented changes. This prevents infinite research loops.

---

## Part 8 — Verification Test Results

*All tests passed on 2026-03-28.*

### Test 1: Stable Tech Path (No Research)

**Scenario**: "Fix the null reference exception in a data store's GetAsync method when the entity is not found"

| Step | Check | Result |
|------|-------|--------|
| Co-dev triage | Correctly identifies as "Established patterns only" → Skip Researcher | ✅ PASS |
| Coder delegation | Research Findings section is empty/absent (conditional not triggered) | ✅ PASS |
| Coder implements | "Check for Research Findings" is conditional — not activated | ✅ PASS |
| Critic delegation | Research Findings section is empty/absent | ✅ PASS |
| Critic reviews | "Research Findings Awareness" does not activate | ✅ PASS |
| Researcher loaded? | **No** — zero context tokens consumed | ✅ PASS |
| "Researcher" mentioned? | Zero mentions in Coder or Critic at any point | ✅ PASS |

**Verdict**: Stable-tech tasks flow through the quartet with **zero research overhead or awareness**.

### Test 2: Preview Tech Path (Full Research)

**Scenario**: "Add a new storage provider using a preview SDK package (e.g., a 4.x-preview NuGet package)"

| Step | Check | Result |
|------|-------|--------|
| Co-dev triage | Preview package → Full Research (force) | ✅ PASS |
| Co-dev → Researcher | Delegation template with correct depth, vendor identification | ✅ PASS |
| Researcher execution | Vendor identification → appropriate research skill, produces Research Summary with "For CoDev — Context Injection Notes" | ✅ PASS |
| Co-dev extracts findings | "After Research Completes" protocol, extracts API names/patterns/warnings | ✅ PASS |
| Co-dev → Coder | Research Findings section populated in delegation template | ✅ PASS |
| Coder uses findings | "Use those findings as your primary API reference" | ✅ PASS |
| Co-dev → Critic | Research Findings section populated in delegation template | ✅ PASS |
| Critic verifies | "Research Findings Awareness" activates, flags deviations | ✅ PASS |
| Coder → Researcher coupling | Zero — Coder escalates to CoDev only | ✅ PASS |
| Critic → Researcher coupling | Zero — Critic escalates to CoDev only | ✅ PASS |

**Verdict**: Full research path works end-to-end with proper findings flow and **zero direct Coder/Critic → Researcher coupling**.

### Test 3: Decoupling Verification

| Pattern Searched | coder.agent.md | critic.agent.md |
|-----------------|----------------|-----------------|
| "Researcher" (capital R) | 0 matches | 0 matches |
| "researcher" (lowercase) | 0 matches | 0 matches |
| "research agent" | 0 matches | 0 matches |
| "researcher.agent.md" | 0 matches | 0 matches |
| `.github/skills/*` | 0 matches | 0 matches |

**Present as expected**: "Research Findings" (the data) ✅, "CoDev" (the orchestrator) ✅, "additional research" (escalation to CoDev) ✅

**Verdict**: **Complete architectural decoupling** — Coder and Critic have zero knowledge the Researcher agent exists.

### Test 4: Context Window Efficiency

| Component | Lines | Cost for stable tech |
|-----------|-------|---------------------|
| Researcher + 2 skills | 612 lines | **0 tokens** (never loaded) |
| Dead weight in co-dev/coder/critic | 42 lines | **3.4% overhead** (conditional, self-gating) |

**Verdict**: The quartet adds **zero context cost** for the majority (~60-70%) of tasks that use stable technology.

---

## Updated Summary

The dev trio (co-dev → coder → critic) delivers **excellent code quality through structured refinement**, but it has no mechanism to verify technology assumptions against current documentation. This blind spot is universal — even "stable" packages deprecate APIs, publish security advisories, and introduce breaking changes.

A dedicated **researcher agent**, orchestrated conditionally by co-dev and leveraging the two existing research skills, fills this gap with minimal disruption to the proven Multi-Pass workflow. The key design decisions — validated by testing — are:

1. **Always available** — the Researcher is part of the quartet for all codebases. It costs zero tokens when not invoked (612 lines deferred). Only 42 lines (3.4%) of conditional content in always-loaded agents.
2. **Invoked per-task** — co-dev triages each task in < 30 seconds. Most tasks (~60–70%) skip research entirely.
3. **Lean** — 359-line agent that delegates methodology to existing skills; zero duplication
4. **Decoupled** — Coder and Critic have zero knowledge the Researcher exists; they only see "Research Findings" from CoDev
5. **Non-blocking** — graceful degradation on failure; max 2 re-research attempts before human escalation

The quartet — **co-dev → researcher (always available, invoked per-task) → coder → critic** — is the standard architecture. Technology moves fast; the Researcher is the safety net that catches stale assumptions before they become bugs.
