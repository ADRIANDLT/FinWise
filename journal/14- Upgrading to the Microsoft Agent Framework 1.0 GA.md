# 14 — Upgrading to the Microsoft Agent Framework 1.0 GA

*April 4–5, 2026*
*The framework we've been building on just went GA — time to upgrade from RC4, chase renamed packages, rewritten APIs, and a sneaky exception-type change that only shows up in production integration tests.*

---

## Where We Are — The MAF Version Journey

FinWise has been built on top of the Microsoft Agent Framework since day one. The project has been through five distinct framework eras since December 2025, riding the MAF release train from its earliest previews — consumed as open-source code cloned directly into the repo — through NuGet packages, release candidates, and finally GA.

Microsoft was publishing MAF builds at a furious pace during this period. The full NuGet history of `Microsoft.Agents.AI` from Dec 2025 onward:

> `1.0.0-preview.251219.1` · `260108.1` · `260121.1` · `260127.1` · `260128.1` · `260205.1` · `260209.1` · `260212.1` → **RC1** → **RC2** → **RC3** → **RC4** → RC5 → **1.0.0 GA**

Eight preview builds, five release candidates, then GA. FinWise touched five of those milestones:

- **Dec 29, 2025 – Feb 2026: `1.0.0-preview.251219.1` (open-source code).** The very first code commits included the full MAF open-source code cloned directly into the repo — 24 `src/Microsoft.Agents.AI.*` directories compiled from source via project references. The version was pinned at `1.0.0-preview.251219.1` in `nuget/nuget-package.props` (`GitTag: 1.0.0-preview.251219.1`). The `FinWise.Orchestrator` project referenced `Microsoft.Agents.AI.Workflows`, `Microsoft.Agents.AI.Abstractions`, and other MAF libraries as sibling projects, building the entire framework from source alongside the app. `Microsoft.Extensions.AI 10.1.1` and `Azure.AI.OpenAI 2.1.0` were used as dependencies of the MAF source build, listed in `Directory.Packages.props`.

