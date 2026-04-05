# 009 — Dockerize FinWise MCP Server

**Status**: Implemented  
**Date**: 2026-03-28  
**Depends on**: [008 — Redis MCP Session](../008-redis-mcp-session/008.A-redis-mcp-session-store-plan.md) (completed)  
**Version target**: v0.5.0  

---

## 1. Summary

Package the FinWise MCP Server as a Docker image and add it as a service in the existing `docker-compose.yml`, so the entire stack (MCP server + Redis + CosmosDB emulator) can be launched with a single `docker compose up`. Additionally, create a new integration test project that runs E2E tests against the containerized server.

---

## 2. Goals & Non-Goals

### Goals

1. **Dockerfile** — Multi-stage build producing a minimal `mcr.microsoft.com/dotnet/aspnet:10.0`-based image.
2. **docker-compose.yml integration** — Add `finwise-mcp` service that depends on Redis and CosmosDB emulator, wired with proper networking.
3. **Container-targeted integration tests** — New xUnit test project (`FinWise.McpServer.ContainerTests`) that exercises the MCP protocol against the Dockerized server.
4. **Developer experience** — One-command startup, clear documentation, no manual steps beyond setting Azure OpenAI env vars.
5. **CI readiness** — The image build and container tests should be runnable in a CI pipeline (no host-dependent paths).

### Non-Goals

- Pushing the Docker image to a container registry (ACR, Docker Hub).
- Kubernetes / Helm manifests.
- HTTPS/TLS termination inside the container (reverse proxy concern).
- Multi-architecture builds (ARM64) — build for the host platform only.
- Windows container support.

---

## 3. Research Summary

### 3.1 .NET 10 Docker Images

**Runtime**: .NET 10 (LTS), C# latest  
**SDK image**: `mcr.microsoft.com/dotnet/sdk:10.0` (build stage)  
**Runtime image**: `mcr.microsoft.com/dotnet/aspnet:10.0` (final stage)  
**Default port**: Since .NET 8, ASP.NET Core container images default to port **8080** (via `ASPNETCORE_HTTP_PORTS`). However, FinWise **explicitly configures Kestrel** to bind `localhost:5000` via `appsettings.json` — explicit Kestrel endpoint configuration takes precedence over the image default. Inside the container, the only required change is `localhost` → `0.0.0.0` (so the port is reachable from outside the container). The port stays **5000**.

**Image variants available**:
- Standard: `aspnet:10.0` (Debian bookworm-slim, ~220 MB)
- Alpine: `aspnet:10.0-alpine` (~110 MB, smaller but may have musl-related issues)
- Distroless/Chiseled: `aspnet:10.0-noble-chiseled` (~100 MB, no shell, non-root by default — best for production)

**Recommendation**: Use standard `aspnet:10.0` for initial implementation (debugging ease), note chiseled as a future hardening step.

