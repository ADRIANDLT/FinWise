# 22 — The CosmosDB Emulator Evolution: From Legacy to vNext

*April 26, 2026*
*The legacy CosmosDB emulator image was 4.1 GB, needed 3 GB of RAM, took over a minute to start, and required TLS certificate hacks. Microsoft's next-gen emulator cuts all of that — but it says "preview" on the tin. Is it worth the leap?*

---

## Where We Are

Journal 21 wrapped up the migration to the Foundry LLM API. The FinWise architecture is stable — three Docker containers (CosmosDB emulator, Redis, MCP Server), full integration and E2E test coverage, and a CI spec (013) ready for implementation. While drafting that CI workflow spec, a nagging detail surfaced: the CosmosDB emulator image we've been running since Journal 12.

```yaml
image: mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest
mem_limit: 3g
cpu_count: 2
```

The legacy image — `mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest` — weighs 4.1 GB, demands 3 GB of RAM plus 2 dedicated CPU cores, takes 60–90 seconds to pass its health check, runs HTTPS-only with self-signed certificates (requiring `DangerousAcceptAnyServerCertificateValidator` hacks in both the application and test code), and exposes five extra ports (10250–10254) that nobody ever explains. On a GitHub Actions runner with 7 GB of RAM, that emulator alone consumes nearly half the available resources before a single test runs.

Microsoft now recommends something better.

---

## The Discovery

