---
name: tech-deep-research
description: 'Deep research protocol for ANY non-Microsoft technology. Inspects repo package versions, fetches official vendor docs, verifies stability/release status, and produces a Research Summary before any approach is proposed. Use for Python, JavaScript/Node, Java, Go, Rust, Ruby, PHP, or any SDK/framework/library NOT from Microsoft — especially pre-release, fast-moving, or unfamiliar packages. Keywords: research, investigate, verify, docs, versions, pre-release, alpha, beta, release candidate.'
---

# Technology Deep Research Protocol

Structured research workflow for non-Microsoft technologies. Produces a **Research Summary** grounded in verified facts before any approach is proposed.

> **Microsoft technologies?** Use **microsoft-tech-deep-research** instead — it leverages the Microsoft Learn MCP server. This includes any Microsoft SDK even on PyPI/npm (e.g., `azure-identity`, `@azure/openai`, `semantic-kernel`).

---

## When to Use & Research Depth

**Mandatory** when the task involves non-Microsoft SDKs, pre-release packages, version upgrades, fast-moving tooling, or unfamiliar technology.

| Depth | Steps | When |
|-------|-------|------|
| **Quick Check** | Step 1 (+ one Step 2 query) | Known tech; confirm version or API |
| **Full Research** ⭐ default | Steps 1→2→3→4 | New packages, upgrades, pre-release, large features |
| **Spot Check** | Step 1 + targeted Step 2 | Review/audit — verify key claims only |

**Force Full Research** for: AI agent frameworks, model provider SDKs, interop protocols (MCP/A2A), and Rust-rewrite tooling (uv, Biome, Oxc). These areas change too fast for Quick Checks.

### Vendor Identification — Microsoft or Not?

| Microsoft → use other skill | Non-Microsoft → use this skill |
|----------------------------|-------------------------------|
| `Azure.*`, `Microsoft.*`, `@azure/`, `com.azure:`, `azure-*` namespaces | Everything else |
| Documented on `learn.microsoft.com` | Documented on vendor's own site |
| `semantic-kernel` (by Microsoft) | `openai`, `anthropic`, `langchain` (by other vendors) |

When unclear, check the `author`/`maintainer` field on the package registry.

---

## Technology Scope

Covers **all non-Microsoft technologies**. Lists below are illustrative — **research anything the repo uses** with the same rigor, even if not listed here.

**Languages**: Python, JavaScript/TypeScript (Node, Deno, Bun), Java/Kotlin, Go, Rust, Ruby, PHP, Swift, Elixir, C/C++, Dart, Zig, Mojo, Gleam, and any emerging language.

**Package ecosystems**:

| Ecosystem | Registry | Key Manifest / Lock Files |
|-----------|----------|--------------------------|
| Python | PyPI / conda | `pyproject.toml`, `requirements.txt` / `uv.lock`, `poetry.lock` |
| JS/TS | npm / yarn / pnpm / jsr | `package.json` / `package-lock.json`, `yarn.lock` |
| Java | Maven / Gradle | `pom.xml`, `build.gradle(.kts)` / `gradle.lockfile` |
| Go | Go modules | `go.mod` / `go.sum` |
| Rust | crates.io | `Cargo.toml` / `Cargo.lock` |
| Ruby | RubyGems | `Gemfile` / `Gemfile.lock` |

New package managers appear regularly (uv, Bun, jsr.io). If the repo uses one not listed, identify its manifest format during Step 1.

**Key areas** (illustrative, not exhaustive): AI agent frameworks (LangChain, CrewAI, Pydantic AI, OpenAI Agents SDK, Google ADK, Vercel AI SDK, Mastra, Spring AI, DSPy, smolagents), agent protocols (MCP, A2A), web frameworks, data libraries, Docker/Kubernetes/Helm, observability (OpenTelemetry), infrastructure (Terraform, Pulumi, OpenTofu).

---

## Research Execution Steps

### Step 1 — Inspect the Repository

Check actual versions and patterns in use:

- **Identify the ecosystem** from manifest files (see table above)
- **Check runtime versions**: `.python-version`, `.nvmrc`, `.node-version`, `.tool-versions`, `rust-toolchain.toml`
- **Check container files**: `Dockerfile`, `docker-compose.yml`, `k8s/`, `charts/`, `values.yaml`
- **Flag pre-release packages**: `0.x` semver, `alpha/beta/rc/dev` suffixes, `@next/@canary` dist-tags, `-SNAPSHOT` (Maven)
- **Group related packages**: When multiple packages share a namespace (e.g., `langchain-core`, `langchain-openai`, `langchain-community`), research them as a **single SDK family**

> **Pre-release detected**: `langchain` version `0.3.0a1`. APIs may differ from stable docs. Full Research required.

### Step 2 — Search Official Documentation

