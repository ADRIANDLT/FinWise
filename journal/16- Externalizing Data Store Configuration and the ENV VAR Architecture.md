# 16 — Externalizing Data Store Configuration and the ENV VAR Architecture

*April 12, 2026*
*Hard-coded connection strings give way to environment variables, a master in-memory toggle, and a three-file Docker Compose structure that can point at local emulators or Azure cloud databases with a single flag.*

---

## Where We Are

FinWise has been running with its data store configuration baked into `appsettings.json` since the beginning. CosmosDB endpoint, the emulator's well-known key, Redis connection string, `Enabled: true` for both — all sitting in a tracked JSON file. It worked for local development, and `appsettings.Docker.json` overrode the hostnames for container networking.

But the cracks were showing. Every deployment scenario required editing config files. Want to run without Docker? Change `Enabled` to `false`. Want to point at Azure databases? Override six properties. Want to run the server in a container against Azure CosmosDB while Redis stays local? Good luck.

The goal: make FinWise's data layer fully configurable from the outside — environment variables, `.env` files, Docker Compose — without touching a single source file. And add a master kill switch for "just run everything in-memory, no infrastructure required."

---

## The Configuration Pyramid

The first design decision was the **precedence chain**. .NET's `IConfiguration` already layers `appsettings.json → appsettings.{Environment}.json → environment variables`. But the `FINWISE_*` env vars needed to be explicit — not the `.NET __`-separator convention, but clean, uppercase, prefixed names that read well in Docker Compose and CI/CD pipelines.

The factories (`UserProfileStoreFactory` and `AgentSessionStoreFactory`) already existed. They read Options classes, checked `Enabled`, and returned either the real store or an in-memory fallback. The change was surgical: after binding from `IConfiguration`, apply `FINWISE_*` env var overrides.

```
appsettings.json (defaults: Enabled=false, localhost endpoints)
  ↓ overridden by
appsettings.Docker.json (Enabled=true, Docker service hostnames)
  ↓ overridden by
FINWISE_* env vars (point anywhere — local, Azure, custom)
  ↓ short-circuited by
FINWISE_FORCE_IN_MEMORY_DATA=true (ignores everything above)
```

### The Naming Question

The master toggle went through three names in one session:

1. `FINWISE_IN_MEMORY_DATA` — first attempt. Problem: `=false` reads like "don't use in-memory → use databases," but it actually means "don't force, check individual flags."
2. `InMemoryData` — config key in appsettings. Same confusion.
3. **`FINWISE_FORCE_IN_MEMORY_DATA`** — final name. The word "Force" makes the semantics obvious: `=true` means "I'm forcing all stores to in-memory, regardless of individual settings." `=false` means "I'm not forcing anything — each store decides on its own."

> "It's a bit confusing. How could it be clearer with the same apply we have but better naming?"

The answer was adding one word: **Force**.

---

## The appsettings Balancing Act

The initial instinct was to strip all data store config from `appsettings.json` entirely. The Options classes already default to `Enabled: false` and `localhost` endpoints. Clean, minimal.

But then the practical question hit:

> "What if I run directly the .NET MCP server without Docker? With `dotnet run`?"

Without connection defaults in `appsettings.json`, switching from in-memory to local Docker infra would require nine environment variables. For everyday local dev, that's painful.

The compromise: **keep the connection defaults** (emulator endpoint, well-known key, `localhost:6379`) in `appsettings.json`, but with `Enabled: false` and `ForceInMemoryData: true`. Flipping to infrastructure needs just two env vars (`FINWISE_COSMOSDB_ENABLED=true`, `FINWISE_REDIS_ENABLED=true`) because the connection details are already there.

`appsettings.Development.json` got stripped to its only useful purpose — Debug-level logging. Everything else was a duplicate.

---

## The Three-File Docker Compose

The existing `docker-compose.yml` bundled everything: infrastructure services, server definition, `depends_on` wiring. That meant you couldn't run the server container against Azure databases — it always brought up the local emulators.

The split:

| File | Purpose |
|------|---------|
| [`docker-compose.finwise.yml`](../docker-compose.finwise.yml) | Server only — single source of truth for the FinWise container definition |
| [`docker-compose.infra.yml`](../docker-compose.infra.yml) | Infrastructure only — CosmosDB emulator + Redis (unchanged) |
| [`docker-compose.yml`](../docker-compose.yml) | Full local stack — `extends:` server from finwise, `includes:` infra, adds `depends_on` |

The key constraint: `docker-compose.finwise.yml` must be **standalone** — no `depends_on` referencing infra services. That's what makes `docker compose -f docker-compose.finwise.yml --env-file .env.azure up -d` work.

The first attempt used `include:` for both files, but Docker Compose threw "services.finwise-mcp-server conflicts with imported resource" — you can't modify an included service. The fix was `extends:`, which copies the definition and lets the parent add `depends_on`.

Three commands, three scenarios:

```bash
docker compose up -d                                                    # Full local stack
docker compose -f docker-compose.infra.yml up -d                        # Infra only (for dotnet run)
docker compose -f docker-compose.finwise.yml --env-file .env.azure up -d # Server → Azure DBs
```