**Confidence**: High  
**Sources**: [Docker Hub dotnet/aspnet](https://hub.docker.com/_/microsoft-dotnet-aspnet), [MCR tags](https://mcr.microsoft.com/en-us/artifact/mar/dotnet/aspnet/tags), [MS Learn — Containerize .NET](https://learn.microsoft.com/en-us/dotnet/core/docker/build-container), [MS Learn — ASP.NET Core Docker](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/docker/building-net-docker-images)

### 3.2 Multi-Stage Build Pattern

Microsoft recommends a two-stage Dockerfile:

1. **Build stage** (`sdk:10.0`): Restore NuGet packages, build, publish.
2. **Runtime stage** (`aspnet:10.0`): Copy published output, set entrypoint.

Key optimizations:
- Copy `.sln`/`.csproj` files first, run `dotnet restore` as a distinct layer (cached when project files don't change).
- Publish with `-c Release` and `--no-restore` in the second step.
- Use `.dockerignore` to exclude `bin/`, `obj/`, `.git/`, etc.

### 3.3 FinWise-Specific Container Concerns

| Concern | Current State | Container Behavior |
|---------|--------------|-------------------|
| **Kestrel binding** | `localhost:5000` (appsettings) | Must bind `0.0.0.0:5000` — only the host changes (`localhost` → `0.0.0.0`), port stays 5000 since explicit Kestrel config overrides the container's default 8080 |
| **Azure OpenAI credentials** | Env vars (`AZURE_OPENAI_*`) | Passed via `docker-compose.yml` `environment:` block (from `.env` file) |
| **Stock Agent credentials** | Env vars (`STOCK_AGENT_*`, `FINWISE_AZURE_*`) | Same — optional, server starts without them |
| **Redis connection** | `localhost:6379` (appsettings) | Override to `redis:6379` (Docker network hostname) via env var or appsettings override |
| **CosmosDB connection** | `https://localhost:8081/` (appsettings) | Override to `https://cosmosdb-emulator:8081/` — already uses `AllowInsecureTls=true` |
| **Console.SetOut → stderr** | Redirects stdout for MCP stdio transport | HTTP transport only in container — safe to keep, but stdout logs via Serilog work fine |
| **Non-root user** | Not configured | Run as non-root (`app` user, UID 1654 — default in aspnet:10.0 images) |

### 3.4 Docker Compose Networking

All services in the same `docker-compose.yml` share a default bridge network. Service names (`redis`, `cosmosdb-emulator`, `finwise-mcp`) resolve as hostnames within the network. The MCP server container references Redis as `redis:6379` and CosmosDB as `https://cosmosdb-emulator:8081/`.

### 3.5 Integration Tests Against Container

The existing `FinWise.McpServer.IntegrationTests` project connects to `http://localhost:5000/mcp` and tests the full MCP protocol flow. For container tests:

- The container maps port `5000:5000` (host:container \u2014 same port, since Kestrel explicitly binds 5000).
- Tests connect to `http://localhost:5000/mcp` \u2014 identical URL whether targeting a local process or the container.
- A separate test project (`FinWise.McpServer.ContainerTests`) ensures container tests don't mix with existing tests that assume a locally-running process.
- Tests must wait for the container to be healthy before executing.

---

## 4. Design

### 4.1 File Layout

```
├── src/FinWise.McpServer/
│   └── (existing — no changes except appsettings.Docker.json)
├── Dockerfile                              # Multi-stage build (repo root)
├── .dockerignore                           # Exclude bin/, obj/, .git/, etc.
├── docker-compose.yml                      # Updated — adds finwise-mcp service
├── .env.template                           # Template for required env vars
└── tests/
    ├── FinWise.McpServer.IntegrationTests/ # Existing — refactored to use shared base
    │   ├── EndToEndMcpTests.cs             # Unchanged test methods, inherits base
    │   └── ...
    ├── FinWise.McpServer.E2ETestBase/      # NEW shared class library (not a test project)
    │   ├── FinWise.McpServer.E2ETestBase.csproj
    │   └── McpEndToEndTestBase.cs          # Extracted MCP helpers + test methods
    └── FinWise.McpServer.ContainerTests/   # NEW test project for Docker
        ├── FinWise.McpServer.ContainerTests.csproj
        ├── ContainerHealthCheck.cs         # Wait-for-healthy utility
        └── DockerizedMcpTests.cs           # Inherits base, adds skip + health wait
```

### 4.2 Dockerfile

Placed at **repo root** (build context needs access to both `src/FinWise.McpServer/` and `src/FinWise.MultiAgentWorkflow/`).

```dockerfile
# ============================================================
# Stage 1: Build
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Copy project files first (layer caching for restore)
COPY Directory.Build.props .
COPY Directory.Packages.props .
COPY src/FinWise.McpServer/FinWise.McpServer.csproj src/FinWise.McpServer/
COPY src/FinWise.MultiAgentWorkflow/FinWise.MultiAgentWorkflow.csproj src/FinWise.MultiAgentWorkflow/

# Restore NuGet packages as distinct layer
# NOTE: Cannot use FinWise.slnx here — it references test projects not in the Docker context
RUN dotnet restore src/FinWise.McpServer/FinWise.McpServer.csproj

# Copy remaining source code
COPY src/ src/

# Publish the MCP server
WORKDIR /source/src/FinWise.McpServer
RUN dotnet publish -c Release -o /app --no-restore

# ============================================================
# Stage 2: Runtime
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for Docker health checks (must be done BEFORE switching to non-root user)
RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*

# Non-root user (default 'app' user in aspnet:10.0, UID 1654)
USER $APP_UID

COPY --from=build /app .

EXPOSE 5000

ENTRYPOINT ["dotnet", "FinWise.McpServer.dll"]
```

**Notes**:
- ⚠️ **Cannot use `FinWise.slnx` for restore** — the solution file references test projects not included in the Docker build context, causing `dotnet restore` to fail. Restore individual `.csproj` files instead.
- `FinWise.slnx` is NOT copied into the Docker context (not needed, saves layer space).
- `Directory.Build.props` and `Directory.Packages.props` must be copied for centralized package management.
- `curl` must be installed **before** `USER $APP_UID` — the non-root user can't install packages.
- `USER $APP_UID` uses the built-in non-root user in the aspnet image.

### 4.3 .dockerignore

```
**/.git
**/bin
**/obj
**/node_modules
**/.vs
**/.vscode
**/TestResults
**/*.user
**/*.suo
**/packages
.memory-bank/
journal/
specs/
docs/
samples/
to-do/
my-specs/
*.md
!README.md
docker-compose.yml
.env
```

### 4.4 appsettings.Docker.json

New file in `src/FinWise.McpServer/` for container-specific configuration overrides:

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:5000"
      }
    }
  },
  "CosmosDb": {
    "Endpoint": "https://cosmosdb-emulator:8081/"
  },
  "Redis": {
    "ConnectionString": "redis:6379"
  }
}
```

The only Kestrel change vs `appsettings.json` is `localhost` → `0.0.0.0` — the port stays 5000. The ASP.NET Core container image defaults to 8080, but explicit Kestrel endpoint configuration takes precedence.

The `ASPNETCORE_ENVIRONMENT=Docker` env var in docker-compose activates this file via the existing `AddJsonFile($"appsettings.{env}.json")` pattern in `Program.cs`.

### 4.5 docker-compose.yml Changes

Add the `finwise-mcp` service after the existing `redis` service:

```yaml
  finwise-mcp:
    build:
      context: .
      dockerfile: Dockerfile
    container_name: finwise-mcp
    ports:
      - "5000:5000"
    environment:
      - ASPNETCORE_ENVIRONMENT=Docker
      # Azure OpenAI (required — from .env file)
      - AZURE_OPENAI_ENDPOINT=${AZURE_OPENAI_ENDPOINT}
      - AZURE_OPENAI_DEPLOYMENT_NAME=${AZURE_OPENAI_DEPLOYMENT_NAME}
      - AZURE_OPENAI_API_KEY=${AZURE_OPENAI_API_KEY}
      # Stock Agent (optional — from .env file)
      - STOCK_AGENT_PROJECT_ENDPOINT=${STOCK_AGENT_PROJECT_ENDPOINT:-}
      - STOCK_AGENT_NAME=${STOCK_AGENT_NAME:-}
      - FINWISE_AZURE_TENANT_ID=${FINWISE_AZURE_TENANT_ID:-}
      - FINWISE_AZURE_CLIENT_ID=${FINWISE_AZURE_CLIENT_ID:-}
      - FINWISE_AZURE_CLIENT_SECRET=${FINWISE_AZURE_CLIENT_SECRET:-}
    depends_on:
      redis:
        condition: service_healthy
      cosmosdb-emulator:
        condition: service_healthy
    healthcheck:
      test: ["CMD-SHELL", "curl -sf -o /dev/null http://localhost:5000/health || exit 1"]
      interval: 10s
      timeout: 5s
      retries: 10
      start_period: 15s
    mem_limit: 512m
```

**Key decisions**:
- `depends_on` with `condition: service_healthy` ensures Redis and CosmosDB are ready before the MCP server starts.
- Port mapping `5000:5000` — same port inside and outside the container, consistent with FinWise's existing Kestrel config.
- Azure OpenAI vars use `${VAR}` syntax to read from a `.env` file (secrets never committed).
- Stock Agent vars use `${VAR:-}` (default empty) making them optional.
- Health check uses `curl` against a dedicated `/health` endpoint (added to `Program.cs`: `app.MapGet("/health", () => "healthy")`). The `aspnet:10.0` base image does **not** include `curl` — it must be installed in the Dockerfile (see Section 4.2). The `/mcp` endpoint returns 405 for GET requests, making it unsuitable for health checks.

**Published ports (all three services accessible from the host)**:

| Service | Port mapping | Purpose |
|---------|-------------|----------|
| `finwise-mcp` | `5000:5000` | MCP clients (VS Code, Claude Desktop) and container tests |
| `redis` | `6379:6379` | Direct access from host for debugging with Redis CLI / UI tools (e.g., RedisInsight) |
| `cosmosdb-emulator` | `8081:8081` | Direct access from host for CosmosDB Data Explorer (`https://localhost:8081/_explorer/`) and UI tools |