While researching CI patterns on [Microsoft Learn](https://learn.microsoft.com/azure/cosmos-db/emulator-linux), the documentation had quietly shifted. The primary Linux emulator page now described a completely different image:

```
mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview
```

An [official DevBlog post](https://devblogs.microsoft.com/cosmosdb/use-azure-cosmos-db-as-a-docker-container-in-ci-cd-pipelines/) by a Principal PM on the Cosmos DB team laid it out explicitly: use the vnext-preview image for CI/CD. Microsoft even published a [dedicated GitHub Actions sample repository](https://github.com/AzureCosmosDB/cosmosdb-linux-emulator-github-actions) showing exactly how — for .NET, Python, Java, and Go.

The question wasn't *should we migrate?* It was *why haven't we already?*

---

## The "Preview" Question

> **User:** "The Cosmos DB image looks like a preview. Are you sure this is going to be a stable image?"

A fair concern. The word "preview" triggers the same instinct in every engineer: *don't ship preview dependencies*. But this case is different.

The "preview" label reflects **feature completeness**, not stability. The vnext emulator doesn't support stored procedures, UDFs, or triggers — and never will ("Not planned" in the docs). It also hasn't implemented parallel partitioned queries yet. But FinWise uses none of these. The core operations — create database, create container, CRUD documents, queries with filters, order-by, paging, aggregates — are all marked "Supported."

More importantly, this is a **development and test emulator**, not a production dependency. It never touches real user data. If a future vnext update breaks something, the integration tests will catch it immediately.

The evidence was decisive:

| Factor | Finding |
|--------|---------|
| Image freshness | Built April 3, 2026 — actively maintained |
| Microsoft recommendation | Explicitly documented for CI on Learn + DevBlog |
| Official tutorials | New .NET tutorials all use vnext-preview |
| Size | 1.7 GB vs 4.1 GB (59% smaller) |
| Test validation | All 13 CosmosDB integration tests pass |

> **User:** "What are all the benefits of this Cosmos DB image preview? Is it really worth and out of risks? Please, confirm based on official Microsoft info."

After fetching the full Microsoft Learn documentation, the official DevBlog, and the GitHub sample repo, the answer was clear: Microsoft is positioning vnext as the replacement. The legacy `:latest` image isn't recommended for CI at all.

---

## The Volume Incompatibility

Before touching any configuration, one critical question:

> **User:** "Will the Cosmos DB volume existing in my Docker installation be compatible with the new image? Or do we need to re-create the volumes?"

**Not compatible.** Two reasons:

1. **Different storage engine.** The legacy emulator uses a proprietary engine; vnext is built on PostgreSQL internally (the docs reference "pglog" and verbose mode prints PostgreSQL logs).
2. **Different data path.** Legacy mounts to `/tmp/cosmos/appdata`; vnext uses `/data`.

The user deleted the old volume manually. A clean slate.

---

## The Migration

The changes to [`docker-compose.infra.yml`](../docker-compose.infra.yml) were surgical but comprehensive:

### Before (Legacy)

```yaml
finwise-cosmosdb-emulator:
  image: mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:latest
  ports:
    - "8081:8081"
    - "10250:10250"
    - "10251:10251"
    - "10252:10252"
    - "10253:10253"
    - "10254:10254"
  environment:
    - AZURE_COSMOS_EMULATOR_PARTITION_COUNT=10
    - AZURE_COSMOS_EMULATOR_ENABLE_DATA_PERSISTENCE=true
  volumes:
    - cosmosdb-data:/tmp/cosmos/appdata
  mem_limit: 3g
  cpu_count: 2
  healthcheck:
    test: [ "CMD-SHELL", "curl -f -k https://localhost:8081/_explorer/emulator.pem || exit 1" ]
    interval: 30s
    timeout: 10s
    retries: 5
    start_period: 60s
```

### After (vNext)

```yaml
finwise-cosmosdb-emulator:
  image: mcr.microsoft.com/cosmosdb/linux/azure-cosmos-emulator:vnext-preview
  ports:
    - "8081:8081"
    - "8080:8080"   # Health probe (/alive, /ready, /status)
  environment:
    - PROTOCOL=https
  volumes:
    - cosmosdb-data:/data
  mem_limit: 1g
  healthcheck:
    test: [ "CMD-SHELL", "curl -f http://localhost:8080/ready || exit 1" ]
    interval: 10s
    timeout: 5s
    retries: 5
    start_period: 15s
```

Every line changed for a reason:

| Change | Why |
|--------|-----|
| Image tag `:latest` → `:vnext-preview` | Microsoft's recommended CI image |
| Ports 10250–10254 **removed**, 8080 **added** | vnext doesn't use compute ports; 8080 is the dedicated health probe |
| `AZURE_COSMOS_EMULATOR_*` env vars **removed** | Legacy-only; vnext doesn't recognize them |
| `PROTOCOL=https` **added** | vnext defaults to HTTP, but .NET SDK requires HTTPS |
| Volume `/tmp/cosmos/appdata` → `/data` | New internal data path |
| `mem_limit: 3g` → `1g` | vnext is dramatically lighter |
| `cpu_count: 2` **removed** | No CPU reservation needed |
| Health check `curl -k https://...emulator.pem` → `curl http://localhost:8080/ready` | Dedicated health probe endpoint — no TLS hack needed |
| `start_period: 60s` → `15s`, `interval: 30s` → `10s` | vnext starts in seconds, not minutes |

The `PROTOCOL=https` setting deserves a note. The vnext emulator defaults to plain HTTP, which would simplify everything — but the .NET Cosmos SDK (v3.46.1) doesn't support HTTP mode against the emulator. Microsoft's own DevBlog confirms this: the HTTPS protocol must be set, and for .NET you still need the `AllowInsecureTls` / `DangerousAcceptAnyServerCertificateValidator` bypass for the self-signed cert. That part of our code didn't change.

---

## Validation

### First Light

After `docker compose -f docker-compose.infra.yml up -d`, the health probe responded almost instantly:

```json
{
  "alive": true,
  "checks": {
    "explorer": "healthy",
    "gateway": "healthy",
    "postgres": "healthy"
  },
  "overall": true,
  "protocol": "https",
  "ready": true,
  "status": "healthy",
  "version": "EN20260331"
}
```

The structured JSON health response — with separate checks for explorer, gateway, and the internal PostgreSQL engine — is a significant upgrade from the old "can you download this PEM file?" hack.

### Integration Tests

```
CosmosDB Integration Tests:  13/13 passed  (3.3s)
```

All 13 tests — CRUD operations, persistence verification, cross-instance reads — passed on the first attempt. No code changes required in any test file. The existing `AllowInsecureTls` configuration worked identically with the vnext emulator's HTTPS mode.

### Full Stack Validation

With the full Docker stack running (`docker compose up` — CosmosDB + Redis + MCP Server container):

```
CosmosDB Integration Tests:     13/13 passed   (5.1s)
Redis Integration Tests:        12/12 passed   (10.1s)
MCP Server E2E Tests:            8/8  passed   (191.0s)
MCP Server Container Tests:     11/11 passed   (49.4s)
─────────────────────────────────────────────────
Total:                          44/44 passed   (~4.3 min)
```

Zero failures. Zero skips. The vnext emulator is a drop-in replacement for every scenario FinWise exercises.

---

## What We Learned

### About the Technology

- **"Preview" doesn't always mean "unstable."** The vnext emulator's preview label reflects incomplete feature coverage (no stored procs, no UDFs), not quality concerns. For standard NoSQL CRUD + queries, it's rock solid.
- **The .NET SDK's HTTP limitation shapes the configuration.** Even though vnext supports plain HTTP (which would eliminate TLS bypass hacks entirely), the .NET Cosmos SDK doesn't support HTTP mode against the emulator. This means `PROTOCOL=https` is mandatory for .NET projects, and the `AllowInsecureTls` code stays.
- **Volume formats are not portable across emulator generations.** The legacy emulator uses a proprietary storage engine; vnext uses PostgreSQL. Old volumes must be deleted, not migrated.
- **Dedicated health probes are a massive improvement for CI.** The legacy health check (`curl -k https://...emulator.pem`) was a hack — it checked whether the TLS certificate endpoint was reachable, not whether the database was actually ready. The vnext `/ready` endpoint on port 8080 reports actual database readiness with structured JSON.

### About the Process

- **Verify Microsoft's "latest" recommendations periodically.** The legacy `:latest` tag is still available and still documented on older pages. But the team has clearly moved on — all new documentation, tutorials, and CI samples reference vnext-preview exclusively. If we hadn't researched CI patterns, we'd have kept running a 4.1 GB image that Microsoft no longer recommends.
- **Integration tests are the migration safety net.** The entire migration — image swap, port changes, volume path, health check, env vars — was validated by existing tests without writing a single new test. Good test coverage pays dividends during infrastructure changes.

---

## What's Next

The vnext emulator migration sets the stage for [Spec 013 — CI GitHub Actions Workflow](../specs/013-ci-github-actions-workflow/013-ci-github-actions-workflow.md). With the emulator now using 1 GB instead of 3 GB, GitHub Actions runners (7 GB RAM) have plenty of headroom for the full integration test suite. The spec has been updated to reference the vnext-preview image.

Next up: implementing the CI workflow itself.

---

*Written: April 26, 2026*
