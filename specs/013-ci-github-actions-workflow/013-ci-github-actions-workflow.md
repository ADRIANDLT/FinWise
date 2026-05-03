# Spec 013 — CI GitHub Actions Workflow

> **Status:** Implemented — see [`.github/workflows/ci.yml`](../../.github/workflows/ci.yml)  
> **Implementation evolved** from the original 3-sequential-job plan to a 4-job parallel fan-out with dual-mode execution. This spec has been updated to reflect the final implementation.

## Goal

Automate build verification and testing on every push/PR using GitHub Actions, leveraging the existing Docker Compose stack to run integration and E2E tests in CI — the same way they run locally.

## Context

- **Before this spec was implemented, no CI existed** — `.github/workflows/` was empty.
- The project already has a full Docker Compose stack (`docker-compose.yml` → includes `docker-compose.infra.yml` + `docker-compose.finwise.yml`) with health checks.
- CosmosDB emulator is `mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview` (1 GB RAM, HTTPS, native `/ready` probe on port 8080, ~15s startup) — already migrated and verified.
- `[SkippableFact]` / `Skippable.If()` is used by some integration tests so they self-skip when their specific dependencies are missing.
- `test.docker-local.runsettings` already configures the env vars for tests against local Docker infrastructure.
- Target framework: **net10.0**.
- `docker-compose.yml` uses `include:` (Compose v2.20+) and `extends:` with `file:` (Compose v2.24+).

## Workflow Design

### Single workflow file: `.github/workflows/ci.yml`

**Triggers:**

- `push` to `main`
- `pull_request` targeting `main`
- `workflow_dispatch` (manual trigger from Actions UI — useful for debugging)

**Runner:** `ubuntu-latest` (Docker Compose v2 is pre-installed; `jq` and `curl` are pre-installed).

**Workflow-level hardening:**

```yaml
permissions:
  contents: read           # Least-privilege: CI only reads source code

concurrency:
  group: ci-${{ github.workflow }}-${{ github.ref }}
  cancel-in-progress: true # Cancel stale runs when new commits land on a PR/branch
```

**Workflow-level constants** (single source of truth for repeated values):

```yaml
env:
  DOTNET_VERSION: '10.0.x'
  TEST_RUNSETTINGS: test.docker-local.runsettings
  DOCKER_COMPOSE_MIN_VERSION: '2.24.0'
```

### Dual-Mode Execution

Controlled by `FINWISE_FORCE_IN_MEMORY_DATA` in the `finwise-ci-testing` GitHub Environment:

| Mode | Value | Jobs that run | Speed |
|------|-------|--------------|-------|
| **Full** | `false` (default) | Unit + Integration + E2E (real databases) | ~12 min |
| **Fast** | `true` | Unit + E2E only (in-memory stores) | ~7 min |

The `resolve-mode` job reads the variable, normalizes it to lowercase (case-insensitive), and outputs the flag. Downstream jobs use `needs.resolve-mode.outputs.in_memory` to adapt behavior.

### Four jobs, parallel fan-out:

```
resolve-mode ──────────┐
                       ├──→ e2e-and-container-tests (always, adapts to mode)
build-and-unit-tests ──┘
                       └──→ integration-tests (full mode only)
```

`resolve-mode` and `build-and-unit-tests` run in parallel (no dependency between them). Both must complete before either downstream job starts. In full mode, `e2e-and-container-tests` and `integration-tests` also run in parallel.

**Prerequisite step (Docker jobs):** Verify Docker Compose version ≥ v2.24 and fail early with a clear message if not met.

---

### Job 1: `build-and-unit-tests`

No infrastructure needed. Fast feedback (~2 min). **`timeout-minutes: 15`**.