Redis and CosmosDB port mappings **already exist** in the current `docker-compose.yml`. They remain unchanged — the `finwise-mcp` service simply joins the same network and reaches them via Docker DNS hostnames (`redis:6379`, `cosmosdb-emulator:8081`), while developers continue accessing them at `localhost` from the host machine.

### 4.6 .env.template

```env
# FinWise Docker Environment Variables
# Copy this file to .env and fill in your values.
# NEVER commit the .env file.

# Azure OpenAI (required)
AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com/
AZURE_OPENAI_DEPLOYMENT_NAME=your-deployment-name
AZURE_OPENAI_API_KEY=your-api-key

# Stock Agent (optional — leave empty to disable)
STOCK_AGENT_PROJECT_ENDPOINT=
STOCK_AGENT_NAME=
FINWISE_AZURE_TENANT_ID=
FINWISE_AZURE_CLIENT_ID=
FINWISE_AZURE_CLIENT_SECRET=
```

Add `.env` to `.gitignore` to prevent accidental secret commits.

### 4.7 Container Integration Tests — Shared Base Approach

#### 4.7.0 Design Decision: Shared Tests, Not Duplicated Tests

The existing `EndToEndMcpTests` and the new Docker container tests are **identical HTTP calls** to the same `localhost:5000/mcp` URL (Docker maps `5000:8080` internally). The only differences are:

| Aspect | Existing `IntegrationTests` | Docker `ContainerTests` |
|--------|---------------------------|------------------------|
| Server started by | Developer (`dotnet run`) | `docker compose up` |
| URL from host | `localhost:5000` | `localhost:5000` (same!) |
| Skip behavior | Tests fail if server not running | Gracefully skip via `SkippableFact` |
| Extra validation | — | Dockerfile correctness, networking, env var injection |

**Approach**: Extract MCP client helpers and test methods into a **shared base class library** (`FinWise.McpServer.E2ETestBase`). Both test projects inherit from it. Zero test logic duplication.

#### 4.7.1 Shared Library: `FinWise.McpServer.E2ETestBase`

A **plain class library** (not a test project) containing the extracted MCP test infrastructure.

**Project setup**:
- Target `net10.0`, `IsPackable=false`, `TreatWarningsAsErrors=true`
- Package references: `FluentAssertions`, `xunit` (for `ITestOutputHelper`)
- No reference to `McpServer` project — pure HTTP client code

**Base class `McpEndToEndTestBase` guidelines**:
- Abstract class implementing `IDisposable` with `HttpClient`, `ITestOutputHelper`, and `SessionId` state
- `McpBaseUrl` configurable via `FINWISE_MCP_URL` env var, defaults to `http://localhost:5000`
- Extract all existing protocol helpers from `EndToEndMcpTests`: `InitializeMcpSession`, `InitializeNewMcpSession`, `CallFinancialAdviceTool`, `CallResetSessionTool`, `SetupTestProfile`, `SetupTestProfileWithEmail`
- Also extract: SSE response parsing, JSON-RPC request builders, `XunitLoggerProvider`
- Each test project only contributes `[Fact]` / `[SkippableFact]` methods — zero test logic duplication

#### 4.7.2 Refactored `EndToEndMcpTests` (existing project)

- Add project reference to `E2ETestBase`
- Change `EndToEndMcpTests` to inherit `McpEndToEndTestBase`
- All existing `[Fact]` methods stay in the class, unchanged — calls move to inherited helpers
- **Purely structural refactoring** — no test logic or behavior changes

#### 4.7.3 New `FinWise.McpServer.ContainerTests` Project

**Project setup**:
- Standard xUnit test project (`IsTestProject=true`)
- Package references: `FluentAssertions`, `Microsoft.NET.Test.Sdk`, `xunit`, `xunit.runner.visualstudio`, `coverlet.collector`, **`Xunit.SkippableFact`**
- Single project reference: `E2ETestBase` — **no reference to McpServer** (pure black-box HTTP client)

#### 4.7.4 Container Health Check Utility

A `ContainerHealthCheck` static helper in the ContainerTests project:

- `IsServerReachableAsync(timeout?)` — polls `McpBaseUrl` with HTTP GET until success or deadline (default 5s), catches `HttpRequestException`, returns `bool`
- `WaitForServerAsync(timeout?)` — wraps the above, throws `TimeoutException` if unreachable (default 60s)
- Used by all container tests via an `EnsureContainerRunning()` guard that calls `Skip.IfNot(await IsServerReachableAsync(), "...")` for graceful skip

#### 4.7.5 `DockerizedMcpTests` — Two Categories of Tests

The Docker test project contains **two test classes**:

**Shared patterns for both classes**:
- Inherit `McpEndToEndTestBase`
- Use `[SkippableFact]` (not `[Fact]`) so tests skip gracefully when container isn't running
- Every test starts with `EnsureContainerRunning()` guard
- Test names prefixed with `Container_` for test-report clarity

##### Category 1: Reused MCP Protocol Tests — `DockerizedMcpTests`

Re-exercise existing E2E scenarios against the containerized server. Call the same inherited helpers (`InitializeMcpSession`, `CallFinancialAdviceTool`, `CallResetSessionTool`) — proving the app works identically inside the container.

Tests: `Container_McpInitialize_ShouldReturnSessionId`, `Container_ToolDiscovery_ShouldExposeTools`, `Container_FinancialAdvice_ShouldAskForEmail`, `Container_ResetConversation_ShouldClear`

##### Category 2: Docker-Specific Tests — `DockerContainerSpecificTests`

Tests that validate concerns **only exercisable** when running against a container. Each test implicitly validates a Docker infrastructure concern through application-level behavior:

| Test | Validation strategy | What it proves |
|------|-------------------|----------------|
| `Container_ShouldBeReachableAndHealthy` | `IsServerReachableAsync(30s)` returns true | Dockerfile builds, Kestrel binds `0.0.0.0:5000`, port mapping works |
| `Container_RedisConnectivity_ShouldWorkOverDockerNetwork` | `CallResetSessionTool()` succeeds (reset writes/clears Redis) | `appsettings.Docker.json` Redis override works, Docker DNS resolves `redis:6379` |
| `Container_AzureOpenAIEnvVars_ShouldBeInjected` | `CallFinancialAdviceTool("Hello")` returns non-empty response | `.env` → docker-compose → container env var injection chain works |
| `Container_CosmosDbConnectivity_ShouldWorkOverDockerNetwork` | Complete profile setup with unique email, verify retrieval in new session | `appsettings.Docker.json` CosmosDB override works, cross-container TLS |
| `Container_StartupTime_ShouldBeReasonable` | `InitializeMcpSession()` completes within 10s | No SDK in runtime image, published assemblies complete |

##### Docker-Specific Test Summary

| Test | What it validates | Why local E2E can't cover it |
|------|-------------------|------------------------------|
| `Container_ShouldBeReachableAndHealthy` | Dockerfile builds, Kestrel binds `0.0.0.0:5000`, port mapping works | Local server binds `localhost:5000` directly |
| `Container_RedisConnectivity_ShouldWorkOverDockerNetwork` | `appsettings.Docker.json` Redis override (`redis:6379`), Docker DNS resolution | Local uses `localhost:6379` — different path |
| `Container_AzureOpenAIEnvVars_ShouldBeInjected` | `.env` → `docker-compose.yml` → container env var injection chain | Local inherits host env vars directly |
| `Container_CosmosDbConnectivity_ShouldWorkOverDockerNetwork` | `appsettings.Docker.json` CosmosDB override (`cosmosdb-emulator:8081`), cross-container TLS | Local uses `localhost:8081` — different path |
| `Container_StartupTime_ShouldBeReasonable` | No accidental SDK in runtime image, published assemblies complete | Local `dotnet run` always recompiles, different profile |

---

## 5. Implementation Plan

> ✅ **Implementation Complete** — All phases implemented. See Section 13 for critical fixes discovered during implementation.

### Phase 1: Dockerfile & Image Build (core infra)

| Step | Task | Files | Acceptance Criteria |
|------|------|-------|-------------------|
| 1.1 | Create `.dockerignore` at repo root | `.dockerignore` | Excludes `bin/`, `obj/`, `.git/`, docs, specs |
| 1.2 | Create `Dockerfile` at repo root | `Dockerfile` | Multi-stage build compiles and produces image |
| 1.3 | Create `appsettings.Docker.json` | `src/FinWise.McpServer/appsettings.Docker.json` | Kestrel binds `0.0.0.0:5000`, Redis/CosmosDB point to container hostnames |
| 1.4 | Verify image builds | — | `docker build -t finwise-mcp .` succeeds |
| 1.5 | Verify image runs standalone | — | `docker run --rm -e AZURE_OPENAI_ENDPOINT=... finwise-mcp` starts and logs "FinWise MCP Server ready" |

### Phase 2: Docker Compose Integration

| Step | Task | Files | Acceptance Criteria |
|------|------|-------|-------------------|
| 2.1 | Create `.env.template` | `.env.template` | Documents all required/optional env vars |
| 2.2 | Add `.env` to `.gitignore` | `.gitignore` | `.env` not tracked |
| 2.3 | Add `finwise-mcp` service to `docker-compose.yml` | `docker-compose.yml` | Service definition with depends_on, healthcheck, port mapping |
| 2.4 | Verify full stack startup | — | `docker compose up -d` starts all 3 services, `docker compose ps` shows all healthy |
| 2.5 | Verify MCP handshake through container | — | `curl -X POST http://localhost:5000/mcp` with initialize payload returns session ID |

### Phase 3: Shared Test Base + Container Integration Tests

| Step | Task | Files | Acceptance Criteria |
|------|------|-------|-------------------|
| 3.1 | Create `FinWise.McpServer.E2ETestBase` shared library | `tests/FinWise.McpServer.E2ETestBase/` | Class library (not test project), contains `McpEndToEndTestBase` |
| 3.2 | Extract MCP helpers from `EndToEndMcpTests` into `McpEndToEndTestBase` | `McpEndToEndTestBase.cs` | All protocol helpers, SSE parsing, `XunitLoggerProvider` moved to base class; `McpBaseUrl` configurable via env var |
| 3.3 | Refactor `EndToEndMcpTests` to inherit `McpEndToEndTestBase` | `EndToEndMcpTests.cs` | Existing tests pass unchanged — pure structural refactoring |
| 3.4 | Add `FinWise.McpServer.IntegrationTests` → `E2ETestBase` project reference | `.csproj` | Existing project references the shared library |
| 3.5 | Create `FinWise.McpServer.ContainerTests` project | `tests/FinWise.McpServer.ContainerTests/` | Builds, references `E2ETestBase` + `Xunit.SkippableFact`, no McpServer project reference |
| 3.6 | Add both new projects to `FinWise.slnx` | `FinWise.slnx` | `dotnet build FinWise.slnx` includes all projects |
| 3.7 | Implement `ContainerHealthCheck` utility | `ContainerHealthCheck.cs` | Polls until server responds or times out |
| 3.8 | Implement `DockerizedMcpTests` inheriting `McpEndToEndTestBase` | `DockerizedMcpTests.cs` | 4 reused MCP protocol tests pass against running container |
| 3.9 | Implement `DockerContainerSpecificTests` with Docker-only validations | `DockerContainerSpecificTests.cs` | 5 Docker-specific tests: health, Redis connectivity, CosmosDB connectivity, env var injection, startup time |
| 3.10 | Verify tests skip gracefully when container is not running | — | `dotnet test` on the project shows skipped (not failed) tests |
| 3.11 | Verify existing `EndToEndMcpTests` still pass after refactoring | — | `dotnet test tests/FinWise.McpServer.IntegrationTests/` — all green |

### Phase 4: Documentation & Cleanup

| Step | Task | Files | Acceptance Criteria |
|------|------|-------|-------------------|
| 4.1 | Update `README.md` with Docker usage section | `README.md` | Instructions for `docker compose up`, `.env` setup |
| 4.2 | Update `AGENTS.md` Build & Test section | `AGENTS.md` | Includes Docker build and container test commands |

