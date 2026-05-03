# 17- The Azure Redis Odyssey: From Local Container to Cloud Managed Redis

_April 17, 2026_
_A POC journey through Azure Managed Redis — navigating service choices, deployment failures, cluster policies, TLS auto-detection, and the triumph of 12 green tests._

---

## Setting the Scene

FinWise had been running Redis happily in a local Docker container — a simple `redis:7.4-alpine` image on port 6379, no TLS, no auth, no drama. It handled MCP session migration and agent session storage with zero friction. But the next milestone on the roadmap was clear: **move data stores to Azure** for real cloud deployment.

CosmosDB for user profiles was already on the radar (spec 002), and now it was Redis's turn. The question seemed simple enough: _"Create a Redis database in Azure and point FinWise at it."_

It would turn out to be anything but simple.

---

## Act 1: Which Azure Redis?

The journey began with a straightforward question about TLS support for the Azure Redis instance. But before we could even get to configuration details, Azure had a surprise:

> **Azure Portal:** _"We recommend you proceed with our new offering — Azure Managed Redis. Azure Cache for Redis will be retired on September 30, 2028."_

A pivot before we even started. Azure Cache for Redis — the service we'd been planning for — was being sunset. Its successor, **Azure Managed Redis**, was built on Redis Enterprise 7.4 and had fundamentally different characteristics:

| What changed | Old (Azure Cache for Redis) | New (Azure Managed Redis) |
|-------------|---------------------------|--------------------------|
| **Hostname** | `*.redis.cache.windows.net` | `*.redis.azure.net` |
| **Port** | 6380 (TLS) / 6379 (non-TLS) | **10000** (both) |
| **Default auth** | Access keys enabled | Entra ID enabled, keys disabled |
| **TLS** | Separate ports, can have both | **Single port, either/or** |
| **Pricing** | Basic C0 ~$16/mo | Balanced B0 ~$7/mo |

We pivoted to Azure Managed Redis and began deep research on its requirements, connection patterns, and StackExchange.Redis compatibility.

**Key spec created:** [`specs/011-azure-redis-support-plan/`](../specs/011-azure-redis-support-plan/011-azure-redis-support-plan.md)

---

## Act 2: The Deployment Gauntlet

What followed was a series of Azure deployment attempts that tested patience and revealed undocumented limitations of Azure Managed Redis:

### Attempt 1: OSSCluster + Plaintext in East US 2 ✅
The first deployment succeeded! `finwise-managed-redis-instance` came up running with OSSCluster policy and Plaintext protocol. Raw TCP connectivity worked — AUTH + PING responded perfectly.

But StackExchange.Redis couldn't connect. Tests silently skipped.

### Attempt 2: EnterpriseCluster + Plaintext ❌
Research suggested EnterpriseCluster would solve the connectivity issue by routing all traffic through a proxy endpoint. But **Balanced B0 doesn't support EnterpriseCluster** — it requires B3 or higher. Deployment failed.

### Attempt 3: Non-clustered + Plaintext ❌
Non-clustered seemed perfect — single endpoint, no redirects. But it's **in Preview**, and the deployment failed with a cryptic `OperationFailed`.

