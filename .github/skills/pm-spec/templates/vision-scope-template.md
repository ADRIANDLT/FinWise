# Vision & Scope Document Template

**Purpose:** Strategic product or initiative-level document — defines the "why", boundaries, and high-level "what" before detailed specifications are written  
**Typical Use Cases:** New products, platform initiatives, major pivots, multi-quarter programs, product strategy alignment  
**Audience:** Leadership, cross-functional stakeholders, product teams, engineering leadership  

**Estimated Completion Time with AI:** 45–90 minutes  
**Recommended AI Tool:** GitHub Copilot (VS Code Agent Mode or CLI) with the Vision & Scope Skill

---

## 1. Vision

[The aspirational future state this initiative creates. Start with the problem's urgency, then paint the picture of what the world looks like when you've succeeded. This section should inspire and align — a reader who only reads this section should understand why this initiative matters and where it's headed. 2–4 paragraphs.]

---

## 2. Who We Serve

[Identify the primary audience for this initiative. Be specific about who they are, what they need, and what success feels like from their perspective.]

**[Primary audience]** — [Role/Description]

[1–2 paragraphs describing these users: their expertise, their context, and what they need from this initiative.]

When we succeed, [describe what the experience looks like for these users — make it concrete and measurable].

---

## 3. The Problem

[Describe the current state and why it's unacceptable. Be specific about gaps, pain points, and evidence. This section grounds the initiative in reality — it should make the reader feel the urgency.]

**Today:**

- [Gap or pain point 1]
- [Gap or pain point 2]
- [Gap or pain point 3]
- [Gap or pain point 4]

[If you have proof points, evidence, or proof-of-concept results that validate the opportunity, include them here. 1–2 paragraphs.]

---

## 4. Scope

[Phased delivery plan. Each phase builds on proven results from the previous one. Include "what success looks like" for each phase so teams know when to move forward.]

### Phase 1: [Name] — [Timeline]

[1–2 sentences describing the phase's purpose and delivery model.]

**What success looks like:** [Concrete, measurable outcomes that signal this phase is complete.]

### Phase 2: [Name] — [Timeline]

[1–2 sentences describing the phase's purpose and delivery model.]

**What success looks like:** [Concrete, measurable outcomes that signal this phase is complete.]

### Phase 3: [Name] — [Timeline]

[1–2 sentences describing the phase's purpose and delivery model.]

**What success looks like:** [Concrete, measurable outcomes that signal this phase is complete.]

### Out of Scope

[What this initiative will NOT address. Prevents scope creep and sets expectations.]

1. [Exclusion 1 — and why it's excluded]
2. [Exclusion 2 — and why it's excluded]

---

## 5. What Makes This Possible

[What proof points, enabling technologies, or differentiators give confidence this initiative will succeed? This section bridges the gap between "we should do this" and "we can do this." Include proof-of-concept results, platform capabilities, team expertise, or market conditions that enable the vision.]

- [Enabler or proof point 1]
- [Enabler or proof point 2]
- [Enabler or proof point 3]

[1–2 paragraphs on the primary enabling technology, partnership, or capability — and what the initiative's success depends on.]

---

## 6. Key Dependencies

[Things we need **from others** to succeed. These are external — other teams, platforms, or organizations must deliver for us to achieve our vision.]

| What | Why It Matters |
|------|---------------|
| [Dependency 1] | [Why this is critical to the initiative's success] |
| [Dependency 2] | [Why this is critical to the initiative's success] |
| [Dependency 3] | [Why this is critical to the initiative's success] |

> **Boundary:** Dependencies describe what we need from others. If a dependency fails to deliver, the *impact and contingency plan* belongs in section 8 (Risks and Mitigations), not here.

---

## 7. Constraints

[Hard, non-negotiable limits imposed on the initiative. These are facts that shape design decisions — not risks (which are uncertain). Constraints don't have mitigations; they have design implications.]

- **[Constraint 1].** [Design implication — how this shapes our approach.]
- **[Constraint 2].** [Design implication — how this shapes our approach.]
- **[Constraint 3].** [Design implication — how this shapes our approach.]

> **Boundary:** Constraints are fixed rules we must work within. If something *might* go wrong, it's a risk (section 8), not a constraint.

---

## 8. Risks and Mitigations

[Things that **could go wrong** — including dependency failures, adoption challenges, technical unknowns, and organizational risks. Each risk has a mitigation strategy and an owner.]

| Risk | Impact | Probability | Mitigation Strategy | Owner |
|------|--------|-------------|---------------------|-------|
| [Risk 1] | High/Med/Low | High/Med/Low | [Strategy] | [Who] |
| [Risk 2] | High/Med/Low | High/Med/Low | [Strategy] | [Who] |
| [Risk 3] | High/Med/Low | High/Med/Low | [Strategy] | [Who] |

> **Boundary:** Risks involve uncertainty. Fixed limits belong in Constraints (section 7). Unresolved questions belong in Open Questions (section 10).

---

## 9. How We Measure Success

[Metrics that tell us whether the initiative is working. Each metric should justify why it matters — not just what it measures.]

| What We Measure | Why It Matters |
|----------------|---------------|
| [Metric 1] | [Why this metric indicates success] |
| [Metric 2] | [Why this metric indicates success] |
| [Metric 3] | [Why this metric indicates success] |
| [Metric 4] | [Why this metric indicates success] |

---

## 10. Open Questions

[Unresolved strategic or scoping questions that need answers before or during execution.]

| # | Question | Owner | Due Date | Status |
|---|----------|-------|----------|--------|
| 1 | [Question] | [Who] | [When] | Open |
| 2 | [Question] | [Who] | [When] | Open |
| 3 | [Question] | [Who] | [When] | Open |

---

## 11. References

[Links or file paths to standards, research, strategy documents, or prior art that inform this document.]

- [Reference 1]
- [Reference 2]

---

## Revision History

| Date | Version | Author | Changes |
|------|---------|--------|---------|
| YYYY-MM-DD | 1.0 | [Your Name] | Initial vision & scope document |

---

*This is a living document. It will evolve as we learn from each phase, as platforms mature, and as the landscape changes.*

---

**Instructions for Use with AI Tools:**

1. **Use the Vision & Scope Skill** — ask the PM Lead agent or Copilot to "create a vision doc" or "write a vision and scope" for your initiative
2. **Create an instructions.md** file with your initiative context (see `.github/skills/pm-spec/templates/vision-scope-instructions-template.md`)
3. **The AI generates your document** using this template's structure — review and refine
4. **Validate with leadership and stakeholders** before distributing
5. **Use this document to drive feature specs** — each phase's scope becomes one or more Feature Specifications
