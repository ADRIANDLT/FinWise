# 13 — Implementing the Docker Container for FinWise MCP Server  - The Docker Gauntlet

*March 28–29, 2026*
*Implementing the Docker containerization plan for FinWise MCP Server — and discovering that the hardest bugs are the ones that don't throw errors.*

---

## Where We Are

Journal 12 ended on a neat, tidy note: a 787-line [technical plan](../specs/009-dockerized-finwise/009-dockerized-finwise-plan.md) for Dockerizing the FinWise MCP Server. Multi-stage Dockerfile, docker-compose integration, shared E2E test base, container-specific integration tests. The plan was thorough. The research was solid. All that remained was typing.

Famous last words.

---

## Phase 1: The Happy Path (Hours 0–2)

The implementation started clean. Two parallel Coder agents worked simultaneously — one on Docker infrastructure, one on the test projects.

**Docker infrastructure** landed first:
- `.dockerignore` excluding build artifacts, IDE files, secrets
- `Dockerfile` — multi-stage build with `sdk:10.0` → `aspnet:10.0`
- `appsettings.Docker.json` — container-specific overrides (`0.0.0.0:5000`, `redis:6379`, `cosmosdb-emulator:8081`)
- `.env.template` for Azure OpenAI credentials

**Test projects** followed:
- `FinWise.McpServer.E2ETestBase` — shared class library with `McpEndToEndTestBase`
- Refactored `EndToEndMcpTests` to inherit from the shared base
- `FinWise.McpServer.ContainerTests` — 9 tests (4 reused MCP protocol tests + 5 Docker-specific validations)

Build succeeded. Zero warnings. The plan was working.

---

## Phase 2: The Dockerfile Gauntlet (Hours 2–3)

Then Docker said no — repeatedly.

**Problem 1: `dotnet restore FinWise.slnx` failed.** The solution file references test projects, but only `src/` was in the Docker build context. Fix: restore individual `.csproj` files instead.

**Problem 2: `Newtonsoft.Json` missing at runtime.** The package reference had `PrivateAssets="all"`, which excludes it from publish output. The CosmosDB SDK loads it via reflection at runtime — silent failure until the first database call. Fix: remove `PrivateAssets="all"`.

**Problem 3: `curl` not found.** The `aspnet:10.0` runtime image is intentionally minimal. No `curl`, which the health check needed. Fix: `apt-get install curl` in the Dockerfile — but it has to happen *before* `USER $APP_UID` because the non-root user can't install packages.

**Problem 4: Environment variables invisible.** The Azure OpenAI credentials were set at the Windows **Machine** (system) level, not User or Process. Standard `$env:VAR_NAME` returned nothing. Fix: `[System.Environment]::GetEnvironmentVariable("VAR_NAME", "Machine")`.

**Problem 5: Health check failing.** The MCP `/mcp` endpoint returns 405 (Method Not Allowed) for GET requests, which `curl -f` treats as failure. Fix: add a dedicated `/health` endpoint — one line of code: `app.MapGet("/health", () => "healthy")`.

Five bugs, five fixes. Each one trivial in isolation, each one a wall when hit for the first time. By the end, all three containers were running and healthy:

```
cosmosdb-emulator   healthy
finwise-redis       healthy
finwise-mcp         healthy
```

---

## Phase 3: The Invisible Hang (Hours 3–6)

Container tests: **6 pass, 3 hang forever.**

The passing tests were all simple: health check, startup time, environment variables, MCP initialize, tool discovery, a single financial advice call. The failing tests all had one thing in common: they called `SetupTestProfile()`, which makes five sequential MCP tool calls to build a user profile through the orchestrator → profile agent → CosmosDB flow.

The second tool call never returned. No error. No timeout. Just silence.

> **User:** "Show me the log or errors when trying to run the integration tests... and why it's stuck."

### Red Herring #1: SSE Streaming

The first hypothesis was reasonable: the MCP Streamable HTTP transport returns `text/event-stream` responses, and the test code was using `ReadAsStringAsync()` — which blocks until the stream closes. For quick responses, the server closes promptly. For longer multi-agent operations, the stream stays open.

Fix applied: `HttpCompletionOption.ResponseHeadersRead` + line-by-line `StreamReader` reading.

Tests still hung.

### Red Herring #2: Client-Side Timeout

Timeout increased from 100 seconds to 5 minutes. Still hung. This wasn't a timeout issue — the response was never coming.

### The Python Diagnostic

A raw Python HTTP client was deployed to bypass any C# peculiarities. Result: INIT works, Tool Call 1 works, Tool Call 2 (set email) **HANGS**. The same behavior as the C# tests.

This proved the bug was **server-side**, not client-side. The SSE fix was correct but irrelevant — the server itself was hanging.

### The Server Logs Tell the Story

Deep log analysis on the container revealed the smoking gun:

```
profileAgent tool: GetProfile called for delatorre@outlook.com
```

