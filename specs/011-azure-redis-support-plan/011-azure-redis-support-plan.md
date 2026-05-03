# 011 — Azure Managed Redis Support Plan

## Overview

Connect FinWise to **Azure Managed Redis** for session/profile persistence when running against Azure infrastructure.

**Current state**: Local `redis:7.4-alpine` container on port 6379 (no TLS, no auth).
**Target state**: Azure Managed Redis on port 10000 (access-key auth, plaintext protocol for POC).

---

## Azure Managed Redis Instance (Created)

| Setting | Value |
|---------|-------|
| **Resource name** | `finwise-managed-redis-poc` |
| **Type** | `Microsoft.Cache/redisEnterprise` |
| **Location** | `eastus` |
| **SKU** | `Balanced_B0` (~$7/mo) |
| **High Availability** | Disabled (POC) |
| **Public network access** | Enabled |
| **Client protocol** | **Plaintext** (non-TLS) |
| **Access keys auth** | **Enabled** |
| **Entra ID auth** | Available (can be enabled later) |
| **Eviction policy** | NoEviction |
| **Clustering** | OSSCluster (single B0 node) |
| **Persistence** | Disabled (no AOF, no RDB) |

**Endpoint**: `finwise-managed-redis-poc.eastus.redis.azure.net:10000`