---

## The .env Architecture

The `.env` file was already git-ignored (`*.env` in `.gitignore`) and used by Docker Compose for Azure OpenAI credentials. Adding the `FINWISE_*` data store vars was straightforward — but it surfaced a question about multi-environment support.

One set of env var **names**, multiple sets of **values**:

| File | Environment | Tracked in git? |
|------|------------|-----------------|
| [`.env`](../.env) | Local Docker (emulator hostnames) | No — secrets |
| [`.env.template`](../.env.template) | Template for new devs | Yes — placeholder values |
| [`.env.azure`](../.env.azure) | Azure-hosted databases | No — secrets |
| [`.env.azure.template`](../.env.azure.template) | Template for Azure config | Yes — placeholder values |

All four files share the same section structure:

```
# --- Azure OpenAI (required) ---
# --- Stock Agent (optional) ---
# --- Data store master toggle ---
# --- CosmosDB user profile store ---
# --- Redis session store ---
```

Consistent naming across `.env`, `.env.template`, `.env.azure.template`, and `docker-compose.yml`.

---

## The Full ENV VAR Map

Ten `FINWISE_*` environment variables, each overriding its appsettings.json counterpart:

| Env Var | Maps to | Default |
|---------|---------|---------|
| `FINWISE_FORCE_IN_MEMORY_DATA` | `ForceInMemoryData` | `true` |
| `FINWISE_COSMOSDB_ENABLED` | `CosmosDb:Enabled` | `false` |
| `FINWISE_COSMOSDB_ENDPOINT` | `CosmosDb:Endpoint` | `https://localhost:8081/` |
| `FINWISE_COSMOSDB_KEY` | `CosmosDb:Key` | Emulator key |
| `FINWISE_COSMOSDB_DATABASE_NAME` | `CosmosDb:DatabaseName` | `FinWise` |
| `FINWISE_COSMOSDB_CONTAINER_NAME` | `CosmosDb:ContainerName` | `UserProfiles` |
| `FINWISE_COSMOSDB_ALLOW_INSECURE_TLS` | `CosmosDb:AllowInsecureTls` | `true` |
| `FINWISE_REDIS_ENABLED` | `Redis:Enabled` | `false` |
| `FINWISE_REDIS_CONNECTION_STRING` | `Redis:ConnectionString` | `localhost:6379` |
| `FINWISE_REDIS_SESSION_TTL_MINUTES` | `Redis:SessionTtlMinutes` | `1440` |

---

## Test Infrastructure Improvements

The configuration changes exposed a gap in how tests were organized. Two improvements were made:

**xUnit Trait Categorization** — All 20 test classes tagged with `[Trait("Category", "...")]`:
- `Unit` (10 classes, 89 tests) — no infrastructure needed
- `Integration` (7 classes) — needs Docker infra or Azure credentials
- `Container` (3 classes, 11 tests) — needs full Docker stack

This enables selective execution: `dotnet test --filter "Category=Unit"` runs only unit tests across the entire solution.

**Environment-specific `.runsettings`** — A `tests/Directory.Build.props` auto-applies `test.runsettings` to all test projects. A `test.docker-local.runsettings` injects all `FINWISE_*` env vars for local Docker infra into the test process:

```powershell
dotnet test --filter "Category=Integration" --settings test.docker-local.runsettings
```

A new container test (`DockerEnvVarConfigTests`) validates the `FINWISE_*` env var pipeline end-to-end — proving the Docker container starts healthy and responds to MCP tool calls with the configured stores.

---

## What We Learned

### About Configuration Design

- **Naming matters more than architecture.** The jump from `IN_MEMORY_DATA` to `FORCE_IN_MEMORY_DATA` eliminated all confusion about what `false` means. One word changed the entire mental model.
- **Defaults should match the zero-infrastructure case.** `ForceInMemoryData: true` means `dotnet run` works out of the box. The developer opts *into* complexity, not out of it.
- **Connection defaults belong in config files, not env vars.** The emulator endpoint and well-known key in `appsettings.json` mean switching to Docker infra needs only `Enabled=true` — not nine env vars.

### About Docker Compose

- **`include:` imports are immutable.** You can't modify an included service — use `extends:` when you need to layer on `depends_on` or other overrides.
- **`.env` is Docker Compose territory.** .NET's `dotnet run` doesn't read it. For local dev, `appsettings.json` is the source of truth; `.env` is for containers.

---

## What's Next

- **Azure infrastructure provisioning** — The `.env.azure.template` is ready, but no Azure CosmosDB or Redis instances exist yet. When they do, `docker compose -f docker-compose.finwise.yml --env-file .env.azure up -d` will connect to them without any code changes.
- **Testcontainers adoption** — Layer 3 of the test strategy: self-contained integration tests that manage their own Docker containers within the test process, eliminating the need for `docker compose up` before running tests.
- **GitHub Actions CI/CD** — The trait categorization and `.runsettings` files are designed for a matrix pipeline: unit tests (no infra), integration tests (Testcontainers or Docker services), container tests (full Docker build).

---

*Written: April 12, 2026*