---

## 6. Environment Variable Summary

| Variable | Required | Where Set | Purpose |
|----------|----------|-----------|---------|
| `ASPNETCORE_ENVIRONMENT` | Yes (in compose) | `docker-compose.yml` | Set to `Docker` to load `appsettings.Docker.json` |
| `AZURE_OPENAI_ENDPOINT` | Yes | `.env` | Azure OpenAI service URL |
| `AZURE_OPENAI_DEPLOYMENT_NAME` | Yes | `.env` | Model deployment name |
| `AZURE_OPENAI_API_KEY` | Yes | `.env` | Azure OpenAI API key |
| `STOCK_AGENT_PROJECT_ENDPOINT` | No | `.env` | Azure AI Foundry endpoint (stock agent) |
| `STOCK_AGENT_NAME` | No | `.env` | Stock agent name |
| `FINWISE_AZURE_TENANT_ID` | No | `.env` | Azure AD tenant (stock agent auth) |
| `FINWISE_AZURE_CLIENT_ID` | No | `.env` | Azure AD client (stock agent auth) |
| `FINWISE_AZURE_CLIENT_SECRET` | No | `.env` | Azure AD secret (stock agent auth) |
| `FINWISE_MCP_URL` | No | Host env (tests) | Override test target URL (default: `http://localhost:5000`) |

---

## 7. Container Networking

### 7.1 Runtime Architecture

```
  ┌───────────────────────────────────────────────────┐
  │                    HOST MACHINE                    │
  │                                                   │
  │  ┌─────────────────┐  ┌─────────────────────────┐  │
  │  │  MCP Clients    │  │  Container Tests (xUnit) │  │
  │  │  ─────────────  │  │  ───────────────────── │  │
  │  │  ○ VS Code      │  │  dotnet test              │  │
  │  │  ○ Claude      │  │  FinWise.McpServer        │  │
  │  │    Desktop    │  │    .ContainerTests       │  │
  │  └────────┬────────┘  └────────────┬────────────┘  │
  │           │                   │                    │
  │           └─────────┬───────┘                    │
  │                   │ HTTP POST                     │
  │                   │ localhost:5000/mcp             │
  │                   ▼                                 │
  │  ╔═════════════════════════════════════════════╗  │
  │  ║         DOCKER NETWORK (bridge)              ║  │
  │  ║                                             ║  │
  │  ║  ┌───────────────────────────────────────┐  ║  │
  │  ║  │          finwise-mcp                     │  ║  │
  │  ║  │  ┌─────────────────────────────────┐  │  ║  │
  │  ║  │  │ ASP.NET Core + MCP Server         │  │  ║  │
  │  ║  │  │ Kestrel → 0.0.0.0:5000            │  │  ║  │
  │  ║  │  │ ASPNETCORE_ENVIRONMENT=Docker     │  │  ║  │
  │  ║  │  │ USER: app (non-root, UID 1654)   │  │  ║  │
  │  ║  │  └─────┬──────────────────────┬─────┘  │  ║  │
  │  ║  │        │                      │        │  ║  │
  │  ║  └────────┼──────────────────────┼────────┘  ║  │
  │  ║           │ Docker DNS              │           ║  │
  │  ║           ▼                          ▼           ║  │
  │  ║  ┌───────────────┐  ┌────────────────────┐  ║  │
  │  ║  │     redis     │  │ cosmosdb-emulator  │  ║  │
  │  ║  │  ───────────  │  │ ──────────────── │  ║  │
  │  ║  │  Redis 7.4    │  │ Azure CosmosDB     │  ║  │
  │  ║  │  Alpine      │  │  Emulator (Linux)  │  ║  │
  │  ║  │  :6379       │  │  :8081 (HTTPS)     │  ║  │
  │  ║  │             │  │  AllowInsecureTls  │  ║  │
  │  ║  │  ┌─────────┐ │  │  ┌──────────────┐ │  ║  │
  │  ║  │  │ VOL:    │ │  │  │ VOL:         │ │  ║  │
  │  ║  │  │ redis-  │ │  │  │ cosmosdb-    │ │  ║  │
  │  ║  │  │ data    │ │  │  │ data         │ │  ║  │
  │  ║  │  └─────────┘ │  │  └──────────────┘ │  ║  │
  │  ║  └───────────────┘  └────────────────────┘  ║  │
  │  ║         ▲                     ▲              ║  │
  │  ╚═════════╪═════════════════════╪══════════════╝  │
  │            │                     │                 │
  │  ┌─────────┴─────────┐  ┌──────┴───────────────┐  │
  │  │  Dev UI Tools     │  │  Dev UI Tools         │  │
  │  │  ─────────────── │  │  ─────────────────── │  │
  │  │  ○ RedisInsight   │  │  ○ Data Explorer      │  │
  │  │  ○ redis-cli      │  │    localhost:8081     │  │
  │  │  localhost:6379   │  │  ○ Azure Data Studio  │  │
  │  └───────────────────┘  └──────────────────────┘  │
  │                                                   │
  └───────────────────────────────────────────────────┘
```

### 7.2 Connection Details

| From → To | Hostname | Port | Protocol | Config Source |
|-----------|----------|------|----------|---------------|
| Host → finwise-mcp | `localhost` | `5000:5000` | HTTP | `docker-compose.yml` port mapping |
| Host → redis | `localhost` | `6379:6379` | Redis | `docker-compose.yml` port mapping (dev tools) |
| Host → cosmosdb-emulator | `localhost` | `8081:8081` | HTTPS | `docker-compose.yml` port mapping (Data Explorer / dev tools) |
| finwise-mcp → redis | `redis` | `6379` | Redis | `appsettings.Docker.json` |
| finwise-mcp → cosmosdb-emulator | `cosmosdb-emulator` | `8081` | HTTPS (insecure TLS) | `appsettings.Docker.json` |
| finwise-mcp → Azure OpenAI | External URL | `443` | HTTPS | `.env` → `AZURE_OPENAI_ENDPOINT` |

### 7.3 Docker Image Build Pipeline

