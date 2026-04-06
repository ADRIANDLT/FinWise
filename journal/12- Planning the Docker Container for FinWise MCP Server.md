# 12 — Planning the Docker Container for FinWise MCP Server

*March 28, 2026*
*With Redis sessions in place and scale-out unlocked, it's time to package the whole stack into a single `docker compose up`.*

---

## Where We Are

Journal 11 closed a chapter: the MCP session mapping moved to Redis, the `ConcurrentDictionary` bottleneck was gone, and FinWise could theoretically run behind a load balancer. But "theoretically" was doing a lot of heavy lifting. The MCP server still ran as a bare `dotnet run` process, Redis and CosmosDB were managed separately via docker-compose, and spinning up the full stack for development meant three terminal windows and a prayer.

The goal for today: produce a complete technical spec — [009 — Dockerize FinWise MCP Server](../specs/009-dockerized-finwise/009-dockerized-finwise-plan.md) — that would package the MCP server into a Docker image and wire it alongside Redis and CosmosDB in the existing `docker-compose.yml`. One command to rule them all.

---

## The Research Deep Dive

Before writing any spec, the first question was straightforward: what .NET 10 Docker images exist, and what surprises will they bring?

The answer: `mcr.microsoft.com/dotnet/aspnet:10.0` for runtime (~220 MB on Debian bookworm-slim), `sdk:10.0` for the build stage. Alpine variants exist at ~110 MB, and the chiseled/distroless variants at ~100 MB — but those have no shell, which makes debugging a nightmare in early development. Standard image first, hardening later.

The interesting finding was the port situation. Since .NET 8, ASP.NET Core container images default to port **8080** via `ASPNETCORE_HTTP_PORTS`. But FinWise's `appsettings.json` explicitly configures Kestrel to bind `localhost:5000` — and explicit Kestrel endpoint configuration takes precedence over the container image's environment variable default.

> **User:** "Are you fully sure about port 8080?"

A fair challenge. The appsettings.json was checked — Kestrel endpoints are explicitly configured to `http://localhost:5000`. That explicit config overrides the image default. Inside the container, the only change needed is `localhost` → `0.0.0.0` so the port is reachable from outside the container. The port stays 5000.

This mattered for docker-compose port mapping: `5000:5000` (host:container), not `5000:8080` as a naive implementation might assume.

---

## The Multi-Stage Build Design

The Dockerfile landed at the repo root — the build context needs access to both `src/FinWise.McpServer/` and `src/FinWise.MultiAgentWorkflow/` (a project dependency).

The two-stage pattern emerged naturally:

1. **Build stage** (`sdk:10.0`): Copy `FinWise.slnx`, `Directory.Build.props`, `Directory.Packages.props`, and `.csproj` files first — then `dotnet restore` as a distinct cached layer. Source code comes after, so NuGet restore only reruns when project files change.
2. **Runtime stage** (`aspnet:10.0`): Copy only the published output. No SDK, no source code, no build artifacts. Run as non-root (`USER $APP_UID`, the built-in `app` user at UID 1654 in aspnet:10.0 images).

The `.dockerignore` was straightforward but important — keeping `bin/`, `obj/`, `.git/`, docs, specs, and journal out of the build context.

---

## The Configuration Layer: appsettings.Docker.json

A new `appsettings.Docker.json` file handles the three things that change between local development and container execution:

| Setting | Local (`appsettings.json`) | Container (`appsettings.Docker.json`) |
|---------|---------------------------|---------------------------------------|
| Kestrel | `localhost:5000` | `0.0.0.0:5000` |
| Redis | `localhost:6379` | `redis:6379` |
| CosmosDB | `https://localhost:8081/` | `https://cosmosdb-emulator:8081/` |

The activation mechanism already exists in `Program.cs`: `AddJsonFile($"appsettings.{env}.json")`. Setting `ASPNETCORE_ENVIRONMENT=Docker` in docker-compose does the rest. No code changes needed.

---

## Docker Compose: Wiring the finwise-mcp Service

The existing `docker-compose.yml` already had Redis 7.4 Alpine and the CosmosDB Linux Emulator with health checks and volumes. The new `finwise-mcp` service plugs in alongside them with `depends_on: condition: service_healthy` — the MCP server won't start until both Redis and CosmosDB report healthy.

Azure OpenAI credentials pass through from a `.env` file (git-ignored, with a `.env.template` committed as documentation). Stock Agent credentials use `${VAR:-}` syntax to make them optional — the server starts fine without them.

The health check was the one area of genuine uncertainty. The `/mcp` endpoint expects POST with JSON-RPC — a GET request returns 405 Method Not Allowed. Docker's `curl -f` treats 4xx as failure. The spec documents this as a known risk with two mitigation paths: adjust the `curl` command to accept 405, or add a lightweight `/health` endpoint. Decision: start simple, refine if needed.

---

## The Test Strategy Question

The existing `FinWise.McpServer.IntegrationTests` project has six E2E tests that hit `http://localhost:5000/mcp`. The Docker container, with port mapping `5000:5000`, exposes the exact same URL. The temptation was obvious: just run the existing tests against the container.

> **User:** "Could the new Docker integration tests somehow use the same tests as the existing integration tests? Both would be targeting localhost:5000, right?"