| Step | Command |
|------|---------|
| Checkout | `actions/checkout@v4` |
| Setup .NET | `actions/setup-dotnet@v4` with `dotnet-version: '10.0.x'` |
| Restore | `dotnet restore FinWise.slnx` |
| Build | `dotnet build FinWise.slnx --no-restore -c Release` |
| Unit tests (McpServer) | `dotnet test tests/FinWise.McpServer.UnitTests/ --no-build -c Release --logger trx --results-directory TestResults` |
| Unit tests (Workflow) | `dotnet test tests/FinWise.MultiAgentWorkflow.UnitTests/ --no-build -c Release --logger trx --results-directory TestResults` |
| Upload test results | `actions/upload-artifact@v4` — upload `TestResults/*.trx` (in `always()` post step, `if-no-files-found: ignore`) |

**Artifacts:** Test result `.trx` files uploaded for inspection on failure. Build output is NOT shared — each job rebuilds (simpler than artifact transfer).

---

### Job 2: `integration-tests`

**Depends on:** `build-and-unit-tests` and `resolve-mode`  
**Conditional:** `if: needs.resolve-mode.outputs.in_memory != 'true'` — **skipped entirely in fast mode**  
**`timeout-minutes: 20`**  
**Environment:** `finwise-ci-testing` (provides `vars.*` and `secrets.*`)  
**Infrastructure:** CosmosDB emulator (`mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview`) + Redis (`redis:7.4-alpine`) via `docker-compose.infra.yml`. **No MCP server in this job** — server-dependent tests run in Job 3.  
**Credentials required:** Azure service principal vars + secret (only for the Stock Agent tests; CosmosDB and Redis tests are infra-only)

| Step | Detail |
|------|--------|
| Checkout | `actions/checkout@v4` |
| Setup .NET | `actions/setup-dotnet@v4` with `dotnet-version: '10.0.x'` |
| Verify Docker Compose | `docker compose version` — fail if < v2.24 |
| Start infra | `docker compose -f docker-compose.infra.yml up -d` |
| Wait for healthy | Inline shell loop with **60-second timeout**: `timeout 60 bash -c 'until docker compose -f docker-compose.infra.yml ps --format json \| jq -se "length > 0 and all(.[]; .Health == \"healthy\")" > /dev/null 2>&1; do sleep 5; done'` with `|| { echo ::error::...; docker compose ps; exit 1; }` error handler. The `-s` flag slurps NDJSON into an array (Docker Compose v2.21+ outputs NDJSON, not a JSON array). |
| Build | `dotnet build FinWise.slnx -c Release` |
| CosmosDB integration tests | `dotnet test tests/FinWise.CosmosDb.IntegrationTests/ --no-build -c Release -s test.docker-local.runsettings --logger trx --results-directory TestResults` |
| Redis integration tests | `dotnet test tests/FinWise.Redis.IntegrationTests/ --no-build -c Release -s test.docker-local.runsettings --logger trx --results-directory TestResults` |
| Stock Agent integration tests | `dotnet test tests/FinWise.StockAgent.IntegrationTests/ --no-build -c Release -s test.docker-local.runsettings --logger trx --results-directory TestResults` — tests self-skip via `Skippable.If()` in their constructor when Stock Agent secrets are missing |
| Upload test results | `actions/upload-artifact@v4` — upload `TestResults/*.trx` (in `always()` post step, `if-no-files-found: ignore`) |
| Teardown infra | `docker compose -f docker-compose.infra.yml down --timeout 10` (in `always()` post step) |

**Job-level env:**

```yaml
environment: finwise-ci-testing
env:
  # Stock Agent config (environment variables — non-sensitive)
  STOCK_AGENT_PROJECT_ENDPOINT: ${{ vars.STOCK_AGENT_PROJECT_ENDPOINT }}
  STOCK_AGENT_NAME: ${{ vars.STOCK_AGENT_NAME }}
  # Azure service principal (vars for config, secret for credential)
  FINWISE_AZURE_TENANT_ID: ${{ vars.FINWISE_AZURE_TENANT_ID }}
  FINWISE_AZURE_CLIENT_ID: ${{ vars.FINWISE_AZURE_CLIENT_ID }}
  FINWISE_AZURE_CLIENT_SECRET: ${{ secrets.FINWISE_AZURE_CLIENT_SECRET }}
```

