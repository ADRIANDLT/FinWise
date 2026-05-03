# 21 — Implementing the Migration to the Foundry LLM API: From Plan to Pushed Image

_April 19–20, 2026_
_The session where the spec became a factory, a dead package finally got buried, and a single source of truth for versioning earned its own README section._

---

## Where We Were

The previous entry ([20 — Planning the Migration to the Latest Foundry LLM API](20-%20Planning%20the%20Migration%20to%20the%20Latest%20Foundry%20LLM%20API.md)) ended with the migration spec at Revision 7 — 100% confidence, source-grounded, with a verified call chain copied straight from `Microsoft.Agents.AI.Foundry` 1.1.0:

```csharp
#pragma warning disable OPENAI001
IChatClient chat = projectClient
    .GetProjectOpenAIClient()
    .GetResponsesClient()
    .AsIChatClient(modelDeploymentName);
#pragma warning restore OPENAI001
```

The spec said the work would be small. The seam was already there. `IChatClient` was the boundary. `FinWise.MultiAgentWorkflow` would not need to know.

So the next session opened, predictably, with:

> **User:** "Implement spec 012 and test with full Docker stack + integration tests."

CoDev declared **RESEARCH TRIAGE: Skip** — the spec was implementation-ready, every API was already cited to a file and line. No new research. Hand off to the Coder agent for a Multi-Pass Draft.

---

## The Draft: Building the Factory

The Coder followed the spec to the letter. New file, new namespace, service-principal credential, the canonical chain:

- **Created** [`src/FinWise.McpServer/Infrastructure/AzureAIFoundry/AzureAIFoundryChatClientFactory.cs`](../src/FinWise.McpServer/Infrastructure/AzureAIFoundry/AzureAIFoundryChatClientFactory.cs)
- **Deleted** the old `src/FinWise.McpServer/Infrastructure/AzureOpenAI/AzureOpenAIChatClientFactory.cs` and its folder
- **Updated** `Program.cs` to call the new factory at the composition root
- **Replaced** `AZURE_OPENAI_*` env vars with `FINWISE_AZURE_AI_FOUNDRY_*` in `.env.template` and `docker-compose.finwise.yml`
- **Refreshed** `README.md` and the `AGENTS.md` files to reflect the new endpoint shape and auth model

So far, so mechanical.

But there was one wrinkle that the spec hadn't resolved cleanly. The spec _said_ the preview package `Azure.AI.Projects.OpenAI` 2.0.0-beta.1 was "never referenced" and could be removed. When the Coder went to compile, though, it _looked_ like there was an ambiguity — `GetProjectOpenAIClient()` appeared in two places at first glance, and the build threw a name-resolution complaint that pointed (misleadingly) at the beta package. So the Coder did what a cautious engineer does on a Draft pass: it added a `ProjectOpenAIClientOptions()` disambiguation argument as a workaround, kept the beta package "to be safe," and moved on.

The Draft pass exit criteria were met. Skeleton compiled. Tests passed. Move on.

---

## The Verification

89 unit tests: green.
Docker stack up: healthy.
Full integration + E2E suite: 48/48 passed — CosmosDB (13), Redis (12), StockAgent (4), McpServer integration (8), container tests (11).

The migration worked. The Foundry deployment responded. The Orchestrator, Profile, and Advisor agents talked to it through the new `IChatClient` chain exactly as the spec promised. The legacy `Azure.AI.OpenAI` package was gone from `FinWise.McpServer.csproj`. The seam held — `FinWise.MultiAgentWorkflow` got zero changes.

It was tempting to call it done.

---

## The Dead Package That Wasn't Dead (Until It Was)

Then the user looked at the package list and asked the question that dissolves a workaround:

> **User:** "Why do we need the package `Azure.AI.Projects.OpenAI` anymore if we switched to another path? If it's referenced by tests, change tests."

CoDev grepped the entire repository for `using Azure.AI.Projects.OpenAI;`.

**Zero hits.**

Not in `FinWise.McpServer`. Not in `FinWise.MultiAgentWorkflow`. Not in `StockAgent`. Not in any test project. Not in any sample. The package was declared in two `.csproj` files and `Directory.Packages.props`, and no line of code anywhere imported it. The Coder's "to be safe" instinct in the Draft pass had been wrong. The compile-time ambiguity it thought it was working around was a phantom — caused by the beta package _itself_ being on the package graph and shadowing the canonical extension.