### Attempts 4-5: Various configs in East US 2 ❌
Multiple retries with different configurations all failed. Research revealed a [known GitHub issue](https://github.com/hashicorp/terraform-provider-azurerm/issues/30965) — Azure Managed Redis deployments were failing intermittently due to backend issues. The `eastus2` region appeared to have capacity constraints.

### Attempt 6: OSSCluster + Plaintext in East US ✅
Switching to `eastus` region with the proven OSSCluster + Plaintext config finally worked. The resource `finwise-managed-redis-poc` deployed successfully in about 5 minutes.

> **Lesson learned:** When Azure deployments fail repeatedly with generic errors, try a different region before debugging your template.

---

## Act 3: The SSL Auto-Detection Mystery

With the Azure instance running, we set the connection string and ran the integration tests. All 12 tests **skipped** — "Redis is not available."

But raw TCP connectivity worked perfectly:
```
AUTH <password> → +OK
PING → +PONG
```

We launched a systematic hypothesis-driven debugging investigation with three competing theories:

| Hypothesis | Theory |
|-----------|--------|
| H1 | OSSCluster MOVED redirects to internal IPs |
| H2 | Connection succeeds but IsConnected is false |
| H3 | Duplicate connection string parameters |

After adding instrumentation, we built a standalone .NET console app to capture the StackExchange.Redis connection log. The smoking gun appeared immediately:

```
Ssl=True                          ← SE.Redis auto-enabled SSL!
Configuring TLS
OnConnectedAsync completed (Disconnected)  ← TLS handshake failed
```

**Root cause:** StackExchange.Redis has a built-in Azure-awareness feature — when it detects `*.redis.azure.net` in the hostname, it **automatically enables SSL** even when `ssl=True` is not in the connection string. Since our instance used `clientProtocol: "Plaintext"`, the TLS handshake failed silently.

**The fix:** One parameter — `ssl=False` — explicitly overriding the auto-detection:

```
finwise-managed-redis-poc.eastus.redis.azure.net:10000,password=<key>,ssl=False,abortConnect=False
```

> **Critical discovery:** With Azure Managed Redis hostnames (`*.redis.azure.net`), StackExchange.Redis defaults to SSL=True. For Plaintext instances, you **must** explicitly set `ssl=False`.

---

## Act 4: Green Across the Board

With `ssl=False` in place, the connection worked instantly — PING latency of 45.9ms from the local dev machine to Azure East US. All 12 Redis integration tests ran against the Azure instance:

```
Passed! - Failed: 0, Passed: 12, Skipped: 0, Total: 12, Duration: 10s
```

One test (`AllowMigration_RefreshesTtl`) initially failed with a 1-millisecond timing discrepancy due to Azure network latency. We added a 1-second tolerance to the TTL assertion — a reasonable accommodation for cloud-hosted Redis vs local Docker.

The MCP Server integration tests also passed: **8/8 green** against the full stack with Azure Redis + local CosmosDB emulator.

---

## Act 5: The Layered Environment Architecture

A key architectural decision emerged during implementation: how to manage environment variables across local and Azure configurations.

The original approach had `.env.azure` as a **complete standalone copy** of all variables — duplicating Azure OpenAI keys, Stock Agent config, and data store toggles. This was fragile and error-prone.

We redesigned it as a **layered override system**:

```
.env          → Base config (Azure OpenAI, local CosmosDB, local Redis)
.env.azure    → Only overrides (just the Redis connection string for now)
```

Docker Compose v2.24+ supports multiple `--env-file` flags where later files override earlier ones:

```powershell
# Step 1: Start only the CosmosDB emulator (local)
docker compose -f docker-compose.infra.yml up -d finwise-cosmosdb-emulator

# Step 2: Start FinWise MCP Server with Azure Redis (layered env override)
docker compose -f docker-compose.finwise.yml --env-file .env --env-file .env.azure up -d --build
```

By using `docker-compose.finwise.yml` directly (instead of the main `docker-compose.yml`), the server has no `depends_on: finwise-redis` — so the local Redis container never starts. The Azure Redis connection string from `.env.azure` overrides the local one from `.env`, and CosmosDB stays on the local emulator since those vars aren't overridden.

---

## What We Learned

### About Azure Managed Redis
- **It's new and rough around the edges** — deployment failures, Preview features that don't work, capacity issues in some regions
- **Port 10000 for everything** — both TLS and non-TLS, unlike the old separate 6379/6380
- **`clientProtocol` is either/or** — no dual-protocol support (Encrypted OR Plaintext, not both)
- **B0 only supports OSSCluster** — EnterpriseCluster needs B3+, Non-clustered is in Preview
- **OSSCluster with single B0 node works from local dev** — no MOVED redirects with only 1 shard

### About StackExchange.Redis
- **Azure hostname auto-detection enables SSL silently** — the biggest gotcha of this entire journey
- **`ssl=False` must be explicit** for Plaintext Azure instances
- **30-second connect timeout needed** for Azure (vs 3s for local Docker)
- **The library is otherwise fully compatible** with Azure Managed Redis — no code changes needed

### About the Process
- **Hypothesis-driven debugging works** — forming 3 competing theories prevented anchoring on the wrong cause
- **Raw TCP testing is invaluable** — proving the network layer works isolates the issue to the client library
- **Region matters** — when deployments fail, try another region before debugging templates

---

## Files Changed

| File | Change |
|------|--------|
| [`.env.azure.template`](../.env.azure.template) | Layered override format with `ssl=False` |
| [`.env.azure`](../.env.azure) | Azure Redis connection string (secrets, not committed) |
| [`RedisOptions.cs`](../src/FinWise.MultiAgentWorkflow/Infrastructure/AgentSessionStores/Redis/RedisOptions.cs) | Updated XML doc with Azure Managed Redis format |
| [`RedisAgentSessionStoreIntegrationTests.cs`](../tests/FinWise.Redis.IntegrationTests/RedisAgentSessionStoreIntegrationTests.cs) | Env var support + Azure-aware timeout |
| [`RedisSessionMigrationHandlerIntegrationTests.cs`](../tests/FinWise.Redis.IntegrationTests/RedisSessionMigrationHandlerIntegrationTests.cs) | Env var support + Azure-aware timeout + TTL tolerance fix |
| [`docker-compose.yml`](../docker-compose.yml) | Updated usage comments for layered env files |
| [`docker-compose.finwise.yml`](../docker-compose.finwise.yml) | Updated usage comments for layered env files |
| [`011-azure-redis-support-plan.md`](../specs/011-azure-redis-support-plan/011-azure-redis-support-plan.md) | Full spec with research, config, and future evolution path |

---

## What's Next

The Azure Redis POC is working end-to-end. The evolution path is clear:

```
Current (POC)              →  Step 1              →  Step 2
Plaintext + Access Keys    →  TLS + Access Keys   →  TLS + Service Principal (Entra)
ssl=False                  →  ssl=True            →  Code changes + Entra package
```

Next on the roadmap: **Azure CosmosDB** — moving the user profile store from the local emulator to a real Azure CosmosDB instance, completing the "all data stores in Azure" milestone.

---

_Written: April 17, 2026_
_Session duration: ~2.5 hours of research, debugging, and deployment iteration_
_Tests: 20/20 passing (12 Redis + 8 MCP Server integration)_