> **Note:** CosmosDB / Redis test config (endpoints, keys, etc.) comes from `test.docker-local.runsettings` — no need to duplicate at job level since no in-process server reads from OS env in this job. The CosmosDB emulator key is a well-known fixed value, not a secret.

---

### Job 3: `e2e-and-container-tests`

**Depends on:** `build-and-unit-tests` and `resolve-mode`  
**Always runs** — adapts behavior based on mode  
**`timeout-minutes: 25`**  
**Environment:** `finwise-ci-testing` (provides `vars.*` and `secrets.*`)  
**Infrastructure (full mode):** Full Docker stack — CosmosDB emulator + Redis + FinWise MCP Server container (built from `src/FinWise.McpServer/Dockerfile`)  
**Infrastructure (fast mode):** Server container only — `docker compose -f docker-compose.finwise.yml up -d --build` with `FINWISE_FORCE_IN_MEMORY_DATA=true`  
**Credentials required:** Azure AI Foundry vars + client secret (injected into the MCP Server container via generated `.env` file)  
**Tests run here:** Both `McpServer.IntegrationTests` (E2E HTTP/MCP protocol) and `McpServer.ContainerTests` (container-specific health, networking, env var injection) — both target the same `FINWISE_MCP_URL`, so they share one running container.

| Step | Detail |
|------|--------|
| Checkout | `actions/checkout@v4` |
| Setup .NET | `actions/setup-dotnet@v4` with `dotnet-version: '10.0.x'` |
| Verify Docker Compose | `docker compose version` — fail if < v2.24 |
| Validate required secrets | Check that 5 required env vars are non-empty; fail with `::error::` naming missing vars and pointing to README |
| Generate `.env` file | Dynamic values (Azure secrets) always written. Infrastructure config (CosmosDB, Redis) appended **only in full mode** — in fast mode, `${VAR:-}` in compose files resolve to empty and the server falls back to in-memory stores. Uses split heredoc: `<<EOF` for dynamic values (shell expansion), `<<'STATIC'` for hardcoded infra values (no expansion). |
| Build & start stack | **Full mode:** `docker compose up -d --build` (full stack). **Fast mode:** `docker compose -f docker-compose.finwise.yml up -d --build` (server only) |
| Wait for MCP server healthy | Inline shell loop. **Full mode:** 120-second timeout. **Fast mode:** 60-second timeout. Both with `|| { echo ::error::...; docker compose logs --tail=40; exit 1; }` error handler. |
| Build test projects | `dotnet build FinWise.slnx -c Release` |
| MCP Server E2E tests | `dotnet test tests/FinWise.McpServer.IntegrationTests/ --no-build -c Release -s test.docker-local.runsettings --logger trx --results-directory TestResults` |
| Container tests | `dotnet test tests/FinWise.McpServer.ContainerTests/ --no-build -c Release -s test.docker-local.runsettings --logger trx --results-directory TestResults` |
| Dump container logs on failure | `docker compose logs finwise-mcp-server > finwise-mcp-server.log` — adapts compose command to mode (in `failure()` post step) |
| Upload test results & logs | `actions/upload-artifact@v4` — upload `TestResults/*.trx` and `finwise-mcp-server.log` if present (in `always()` post step, `if-no-files-found: ignore`) |
| Teardown | `docker compose down --timeout 10` or `docker compose -f docker-compose.finwise.yml down --timeout 10` depending on mode (in `always()` post step) |
| Cleanup `.env` | `rm -f .env` (in `always()` post step, **after** teardown) |

> **Why both test projects in one job?** Both `EndToEndMcpTests` and `DockerizedMcpTests` extend `McpEndToEndTestBase` and POST to `{FINWISE_MCP_URL}/mcp`. They differ only in skip behavior (container tests use `Skip.IfNot(reachable)`; E2E tests fail hard). Running both against the same Docker container avoids spinning up the server twice and eliminates an entire background-process management mechanism.

---

## GitHub Environment Configuration

A GitHub Environment named **`finwise-ci-testing`** must be created at Settings → Environments. Jobs 2 and 3 reference this environment to access its variables and secrets.

### Environment secret (truly sensitive credential):

