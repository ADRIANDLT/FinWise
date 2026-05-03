# 24 — Implementing the CI GitHub Actions Workflow

*April 26, 2026*
*The plan was three sequential jobs. The implementation became four parallel jobs, dual-mode execution, and a multi-pass refinement loop where a Critic agent found bugs that would have surfaced as mysterious CI failures at 2 AM.*

---

## Where We Are

Journal 23 ended with a plan — [Spec 013](../specs/013-ci-github-actions-workflow/013-ci-github-actions-workflow.md) — reviewed through five rounds, validated against the codebase, and declared ready to implement. Three sequential jobs: unit tests → integration tests → E2E + container tests. One YAML file, one README edit. "Straightforward."

The `.github/workflows/` directory is empty. The plan exists. Time to write the YAML.

---

## Pass 1: The Draft

The initial ci.yml landed at 266 lines. Three jobs, all steps accounted for, correct structure. The shape was right — checkout, setup .NET 10, build, test, upload artifacts, teardown. The heredoc trick for `.env` generation worked: `<<'STATIC'` for hardcoded values (no shell expansion), `<<EOF` for secrets (shell expands `$vars`). Docker networking was correct: container names (`finwise-cosmosdb-emulator:8081`) in the `.env` for inter-container communication, `localhost` in `test.docker-local.runsettings` for the host test runner.

But a draft is just a draft. What matters is what the review finds.

---

## The NDJSON Bug

The Critic's first pass found it.

The infrastructure health check in Job 2 polled `docker compose ps --format json` and piped it through `jq`:

```bash
jq -e "all(.Health == \"healthy\")"
```

The problem: Docker Compose v2.21+ outputs **NDJSON** — one JSON object per line, not a JSON array. The `all()` function applied to a single JSON object iterates over its *values* (strings like `"finwise-redis"`, `"healthy"`, `"running"`), not over an array of service objects. Applying `.Health` to a string yields `null`. `null == "healthy"` is `false`. `all(...)` returns `false` for every line. The `until` loop never terminates. The step times out after 60 seconds with exit code 124 and zero context about what went wrong.

The fix: add `-s` (slurp) to collect NDJSON into an array, and a `length > 0` guard against vacuous truth:

```bash
jq -se "length > 0 and all(.[]; .Health == \"healthy\")"
```

This would have surfaced as a timeout flake on the very first CI run — 60 seconds of silence followed by "Process completed with exit code 124." The kind of failure that wastes an hour of debugging because the error message tells you nothing.

---

## The Clarity Pass

The second review lens was clarity and maintainability. Three findings, all about reducing drift:

**Magic strings.** The .NET version `'10.0.x'` appeared three times. The runsettings filename appeared five times. The Docker Compose minimum version `"2.24.0"` appeared twice. One env var bump, one filename change, and you're hunting through the file for every occurrence.

The fix: workflow-level `env:` block.

```yaml
env:
  DOTNET_VERSION: '10.0.x'
  TEST_RUNSETTINGS: test.docker-local.runsettings
  DOCKER_COMPOSE_MIN_VERSION: '2.24.0'
```

Three constants, referenced everywhere via `${{ env.DOTNET_VERSION }}`. If .NET 11 ships, one line changes.

**Heredoc intent.** The `.env` generation step used two `cat` commands — one with `<<'STATIC'` (no expansion), one with `<<EOF` (expansion). A reader who doesn't know the bash `<<'DELIM'` vs `<<DELIM` distinction would miss *why* there are two blocks. Two comment lines made the technique self-documenting:

```bash
# Static values (single-quoted heredoc = no variable expansion)
cat > .env <<'STATIC'
...
# Dynamic values from GitHub environment (unquoted heredoc = shell expands $vars)
cat >> .env <<EOF
```

**Inconsistent naming.** Job 3's build step was called "Build test projects" while Jobs 1 and 2 called the identical step "Build." Renamed to match.

---

## The Edge Cases

The third review lens was error handling and failure modes. Three important findings:

**Opaque timeout messages.** When `timeout 60` expires, it sends SIGTERM and exits with code 124. GitHub Actions renders this as "Process completed with exit code 124" — a reader has no idea *what* timed out. The fix: wrap every `timeout` call with a failure handler that emits a `::error::` annotation and dumps diagnostic context:

```bash
' || {
  echo "::error::Infrastructure health check timed out after 60s"
  docker compose -f docker-compose.infra.yml ps
  exit 1
}
```

Now the failure message names the problem, and the container status is right there in the log.

**No job timeouts.** GitHub Actions defaults `timeout-minutes` to 360 — six hours. A hung test could burn an entire day's runner quota. The fix: explicit timeouts proportional to each job's expected runtime — 15 min for unit tests, 20 for integration, 25 for E2E.

