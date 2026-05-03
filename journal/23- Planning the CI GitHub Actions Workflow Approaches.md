# 23 — Crafting the CI GitHub Actions Workflow Plan

*April 26, 2026*
*The FinWise stack runs end-to-end in Docker. The tests are solid. The missing piece: a CI pipeline that proves it on every push. What sounds like "just a YAML file" turns into a multi-round design exercise where every assumption gets challenged.*

---

## Where We Are

Journal 22 closed the CosmosDB emulator migration — from the 4.1 GB legacy image to the 1 GB vnext-preview, with native health probes and dramatically faster startup. The full Docker stack is now three lean containers: CosmosDB emulator, Redis, and the MCP Server. Unit tests, integration tests against real infrastructure, E2E MCP protocol tests, and container-specific tests — all passing locally.

But locally is the operative word. There's no CI. No GitHub Actions workflow. Every merge to `main` is a trust exercise. The `.github/workflows/` directory is empty.

> **User:** "Since we are able to deploy this solution end to end in Docker, it'd be pretty much straightforward to create a CI workflow in GitHub Actions which builds the solution, deploys it into Docker in GitHub and run all the tests."

The word "straightforward" is doing a lot of heavy lifting in that sentence.

---

## The First Draft

The initial decomposition felt clean. Three sequential jobs: build + unit tests, integration tests against Docker infrastructure, container tests against the full stack. Fast-fail chain: if unit tests break, don't waste runner minutes pulling Docker images.

The first draft landed quickly — a spec at [specs/013-ci-github-actions-workflow/013-ci-github-actions-workflow.md](../specs/013-ci-github-actions-workflow/013-ci-github-actions-workflow.md). It mapped existing test projects to CI jobs, referenced the docker-compose files already in the repo, and proposed generating a `.env` file at runtime for secrets.

It also had the CosmosDB emulator image wrong (`:latest` instead of `:vnext-preview`), budgeted 3 GB of RAM for it, included a `PARTITION_COUNT` override that doesn't exist on the new image, and put the MCP Server E2E tests in a job that had no running MCP server.

The first draft was a starting point. What followed was the real work.

---

## The Review That Found the Blocker

> **User:** "Do a full and deep review of this plan while deeply analyzing the repo's codebase."

The codebase exploration went test file by test file. How does each test project discover its configuration? The answer varied:

- **CosmosDB tests** — `Environment.GetEnvironmentVariable()` with hardcoded emulator defaults
- **Redis tests** — `Environment.GetEnvironmentVariable()` with `"localhost:6379"` fallback
- **Stock Agent tests** — Four env vars, all required, `Skippable.If()` in the constructor when missing
- **MCP Server E2E tests** — Reads `FINWISE_MCP_URL`, POSTs to an external server. Uses `[Fact]`, not `[SkippableFact]`. **Fails hard if the server isn't running.**

That last finding was the blocker. The plan had `McpServer.IntegrationTests` in Job 2 — a job that spins up CosmosDB and Redis but *never starts the MCP server*. Those tests would fail, not skip. No graceful degradation. Hard crash.

The initial fix: add a background `dotnet run` to Job 2. Capture the PID, write stdout to a log file, poll the `/health` endpoint, kill the process in teardown. It worked on paper, but it was complex — PID files, `nohup`, log capture, Azure secrets duplicated across two jobs, a 60-second health wait loop.

---

## The Leaner Path

The breakthrough came from looking at the test base class more carefully. Both `McpServer.IntegrationTests` and `McpServer.ContainerTests` extend `McpEndToEndTestBase`. Both read `FINWISE_MCP_URL`. Both POST to `{url}/mcp`. They differ only in one thing: container tests have a `Skip.IfNot(reachable)` guard, E2E tests don't.

If both test projects target the same HTTP endpoint... they can share the same running server.

> **User:** "Still we want a lean implementation while having quality, but not a too complex implementation, since this is a POC."

That comment sealed it. The background `dotnet run` mechanism — PID files, log capture, kill steps, duplicated Azure secrets — all of it could be eliminated by consolidating both test projects into Job 3, where the Docker container is already running. One server, two test suites, zero process management.

Job 2 became clean: infrastructure containers + isolated integration tests (CosmosDB, Redis, Stock Agent). No MCP server. No Azure AI Foundry secrets except for the optional Stock Agent.

Job 3 became the full-stack job: `docker compose up -d --build`, wait for the health endpoint, run both MCP test projects, dump logs on failure, tear down.

---

## The Image Correction

A separate review catch: the plan still referenced the old CosmosDB emulator image.

> **User:** "Note that we already migrated and tested the new Cosmos DB emulator image for Docker and is the image in use right now."

The actual `docker-compose.infra.yml` had already been updated in Journal 22:

```yaml
image: mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview
mem_limit: 1g
healthcheck:
  test: [ "CMD-SHELL", "curl -f http://localhost:8080/ready || exit 1" ]
  start_period: 15s
```

The plan's references to `:latest`, 3 GB memory, `PARTITION_COUNT=3`, 5-minute health timeouts, and the "Future Optimization: migrate to vnext-preview" — all artifacts of a reality that no longer existed. Every one of them was removed or corrected.

