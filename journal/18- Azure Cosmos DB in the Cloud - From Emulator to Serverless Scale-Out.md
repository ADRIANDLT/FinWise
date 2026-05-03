# 18- Azure Cosmos DB in the Cloud: From Emulator to Serverless Scale-Out

_April 17, 2026_
_FinWise breaks free from the local CosmosDB emulator, connects to Azure Cosmos DB Serverless, and proves stateless scale-out with 5 container replicas — all tests green._

---

## Setting the Scene

After the [Azure Redis Odyssey](17-%20The%20Azure%20Redis%20Odyssey.md), FinWise had its session store running in Azure Managed Redis. But user profiles were still tied to the CosmosDB Docker emulator — a 3GB container with self-signed TLS certificates and a well-known master key. The next step was clear: **move CosmosDB to Azure cloud** and prove the entire system works at scale.

The goal wasn't just "connect to Azure Cosmos DB." It was bigger: prove that FinWise MCP Server containers are truly stateless — that you can run 5 replicas behind a load balancer and any replica can serve any request, because all state lives in external stores.

---

## Act 1: The ARM Template Review

The journey started with an ARM template exported from the Azure Portal's "Download a template for automation" button. The configuration was sensible for dev/testing:

- **Serverless** capacity mode — pay-per-request, no throughput provisioning
- **East US 2** — single region, matching the existing Redis and Container Apps
- **Key-based auth** — matching FinWise's existing `CosmosDbOptions.Key` approach
- **All networks** — accessible from local dev machines and Azure

The template had a few Azure Portal export quirks — `dependsOn` nested inside `properties`, unused parameters, old schema version — but these are harmless for deployment.

---

## Act 2: The Throughput Bug

With the Azure account created, it was time to check code compatibility. A full scan of the codebase revealed something critical:

```csharp
// CosmosDbUserProfileStore.cs — line 52
var database = await _client.CreateDatabaseIfNotExistsAsync(
    id: _options.DatabaseName,
    throughput: 400  // ← FAILS on Serverless!
);
```

**Serverless Cosmos DB accounts don't support provisioned throughput.** This call would throw a `CosmosException` at runtime. The fix was surgical — remove the `throughput` parameter:

```csharp
var database = await _client.CreateDatabaseIfNotExistsAsync(
    id: _options.DatabaseName
);
```

This works for both Serverless (Azure) and the emulator (Docker). The emulator was started, and all 13 CosmosDB integration tests passed — confirming backward compatibility.

---

## Act 3: Making Tests Target-Agnostic

The CosmosDB integration tests had a bigger problem: they were **hardcoded to the emulator**.

```csharp
private const string EmulatorEndpoint = "https://localhost:8081/";
private const string EmulatorKey = "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+...";
```

Two test files — `CosmosDbUserProfileStoreIntegrationTests.cs` and `CosmosDbPersistenceIntegrationTests.cs` — had emulator endpoints, emulator keys, and an availability check that pinged `/_explorer/emulator.pem`. None of this would work against Azure cloud.

The rubber-duck agent was consulted and flagged several blind spots:

1. **Config should be treated as a bundle** — if endpoint is set but key isn't, fail fast with a clear error
2. **Use `ReadAccountAsync()` for the availability probe** — not just an HTTP ping. This validates both connectivity AND authentication
3. **Default `AllowInsecureTls` based on endpoint** — `true` for localhost, `false` for everything else

The fix replaced hardcoded constants with environment-variable-driven configuration:

```csharp
private static readonly string Endpoint =
    Environment.GetEnvironmentVariable("FINWISE_COSMOSDB_ENDPOINT") is { Length: > 0 } ep
        ? ep : DefaultEmulatorEndpoint;

private static readonly bool AllowInsecureTls =
    Environment.GetEnvironmentVariable("FINWISE_COSMOSDB_ALLOW_INSECURE_TLS") is { Length: > 0 } val
        ? string.Equals(val, "true", StringComparison.OrdinalIgnoreCase)
        : Endpoint.Contains("localhost") || Endpoint.Contains("127.0.0.1");
```

Same pattern as the Redis tests — env vars with emulator fallback. Consistent experience across all infrastructure tests.

---

## Act 4: First Green Against Azure Cloud

With the Azure Cosmos DB account provisioned and the `.env.azure` file updated with the real endpoint and primary key, the moment of truth arrived:

```
--- Running CosmosDB Integration Tests against Azure cloud ---
Test summary: total: 13, failed: 0, succeeded: 13, skipped: 0, duration: 29.0s
```

**All 13 tests passed.** The database (`FinWise`) and container (`UserProfiles`) were auto-created by the code on first request — no manual Azure Portal setup needed.

Then the MCP E2E tests, with the server pointing at both Azure Cosmos DB and Azure Redis:

```
Test summary: total: 8, failed: 0, succeeded: 8, skipped: 0, duration: 101.4s
```

**21 tests green across both Azure data stores.**

---

## Act 5: Hardening the E2E Tests

The E2E tests had a recurring problem: **LLM non-determinism**. Three tests — `AggressiveShortTerm`, `CompleteUserJourney`, and `TwoSessions_SameEmail` — had rigid intermediate assertions on LLM wording:

```csharp
Assert.Contains("email", response1.ToLowerInvariant());  // Fragile!
Assert.Contains("risk", response2.ToLowerInvariant());    // Fragile!
```

When the LLM rephrased its responses, these assertions broke — even though the actual functionality was fine. The fix was a resilience pattern: **drive through profile setup steps without asserting on intermediate wording**, then assert only on what matters:

```csharp
// Drive through steps, check for PROFILE_READY at each
var r2 = await CallFinancialAdviceTool(testEmail);
string? profileReadyResponse = r2.Contains("PROFILE_READY:") ? r2 : null;

if (profileReadyResponse == null)
{
    var r3 = await CallFinancialAdviceTool("Moderate");
    profileReadyResponse = r3.Contains("PROFILE_READY:") ? r3 : null;
}
// ... continue driving, then assert on final outcome
Assert.Contains("PROFILE_READY:", profileReadyResponse);
Assert.Contains(testEmail, profileReadyResponse);
```

A test quality analysis across all 112 tests confirmed the E2E tests were the only fragile area. After the fix, all 8 E2E tests passed consistently against Azure.

---

## Act 6: The Scale-Out Proof

The final milestone: **5 replicas of the FinWise MCP Server** in Azure Container Apps, all hitting the same Azure Cosmos DB and Azure Redis.

```
Passed!  - Failed: 0, Passed: 8, Skipped: 0, Total: 8, Duration: 1m 40s
```

**All 8 E2E tests passed with 5 replicas.** Sessions created on one replica were correctly retrieved by another. Profiles persisted by one container instance were found by a different instance. No session affinity needed — because there IS no session affinity. The containers are truly stateless: all state lives in Redis (sessions) and Cosmos DB (profiles).

This was also validated manually through the VS Code MCP client, where consecutive requests were visibly handled by different container instances.

---

## What Changed

| File | Change |
|------|--------|
| `CosmosDbUserProfileStore.cs` | Removed `throughput: 400` from `CreateDatabaseIfNotExistsAsync` — Serverless compatibility |
| `CosmosDbUserProfileStoreIntegrationTests.cs` | Env-var-driven config, `ReadAccountAsync()` probe, resilient skip logic |
| `CosmosDbPersistenceIntegrationTests.cs` | Same env-var pattern, renamed `ReadProfile_SurvivesRestart` |
| `EndToEndMcpTests.cs` | Resilient profile setup in 3 tests — no rigid intermediate LLM assertions |
| `.env.azure.template` | Uncommented Cosmos DB endpoint/key for Azure |
| `.gitignore` | Added `test.azure.runsettings` (contains secrets) |
| `.vscode/mcp.json` | Commented out Azure MCP server to avoid routing conflicts |

---

## What We Learned

### About Cosmos DB Serverless

- **No throughput provisioning** — `CreateDatabaseIfNotExistsAsync` must be called without a `throughput` parameter, or it throws. Omitting throughput works for both Serverless and the emulator.
- **Auto-creates everything** — database and container are created on first request via `CreateDatabaseIfNotExistsAsync` / `CreateContainerIfNotExistsAsync`. No Azure Portal setup needed beyond the account.

### About Test Design for AI Systems

- **Never assert on intermediate LLM wording** — LLMs rephrase constantly. Assert on structural markers (`PROFILE_READY:`) and final outcomes, not on whether the LLM said "email" vs "e-mail address."
- **`ReadAccountAsync()` > HTTP ping** — for availability probes, an authenticated SDK call validates both connectivity AND credentials in one shot.

### About Scale-Out Architecture

- **Stateless containers + external stores = horizontal scaling** — no session affinity, no sticky sessions, no coordination between replicas. Redis handles sessions, Cosmos DB handles profiles. Each container is disposable.
- **The .env layering pattern works well** — `.env` for base config, `.env.azure` for cloud overrides. Docker Compose's `--env-file .env --env-file .env.azure` layering is clean and intuitive.

---

## What's Next

- **Version bump to 0.6.0** — incorporating all Azure database support changes
- **CI/CD pipeline** — automate the test matrix (emulator vs Azure cloud) in GitHub Actions
- **Entra ID authentication** — replace key-based auth with managed identity for production security
- **Cost monitoring** — track Cosmos DB Serverless RU consumption and Redis usage in Azure

---

_Written: April 18, 2026_
