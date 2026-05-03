---
name: vision-scope
description: 'AI-assisted Vision & Scope document creation for Product Managers. Use when asked to "create a vision doc", "write a vision and scope", "vision document", "scope document", or "product vision". Guides PMs through context gathering and structured document generation with review. Keywords: vision, scope, strategy, product vision, initiative planning, roadmap.'
---

# Vision & Scope Skill: AI-Assisted Vision & Scope Document Creation

A structured workflow for Product Managers to create professional Vision & Scope documents with AI assistance. This is a standalone skill that orchestrates context gathering, document generation, and review for strategic, initiative-level documents.

## Core Principles
  
1. **Strategic altitude** — Vision & Scope documents operate above feature-level detail; stay at the initiative level
2. **Narrative flow** — The document tells a story: why → who → problem → scope → enablers → dependencies → constraints → risks → measurement
3. **No assumptions** — Gather context before writing; use [TBD] for unknowns
4. **Quality through review** — Every document goes through a write-review cycle
5. **PM-friendly** — No coding required; conversational workflow

---

## When to Use

- You need a Vision & Scope document for a new product, platform, or initiative
- You want to define the strategic "why", scope boundaries, and phased delivery before writing feature specs
- User asks to "create a vision doc", "write a vision and scope", "vision document", "product vision"

---

## Relationship to Feature Specs

Vision & Scope documents sit **above** feature specifications in the documentation hierarchy:

```
Vision & Scope Document (this skill)
  └── Feature Spec 1 (PM agent workflow) — from Phase 1 scope
  └── Feature Spec 2 (PM agent workflow) — from Phase 2 scope
  └── Feature Spec N ...
```

Each **phase** in the Vision & Scope document typically drives one or more Feature Specifications. The Vision & Scope sets direction; Feature Specs detail implementation.

---

## Document Structure (11 Sections)

| # | Section | Purpose |
|---|---------|---------|
| 1 | **Vision** | Aspirational future state — why this matters, where we're headed |
| 2 | **Who We Serve** | Target users, their needs, what success feels like for them |
| 3 | **The Problem** | Current state gaps, evidence, urgency |
| 4 | **Scope** | Phased delivery with success criteria per phase + out of scope |
| 5 | **What Makes This Possible** | Proof points, enabling technology, differentiators |
| 6 | **Key Dependencies** | Things we need **from others** to succeed |
| 7 | **Constraints** | Hard **non-negotiable limits** that shape design |
| 8 | **Risks and Mitigations** | Things that **could go wrong** and contingency plans |
| 9 | **How We Measure Success** | Metrics with "why it matters" rationale |
| 10 | **Open Questions** | Tracked unknowns with owners and due dates |
| 11 | **References** | Standards, research, prior art |

### Critical Boundaries: Sections 6, 7, 8

These three sections are adjacent and can blur. Enforce these rules:

| Section | Contains | Test | Does NOT Contain |
|---------|----------|------|-----------------|
| **6. Key Dependencies** | Things we need from others; "what" and "why it matters" | "Is this something another team/platform must deliver?" | Impact of dependency failure (that's a Risk) |
| **7. Constraints** | Fixed, non-negotiable rules with design implications | "Is this a hard rule we must work within, regardless?" | Uncertainties (those are Risks) |
| **8. Risks and Mitigations** | Things that might not go as planned, with mitigation and owner | "Is there uncertainty? Could this go wrong?" | Fixed rules (those are Constraints) or unresolved questions (those are Open Questions) |

---

## Workflow

### Phase 1: Context Gathering

Gather all the information needed to write the Vision & Scope document.

#### Context Sources (Priority Order)

1. **Auto-discover instructions file** — Check `my-specs/` for `vision-scope-instructions.md` first.

   - **Found** → Read it. Extract initiative summary, vision, users, scope, and context before asking questions.
   - **Not found in `my-specs/`** → Tell the PM and offer three options:
     > "I couldn't find a `vision-scope-instructions.md` in the `my-specs/` folder. Would you like me to:
     > 1. **Search** other folders in this repository for it
     > 2. **Provide the path** — tell me where your instructions file is
     > 3. **Skip it** — I'll gather context through conversation instead"

2. **Conversational discovery** — For any gaps not covered by the instructions file, ask questions **one at a time**. Never batch multiple questions in a single response.

3. **Tool-based research** — Use explore/search tools to understand existing context, strategy documents, or prior art.

4. **Reference materials** — If the instructions file lists reference documents (files, web pages, repositories):
   - Attempt to read or fetch each reference.
   - **If a reference cannot be read** (unsupported format, access denied, broken link, or any other reason): **STOP.** Do not assume, infer, or use data from other sources as a substitute. Inform the PM which references could not be read and why.
   - Work with the PM to resolve: they may convert the file, provide the data directly, paste relevant excerpts, or agree to proceed without it.
   - You may suggest alternatives (e.g., "Can you convert this RTF to plain text?" or "Can you paste the key data points from this document?"), but the PM decides how to proceed.
   - **Never silently skip a reference.** Every reference the PM provided was provided for a reason.

#### Discovery Questions

Ask about these topics (one at a time, skip what's already provided):

1. **Vision** — What is the aspirational future state? What does success look like?
2. **Who We Serve** — Who are the primary users? What do they need? What are their pain points?
3. **The Problem** — What's broken today? What gaps exist? Is there evidence or proof-of-concept results?
4. **Scope** — What's the phased delivery plan? What's in scope? What's explicitly out?
5. **What Makes This Possible** — What proof points or enabling technologies give confidence this will work?
6. **Key Dependencies** — What do you need from other teams, platforms, or organizations?
7. **Constraints** — What hard limits shape how this must be designed or delivered?
8. **Risks** — What are the biggest threats to success?
9. **How We Measure Success** — What metrics will tell you it's working?

#### Context Summary

When context gathering is complete, produce this summary:

```
## Context Summary

**Initiative**: [name]
**Vision**: [1-2 sentences]
**Primary Users**: [who]
**Problem**: [key gaps]
**Scope**: [phased plan summary]
**Enablers**: [proof points / technology]
**Dependencies**: [key external needs]
**Constraints**: [hard limits]
**Key Risks**: [top risks]
**Success Metrics**: [how measured]
**Source**: [vision-scope-instructions.md | conversational | mixed]
```

---

### Phase 2: Document Generation

Generate the Vision & Scope document following the template.

#### Template

Use `.github/skills/pm-spec/templates/vision-scope-template.md`. **Read the full template before writing.** Follow its structure exactly — all 11 sections in order plus Revision History.

#### Writing Standards

**Voice and Tone:**
- Strategic and forward-looking — this document inspires and aligns, not just informs
- Use direct, active voice: "We envision..." not "It is envisioned that..."
- Professional tone — reads as if authored by a senior PM or product leader
- Define acronyms on first use

**Writing Discipline:**
- Be assertive and direct. State facts, state the plan, move on.
- Use precise terminology. Say "network endpoint scanning" not "dynamic scanning." If a term is vague, make it specific.
- Never assign blame or point fingers at teams. Frame gaps as problems to solve, not failures to call out.
- Keep it short. If a sentence can be cut without losing meaning, cut it.
- Every sentence should earn its place. Connective phrases that guide the reader ("This is an opportunity to...", "When we succeed...") are fine. Phrases that repeat what's already evident ("It is worth noting," "As previously stated") are not.
- One idea per bullet. One purpose per paragraph.
- Tables over prose for structured data (dependencies, risks, metrics). Keep table cells short.
- Do not overstate the current state. If controls don't exist yet, say so plainly. If a baseline is in draft, say "drafted" not "established."

**Narrative Flow:**
- The document tells a story: urgency → audience → problem → plan → enablers → what we need → limits → risks → measurement
- Each section should flow naturally into the next
- The Vision section should make a reader care; The Problem section should make them feel urgency; Scope should give them confidence there's a plan

**Strategic Altitude:**
- Stay at the initiative level. Do NOT write detailed requirements — that's for Feature Specs
- Scope describes phases and success criteria, not exhaustive feature lists
- Metrics focus on "why it matters" alongside "what we measure"

**"Why It Matters" Pattern:**
- Key Dependencies, How We Measure Success — each entry should include a rationale, not just a label
- Use the "What / Why It Matters" table format from the template

**Section 6/7/8 Discipline:**
- Dependencies: state what we need and why. Do NOT describe failure scenarios here.
- Constraints: state hard rules and design implications. These are facts, not risks.
- Risks: state what could go wrong, impact, mitigation, and owner. This is the place for dependency-failure scenarios.

**Requirements Language:**
- Vision & Scope docs do NOT use "shall/must" requirements language — that belongs in Feature Specs
- Use declarative statements: "The platform provides...", "Developers will..."
- Use future-oriented framing: "When we succeed...", "Phase 1 delivers..."

#### Quality Checklist

Before signaling completion, verify:

- [ ] All 11 sections are populated (no empty sections)
- [ ] Vision section is inspiring, specific, and stands alone as a summary
- [ ] Who We Serve describes real users with concrete needs
- [ ] The Problem section has specific gaps and evidence
- [ ] Scope has phased delivery with "what success looks like" per phase
- [ ] Out of Scope is explicitly defined
- [ ] What Makes This Possible includes concrete proof points
- [ ] Dependencies, Constraints, and Risks have clear boundaries (no overlap)
- [ ] Dependencies have "Why It Matters" rationale
- [ ] Constraints are facts with design implications, not risks
- [ ] Risks have mitigation strategies and owners
- [ ] Metrics have "Why It Matters" rationale
- [ ] No fabricated names, dates, or metrics — use [TBD] for unknowns
- [ ] Consistent terminology throughout
- [ ] Professional tone — reads like a senior PM authored it

#### Output

Save the document to a location agreed with the PM. Default: `my-specs/[initiative-name]-vision-scope.md`

---

### Phase 3: Review

> ⚠️ Before reviewing, clear your writing persona. Evaluate the document as if written by a different author.

Review the generated document across these dimensions:

| Dimension | What to Look For |
|-----------|-----------------|
| **Completeness** | Are all 11 sections populated? Any gaps? |
| **Narrative Flow** | Does the document tell a compelling story from Vision through Measurement? |
| **Strategic Clarity** | Is the vision clear and inspiring? Does the problem create urgency? |
| **Scope Precision** | Are phase boundaries and out-of-scope sharp enough to prevent debates? |
| **Section Boundaries** | Are Dependencies, Constraints, and Risks cleanly separated with no overlap? |
| **Audience Fit** | Can leadership, engineering, and cross-functional teams all understand this? |
| **Consistency** | Does terminology stay consistent? Do sections contradict each other? |
| **Actionability** | Can teams use this to start planning feature specs and roadmaps? |
| **Measurability** | Do success metrics have "why it matters" rationale? |
| **Altitude** | Is the document at the right strategic level — not too detailed, not too vague? |

#### Severity Classification

| Severity | Criteria | Examples |
|----------|----------|----------|
| **Critical** | Document cannot serve its purpose as-is | Missing vision, no scope boundaries, contradictory objectives, section overlap between 6/7/8 |
| **Important** | Document usable but will cause confusion or misalignment | Vague success criteria, unclear user description, fuzzy phase boundaries |
| **Suggestion** | Improvement opportunity, not blocking | Better phrasing, additional context, formatting improvements |

#### Review Summary Format

```
## Review Summary

**Document**: [initiative name] — Vision & Scope
**Template Compliance**: [Fully compliant | Minor deviations | Significant gaps]

**Overall Assessment**: [1-2 sentences on document quality and readiness]

**Findings**:
- Critical: [count]
- Important: [count]
- Suggestions: [count]

**Verdict**: [Ready for PM review | Needs revision — [summary of what to fix]]
```

---

### Phase 4: Refinement

Address review findings by severity:
- **Critical findings** — Must fix before document is usable
- **Important findings** — Should address for quality
- **Suggestions** — Note for PM consideration

Maximum 3 review cycles. If not converging, escalate to PM.

---

### Phase 5: Present to PM

Only present the document to the PM after review confirms it meets quality standards.

---

## Write-Review Loop

**MANDATORY: Never present a document to the PM without review.**

After generating or updating a draft:

1. **Review** using the dimensions above — this step is NEVER optional
2. **Triage findings** using severity rules
3. **If critical/important findings exist**: Revise, then review again
4. **If no blocking findings**: Present to the PM for approval
5. **Maximum 3 iterations** — if not converging, escalate to PM with summary

---

## Completion Reporting

When the document is approved:

```
## Vision & Scope Document Complete

**Initiative**: [name]
**Iterations**: [number of write-review cycles]

**Document Location**: [file path]

**Review Summary**:
- Findings addressed: [count]
- Suggestions noted for future: [list if any]

**Next Steps**:
- Share with leadership and stakeholders for alignment
- Use phased scope to drive Feature Specification creation (use the PM agent's Feature Spec workflow)
- Revisit quarterly or when strategic direction changes
```

---

## Templates Reference

| Template | Purpose | Location |
|----------|---------|----------|
| Vision & Scope Document | The output document template | `.github/skills/pm-spec/templates/vision-scope-template.md` |
| Vision & Scope Instructions | PM context input template | `.github/skills/pm-spec/templates/vision-scope-instructions-template.md` |

---

## Writing Style Adaptation

When the PM provides writing samples in their instructions file:

1. Read the sample documents (1–2 max) before generating the document
2. Silently extract and adapt to these style dimensions:
   - Sentence rhythm (short and punchy vs. longer and flowing)
   - Voice (first person "we" vs. third person vs. direct address)
   - Problem framing approach (data-first, narrative, urgency-driven)
   - Formatting preferences (tables vs. bullets vs. prose paragraphs)
   - Tone (formal, conversational, assertive, confident)
   - What they avoid (buzzwords, hedging, passive voice, jargon)
3. Apply the PM's style to the generated document
4. **NEVER** override template structure, section order, or quality standards for style
5. **NEVER** compromise measurability, testability, or specificity for style
6. If the PM's style conflicts with a quality requirement, keep the quality requirement and express it in the PM's voice

**Style controls HOW we write. Quality standards control WHAT we write.**

If no writing samples are provided, use the default writing standards defined in this skill.

---

## Rules

- **One question at a time** — Never overwhelm the PM with multiple questions
- **Skip what's provided** — If vision-scope-instructions.md covers a topic, don't ask about it again
- **Don't fabricate** — If context is missing, mark as [TBD]
- **Stay at strategic altitude** — Do not drift into feature-level detail
- **Enforce section boundaries** — Dependencies ≠ Constraints ≠ Risks. Keep them clean.
- **Respect the PM's time** — Be efficient. Don't repeat questions.
- **Signal completion clearly** — When you have enough context, say so and present the summary