```
   ┌───────────────────────────────────────────────┐
   │  Stage 1: BUILD  (sdk:10.0 ~ 900 MB)           │
   │                                                 │
   │  COPY ┌───────────────────────────────────┐  │
   │       │ FinWise.slnx                        │  │
   │       │ Directory.Build.props               │  │
   │       │ Directory.Packages.props             │  │
   │       │ FinWise.McpServer.csproj             │  │
   │       │ FinWise.MultiAgentWorkflow.csproj    │  │
   │       └───────────────────────────────────┘  │
   │             │                                    │
   │             ▼                                    │
   │       dotnet restore  ─── (cached layer)        │
   │             │                                    │
   │             ▼                                    │
   │       COPY src/  ───── (source code)            │
   │             │                                    │
   │             ▼                                    │
   │       dotnet publish -c Release -o /app         │
   │             │                                    │
   └─────────────┼─────────────────────────────────┘
              │  COPY --from=build /app
              ▼
   ┌───────────────────────────────────────────────┐
   │  Stage 2: RUNTIME  (aspnet:10.0 ~ 220 MB)      │
   │                                                 │
   │  /app/                                          │
   │    ├── FinWise.McpServer.dll                   │
   │    ├── FinWise.MultiAgentWorkflow.dll          │
   │    ├── appsettings.json                       │
   │    ├── appsettings.Docker.json                │
   │    └── (NuGet dependencies)                   │
   │                                                 │
   │  USER: app (non-root)                           │
   │  EXPOSE: 5000                                   │
   │  ENTRYPOINT: dotnet FinWise.McpServer.dll       │
   │                                                 │
   │  ✘ No SDK    ✘ No source    ✘ No build artifacts │
   └───────────────────────────────────────────────┘
```

### 7.4 Environment & Configuration Flow

```
  .env (secrets, git-ignored)        appsettings.json (base config)
  ─────────────────────────        ─────────────────────────
  AZURE_OPENAI_ENDPOINT=...         Kestrel: localhost:5000
  AZURE_OPENAI_DEPLOYMENT_NAME=...  Redis:   localhost:6379
  AZURE_OPENAI_API_KEY=...          CosmosDb: localhost:8081
           │                                   │
           ▼                                   ▼
  docker-compose.yml                appsettings.Docker.json
  ────────────────────                ────────────────────────
  environment:                      Kestrel: 0.0.0.0:5000  ← only
    - AZURE_OPENAI_*=${...}         Redis:   redis:6379       host
    - ASPNETCORE_ENVIRONMENT=       CosmosDb: cosmosdb-       changes
        Docker                        emulator:8081
           │                                   │
           └───────────┬───────────────┘
                       ▼
              ┌─────────────────────┐
              │  finwise-mcp         │
              │  container            │
              │                       │
              │  ENV vars → Azure AI  │
              │  appsettings.Docker   │
              │    → Redis, CosmosDB  │
              └─────────────────────┘
```

---

## 8. Health Check Strategy

| Level | Method | Purpose |
|-------|--------|---------|
| **Docker health check** | `curl -sf -o /dev/null http://localhost:5000/health` in container | Compose reports service health, `depends_on` gates |
| **Test health check** | `ContainerHealthCheck.WaitForServerAsync()` | Tests wait for server availability before executing |
| **Startup probe** | `start_period: 15s` in compose | Grace period for .NET app startup + NuGet warmup |

**✅ Implemented: Dedicated `/health` endpoint.** A lightweight endpoint was added to `Program.cs`:

```csharp
app.MapGet("/health", () => "healthy");
```

This returns HTTP 200 with the body `"healthy"`. The original plan considered using the `/mcp` endpoint, but it returns 405 (Method Not Allowed) for GET requests, which causes `curl -f` to report failure. The dedicated `/health` endpoint is the clean solution — no workarounds needed.

**Note**: `curl` is not included in the `aspnet:10.0` base image and must be installed in the Dockerfile (see Section 4.2).

---

## 9. Testing Strategy

### 9.1 Test Architecture

```
  ┌──────────────────────────────────────────────────────────┐
  │                   TEST PROJECTS                          │
  │                                                          │
  │  ┌──────────────────────────┐  ┌────────────────────────┐  │
  │  │ IntegrationTests         │  │ ContainerTests         │  │
  │  │ ────────────────────── │  │ ──────────────────── │  │
  │  │ EndToEndMcpTests         │  │ DockerizedMcpTests     │  │
  │  │   6× [Fact]               │  │   4× [SkippableFact]   │  │
  │  │                          │  │                        │  │
  │  │ Server: dotnet run       │  │ DockerContainer-       │  │
  │  │ Skip: none (fails if    │  │   SpecificTests        │  │
  │  │   server not running)    │  │   5× [SkippableFact]   │  │
  │  │                          │  │                        │  │
  │  └────────────┬─────────────┘  │ Server: docker compose  │  │
  │               │               │ Skip: graceful if      │  │
  │               │               │   container not up     │  │
  │               │               └───────────┬────────────┘  │
  │               │                          │              │
  │               └──────────┬───────────┘              │
  │                          │ inherits                  │
  │                          ▼                            │
  │           ┌──────────────────────────────┐          │
  │           │ E2ETestBase (class library) │          │
  │           │ ────────────────────────── │          │
  │           │ McpEndToEndTestBase         │          │
  │           │   InitializeMcpSession()    │          │
  │           │   CallFinancialAdviceTool()  │          │
  │           │   CallResetSessionTool()     │          │
  │           │   SetupTestProfile()         │          │
  │           │   SSE / JSON-RPC parsing     │          │
  │           │   XunitLoggerProvider        │          │
  │           └──────────────────────────────┘          │
  │                                                          │
  └──────────────────────────────────────────────────────────┘
```

### 9.2 Test Pyramid

```
                    ┌────────────────────────┐
                    │ E2E Container (9)    │  ← NEW
                    │ 4 reused + 5 Docker  │
                    ├────────────────────────┤
                    │ E2E Local (6)        │  ← Existing
               ┌────┴────────────────────────┴────┐
               │ Integration (per-component)     │  ← Existing
               │ Redis, CosmosDB, Stock           │
          ┌────┴──────────────────────────────────┴────┐
          │ Unit Tests (fast, isolated)                │  ← Existing
          │ MultiAgentWorkflow, McpServer               │
          └────────────────────────────────────────────┘
```

### 9.3 What Container Tests Validate (That Other Tests Don't)