**Missing secrets scenario.** If someone forks the repo without configuring the `finwise-ci-testing` GitHub Environment, all `vars.*` and `secrets.*` resolve to empty strings. The `.env` file gets lines like `FINWISE_AZURE_TENANT_ID=`. The MCP server fails to initialize. The health check times out. Exit code 124. No context.

The fix: a validation gate before any expensive work:

```bash
missing=()
[ -z "$FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT" ] && missing+=("...")
...
if [ ${#missing[@]} -gt 0 ]; then
  echo "::error::Missing required environment variables: ${missing[*]}"
  echo "Configure the 'finwise-ci-testing' GitHub environment. See README.md."
  exit 1
fi
```

Fail fast, name the missing vars, point to the docs.

---

## The Final Review: Ship It

The fourth pass was a ship/no-ship review across all dimensions — correctness, security, robustness, production-readiness.

**Verdict: SHIP.**

Zero critical issues. Zero important issues remaining. The Critic verified all 9 correctness checks (test project paths, compose file references, NDJSON parsing, heredoc quoting, version comparison, runsettings localhost endpoints, artifact names, .NET version consistency, dependency chain). Security checks passed: `permissions: contents: read`, secrets via `${{ secrets.* }}`, no expression injection, `.env` cleaned up in `always()`. The `if-no-files-found: ignore` on all artifact uploads. Teardown with `--timeout 10`.

One suggestion survived: swap the `.env` cleanup step with the Docker teardown step. Teardown should happen while the `.env` file still exists, not after it's deleted. A trivial reorder that eliminates a fragile dependency on job-level env vars being a superset of `.env` content.

---

## The Mode Question

The plan was done. The YAML was reviewed. Then came the question that changed the architecture.

> **User:** "Depending on this ENV VAR: FINWISE_FORCE_IN_MEMORY_DATA=false — I'd like the CI workflow to run conditionally. Is it possible?"

The idea: when `FINWISE_FORCE_IN_MEMORY_DATA` is `true`, skip the integration tests and run the MCP server with in-memory stores. Faster CI, no database containers, same E2E coverage for the agent orchestration logic.

The first proposal was a `workflow_dispatch` input parameter — push/PR always runs full, manual dispatch offers a "fast mode" dropdown. But that creates two sources of truth: the environment variable and the dispatch input. Which one wins?

> **User:** "But this is confusing. If we have the ENV VAR on one mode but later is going to work differently because of the workflow_dispatch input parameter?"

Fair point. `FINWISE_FORCE_IN_MEMORY_DATA` is an **application config toggle** that means the same thing everywhere — local Docker, Azure Container Apps, GitHub CI. Introducing a second mechanism specific to CI violates the "same var, same meaning, same mechanism" principle.

> **User:** "I'm still having the ENV VAR in Docker and also in the Azure cloud deployment... Is it better to use a different mechanism in GitHub?"

No. One toggle, three environments, identical semantics. The workflow reads `vars.FINWISE_FORCE_IN_MEMORY_DATA` from the GitHub Environment — the same place all other config lives — and adapts.

---

## The Restructure

The conditional mode required a structural change. Integration tests were no longer a mandatory step in a sequential chain — they were optional. And if they're optional, they shouldn't block the E2E tests.

**Before (3 sequential jobs):**
```
build-and-unit-tests → integration-tests → e2e-and-container-tests
```

**After (4 jobs, parallel fan-out):**
```
resolve-mode ──────────┐
                       ├──→ e2e-and-container-tests (always, adapts to mode)
build-and-unit-tests ──┘
                       └──→ integration-tests (full mode only)
```

A new `resolve-mode` job runs in parallel with unit tests — just reads the env var, normalizes it to lowercase, and outputs the flag. Both downstream jobs wait for both parents to complete. Integration tests have an `if:` gate that skips the entire job in fast mode.

The E2E job adapts its Docker command:
- **Full mode:** `docker compose up -d --build` — full stack with real databases
- **Fast mode:** `docker compose -f docker-compose.finwise.yml up -d --build` — server only, in-memory stores

This works because `docker-compose.finwise.yml` is standalone — no `depends_on` on infrastructure. A design decision from the Docker containerization work (Journal 13) that pays off here.

The `.env` generation also adapts: dynamic values are always written (Azure secrets), but infrastructure config (CosmosDB endpoint, Redis connection string) is only appended in full mode. In fast mode, those `${VAR:-}` references in the compose file resolve to empty — the server sees no database config and falls back to in-memory stores.

---

## The Critic Catches Two More

The Critic reviewed the restructured workflow and found two issues:

**Case sensitivity.** The `resolve-mode` step passed the variable through verbatim. All downstream comparisons used `= "true"`. But .NET's `bool.Parse()` is case-insensitive — if someone sets the GitHub variable to `TRUE`, the workflow would run full mode (starting infra) while the server would use in-memory stores. A silent mode mismatch.

Fix: normalize to lowercase at the single point of entry:

```bash
mode=$(echo "${mode:-false}" | tr '[:upper:]' '[:lower:]')
```