- **Mar 2, 2026: `1.0.0-preview.260212.1` (first NuGet packages).** The major refactoring (PR #2) removed all 24 MAF source directories and split the monolith into `FinWise.McpServer` + `FinWise.MultiAgentWorkflow`. MAF was now consumed as NuGet packages instead of source code — `Microsoft.Agents.AI`, `Microsoft.Agents.AI.Abstractions`, and `Microsoft.Agents.AI.Workflows` at `1.0.0-preview.260212.1`. This was the transition from "building MAF from source" to "consuming MAF as a dependency." No Azure Foundry bridge package yet.

- **Mar 15, 2026: `1.0.0-rc4`** — Big leap, skipping RC1–RC3 entirely. RC4 was the first version with the stable `Microsoft.Agents.AI.AzureAI` bridge package needed for the new StockSpecializedAgent (Foundry integration). Added `Azure.AI.Projects 2.0.0-beta.1` alongside it. Six days later, `Microsoft.Agents.AI.Hosting` (preview-only, `1.0.0-preview.260311.1`) was added for the `AgentSessionStore` refactoring. RC4 carried FinWise through the Redis session migration, MCP session work, and Docker containerization — all of March.

- **Apr 4, 2026: `1.0.0` GA** — This upgrade. Package rename (`AzureAI` → `Foundry`), removed convenience APIs, companion Azure SDK going GA simultaneously, and a sneaky exception-type change. Details below.

The StockSpecializedAgent is the component most affected by each MAF upgrade — it lives in Azure AI Foundry and sits at the intersection of the Azure SDK and the Agent Framework's type system.

---

## Phase 1: Deep Research — Trust Nothing, Verify Everything

The first instinct was to just bump version numbers. But this is a GA release following four release candidates, with a companion Azure SDK that also jumped from beta to GA simultaneously. Two independent teams shipping breaking changes on the same day. Research first.

Three parallel investigations kicked off:

1. **NuGet API queries** — hitting `api.nuget.org/v3-flatcontainer/{package}/index.json` for every `Microsoft.Agents.AI.*` package to see what versions actually exist
2. **Microsoft Learn MCP queries** — searching for migration guides, handoff workflow docs, Foundry provider docs
3. **GitHub release notes** — the `dotnet-1.0.0` release tag in the agent-framework repo

The NuGet queries surfaced the first surprise:

> **`Microsoft.Agents.AI.AzureAI`** has no 1.0.0 GA version. It stops at RC5. It's been **renamed** to **`Microsoft.Agents.AI.Foundry`**.

If we'd just bumped the version number, `dotnet restore` would have silently failed to find the package and the error would have cascaded into a confusing build failure. Research paid for itself in the first five minutes.

Other findings that shaped the upgrade:

- **`Microsoft.Agents.AI.Hosting`** — still preview-only. No GA. Latest: `1.0.0-preview.260402.1`
- **`Azure.AI.Projects.OpenAI`** — also no GA. Stays at `2.0.0-beta.1`
- **Experimental diagnostic IDs** — the handoff APIs use `MAAIW001` and `OPENAI001`, not the Semantic Kernel IDs (`SKEXP0001`). With `TreatWarningsAsErrors` on, the build would fail without suppressions
- **`GetAIAgentAsync()` removed** — the one-step convenience method for resolving Foundry agents is gone, replaced by a two-step pattern

**Key files produced:** [upgrade-to-microsoft-agent-framework-1.0-ga.md](../specs/Technology-Upgrades/upgrade-to-microsoft-agent-framework-1.0-ga.md) — the full breaking changes reference

---

## Phase 2: The Upgrade — 11 Packages, 8 Files

With the research in hand, the Coder agent took the first pass. The scope was larger than expected:

**Package changes in [`Directory.Packages.props`](../Directory.Packages.props):**
- 4 MAF packages → `1.0.0` GA
- 1 package renamed: `AzureAI` → `Foundry`
- 3 `Microsoft.Extensions.AI` packages → `10.4.1`
- `Azure.AI.Projects` → `2.0.0` GA
- 2 transitive deps bumped (`Azure.Identity` 1.17.1→1.20.0, `Microsoft.Extensions.Options` 10.0.2→10.0.3)

**Code changes in [`StockSpecializedAgentFactory.cs`](../src/FinWise.MultiAgentWorkflow/Agents/StockSpecializedAgent/StockSpecializedAgentFactory.cs):**

The biggest migration was the agent resolution pattern. RC4 had a single extension method:

```csharp
AIAgent agent = await _projectClient.GetAIAgentAsync(_agentName);
```

GA splits this into two steps that make the SDK boundary explicit:

```csharp
ProjectsAgentRecord agentRecord = await _projectClient
    .AgentAdministrationClient.GetAgentAsync(_agentName);
FoundryAgent agent = _projectClient.AsAIAgent(agentRecord);
```

Step 1 is pure Azure SDK (fetches the record). Step 2 is the bridge package (adapts it into the Agent Framework). Clean separation of concerns — but it means every consumer has to know both layers exist.

**Build result:** 10 projects, 0 warnings, 0 errors. **89 unit tests:** all green.

---

## Phase 3: Integration Tests — The Exception Nobody Warned About

Unit tests passed, but the real validation was the integration suite. Docker Compose up — CosmosDB emulator, Redis, MCP Server — all three containers healthy. Five test suites kicked off in parallel:

| Suite | Result |
|-------|--------|
| CosmosDB integration (10 tests) | ✅ |
| Redis integration (12 tests) | ✅ |
| MCP Server integration (8 tests) | ✅ |
| MCP Server container (9 tests) | ✅ |
| **StockAgent integration (4 tests)** | **❌ 1 failure** |

The failing test: `CreateAgentAsync_ThrowsInvalidOperationException_WhenAgentNameNotFound`.

The test expects that when you ask for a nonexistent agent name, the factory catches the Azure SDK error and wraps it in `InvalidOperationException`. Simple contract. But after the upgrade:

```
Expected a <System.InvalidOperationException> to be thrown,
but found <System.ClientResultException>: HTTP 404 NotFound
```

The catch block was catching `RequestFailedException` (from `Azure.Core`), but `Azure.AI.Projects` 2.0.0 GA now throws `ClientResultException` (from `System.ClientModel`) for HTTP errors. The exception flew right past the catch block.

This is the kind of breaking change that no migration guide mentioned. The Azure SDK team changed the exception hierarchy at the GA boundary — `ClientResultException` is actually the *base class* that `RequestFailedException` derives from, but the GA SDK is now throwing the base type directly for certain operations.

**The fix:** one line — `catch (RequestFailedException ex)` → `catch (ClientResultException ex)`, with the `using` updated from `Azure` to `System.ClientModel`.

After the fix: **all 4 StockAgent integration tests green**. Total: **132 tests (89 unit + 43 integration), 0 failures**.

---

## What We Learned

### About the Technology

- **Package renames are the silent killer in GA upgrades.** `AzureAI` → `Foundry` wouldn't have been caught by a simple version bump. NuGet queries before upgrading are essential.
- **Exception types can change between beta and GA.** `Azure.AI.Projects` moved from `RequestFailedException` to `ClientResultException` — a change that only surfaces at runtime against a real service, not in unit tests with mocks.
- **Experimental APIs ship inside GA packages.** MAF 1.0.0 GA marks its own handoff orchestration as `[Experimental]` with custom diagnostic IDs (`MAAIW001`, `OPENAI001`). The GA label on the package doesn't mean every API inside is stable.
- **Two packages have no GA path yet:** `Microsoft.Agents.AI.Hosting` (preview) and `Azure.AI.Projects.OpenAI` (beta). Both are required by FinWise and must be monitored for their own GA releases.

### About the Process

- **Research-first saved hours.** The parallel NuGet + Learn + GitHub investigation took ~10 minutes but prevented at least three wrong turns (wrong package name, wrong diagnostic IDs, wrong API patterns).
- **Integration tests caught what unit tests couldn't.** The exception-type change is invisible to mocked unit tests — only a real HTTP 404 from Foundry surfaces the difference. This validates the investment in the multi-tier test suite from earlier journals.
- **The CoDev loop works for upgrades.** Research → Coder (draft) → Critic (review) → Coder (fix) → full verification. The Critic review after the first build caught zero issues because the research phase had already identified the gotchas. The real value of the loop was the final integration test pass, which caught the one thing nobody — human or agent — predicted.

---

## What's Next

The MAF 1.0 GA upgrade is complete and verified. The immediate backlog:

- **Monitor `Microsoft.Agents.AI.Hosting` for GA** — the session store and agent lifecycle types are still preview
- **Monitor `Azure.AI.Projects.OpenAI` for GA** — the Responses client integration depends on this beta
- **Commit and merge** the 9 changed files on the `microsoft-agent-framework-1.0-support` branch

The foundation is current. Whatever comes next — graph workflows, checkpointing, new agent patterns — it'll build on 1.0 GA, not a release candidate.

---

*Written: April 5, 2026*