| Concern | Unit Tests | Local E2E | Container E2E (reused) | Container E2E (Docker-specific) |
|---------|-----------|-----------|----------------------|--------------------------------|
| Business logic | ✅ | ✅ | ✅ | — |
| MCP protocol | ❌ | ✅ | ✅ | — |
| Dockerfile correctness | ❌ | ❌ | ✅ (implicitly) | ✅ `ShouldBeReachableAndHealthy` |
| appsettings.Docker.json | ❌ | ❌ | ✅ (implicitly) | ✅ `Redis/CosmosDb Connectivity` |
| Container networking (Redis) | ❌ | ❌ | — | ✅ `RedisConnectivity` |
| Container networking (CosmosDB) | ❌ | ❌ | — | ✅ `CosmosDbConnectivity` |
| Port binding (`0.0.0.0` vs `localhost`) | ❌ | ❌ | ✅ (implicitly) | ✅ `ShouldBeReachableAndHealthy` |
| Env var injection (.env → container) | ❌ | ❌ | — | ✅ `AzureOpenAIEnvVars` |
| Startup time regression | ❌ | ❌ | — | ✅ `StartupTime` |

### 9.4 Running Container Tests

```powershell
# 1. Start the full stack
docker compose up -d --build

# 2. Wait for health checks
docker compose ps  # all services should show "healthy"

# 3. Run container tests
dotnet test tests/FinWise.McpServer.ContainerTests/

# 4. Teardown
docker compose down
```

### 9.5 Graceful Skip Pattern

Tests use `Xunit.SkippableFact` to avoid CI failures when the container environment isn't available:

```csharp
[SkippableFact]
public async Task Container_McpInitialize_ShouldReturnSessionId()
{
    Skip.IfNot(await IsContainerRunning(), "FinWise container not running");
    // ... test logic
}
```

---

## 10. Security Considerations

| Concern | Mitigation |
|---------|-----------|
| **Secrets in image** | Azure OpenAI keys passed via env vars at runtime, never baked into the image |
| **Non-root execution** | `USER $APP_UID` in Dockerfile (default non-root user in aspnet:10.0) |
| **`.env` file** | Added to `.gitignore`; `.env.template` committed without real values |
| **CosmosDB emulator TLS** | `AllowInsecureTls=true` already in place — dev only, not for production |
| **Image layers** | Multi-stage build ensures SDK, source code, and intermediate build artifacts are not in the final image |
| **Base image updates** | Use `aspnet:10.0` floating tag for dev; pin to digest in production/CI |

---

## 11. Risks & Mitigations

| Risk | Impact | Likelihood | Mitigation |
|------|--------|-----------|-----------|
| CosmosDB emulator slow startup (60s+) delays MCP server start | Container restarts or unhealthy | Medium | `depends_on: condition: service_healthy` + `start_period: 15s` on MCP service |
| `aspnet:10.0` image doesn't include `curl` | Health check fails | **Confirmed** | ✅ **Fixed**: Install `curl` in Dockerfile before `USER $APP_UID` (see Section 4.2) |
| MCP endpoint returns 405 for GET health check | Docker reports unhealthy | **Confirmed** | ✅ **Fixed**: Added dedicated `/health` endpoint returning HTTP 200 (see Section 8) |
| .NET 10 preview SDK image not available during early development | Build fails | Low (LTS, already at GA by 2026-03) | Pin specific tag version in Dockerfile comment |
| Large image size (~220 MB) | Slow pulls in CI | Low | Note chiseled variant as optimization path |
| CosmosDB emulator reports unreachable IPs in metadata | SDK routes requests to Docker-internal IPs, hangs indefinitely | **High — Confirmed** | ✅ **Fixed**: Remove `AZURE_COSMOS_EMULATOR_IP_ADDRESS_OVERRIDE` entirely + set `CosmosClientOptions.LimitToEndpoint = true` (see Section 13A) |
| `ReadAsStringAsync()` blocks on SSE responses | Tests hang on multi-call MCP sequences | **High — Confirmed** | ✅ **Fixed**: Use `HttpCompletionOption.ResponseHeadersRead` + line-by-line SSE reading (see Section 13B) |
| `PrivateAssets="all"` on runtime dependencies | DLL missing at container runtime, app crashes on startup | Medium | Verify all runtime-needed packages don't have `PrivateAssets="all"` — remove it from Newtonsoft.Json (see Section 13C) |
| Non-root user can't write log files in `/app` | Silent logging failure, no visibility into app behavior | Medium | Use console/stdout logging in Docker (`docker compose logs`); mount writable volume if file logging needed (see Section 13C) |

---

## 12. Future Improvements