The fix was three deletions and one revert:

- Removed `Azure.AI.Projects.OpenAI` from `MultiAgentWorkflow.csproj`
- Removed it from `StockAgent.IntegrationTests.csproj`
- Removed the central declaration from `Directory.Packages.props`
- Reverted the `ProjectOpenAIClientOptions()` workaround in the new factory — the parameterless `.GetProjectOpenAIClient()` chain (the one Microsoft itself ships in `Microsoft.Agents.AI.Foundry`) now resolved cleanly, because the colliding extension was finally gone

Rebuilt. Zero errors. Zero warnings. The factory now matched the spec's verified call chain _byte for byte_.

The lesson hit immediately: **a 100%-confidence spec is not the same as 100%-correct code on the first try.** The spec had said "the beta is unreferenced, remove it." The Draft pass had read the spec, hit a confusing compiler error, and second-guessed it instead of trusting it. One sharp question from the user — _"if it's not used, remove it"_ — cut through the ambiguity in seconds.

---

## From Scratch and Forward Again

Trust, but rebuild.

> **User:** "Build from scratch, Docker image, full stack, run ALL tests."

The full sequence:

```pwsh
docker compose -f docker-compose.finwise.yml down -v
dotnet clean
dotnet build      # 0 errors, 0 warnings
docker compose -f docker-compose.finwise.yml build --no-cache
docker compose -f docker-compose.finwise.yml up -d
# wait for health checks
dotnet test       # 89 unit + 48 integration/E2E = 137/137 green
```

Image tag still `finwise-mcp-server:1.0.0`. Everything reproducible from a cold cache. Migration confirmed end-to-end.

---

## Versioning Almost in Two Places

With the implementation locked in and the test suite green from a clean build, the user pulled the release lever:

> **User:** "Bump to 1.0.1 before pushing to Docker Hub."

CoDev did the obvious thing: edited the version constant in `Program.cs`, edited the image tag in `docker-compose.finwise.yml`, and added a `<Version>1.0.1</Version>` element to two `.csproj` files for good measure. Built. Pushed to Docker Hub.

Done.

Except.

> **User:** "You did not change the version of the .NET projects here: Directory.Build.props"

CoDev opened [`Directory.Build.props`](../Directory.Build.props) and immediately understood the miss. The repo had a centralized `<FinWiseVersion>` MSBuild property in `Directory.Build.props` that propagated `<Version>` to **every** project automatically. The `<Version>1.0.1</Version>` elements that had just been added to the two csproj files weren't bumping the version — they were _duplicating_ a value the central props file was already supposed to own, and they would silently shadow the central source of truth on the next bump.

The repair:

- Removed the redundant `<Version>` elements from the two `.csproj` files
- Bumped `<FinWiseVersion>1.0.1</FinWiseVersion>` in `Directory.Build.props` (now the single place this number lives for .NET projects)
- Rebuilt with `--no-cache`
- Re-pushed to Docker Hub

New image digest published: `sha256:f6a74c23…`. Same tag, correct provenance.

The user's catch turned a near-miss into a permanent guardrail. The centralized version had been invisible — exactly the kind of "obvious once you see it" detail that gets missed when you're scanning csproj files for `<Version>` and don't know to grep for `<FinWiseVersion>` in `Directory.Build.props` instead. Every team member who ever bumps a version after this moment now has a chance to make the same mistake.

So the next request was, predictably:

> **User:** "Add the info about where to change the versions of .NET projects and FinWise Docker Image in the main AGENTS.md."

---

## The Critic Finds Its Own Author's Drift

The Coder added a `## Versioning` section to [`AGENTS.md`](../AGENTS.md) listing three files:

1. `Directory.Build.props` — `<FinWiseVersion>` (single source of truth for all .NET projects)
2. `docker-compose.finwise.yml` — image tag
3. `src/FinWise.McpServer/Program.cs` — startup banner version constant

Then the Critic agent reviewed it.