> ⚠️ **Plaintext protocol**: Since `clientProtocol: "Plaintext"` is configured, the connection
> does **NOT** use TLS. The connection string should **NOT** include `ssl=True`.
> This is acceptable for a POC. For production evolution, see [Future Steps](#future-steps--evolution).

---

## What's Needed (Initial Implementation Scope)

**Scope**: Access key auth + plaintext protocol. No TLS, no Entra auth — keep it simple for the POC.
**Code changes**: None. Only environment variable / config file updates.

### 1. Azure Portal Configuration (Mostly Done ✅)

#### 1.1 Create the Resource ✅
- Resource: `finwise-managed-redis-poc` in `eastus`
- SKU: Balanced B0, HA disabled, public access enabled
- Access keys enabled, plaintext protocol

#### 1.2 Obtain Secrets from Azure Portal (⚡ ACTION REQUIRED)

Once the resource is created, you need **one value** from the Azure Portal:

| What to copy | Where to find it | Where to put it |
|--------------|-----------------|-----------------|
| **Primary Access Key** | Azure Portal → `finwise-managed-redis-poc` → **Settings → Authentication → Access keys** → copy **Primary** | `.env.azure` → `FINWISE_REDIS_CONNECTION_STRING` (as the `password=` value) |

The **endpoint hostname** is already known from the resource name and region:
```
finwise-managed-redis-poc.eastus.redis.azure.net
```

So the full connection string to put in `.env.azure` is:
```
finwise-managed-redis-poc.eastus.redis.azure.net:10000,password=<paste-primary-access-key-here>,ssl=False,abortConnect=False
```

> ⚠️ **`ssl=False` is required**: StackExchange.Redis auto-enables SSL when it detects
> `*.redis.azure.net` in the hostname. Since our instance uses `clientProtocol: "Plaintext"`,
> you must explicitly set `ssl=False` to prevent TLS handshake failures.

#### 1.3 Networking ✅
**"Enable public access from all networks"** is selected — any IP can connect with the
access key. No firewall rules or IP whitelisting needed for dev/test.

> **Production note**: For production, switch to **Private Endpoint** or add specific
> firewall rules (Azure Portal → Administration → Networking → Firewall) to restrict
> access to known IPs only.

---

### 2. Environment Variable / Config Changes

#### 2.1 Connection String Format

```
finwise-managed-redis-poc.eastus.redis.azure.net:10000,password=<your-access-key>,ssl=False,abortConnect=False
```

Key points:
- **Hostname**: `<name>.<region>.redis.azure.net` (Azure Managed Redis format)
- **Port**: `10000` (used for both TLS and non-TLS in Azure Managed Redis)
- **`ssl=False`** — **required** for Plaintext instances. StackExchange.Redis auto-enables SSL
  when it detects `*.redis.azure.net` in the hostname, so you must explicitly disable it.
- **`abortConnect=False`** — recommended so the app doesn't crash if Redis is temporarily unavailable at startup

#### 2.2 Files to Update

| File | Change |
|------|--------|
| `.env.azure.template` | Update connection string example + section header comment |
| `.env.azure` | Fill in real values (never committed) |
| `src/.../RedisOptions.cs` | Update XML doc comment showing connection string format |

#### 2.3 `.env.azure` Values

```env
# --- Redis session store (Azure Managed Redis) ---
FINWISE_REDIS_CONNECTION_STRING=finwise-managed-redis-poc.eastus.redis.azure.net:10000,password=<primary-access-key>,ssl=False,abortConnect=False
```

---

### 3. Docker / Container Considerations

| Concern | Status |
|---------|--------|
| **DNS resolution** | ✅ Works out of the box — `*.redis.azure.net` resolves via Docker's DNS |
| **Outbound port 10000** | ✅ Docker containers have outbound access by default |
| **Dockerfile changes** | ✅ None needed |
| **docker-compose changes** | ✅ None needed — `FINWISE_REDIS_CONNECTION_STRING` already passed through |

**How to run with Azure Redis (CosmosDB local + Redis in Azure)**:
```powershell
# Start CosmosDB emulator separately
docker compose -f docker-compose.infra.yml up -d finwise-cosmosdb-emulator

# Start FinWise server with Azure Redis (no local Redis dependency)
docker compose -f docker-compose.finwise.yml --env-file .env --env-file .env.azure up -d --build
```

> **Note**: `.env.azure` is a layered override file — it only contains Azure-specific overrides.
> The base `.env` provides all shared config (Azure OpenAI keys, data store toggles, etc.).
> Docker Compose reads `.env` first, then `.env.azure` overrides matching vars.

---

### 4. Local Redis (Dev/Test) — No Changes

The local Redis container setup remains unchanged:
- `docker-compose.infra.yml` → `redis:7.4-alpine` on port 6379
- `.env.template` → `FINWISE_REDIS_CONNECTION_STRING=finwise-redis:6379`
- No TLS, no auth — appropriate for local dev

The two environments (local vs Azure) are isolated by which `.env` file you use.

---

## Summary of Work Items

1. **Azure Portal**: Create Azure Managed Redis instance ✅, enable access keys ✅, public access ✅
2. **Update `.env.azure.template`**: New connection string format + comment
3. **Update `.env.azure`**: Fill in real Azure Managed Redis values
4. **Update `RedisOptions.cs`**: XML doc comment showing new format
5. **Test**: Run app against Azure Managed Redis from Docker and local .NET

---

## Future Steps / Evolution

The evolution path is incremental — each step builds on the previous one:

```
Current (POC)              →  Step 1              →  Step 2
Plaintext + Access Keys    →  TLS + Access Keys   →  TLS + Service Principal (Entra)
No code changes            →  Config change only   →  Code + config changes
```

### Future Step 1: Enable TLS (Encrypted Protocol)

When moving toward production, switch from `Plaintext` to `Encrypted` on the Azure Managed Redis instance.

> **Important**: Azure Managed Redis `clientProtocol` is either/or — `Encrypted` or `Plaintext` — there is
> no "both" option. The ARM schema only accepts `'Encrypted'` or `'Plaintext'`. Both use port 10000.

#### What changes in Azure
1. Navigate to `finwise-managed-redis-poc` → **Advanced settings**
2. Disable **"Non-TLS access only"** (switches `clientProtocol` from `Plaintext` to `Encrypted`)
3. Save — takes effect almost immediately; existing plaintext connections will be dropped

#### What changes in code / config

**No code changes** — only the connection string value changes.

| File | Change |
|------|--------|
| `.env.azure` | Change `ssl=False` to `ssl=True` in the connection string |
| `.env.azure.template` | Update example connection string to use `ssl=True` |

**Connection string with TLS**:
```
finwise-managed-redis-poc.eastus.redis.azure.net:10000,password=<key>,ssl=True,abortConnect=False
```

#### Docker impact
- **No Dockerfile changes** — `aspnet:10.0` (Debian Bookworm) already includes
  Azure's DigiCert Global Root G2 CA in its certificate trust store
- **No docker-compose changes** — same env var, just a different value
- **Port stays 10000** — only the protocol on that port changes

#### Risk assessment
- **Low risk** — the only change is adding `ssl=True` to the connection string.
  StackExchange.Redis handles TLS negotiation automatically.
- **Breaking change** — any client still using the old (non-TLS) connection string will fail
  immediately after the switch. Coordinate the portal change with the `.env.azure` update.

#### How to verify
```powershell
# From host (requires redis-cli with TLS support)
redis-cli -h finwise-managed-redis-poc.eastus.redis.azure.net -p 10000 --tls -a <access-key>
```

---

### Future Step 2: Switch to Service Principal (Entra ID) Authentication

Replace access key auth with the FinWise Service Principal for identity-based, secretless Redis access.

> **Prerequisite**: TLS must be enabled first (Future Step 1). Microsoft Entra authentication
> requires `clientProtocol: "Encrypted"` — it will not work with `Plaintext`.

#### What changes in Azure

1. **Enable Entra ID auth** (if not already):
   - Azure Portal → `finwise-managed-redis-poc` → **Settings → Authentication**
   - Enable **Microsoft Entra Authentication**

2. **Grant the Service Principal access**:
   - Azure Portal → `finwise-managed-redis-poc` → **Data Access Configuration**
   - Click **"+ Add"** → select your FinWise Service Principal
   - Assign the **Data Contributor** role (read/write access)
   - Save

3. **Optionally disable access keys** (for tighter security):
   - Azure Portal → **Settings → Authentication → Access keys**
   - Disable access keys — only Entra auth will work
   - ⚠️ Only do this after verifying Entra auth works end-to-end

#### What changes in code

**This step requires code changes** — unlike the previous steps.

##### Add NuGet Package

In `Directory.Packages.props`:
```xml
<PackageVersion Include="Microsoft.Azure.StackExchangeRedis" Version="3.3.1" />
```

In the relevant project `.csproj` (e.g., `FinWise.MultiAgentWorkflow.csproj`):
```xml
<PackageReference Include="Microsoft.Azure.StackExchangeRedis" />
```

> `Azure.Identity` (provides `DefaultAzureCredential`) is already in the repo at v1.20.0.

##### Refactor `AgentSessionStoreFactory.cs`

The current code uses a plain connection string:
```csharp
var redis = await ConnectionMultiplexer.ConnectAsync(redisOptions.ConnectionString);
```

Refactor to support both auth modes (access key and Entra), selected by configuration:

```csharp
// Option: Access key auth (existing path — connection string includes password)
if (!string.IsNullOrEmpty(redisOptions.ConnectionString))
{
    var redis = await ConnectionMultiplexer.ConnectAsync(redisOptions.ConnectionString);
}
// Option: Entra / Service Principal auth (new path)
else if (!string.IsNullOrEmpty(redisOptions.Host))
{
    var configOptions = ConfigurationOptions.Parse($"{redisOptions.Host}:10000");
    configOptions.Ssl = true;
    configOptions.AbortOnConnectFail = false;
    configOptions.Protocol = RedisProtocol.Resp3; // Recommended for seamless token re-auth
    await configOptions.ConfigureForAzureWithTokenCredentialAsync(
        new DefaultAzureCredential());
    var redis = await ConnectionMultiplexer.ConnectAsync(configOptions);
}
```

Key details:
- `Microsoft.Azure.StackExchangeRedis` provides the `ConfigureForAzureWithTokenCredentialAsync`
  extension method on `ConfigurationOptions`
- `DefaultAzureCredential` picks up the Service Principal from `AZURE_TENANT_ID`,
  `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET` env vars
- **RESP3 protocol is recommended** — with RESP2 (default), the subscription connection
  cannot be re-authenticated when the token expires. RESP3 bundles all traffic on a
  single connection, avoiding this issue.
- The library handles **automatic token refresh** — no manual token management needed

##### Add new properties to `RedisOptions.cs`

```csharp
/// <summary>
/// Redis host for Entra/AAD authentication (without port).
/// Example: "finwise-managed-redis-poc.eastus.redis.azure.net"
/// When set, Entra auth is used instead of ConnectionString.
/// </summary>
public string? Host { get; set; }

/// <summary>
/// Whether to use Microsoft Entra ID (Service Principal) authentication.
/// When true, Host must be set and ConnectionString is ignored.
/// </summary>
public bool UseEntraAuth { get; set; }
```

##### Wire up environment overrides

In `AgentSessionStoreFactory.ApplyEnvironmentOverrides()`, add:
```csharp
if (Environment.GetEnvironmentVariable("FINWISE_REDIS_USE_ENTRA_AUTH") is "true")
    redisOptions.UseEntraAuth = true;
if (Environment.GetEnvironmentVariable("FINWISE_REDIS_HOST") is { } host)
    redisOptions.Host = host;
```

#### What changes in config

```env
# --- Redis session store (Azure Managed Redis — Entra auth) ---
FINWISE_REDIS_ENABLED=true
FINWISE_REDIS_USE_ENTRA_AUTH=true
FINWISE_REDIS_HOST=finwise-managed-redis-poc.eastus.redis.azure.net
FINWISE_REDIS_SESSION_TTL_MINUTES=1440

# Service Principal credentials (already in .env.azure for StockAgent)
FINWISE_AZURE_TENANT_ID=<tenant-id>
FINWISE_AZURE_CLIENT_ID=<client-id>
FINWISE_AZURE_CLIENT_SECRET=<client-secret>
```

> `FINWISE_REDIS_CONNECTION_STRING` is no longer needed when using Entra auth.
> The `ConnectionString` and `Host` paths are mutually exclusive.

#### Docker impact
- **No Dockerfile changes** — TLS certs already trusted (see Future Step 1)
- **Environment variables**: The Service Principal env vars (`FINWISE_AZURE_TENANT_ID`,
  `FINWISE_AZURE_CLIENT_ID`, `FINWISE_AZURE_CLIENT_SECRET`) are already passed through
  in `docker-compose.finwise.yml` for the StockAgent — they'll be
  picked up by `DefaultAzureCredential` automatically

#### Testing checklist
1. ✅ Service Principal has **Data Contributor** role on the Redis instance
2. ✅ `clientProtocol` is set to `Encrypted` (Entra requires TLS)
3. ✅ Firewall allows your client IP
4. ✅ Environment variables set for tenant, client ID, and client secret
5. ✅ App connects and can read/write session data
6. ✅ Token refresh works (leave app running >1 hour to verify)

#### Summary of all changes for Entra Auth

| Component | Change |
|-----------|--------|
| `Directory.Packages.props` | Add `Microsoft.Azure.StackExchangeRedis` v3.3.1 |
| Project `.csproj` | Add `<PackageReference>` |
| `RedisOptions.cs` | Add `Host` and `UseEntraAuth` properties |
| `AgentSessionStoreFactory.cs` | Dual-path: connection string vs Entra token auth |
| `AgentSessionStoreFactory.cs` | New env var overrides for `FINWISE_REDIS_HOST`, `FINWISE_REDIS_USE_ENTRA_AUTH` |
| `.env.azure.template` | Add Entra auth example (commented out) |
| `.env.azure` | Switch to Entra auth values |

---

## Research Confidence: 92%

**Sources**: Microsoft Learn (Azure Managed Redis docs), StackExchange.Redis docs, NuGet package metadata,
ARM template reference (`Microsoft.Cache/redisEnterprise/databases`), codebase analysis.

**Known gaps**:
- The `Microsoft.Azure.StackExchangeRedis` GitHub README still shows port 6380 in examples
  (not yet updated for Azure Managed Redis), but the official Azure docs confirm port 10000.
- `clientProtocol` only supports `'Encrypted'` or `'Plaintext'` — there is no dual-protocol option
  (confirmed via ARM template schema at `2025-08-01-preview` API version).

---

## Appendix A: Azure Cache for Redis (Legacy) — Migration Reference

> This section is for reference only. FinWise was never deployed on Azure Cache for Redis.
> The `.env.azure.template` previously contained a placeholder in this legacy format, which must be updated.

Azure Cache for Redis is being retired (Sep 30, 2028). Azure Managed Redis is its successor.

| Aspect | Azure Cache for Redis (legacy) | Azure Managed Redis (new) |
|--------|-------------------------------|--------------------------|
| **Hostname** | `<name>.redis.cache.windows.net` | `<name>.<region>.redis.azure.net` |
| **TLS port** | 6380 | **10000** |
| **Non-TLS port** | 6379 (separate port, can coexist with TLS) | **10000** (same port, either/or protocol) |
| **Default auth** | Access keys enabled | Entra ID enabled, access keys disabled |
| **Engine** | Redis OSS | Redis Enterprise 7.4 |
| **Smallest dev tier** | Basic C0 (~$16/mo) | Balanced B0 (~$7/mo) |
| **Dual protocol support** | Yes (ports 6379 + 6380 simultaneously) | No (single port, one protocol) |

**Legacy connection string format** (do NOT use):
```
your-redis.redis.cache.windows.net:6380,password=your-key,ssl=True,abortConnect=False
```

**Current connection string format** (Azure Managed Redis):
```
finwise-managed-redis-poc.eastus.redis.azure.net:10000,password=your-key,ssl=False,abortConnect=False
```