| Item | Priority | Description |
|------|----------|-------------|
| Chiseled/distroless image | Medium | Switch to `aspnet:10.0-noble-chiseled` for smaller attack surface |
| ~~`/health` endpoint~~ | ~~Medium~~ | ✅ **Done** — Implemented as `app.MapGet("/health", () => "healthy")` in `Program.cs` |
| GitHub Actions CI | High | Build image + run container tests in CI pipeline |
| ACR push | Low | Push tagged images to Azure Container Registry |
| Multi-arch builds | Low | `docker buildx` for linux/amd64 + linux/arm64 |
| Testcontainers | Medium | Use [Testcontainers for .NET](https://dotnet.testcontainers.org/) to manage container lifecycle within tests programmatically instead of requiring external `docker compose up` |
| Upgrade Microsoft.Azure.Cosmos | Medium | Upgrade from 3.46.1 to 3.57.0+ (recommended minimum) for latest emulator compatibility and bug fixes |

---

## 13. Implementation Learnings & Critical Fixes

> **Context**: These discoveries were made during implementation. They are essential for anyone Dockerizing a .NET application that uses the CosmosDB Linux emulator, or implementing MCP Streamable HTTP clients. Each subsection documents a problem, root cause, and fix.

### 13A. CosmosDB Emulator Docker Networking (THE core problem)

The CosmosDB Linux emulator reports endpoint addresses in its account metadata (`writableLocations` / `readableLocations`). The .NET Azure.Cosmos SDK reads these and routes subsequent requests to the reported addresses — even when using `ConnectionMode.Gateway`. This creates a critical networking problem in Docker environments where the emulator's internal IP is unreachable from the host (or from other containers, depending on configuration).

**Behavior matrix for `AZURE_COSMOS_EMULATOR_IP_ADDRESS_OVERRIDE`**:

| `IP_ADDRESS_OVERRIDE` value | Metadata reports | Host access | Container access |
|---|---|---|---|
| `127.0.0.1` | `https://127.0.0.1:8081/` | ✅ port mapping works | ❌ loopback = self (not emulator) |
| `0.0.0.0` | `https://0.0.0.0:8081/` | ❌ `ServiceUnavailable` | ❌ `ServiceUnavailable` |
| Removed entirely | `https://172.18.0.x:8081/` | ❌ Docker VM IP unreachable from Windows host | ✅ Docker network resolves |
| **Removed + `LimitToEndpoint=true`** | **Ignored by SDK** | **✅ uses connection string endpoint** | **✅ uses connection string endpoint** |

**The fix** — two-part solution:

1. **Remove** `AZURE_COSMOS_EMULATOR_IP_ADDRESS_OVERRIDE` entirely from `docker-compose.yml` — do NOT set it to `127.0.0.1` or `0.0.0.0`.
2. **Add** `CosmosClientOptions.LimitToEndpoint = true` in application code when configuring for the emulator.

`LimitToEndpoint = true` sets `EnableEndpointDiscovery = false` internally — the SDK only uses the endpoint URL from the connection string, ignoring all metadata addresses. This is a GA property in Azure.Cosmos SDK, not a workaround or bug. It works correctly with `ConnectionMode.Gateway`.

**⚠️ CRITICAL**: `LimitToEndpoint = true` must be **conditional** (emulator only, not production). In FinWise, it's inside the `if (AllowInsecureTls)` block — the same guard that enables `DangerousAcceptAnyServerCertificateValidator` and `ConnectionMode.Gateway`. Production Azure CosmosDB has `AllowInsecureTls = false`, so `LimitToEndpoint` stays at the default `false`, preserving multi-region failover.

The three emulator-specific `CosmosClientOptions` that must be set together:

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

**Community reference**: GitHub issues [#98](https://github.com/Azure/azure-cosmos-db-emulator-docker/issues/98) and [#161](https://github.com/Azure/azure-cosmos-db-emulator-docker/issues/161) in `azure-cosmos-db-emulator-docker` repo document the same Docker networking IP problem.

### 13B. SSE Streaming for MCP Streamable HTTP

MCP servers using the Streamable HTTP transport return `text/event-stream` (Server-Sent Events) responses. Test HTTP clients (and any MCP client) must handle this correctly:

- **WRONG**: `HttpClient.PostAsync()` + `response.Content.ReadAsStringAsync()` — blocks until the stream closes, which may never happen for long-running operations.
- **RIGHT**: `HttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead)` + read the response stream line-by-line with `StreamReader`, extracting the first complete `data:` event in SSE format (`event: message\ndata: {json}\n\n`).

This was a secondary fix — the primary hang was caused by the CosmosDB networking issue (13A), but the SSE fix is required for robustness with any MCP server using Streamable HTTP transport.

### 13C. Dockerfile Gotchas for .NET Solutions

1. **Can't restore with solution file in Docker**: If the `.slnx` references test projects not included in the Docker build context, `dotnet restore FinWise.slnx` fails. **Fix**: Restore individual `.csproj` files instead:
   ```dockerfile
   RUN dotnet restore src/FinWise.McpServer/FinWise.McpServer.csproj
   ```

2. **Newtonsoft.Json `PrivateAssets`**: If a NuGet `PackageReference` has `PrivateAssets="all"`, the dependency is excluded from publish output. The CosmosDB SDK requires `Newtonsoft.Json` at runtime. **Fix**: Remove `PrivateAssets="all"` from the `Newtonsoft.Json` PackageReference.

3. **`aspnet:10.0` does NOT include `curl`**: The runtime image doesn't have `curl` for health checks. **Fix**: Install it in the Dockerfile **before** switching to non-root user:
   ```dockerfile
   RUN apt-get update && apt-get install -y --no-install-recommends curl && rm -rf /var/lib/apt/lists/*
   USER $APP_UID
   ```

4. **Serilog file sink silently fails**: The app runs as non-root (`$APP_UID`) but `/app` is owned by root. File logging fails silently — no error, no log files. **Fix**: Use console/stdout logging in Docker via `docker compose logs`. If file logging is needed, create a writable directory or mount a volume.

5. **Health check endpoint**: The MCP `/mcp` endpoint returns 405 (Method Not Allowed) for GET requests, making it unsuitable for Docker health checks. **Fix**: Add a dedicated `/health` endpoint:
   ```csharp
   app.MapGet("/health", () => "healthy");
   ```
   And use it in the Docker health check:
   ```yaml
   test: ["CMD-SHELL", "curl -sf -o /dev/null http://localhost:5000/health || exit 1"]
   ```

### 13D. Environment Variables on Windows

When generating `.env` files from host machine environment variables for Docker, be aware that Azure credentials may be set at the **Machine** (system) level, not User or Process level. In PowerShell:

```powershell
# Standard $env:VAR_NAME may return $null if set at Machine level only
[System.Environment]::GetEnvironmentVariable("VAR_NAME", "Machine")
```

Always check all three scopes (Process, User, Machine) when debugging missing environment variables.

### 13E. Dual-Access Pattern (Host + Docker Accessing Same Emulator)

The combination of removing `IP_ADDRESS_OVERRIDE` + `LimitToEndpoint = true` enables both access scenarios simultaneously:

| Scenario | Connection string endpoint | Why it works |
|----------|---------------------------|-------------|
| **.NET process on host** | `https://localhost:8081/` | Port mapping routes to emulator; `LimitToEndpoint` ignores Docker-internal IP in metadata |
| **Container on Docker network** | `https://cosmosdb-emulator:8081/` | Docker DNS resolves service name; `LimitToEndpoint` ignores metadata addresses |

This dual-access pattern means developers can run the emulator in Docker and connect to it from both the host (for local debugging with `dotnet run`) and from other containers (for the full `docker compose` stack) without any configuration changes to the emulator.

---

## 14. Commands Reference

```powershell
# Build image only
docker build -t finwise-mcp .

# Start full stack (build + run)
docker compose up -d --build

# View logs
docker compose logs -f finwise-mcp

# Check health
docker compose ps

# Run container tests
dotnet test tests/FinWise.McpServer.ContainerTests/

# Stop everything
docker compose down

# Stop and remove volumes (clean slate)
docker compose down -v
```