Then silence. `"CosmosDB initialization complete"` never appeared in the entire container lifetime. Instead, `"Initializing CosmosDB"` appeared 11 times, each exactly ~4 minutes and 12 seconds apart — the exact duration of a TCP SYN retry timeout on Linux.

The `SemaphoreSlim _initLock` in `CosmosDbUserProfileStore` was blocking all concurrent requests while each initialization attempt timed out, then releasing the lock only for the next request to try and fail identically.

---

## Phase 4: The Root Cause — `127.0.0.1` (Hours 6–8)

> **User:** "Could it be related to the following? `AZURE_COSMOS_EMULATOR_IP_ADDRESS_OVERRIDE=127.0.0.1` — this is the problem! When other Docker containers try to connect to the cosmosdb-emulator, they use the service name `cosmosdb-emulator`, but the emulator is configured to only listen on `127.0.0.1` (localhost)."

The user nailed it. Here's what was happening:

The CosmosDB Linux emulator reports endpoint addresses in its account metadata (`writableLocations`/`readableLocations`). The .NET SDK reads these and uses them for actual requests — even in Gateway mode. With `IP_ADDRESS_OVERRIDE=127.0.0.1`, the emulator reported `https://127.0.0.1:8081/` as its address.

From the host machine, this works — Docker port mapping routes `127.0.0.1:8081` to the emulator container. But from the `finwise-mcp` container, `127.0.0.1` means *the finwise-mcp container itself*. Nothing is listening on port 8081 there. TCP SYN → retry → retry → timeout after 4 minutes 12 seconds.

### Fix Attempt 1: `0.0.0.0`

Changed the override to `0.0.0.0`. The emulator now reported `https://0.0.0.0:8081/`. Container-to-container: `ServiceUnavailable`. Host-to-emulator: `ServiceUnavailable`. Worse than before.

### Fix Attempt 2: Remove It Entirely

The user found a GitHub branch (`copilot/fix-docker-hung-request-issue`) that simply removed the environment variable. Without the override, the emulator reports its Docker-internal IP (e.g., `https://172.18.0.3:8081/`). From other containers on the same Docker network, this is reachable.

Applied. Container tests: **9/9 passed.** 🎉

But then...

---

## Phase 5: The Reverse Problem (Hours 8–10)

> **User:** "OK, now run ALL integration tests from FINWISE."

The CosmosDB integration tests — which run from the **host machine** against the emulator in Docker — hung indefinitely. The exact same class of bug, in reverse.

Without `IP_ADDRESS_OVERRIDE`, the emulator reports `https://172.18.0.3:8081/`. From the Windows host, `172.18.0.3` is inside the Docker Desktop Linux VM — completely unreachable. The SDK reads the metadata, tries to connect to the Docker-internal IP, and hangs.

The behavior matrix crystallized:

| `IP_ADDRESS_OVERRIDE` | Host → Emulator | Container → Emulator |
|---|---|---|
| `127.0.0.1` | ✅ | ❌ hangs |
| `0.0.0.0` | ❌ | ❌ |
| Removed | ❌ hangs | ✅ |

No value of the environment variable works for both scenarios. The problem isn't the emulator's listening address — it's what the **SDK does with the metadata**.

### The Real Fix: `LimitToEndpoint = true`

```csharp
cosmosClientOptions.LimitToEndpoint = true;
```

One line. This tells the Azure.Cosmos SDK: "Only use the endpoint URL I gave you in the connection string. Ignore whatever the emulator reports in its metadata."

Internally, `LimitToEndpoint = true` sets `EnableEndpointDiscovery = false`. The SDK stops querying account metadata for replica endpoints, stops trying to connect to Docker-internal IPs, and just uses `https://localhost:8081/` (from host) or `https://cosmosdb-emulator:8081/` (from container) — exactly what was specified in the connection string.

The fix was placed inside the existing `if (AllowInsecureTls)` guard in `UserProfileStoreFactory.cs` — the same block that enables `DangerousAcceptAnyServerCertificateValidator` and `ConnectionMode.Gateway`. Three settings that only apply to the emulator, never to production Azure CosmosDB:

```csharp
if (cosmosDbOptions.AllowInsecureTls)
{
    cosmosClientOptions.HttpClientFactory = () => new HttpClient(new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });
    cosmosClientOptions.ConnectionMode = ConnectionMode.Gateway;
    cosmosClientOptions.LimitToEndpoint = true;
}
```

> **User:** "What if I migrate from the CosmosDB emulator to Azure CosmosDB in production in the Cloud? Having that HARDCODED can be a problem."

A valid concern — but it's already conditional. Production would have `AllowInsecureTls = false`, so `LimitToEndpoint` stays at the default `false`, preserving multi-region failover. The guard was already there.

### Research Validation

Two independent researcher agents were deployed to verify:

- **GPT-5.1-Codex** (82% confidence): Confirmed `LimitToEndpoint` is GA, works with Gateway mode, no side effects for emulator use.
- **Claude Opus 4.6** (95% confidence): Verified via SDK source code that `LimitToEndpoint = true` sets `EnableEndpointDiscovery = false`. Found GitHub issues [#98](https://github.com/Azure/azure-cosmos-db-emulator-docker/issues/98) and [#161](https://github.com/Azure/azure-cosmos-db-emulator-docker/issues/161) in the `azure-cosmos-db-emulator-docker` repo documenting the exact same problem.

We weren't the first to hit this. We just found the cleanest fix.

---

## Phase 6: Victory Lap

Final test results:

| Test Suite | Result | Duration |
|---|---|---|
| CosmosDB Integration Tests (host) | **10/10 ✅** | 15 seconds |
| Container Tests (Docker) | **9/9 ✅** | 23 seconds |

Both suites were stuck indefinitely before the fix. Now they complete in seconds.

> **User:** "Can you confirm that definitely we don't need the line `AZURE_COSMOS_EMULATOR_IP_ADDRESS_OVERRIDE=127.0.0.1` in the docker-compose.yml, not for any scenario?"

Confirmed. With `LimitToEndpoint = true` on the client side, the emulator's metadata addresses are irrelevant. The override variable serves no purpose and was removed permanently.

---

## The Final Tally

### Files Created (10)
- `.dockerignore`, `Dockerfile`, `appsettings.Docker.json`, `.env.template`
- `McpEndToEndTestBase.cs` (shared E2E test base)
- `ContainerHealthCheck.cs`, `DockerizedMcpTests.cs`, `DockerContainerSpecificTests.cs`
- Two `.csproj` files for the new test projects

### Files Modified (9)
- `docker-compose.yml` — added `finwise-mcp` service, **removed** `AZURE_COSMOS_EMULATOR_IP_ADDRESS_OVERRIDE`
- `UserProfileStoreFactory.cs` — added `LimitToEndpoint = true`
- `CosmosDbUserProfileStoreIntegrationTests.cs` — added `LimitToEndpoint = true`
- `Program.cs` — added `/health` endpoint
- `FinWise.McpServer.csproj` — fixed Newtonsoft.Json packaging
- `EndToEndMcpTests.cs` — refactored to inherit shared base
- `FinWise.slnx`, `Directory.Packages.props`, integration test `.csproj`

### Bugs Fixed (3 critical)
1. **CosmosDB emulator IP metadata** — removed `AZURE_COSMOS_EMULATOR_IP_ADDRESS_OVERRIDE` + added `LimitToEndpoint = true`
2. **SSE streaming** — `HttpCompletionOption.ResponseHeadersRead` + line-by-line reading
3. **Five Dockerfile gotchas** — solution restore, PrivateAssets, curl, non-root logging, health endpoint

---

## What We Learned

### About CosmosDB in Docker

The CosmosDB Linux emulator's `AZURE_COSMOS_EMULATOR_IP_ADDRESS_OVERRIDE` is a trap. It seems helpful — "just tell the emulator what IP to use" — but any value you pick works for one scenario and breaks another. The real fix is client-side: `LimitToEndpoint = true` makes the SDK ignore the metadata entirely.

This is documented in GitHub issues but not prominently in Microsoft's official emulator docs. It cost us ~4 hours to discover what should have been a one-line configuration.

### About Debugging Silent Hangs

The worst bugs are the ones that produce no errors. The CosmosDB SDK didn't throw, didn't log, didn't timeout (within any reasonable window). It just... waited. For 4 minutes and 12 seconds per attempt, then the semaphore released and the next request tried the same doomed connection.

The key diagnostic was the **~4:12 interval pattern** in the logs — recognizable as a Linux TCP SYN retry timeout. Without that pattern recognition, we might have spent another day blaming the SSE streaming layer.

### About Plans vs. Reality

The 787-line plan was good. It covered the Dockerfile, docker-compose, test architecture, networking, security, and health checks. But it couldn't predict:
- That `127.0.0.1` in the existing docker-compose would become a critical bug when a new container joined the network
- That the CosmosDB SDK's endpoint discovery would route traffic to unreachable Docker-internal IPs
- That `aspnet:10.0` would be missing `curl`
- That `ReadAsStringAsync()` blocks on SSE streams

The plan got us 80% of the way. The other 20% was debugging. The updated plan now includes a new [Section 13 — Implementation Learnings & Critical Fixes](../specs/009-dockerized-finwise/009-dockerized-finwise-plan.md) so the next team doesn't have to rediscover these the hard way.

---

## What's Next

The Docker stack works end-to-end. All tests pass. But there's still work:

- **Documentation**: README.md needs a Docker usage section, AGENTS.md needs updated build/test commands
- **Git commit**: All changes are uncommitted (17 files changed, +942 lines)
- **CI pipeline**: The container tests should run in GitHub Actions
- **Microsoft.Azure.Cosmos upgrade**: Currently on 3.46.1, recommended minimum is 3.57.0+

The FinWise MCP Server now launches with a single `docker compose up -d --build`. Three containers, one command, zero prayers.

---

*Written: March 29, 2026*