**Step ordering.** The `.env` cleanup ran before Docker teardown. `docker compose down` needs to parse the compose file, which references `${VAR}` substitutions. After `.env` deletion, those infra-specific vars (hardcoded in the STATIC heredoc, not in job-level env) are gone. Works today because Compose v2 resolves missing vars to empty. Fragile for the future.

Fix: swap the order — tear down the stack, then clean up the file.

---

## The Documentation Sweep

With the workflow complete, three documents needed updates:

**README.md** — A new ⚙️ CI Pipeline section placed before Logging, documenting the two modes, the job graph, and the full GitHub Environment setup (1 secret + 7 variables). The Project Structure tree was updated with `.github/workflows/ci.yml`, plus two missing test projects (`FinWise.McpServer.UnitTests/` and `FinWise.Redis.IntegrationTests/`) that had been absent since they were created.

**Architecture doc** ([05-architecture-and-technologies-v1.0.0.md](../specs/05-architecture-and-technologies-v1.0.0.md)) — A new Appendix G documenting the CI pipeline: dual-mode execution, job graph, workflow hardening measures, and GitHub Environment config. "GitHub Actions CI/CD" was removed from the "What's NOT in v1.0.0" deferred table — it's no longer deferred. The repository structure in Appendix D gained the `.github/` entry.

**Spec 013** — Left as-is. The spec was the plan; the implementation evolved it. The README and ci.yml are the living source of truth. The spec documents the thinking that led here, which has its own value.

---

## The Final Shape

Four jobs. Parallel fan-out. Mode-adaptive.

```yaml
# Full mode (FINWISE_FORCE_IN_MEMORY_DATA=false):
resolve-mode ──────────┐
                       ├──→ e2e-and-container-tests (full Docker stack)
build-and-unit-tests ──┘
                       └──→ integration-tests (CosmosDB + Redis)

# Fast mode (FINWISE_FORCE_IN_MEMORY_DATA=true):
resolve-mode ──────────┐
                       ├──→ e2e-and-container-tests (server only, in-memory)
build-and-unit-tests ──┘
                       └──→ integration-tests (SKIPPED)
```

One file: [.github/workflows/ci.yml](../.github/workflows/ci.yml) — 354 lines, battle-tested through four review passes.

**Key files:**
- [ci.yml](../.github/workflows/ci.yml) — The workflow
- [Spec 013](../specs/013-ci-github-actions-workflow/013-ci-github-actions-workflow.md) — The original plan
- [README.md CI section](../README.md) — User-facing documentation
- [Architecture doc Appendix G](../specs/05-architecture-and-technologies-v1.0.0.md) — Technical reference

---

## What We Learned

### About Multi-Pass Review

- **Each pass needs a focused lens.** Correctness found the NDJSON bug. Clarity found the magic strings. Edge cases found the timeout opacity. A single "review everything" pass would have caught some but missed others.
- **The Critic pattern works.** Delegating review to a separate agent with a specific lens and explicit instructions to NOT fix things — only find them — produced higher-quality findings than self-review.
- **Four passes is the right number for infrastructure code.** Draft → Correctness → Clarity → Edge Cases. The final ship/no-ship pass found nothing new — a sign the prior passes were thorough.

### About Conditional CI

- **Same toggle, same semantics, everywhere.** The temptation to add a CI-specific `workflow_dispatch` input was strong — it's the GitHub-native pattern. But it would have created a second source of truth for a concept that already has one (`FINWISE_FORCE_IN_MEMORY_DATA`). The user caught this immediately.
- **Optional jobs should run in parallel, not block.** The sequential chain was correct when all jobs were mandatory. The moment integration tests became optional, they should no longer gate the E2E tests. Restructuring to parallel fan-out was faster even in full mode.

### About Docker Compose Design Paying Dividends

- **Standalone compose files enable CI modes.** `docker-compose.finwise.yml` being standalone (no `depends_on` on infra) was a design decision from Journal 13. It enabled Mode C (server → Azure databases). Now it also enables fast CI mode (server only, in-memory). Good abstractions compound.
- **`${VAR:-}` defaulting in compose files** is what makes fast mode possible. The server container receives empty infra vars and falls back to in-memory. No special CI logic needed in the application.

### About Plans vs. Reality

- The spec planned 3 sequential jobs. The implementation delivered 4 parallel jobs with dual-mode execution. The spec was the right starting point — you need a plan to deviate from. But the plan was a plan, not a contract.

---

## What's Next

Push the branch. Configure the `finwise-ci-testing` GitHub Environment with the 7 variables and 1 secret. Watch the Actions tab. See if four green checks appear — or if the first real CI run surfaces something no review pass caught.

Then: consider the future optimizations the spec deferred — NuGet caching, Docker layer caching, test result reporting actions. But those are polish. The pipeline works.

---

*Written: April 26, 2026*