1. **Check for `llms.txt`** at `https://[docs-site]/llms.txt` ([spec](https://llmstxt.org/)). Also try `[url].md` for markdown versions.
2. **Fetch official docs** — use web fetch tools to retrieve from the vendor's site.
3. **No web access?** — Flag low confidence.

**Key doc sources**: `docs.python.org`, `nodejs.org/docs`, `go.dev/doc`, `docs.rs/[crate]`, `pkg.go.dev/[module]`, `[package].readthedocs.io`, vendor portals (e.g., `python.langchain.com/docs/`), GitHub README.

Focus on: setup/config guidance, version-specific APIs, migration guides, breaking changes, recommended patterns.

### Step 3 — Web Research for Gaps

When docs are insufficient (common with pre-release packages):

- **GitHub repo**: README, CHANGELOG, MIGRATION.md, issues, releases
- **Registry**: PyPI (`pypi.org/project/[pkg]`), npm (`npmjs.com/package/[pkg]`), crates.io, Maven Central, Docker Hub, Artifact Hub
- **Vendor blog** for release announcements
- **Security advisories** on the GitHub repo's Security tab

### Step 4 — Research Summary

```
### Research Summary — [Technology/Package Name]

**Package version in repo**: X.Y.Z
**Latest available**: X.Y.Z (via [source])
**Stability**: Pre-release / Stable / LTS
**Ecosystem**: Python / Node / Go / Rust / etc.
**Runtime/Language version**: Python 3.12 / Node 22 / etc.

**Key findings**:
- [Finding 1]
- [Finding 2]

**Version-sensitive warnings**:
- [API/pattern changes]

**Confidence**: High / Medium / Low
**Gaps**: [What couldn't be verified]
**Sources**: [URLs consulted]
```

**Do not present guesses as facts.** If research is incomplete, say so.

---

## Research Quality

### Source Priority

1. Repo code and config (ground truth) → 2. Official vendor docs → 3. Official samples/repos → 4. Registry metadata → 5. Release notes/changelogs → 6. Reputable ecosystem sources (for gaps only)

### Credibility & Recency

- **< 3 months old**: Trust directly.
- **3–12 months**: Cross-check against current docs.
- **> 1 year**: Likely outdated for fast-moving tech — verify every claim.
- **AI-generated content**: Never cite as a source.
- **StackOverflow**: Check answer date AND vote recency.

### When Sources Contradict

1. Code > docs. 2. Newer > older. 3. Official > community. 4. **Always flag contradictions** in the Research Summary — never silently pick one.

### Common Research Pitfalls

- **Don't assume features are pattern-exclusive.** A framework may offer features (e.g., streaming, checkpointing) at the framework level, available to all patterns. Don't recommend switching architectures just to access a feature — verify scope first.
- **Overview pages reference features documented elsewhere.** A landing page may list capabilities as bullets but the API details are on linked sub-pages. Follow the links.
- **Existing repo code is the best API documentation for pre-release packages.** The actual function calls in the codebase reveal the real API — sometimes more accurately than the docs.
- **Registry APIs are faster than HTML pages.** For version lists: PyPI JSON API (`pypi.org/pypi/[pkg]/json`), npm registry (`registry.npmjs.org/[pkg]`), NuGet flat-container (`api.nuget.org/v3-flatcontainer/[pkg]/index.json`). Faster and more complete than scraping HTML.

### Time-Boxing

| Quick Check | Spot Check | Full Research |
|-------------|-----------|---------------|
| ~2 min | ~5 min | ~10 min max |

Stop when: 3+ sources corroborate, or queries return no new info. Don't stop when: docs and code disagree, or package is pre-release without issue check.

---

## Caution Zones

### Pre-release Packages

For `alpha/beta/rc`, `0.x` semver, `@next`, `-SNAPSHOT`: pin exact versions, check GitHub issues, warn about production readiness, track upgrade path to stable. Add banner: `> **Pre-release API**: Uses [package] [version]. May change.`

### Supply Chain Security

Check dependency depth (AI frameworks pull hundreds of transitive deps), `dependabot.yml`/`renovate.json` presence, GitHub Security tab for advisories, yanked versions on registry.

### Deprecation

Flag deprecated patterns. Distinguish "deprecated" (will be removed) from "superseded" (still works, newer available). Check ecosystem signals: Python `DeprecationWarning`, Node `[DEP####]`, Java `@Deprecated(forRemoval=true)`, Rust `#[deprecated]`.

---

## Comparative Research (Technology Selection)

When choosing **between** technologies: define criteria (performance, maturity, ecosystem fit, licensing, MCP/A2A support) → run Steps 1–4 for each candidate → present a Comparison Matrix:

```
| Criterion | Option A | Option B |
|-----------|---------|---------|
| Stability | GA | Beta |
| MCP support | ✅ | ❌ |
| Last release | 2 weeks ago | 6 months ago |

**Recommendation**: [pick] because [reasons]. **Avoid**: [pick] because [dealbreaker].
```

---

## Approach Presentation

Always distinguish: **Official recommendation** (vendor docs) vs **Repo constraint** (existing code) vs **Engineering judgment** (your recommendation and why).

---

## Environment & Tools

Uses **web fetch tools** — no vendor-specific MCP needed. Works in Copilot CLI (`web_fetch`), VS Code, Coding Agent. No web access? Complete Step 1, flag `Confidence: Low`. Session reuse: don't re-research same package/version; state what was reused.

---

## Relationship to microsoft-tech-deep-research

| Microsoft vendor (any language) | **microsoft-tech-deep-research** |
|--------------------------------|--------------------------------|
| Non-Microsoft vendor | **This skill** |
| Mixed vendor task | Run **both** in parallel, cross-reference version compatibility |

---

## Future-Proofing

This skill teaches **how** to research, not **what** to research. Technology lists are illustrative — Step 1 (repo inspection) is the ground truth.

**Meta-trends**: Rust is rewriting dev tooling. AI agent frameworks are the new web frameworks. MCP/A2A are standardizing interop. Vendors ship multi-language SDKs simultaneously. New things we can't predict will appear — the 4-step protocol handles them all.