The resource budget shrank from "tight" to "comfortable": 1 GB + 256 MB + 512 MB = ~1.8 GB on a 7 GB runner. The health timeout dropped from 5 minutes to 60 seconds. The plan got simpler because the infrastructure got better.

---

## What Microsoft Recommends (and Why We Diverged)

Research via the Microsoft Learn MCP server surfaced an interesting pattern. Microsoft's [official CI sample for the Cosmos DB Linux emulator](https://learn.microsoft.com/azure/cosmos-db/emulator-linux#use-in-continuous-integration-workflow) uses **GitHub Actions service containers** — the `services:` block in workflow YAML. GitHub manages the container lifecycle automatically.

But FinWise's explicit goal is *"CI runs the same stack as local dev."* The project has three docker-compose files that compose, extend, and include each other. Service containers would duplicate that infrastructure definition in a separate YAML format, creating a parallel source of truth that would inevitably drift.

The trade-off was documented as Design Decision #11: reuse production compose files, accept the `docker compose up/down` lifecycle management, skip the GitHub-native service containers.

---

## The Hardening Details

The final review rounds added the operational polish that separates a POC from an experiment:

- **`permissions: contents: read`** — Least-privilege `GITHUB_TOKEN`. The CI workflow only reads code; it doesn't push, comment, or create releases. OWASP-aligned default.
- **`concurrency: cancel-in-progress: true`** — When a developer pushes three commits in quick succession to a PR, the first two workflow runs are cancelled. No wasted runner minutes.
- **`workflow_dispatch`** — Manual trigger from the Actions UI. Essential for debugging a new pipeline without pushing dummy commits.
- **Docker Compose version check** — The compose files use `include:` (v2.20+) and `extends: file:` (v2.24+). A version check at the start of Docker jobs catches runner image regressions early.
- **Container log dump on failure** — `docker compose logs finwise-mcp-server` captured and uploaded as an artifact. When an E2E test fails against the container, you need the server's perspective.
- **`.env` cleanup** — The generated `.env` file contains a credential. Ephemeral runners handle this, but explicit `rm -f .env` in an `always()` step is good hygiene.

---

## The Secrets-vs-Variables Split

The last design refinement came from setting up the actual GitHub configuration.

> **User:** "I'm doing it like the attached. All as ENV VARS in GitHub except the real secret FINWISE_AZURE_CLIENT_SECRET which is as Environment secret in GitHub. Is that right?"

Exactly right. Tenant IDs, client IDs, endpoint URLs, and deployment names identify resources but don't grant access alone. Only the client secret is a credential. A GitHub Environment named `finwise-ci-testing` was created with:

- **1 environment secret:** `FINWISE_AZURE_CLIENT_SECRET` (masked in logs, `${{ secrets.* }}`)
- **7 environment variables:** everything else (visible in the GitHub UI for debugging, `${{ vars.* }}`)

This included `FINWISE_FORCE_IN_MEMORY_DATA` as a configurable toggle — flipping it to `true` in the GitHub UI would skip all database containers entirely, enabling a fast smoke-test CI run without editing the workflow YAML.

The remaining infrastructure values (Docker DNS hostnames like `finwise-cosmosdb-emulator:8081`, the well-known emulator key, Redis connection strings) are hardcoded in the `.env` generation script — they're determined by docker-compose container names and never change between CI runs.

---

## The Final Shape

Three jobs. Sequential. Each with a clear purpose.

```
Job 1: build-and-unit-tests       (no infra, ~2 min)
Job 2: integration-tests          (CosmosDB emulator + Redis, infra-only tests)
Job 3: e2e-and-container-tests    (full Docker stack, MCP E2E + container tests)
```

Two files to create at implementation time: `.github/workflows/ci.yml` and a minor `README.md` addition documenting the required GitHub Environment configuration. Zero code changes. Zero test changes. The existing stack and the existing tests — running in CI exactly as they run locally.

**Spec:** [013-ci-github-actions-workflow.md](../specs/013-ci-github-actions-workflow/013-ci-github-actions-workflow.md)

---

## What We Learned

### About CI Design

- **"Straightforward" hides complexity.** The first draft had a blocker (no running server for E2E tests) that would have surfaced as a mysterious CI failure hours into implementation.
- **Know your test architecture before designing your pipeline.** The distinction between `[Fact]` (fails hard) and `[SkippableFact]` (skips gracefully) determined the entire job structure.
- **Consolidation beats duplication.** Two test projects sharing one server eliminated an entire background-process management mechanism.

### About the Emulator Migration Dividend

- The vnext-preview emulator migration (Journal 22) paid off immediately in CI planning. The resource budget went from "tight, might be flaky" to "comfortable, plenty of headroom." The health check went from "curl a TLS certificate endpoint and hope" to a native `/ready` probe. Decisions compound.

### About Plan Reviews

- The plan went through five review rounds. Each one found real issues — not hypotheticals, not style nits. The blocker was found in round one. The emulator image mismatch was found in round three. The OWASP hardening was added in round four. Each pass had a focused lens, and each made the plan materially better.

---

## What's Next

Implementation. One YAML file, one README edit. The plan is reviewed, validated against the codebase, cross-checked with Microsoft Learn guidance, and confirmed lean enough for a POC while maintaining quality.

Then: push, watch the Actions tab, and see if three green checks appear.

---

*Written: April 26, 2026*