Exactly right. Both test suites make identical HTTP calls to the same URL. The only differences: who starts the server (developer vs. docker-compose), and what happens when the server isn't running (fail vs. graceful skip).

The design: a **shared base class library** (`FinWise.McpServer.E2ETestBase`) that extracts all MCP client helpers — `InitializeMcpSession`, `CallFinancialAdviceTool`, `CallResetSessionTool`, SSE parsing, JSON-RPC helpers — into `McpEndToEndTestBase`. Both test projects inherit from it. Zero test logic duplication.

The existing `EndToEndMcpTests` becomes a thin class with `[Fact]` methods calling inherited helpers. The new `DockerizedMcpTests` does the same but with `[SkippableFact]` and an `EnsureContainerRunning()` guard that gracefully skips when the container isn't up.

### But What About Docker-Specific Concerns?

> **User:** "Propose approach 1 plus also implementing a few specific tests particular to testing Docker."

The inherited MCP protocol tests prove the app works inside the container. But they don't explicitly validate the *container-specific* concerns that local `dotnet run` can never exercise:

| Docker-Specific Test | What It Proves |
|------|----------------|
| `Container_ShouldBeReachableAndHealthy` | Dockerfile builds correctly, Kestrel binds `0.0.0.0:5000`, port mapping works |
| `Container_RedisConnectivity_ShouldWorkOverDockerNetwork` | `appsettings.Docker.json` Redis override works, Docker DNS resolves `redis:6379` |
| `Container_CosmosDbConnectivity_ShouldWorkOverDockerNetwork` | CosmosDB emulator reachable at `cosmosdb-emulator:8081` across containers |
| `Container_AzureOpenAIEnvVars_ShouldBeInjected` | `.env` → docker-compose → container env var chain works end-to-end |
| `Container_StartupTime_ShouldBeReasonable` | No accidental SDK in runtime image, no NuGet restore at boot |

These five tests live in a separate `DockerContainerSpecificTests` class. Together with the four reused MCP protocol tests, the container test project has nine tests total — all skippable, all black-box HTTP clients with no project reference to the MCP server itself.

---

## Published Ports: External Access for Dev Tooling

The existing docker-compose already publishes Redis on `6379:6379` and CosmosDB on `8081:8081`. These port mappings, combined with the new `5000:5000` for the MCP server, mean all three services are accessible from the host machine — and from external tools like Redis Commander, Azure CosmosDB Data Explorer, or any MCP client on the network.

The connection table tells the complete networking story:

| From → To | Hostname | Port | Protocol |
|-----------|----------|------|----------|
| Host → finwise-mcp | `localhost` | `5000:5000` | HTTP |
| Host → redis | `localhost` | `6379:6379` | Redis |
| Host → cosmosdb-emulator | `localhost` | `8081:8081` | HTTPS |
| finwise-mcp → redis | `redis` | `6379` | Redis (Docker DNS) |
| finwise-mcp → cosmosdb-emulator | `cosmosdb-emulator` | `8081` | HTTPS (Docker DNS) |
| finwise-mcp → Azure OpenAI | External URL | `443` | HTTPS |

---

## The Implementation Plan

The spec breaks implementation into four phases:

1. **Dockerfile & Image Build** — Create `.dockerignore`, `Dockerfile`, `appsettings.Docker.json`. Verify the image builds and runs standalone.
2. **Docker Compose Integration** — Create `.env.template`, add `.env` to `.gitignore`, add the `finwise-mcp` service. Verify full stack startup.
3. **Shared Test Base + Container Tests** — Extract `McpEndToEndTestBase`, refactor existing tests, create `ContainerTests` project with both reused and Docker-specific tests. Eleven steps total.
4. **Documentation** — Update `README.md` and `AGENTS.md` with Docker usage.

Twenty steps total. Each with explicit acceptance criteria.

---

## What We Learned

### About Container Configuration

The port 8080 vs 5000 question was the most instructive moment. The instinct to use the "container default" is strong — ASP.NET Core images default to 8080, and most examples show that. But FinWise has explicit Kestrel configuration, and explicit always wins. The lesson: read the actual `appsettings.json` before assuming defaults apply.

### About Test Architecture

The shared base class approach is clean but introduces a real dependency: both test projects inherit from the same base, so changes to helper methods ripple into both. The tradeoff is worth it — duplicating test logic across two projects would be worse — but it means the `E2ETestBase` library needs to be treated as a contract.

### About Docker Compose as a Dev Environment

With Redis, CosmosDB, and now the MCP server all in docker-compose, the developer experience goes from "start three things manually" to `docker compose up -d --build`. That's a meaningful improvement. But it comes with a cost: Azure OpenAI credentials must be in a `.env` file, which means one more setup step for new developers. The `.env.template` mitigates this, but it's still friction.

---

## What's Next

The spec sits at Draft status. Implementation begins with Phase 1 — the Dockerfile and image build. The multi-stage build should be straightforward given how clean the project structure is (two source projects, centralized package management, no exotic build steps).

The real test comes in Phase 2 when docker-compose wires everything together and we find out if the MCP health check works with a 405 response — or if we need that `/health` endpoint after all.

---

*Written: March 28, 2026*
