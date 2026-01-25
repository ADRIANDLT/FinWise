# FinWise Constitution

<!-- 
SYNC IMPACT REPORT
==================
Version Change: 1.0.0 → 2.0.0 (MAJOR: Principle removal - backward incompatible governance change)
Principles Removed:
  - II. Test-Driven Development (Mandatory)
Principles Renumbered:
  - III. User Experience Consistency → II. User Experience Consistency
  - IV. Performance Requirements → III. Performance Requirements
Templates Updated: ✅ spec-template.md (removed TDD acceptance test language), ✅ tasks-template.md (removed Phase 0 and TDD Red-Green-Refactor), ✅ plan-template.md (Quality Gates section retained but TDD references removed), ✅ checklist-template.md (removed TDD checklist items)
Follow-up TODOs: None - all TDD references removed
-->

## Core Principles

### I. Code Quality Standards

**Definition**: Every code change must meet explicit quality criteria before merge.

**Non-negotiable rules**:
- No use of deprecated APIs or deprecated language features
- Code duplication MUST be refactored into reusable functions/modules
- Complex functions (>50 lines) require docstrings with parameters and return types documented
- Comments MUST explain *why*, not *what*—code itself explains *what*
- Variable and function names MUST be descriptive and use domain terminology from investment/finance contexts

**Rationale**: High code quality reduces bugs, improves maintainability, and enables faster development cycles. Explicit criteria ensure consistency across the team and new contributors.

---

### II. User Experience Consistency

**Definition**: User-facing features across all platforms (web, mobile, API, CLI) MUST maintain consistent behavior, terminology, and interaction patterns.

**Non-negotiable rules**:
- Financial terminology (e.g., "portfolio", "rebalance", "risk score") MUST be used consistently across all UIs and APIs
- Error messages MUST be user-friendly and actionable (e.g., "Budget limit exceeded. Reduce purchase amount by $50." not "Error code 403")
- Loading states, success confirmations, and error recovery flows MUST be identical across web and mobile platforms
- API responses for the same logical operation MUST return identical data structures regardless of client type
- Accessibility standards (WCAG 2.1 AA minimum) MUST be verified before features ship

**Rationale**: Consistency reduces user confusion, lowers support burden, and builds trust in the FinWise brand. Financial applications must be reliable and predictable—inconsistency erodes confidence.

---

### III. Performance Requirements

**Definition**: All code MUST meet explicit latency, memory, and throughput targets.

**Non-negotiable rules**:
- API responses MUST complete within 500ms (p95) for standard queries; 2s (p95) for complex portfolio analyses
- UI interactions (button clicks, form submissions, page transitions) MUST respond within 100ms (perceived responsiveness)
- Mobile app startup time MUST be <2 seconds on modern devices (iPhone 12+, Pixel 6+)
- Memory usage MUST not exceed allocated limits: Web app <100MB, Mobile app <150MB
- Background services (portfolio analyzers, risk engines) MUST complete daily tasks within scheduled windows without timeout
- Database queries MUST have execution plans reviewed and indexed; no full table scans on production tables >100k rows
- New features MUST include performance tests (load tests for APIs, profile analysis for heavy computation)
- Performance regressions (>10% slower than previous release) block release deployment

**Rationale**: Financial applications serve time-sensitive decision-making. Slow performance leads to poor decisions and user frustration. Explicit targets ensure performance is treated as a requirement, not an afterthought.

---

## Performance & Scalability Standards

**Monitoring & Observability**:
- All services MUST emit structured logs (JSON format) with request IDs, user IDs, and execution time
- Critical paths (auth, portfolio calculations, transactions) MUST have application performance monitoring (APM) dashboards
- Alerts MUST trigger if API p95 latency exceeds 500ms or error rate exceeds 1%

**Capacity Planning**:
- Architecture MUST scale to 10x current user load without code changes (horizontal scaling via stateless design)
- Database MUST support read replicas for reporting queries; primary reserved for transactional writes
- Caching strategy (Redis/in-memory) MUST be defined for frequently accessed data (user profiles, market data)

---

## Development Workflow

**Code Review Gate**:
- All PRs MUST include a code quality checklist (linting, test coverage, performance impact)
- At least one code review approval MUST be obtained before merge
- Reviewers MUST verify: (1) tests pass, (2) coverage ≥85%, (3) acceptance criteria met, (4) no performance regressions

**Testing Gates**:
- Unit tests MUST pass locally before pushing to remote
- CI/CD pipeline MUST run full test suite, linting, and coverage analysis on every push
- PR merge blocked if: unit tests fail, coverage <85%, linting issues remain, or integration tests fail

**User Story Lifecycle**:
1. User story written with acceptance criteria (Given-When-Then scenarios)
2. Acceptance tests written and approved (Red phase)
3. Implementation tasks identified and assigned
4. PR submitted with all quality gates passing
5. User/stakeholder approves feature behavior
6. Merge and deploy to production

---

## Governance

**Constitution Authority**:
- This Constitution supersedes all informal practices, and ad-hoc decisions
- The Constitution is the source of truth for development standards and quality expectations
- Ambiguous cases escalate to the tech lead; amendments follow the procedure below

**Amendment Procedure**:
1. Proposed change documented with rationale and impact analysis
2. Team discussion and consensus-building (async in GitHub issue)
3. Change approved by maintainers
4. Constitution updated with new version (semantic versioning)
5. `Last Amended` date updated
6. Affected templates and documentation updated within one week
7. Team notified of change and expectations reset

**Compliance Review**:
- Monthly: Code quality metrics reviewed (coverage, linting, performance p95 latency)
- Per-PR: Checklist verified before merge
- Quarterly: Principles re-assessed for effectiveness and alignment with product roadmap

**Guidance for Runtime Development**:
- Developers reference `.github/AGENTS.md` and `/docs/` folder for implementation patterns and tooling decisions
- Speckit (this constitution system) governs specification and quality gates; AGENTS.md governs tool selection and CI/CD

**Version**: 2.0.0 | **Ratified**: 2025-12-23 | **Last Amended**: 2025-12-23
2