| Secret Name | Purpose | Syntax in workflow |
|---|---|---|
| `FINWISE_AZURE_CLIENT_SECRET` | Service principal client secret | `${{ secrets.FINWISE_AZURE_CLIENT_SECRET }}` |

### Environment variables (non-sensitive configuration):

| Variable Name | Purpose | Syntax in workflow |
|---|---|---|
| `FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT` | Azure AI Foundry project endpoint | `${{ vars.FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT }}` |
| `FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME` | LLM deployment name | `${{ vars.FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME }}` |
| `FINWISE_AZURE_TENANT_ID` | Azure AD tenant ID | `${{ vars.FINWISE_AZURE_TENANT_ID }}` |
| `FINWISE_AZURE_CLIENT_ID` | Service principal client ID | `${{ vars.FINWISE_AZURE_CLIENT_ID }}` |
| `STOCK_AGENT_PROJECT_ENDPOINT` | Azure AI Foundry endpoint for Stock Agent | `${{ vars.STOCK_AGENT_PROJECT_ENDPOINT }}` |
| `STOCK_AGENT_NAME` | Stock Agent name | `${{ vars.STOCK_AGENT_NAME }}` |
| `FINWISE_FORCE_IN_MEMORY_DATA` | Data store master toggle (`false` = use Docker DBs, `true` = in-memory only) | `${{ vars.FINWISE_FORCE_IN_MEMORY_DATA }}` |

> **Why this split?** Tenant IDs, client IDs, endpoint URLs, and deployment names identify resources but do not grant access on their own. Only the client secret is a credential. Storing non-sensitive config as environment variables (not secrets) makes them visible in the GitHub UI for easier debugging, while the client secret remains masked in logs.

---

## Implementation Tasks

| # | Task | Files |
|---|------|-------|
| 1 | Create the workflow file | `.github/workflows/ci.yml` |
| 2 | Document CI pipeline and GitHub Environment config | `README.md` (new CI section + Project Structure update) |
| 3 | Update architecture document | `specs/05-architecture-and-technologies-v1.0.0.md` (new Appendix G + repo structure update) |

**Actual file changes:** 1 new file (workflow YAML), 2 edits (README, architecture doc). No code or test changes needed.

---

## Key Design Decisions

1. **Four jobs with parallel fan-out** — `resolve-mode` + `build-and-unit-tests` run in parallel, then `e2e-and-container-tests` + `integration-tests` fan out in parallel. Fast-fail: unit failures stop CI before any Docker spin-up. Integration tests are conditional (full mode only). Originally planned as 3 sequential jobs; restructured during implementation to support dual-mode execution and faster CI in full mode.

2. **Dual-mode execution via `FINWISE_FORCE_IN_MEMORY_DATA`** — The same environment variable used in Docker Compose and Azure Container Apps controls CI mode. `false` = full suite with real databases; `true` = unit + E2E only with in-memory stores. One toggle, three environments, identical semantics. No CI-specific mechanism.

3. **Rebuild in each job** rather than transferring artifacts — simpler, avoids artifact upload/download overhead for a solution this size.

4. **Consolidate MCP server tests into the E2E job** — Both `McpServer.IntegrationTests` (E2E) and `McpServer.ContainerTests` connect to the same `FINWISE_MCP_URL`. Running them against one Docker container (instead of one local `dotnet run` + one container) eliminates background-process management (PID files, log capture, kill steps) and removes the need for Azure secrets in the integration job except for the Stock Agent test.

5. **`test.docker-local.runsettings` for test config** — provides infrastructure env vars to the test process. Azure secrets are added at job level only where needed (Stock Agent in integration job; via `.env` for the container in E2E job).

6. **Generate `.env` at runtime for the E2E job** — credentials can't be committed. Dynamic values (Azure secrets) always written via `<<EOF` heredoc (shell expansion). Infrastructure config (CosmosDB, Redis) appended only in full mode via `<<'STATIC'` heredoc (no expansion). The `.env` file is deleted in the `always()` cleanup step **after** Docker teardown.