The Critic found a fourth: `README.md` line 170 hardcoded `finwise-mcp-server:1.0.0` in a `docker run` example.

There was a quiet symmetry to that finding. The whole point of the new `## Versioning` section was to prevent stale version references from drifting across the repo. The Critic's pass — on the very same review — surfaced an existing drift that the section was meant to prevent. The system worked: the docs that codify the rule caught a violation of the rule on first read.

Updated `README.md:170` to `1.0.1`. Updated the `## Versioning` section to list **four** files, not three. Re-ran the build to confirm nothing was disturbed.

---

## What We Learned

### About the Technology

- **A 100%-confidence spec still meets the compiler.** The spec was right that `Azure.AI.Projects.OpenAI` was unreferenced. The compiler's complaint when the beta was on the graph _looked_ like it was contradicting the spec, but it was actually evidence _for_ the spec — the beta was the source of the ambiguity, not the cure. Trusting the verified spec over a misread compiler error would have skipped a whole detour.
- **Centralized version properties are invisible until they aren't.** `Directory.Build.props` quietly sets `<Version>` for every project in the repo via `<FinWiseVersion>`. A new contributor scanning for "where do I change the version?" will look in csproj files first and find nothing — or, worse, _add_ a `<Version>` there, which silently shadows the central one. The fix is documentation: `## Versioning` in `AGENTS.md` now points everyone to the right file on the first try.
- **Service-principal auth + Foundry project endpoint = no surprises.** Once the dead beta package was gone and the canonical chain compiled cleanly, the migration produced exactly the behavior the spec predicted. 137 tests, two cold rebuilds, one image push. No runtime drama. The seam architecture earned its keep again.

### About the Process

- **Sub-agents earn trust by getting course-corrected, not by being infallible.** The Coder's first instinct — "keep the beta package to be safe, add a workaround" — was the wrong call. The Multi-Pass workflow assumes Refine passes (or, in this case, a sharp user question playing the role of Refine 1) will catch exactly this kind of cautious-but-incorrect drift. The mechanism worked: a workaround that survived the Draft pass did not survive contact with the user's question. The result is a cleaner factory than the Draft would have shipped.
- **The Critic catches the author's own drift on the same review.** The `## Versioning` section was added to prevent stale version references. The Critic's review of that very change found a stale version reference in `README.md:170` that pre-dated the section. The lesson: a doc that codifies a rule is also a tool for finding existing violations of that rule, _if you let the Critic loop run on it._
- **One question can collapse a workaround.** _"Why do we need this package if we switched to another path?"_ took the Coder's elaborate defensive posture and dissolved it in eight words. Simple, direct, falsifiable. A single grep proved the package was dead. Sometimes the highest-leverage move in a code review is just asking why the cautious thing is cautious about.

---

## What's Next

The migration is done. The factory is in. The dead beta is buried. The image is on Docker Hub at `1.0.1` with provenance traceable to a single MSBuild property. The `## Versioning` rule is documented and the Critic has already used it to catch one drift.

Open follow-ups, in order of likelihood:

1. **The future one-liner.** When `Microsoft.Agents.AI.Foundry` 1.1.0+ lands stably across all consumers, McpServer can pass `AIProjectClient` straight into the workflow library and let each agent factory call `aiProjectClient.AsAIAgent(model, instructions, ...)`. That eliminates the manual `IChatClient` plumbing _and_ the `OPENAI001` pragma. The current factory is already the right shape to be deleted when that day comes.
2. **CI version-drift check.** Now that the rule is "version lives in `Directory.Build.props` only," a tiny CI guardrail that greps for `<Version>` outside of that file (or for hardcoded image tags outside the four sanctioned places) would prevent the same near-miss from recurring.
3. **Anthropic Claude factory.** The spec's Model Compatibility Matrix flagged this as out of scope. The current factory shape — single seam at `IChatClient`, env-var-driven config — makes a sibling factory straightforward when there's a business reason for it.

Two sessions, one migration. Plan to pushed image. The journal entry that started with _"can the new Foundry deployment even respond?"_ closes with a cleanly cited factory, a centrally versioned image on Docker Hub, and a `## Versioning` section that exists because the user noticed what the agent did not.

---

_Written: April 20, 2026_