7. **GitHub Environment `finwise-ci-testing`** — All jobs that need secrets declare `environment: finwise-ci-testing`. Only `FINWISE_AZURE_CLIENT_SECRET` is stored as an environment secret (masked in logs). All other Azure config is stored as environment variables — visible in the GitHub UI for easier debugging, not sensitive on their own.

8. **Resource budget fits comfortably** — `ubuntu-latest` runners have 7 GB RAM / 2 CPU. CosmosDB emulator vnext-preview is 1 GB; Redis is 256 MB; MCP server container is 512 MB. Total ~1.8 GB leaves plenty of headroom for the .NET build/test processes.

9. **Docker Compose version check** — `docker-compose.yml` uses `include:` (Compose v2.20+) and `extends:` with `file:` (Compose v2.24+). Docker jobs verify the version early and fail with a clear message if too old. The minimum version is defined once in the workflow-level `env:` block.

10. **Test result artifacts** — All jobs produce `.trx` files via `--logger trx` and upload them with `actions/upload-artifact@v4` in an `always()` step with `if-no-files-found: ignore`. The E2E job also dumps container logs on failure for diagnosis.

11. **Teardown with `--timeout 10`** — Docker compose teardown steps use `--timeout 10` to speed up CI runs. Teardown happens **before** `.env` cleanup to ensure compose files can still resolve `${VAR}` references.

12. **Reuse production compose files (not GitHub Actions service containers)** — Microsoft's official CI sample uses [GitHub Actions service containers](https://docs.github.com/en/actions/use-cases-and-examples/using-containerized-services/about-service-containers). We deliberately use `docker compose` instead because the project's explicit goal is *"CI runs the same stack as local dev."* Service containers would duplicate infra definitions and drift from `docker-compose.infra.yml`.

13. **Workflow hardening** — `permissions: contents: read` enforces least-privilege for `GITHUB_TOKEN` (OWASP-aligned). `concurrency: cancel-in-progress: true` prevents wasted CI minutes. Job-level `timeout-minutes` (2/15/25/20) prevents hung tests from burning runner quota. Timeout error handlers emit `::error::` annotations with diagnostic context. Secrets validation gate fails fast with actionable messages. Mode normalization via `tr '[:upper:]' '[:lower:]'` prevents case-sensitivity mismatches between CI and the .NET server.

14. **Test result reporting** — Each job uses `dorny/test-reporter@v3` with `reporter: dotnet-trx` to render `.trx` files as GitHub Check Run annotations. This produces three separate check runs (*Unit Tests*, *E2E & Container Tests*, *Integration Tests*) with per-test pass/fail details visible in the PR checks tab and workflow summary. The `checks: write` permission is added at the workflow level to enable this. The reporting step uses `if: ${{ !cancelled() }}` per the action's official guidance so results are published after success or failure, but not on cancelled runs. Each reporter step also uses `continue-on-error: true`, so GitHub API/permission/reporting problems do not flip a green test job red — the `dotnet test` steps remain the source of truth for job success/failure. We evaluated `EnricoMi/publish-unit-test-result-action` as the main alternative; it is more feature-rich (especially PR comments and broader publishing modes), but `dorny/test-reporter` is the better fit for this repo because the workflow already emits `.trx`, runs only on `ubuntu-latest`, and needs GitHub Checks + workflow summaries rather than PR comments. Both actions still need a separate trusted reporting workflow for fork/Dependabot PRs because of GitHub token permission limits.

---

## Future Optimizations (not blocking this POC)

- **Refactor `EndToEndMcpTests` to use `WebApplicationFactory<Program>`** — Would allow MCP E2E tests to run in-process in Job 2 without any external server, further reducing Job 3 to container-specific tests only. Non-trivial test refactor.
- **NuGet & Docker layer caching** — `actions/cache@v4` keyed on `Directory.Packages.props` hash; Docker buildx cache for the MCP server image.

---

## Out of Scope

- Docker image push to registry (CD concern, not CI)
- Deployment to Azure Container Apps
- Code coverage reporting
- Matrix builds (multiple .NET versions / multiple OS)
- Scheduled/nightly runs
- Self-hosted runners
