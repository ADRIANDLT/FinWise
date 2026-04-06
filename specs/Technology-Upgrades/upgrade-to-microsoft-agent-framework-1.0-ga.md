# Upgrade: Microsoft Agent Framework RC4 → 1.0 GA

## Context

FinWise is a multi-agent investment assistant that uses the **Microsoft Agent Framework (MAF)** to orchestrate four AI agents (Orchestrator, Profile, Advisor, Stock) via a hub-and-spoke pattern. The system runs as an MCP server on .NET 10, with the core orchestration library in `src/FinWise.MultiAgentWorkflow/`.

The **StockSpecializedAgent** is unique among the four agents: it is provisioned and hosted in **Azure AI Foundry** (not defined purely in code like the others). The factory class `StockSpecializedAgentFactory` resolves this remote agent by name and adapts it into the framework's `AIAgent` abstraction so the Orchestrator can hand off to it like any other agent.

This document captures the breaking changes encountered when upgrading from **MAF 1.0.0-rc4** to **MAF 1.0.0 GA** and the companion Azure SDK updates.

---

## Package Version Changes

All versions are centrally managed in `Directory.Packages.props`.

### Microsoft Agent Framework

| Package | RC4 Version | 1.0 GA Version | Notes |
|---------|-------------|-----------------|-------|
| `Microsoft.Agents.AI` | `1.0.0-rc4` | `1.0.0` | Core abstractions (`AIAgent`, etc.) |
| `Microsoft.Agents.AI.Abstractions` | `1.0.0-rc4` | `1.0.0` | |
| `Microsoft.Agents.AI.Hosting` | `1.0.0-preview.260311.1` | `1.0.0-preview.260402.1` | Still preview-only; no GA release exists |
| `Microsoft.Agents.AI.Workflows` | `1.0.0-rc4` | `1.0.0` | |
| `Microsoft.Agents.AI.AzureAI` | `1.0.0-rc4` | — | **Removed** (renamed/replaced) |
| `Microsoft.Agents.AI.Foundry` | — | `1.0.0` | **New** — replaces `Microsoft.Agents.AI.AzureAI` |

### Azure SDK

| Package | RC4-era Version | 1.0-era Version | Notes |
|---------|-----------------|------------------|-------|
| `Azure.AI.Projects` | `2.0.0-beta.1` | `2.0.0` | GA release; significant API surface changes |
| `Azure.AI.Projects.OpenAI` | `2.0.0-beta.1` | `2.0.0-beta.1` | No GA release yet; stays on beta |
| `Azure.Identity` | `1.17.1` | `1.20.0` | Required by `Azure.AI.Projects` 2.0.0 |

### Microsoft.Extensions.AI

| Package | RC4-era Version | 1.0-era Version |
|---------|-----------------|------------------|
| `Microsoft.Extensions.AI` | `10.3.0` | `10.4.1` |
| `Microsoft.Extensions.AI.Abstractions` | `10.3.0` | `10.4.1` |
| `Microsoft.Extensions.AI.OpenAI` | `10.3.0` | `10.4.1` |

### Transitive Dependencies

| Package | RC4-era Version | 1.0-era Version | Notes |
|---------|-----------------|------------------|-------|
| `Azure.Identity` | `1.17.1` | `1.20.0` | Required at runtime by `Azure.AI.Projects` 2.0.0 GA |
| `Microsoft.Extensions.Options` | `10.0.2` | `10.0.3` | Transitive alignment with M.E.AI 10.4.1 |

---

## Breaking Change 1: Bridge Package Renamed

**`Microsoft.Agents.AI.AzureAI`** → **`Microsoft.Agents.AI.Foundry`**

The bridge package that connects the Azure AI Foundry SDK to the Microsoft Agent Framework was renamed to align with Azure AI Foundry branding.

### What changed

In `FinWise.MultiAgentWorkflow.csproj`:

```xml
<!-- RC4 -->
<PackageReference Include="Microsoft.Agents.AI.AzureAI" />

<!-- 1.0 GA -->
<PackageReference Include="Microsoft.Agents.AI.Foundry" />
```

The `using` directive changed accordingly:

```csharp
// RC4 — this namespace no longer exists
// (the extension methods lived directly on AIProjectClient)

// 1.0 GA
using Microsoft.Agents.AI.Foundry;
```

---

## Breaking Change 2: Agent Resolution API — One-Step → Two-Step Pattern

This is the most significant behavioral change and affects the `StockSpecializedAgentFactory`.

### RC4 pattern: single convenience method

```csharp
using System.ClientModel;
using Azure.AI.Projects;
using Microsoft.Agents.AI;

// One call — returns AIAgent directly
AIAgent agent = await _projectClient.GetAIAgentAsync(_agentName);
```

`GetAIAgentAsync()` was a high-level extension method on `AIProjectClient` (provided by the bridge package). It internally fetched the agent definition from Foundry **and** wrapped it into an `AIAgent` — all in one call.

### 1.0 GA pattern: explicit two-step

```csharp
using Azure;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;

// Step 1: Fetch agent record from Azure AI Foundry (Azure SDK layer)
ProjectsAgentRecord agentRecord = await _projectClient
    .AgentAdministrationClient
    .GetAgentAsync(_agentName);

// Step 2: Adapt into Agent Framework type (bridge layer)
FoundryAgent agent = _projectClient.AsAIAgent(agentRecord);
```

### Why the two-step split?

The two steps cross the boundary between two independently-versioned SDK layers:

| Step | SDK Owner | Type Returned | Responsibility |
|------|-----------|---------------|----------------|
| `GetAgentAsync()` | Azure SDK (`Azure.AI.Projects`) | `ProjectsAgentRecord` | Calls the Foundry REST API, returns a raw data record |
| `AsAIAgent()` | Bridge (`Microsoft.Agents.AI.Foundry`) | `FoundryAgent` → `AIAgent` | Adapts the record into the Agent Framework's type system |

The Azure SDK team does not reference `Microsoft.Agents.AI`. The Agent Framework team does not embed Azure-specific REST calls. The bridge package (`Microsoft.Agents.AI.Foundry`) depends on *both* and provides the `AsAIAgent()` extension method as the adapter.

In RC4, the bridge offered a convenience method (`GetAIAgentAsync`) that hid both steps. In 1.0 GA, that convenience method was removed, making the SDK boundary explicit.

### Additional sub-client change

The Azure SDK also restructured how agent operations are accessed:

```csharp
// RC4 — agent operations directly on AIProjectClient
_projectClient.GetAIAgentAsync(name);
_projectClient.Agents.GetAgents();

// 1.0 GA — dedicated AgentAdministrationClient sub-client
_projectClient.AgentAdministrationClient.GetAgentAsync(name);
_projectClient.AgentAdministrationClient.GetAgents();
```

---

## Breaking Change 3: Exception Type for HTTP Errors

The exception type thrown by `Azure.AI.Projects` for HTTP errors (e.g., 404 Not Found) changed between the beta and GA releases.

```csharp
// RC4 (Azure.AI.Projects 2.0.0-beta.1)
using Azure;
catch (RequestFailedException ex)  // from Azure namespace (Azure.Core)

// 1.0 GA (Azure.AI.Projects 2.0.0)
using System.ClientModel;
catch (ClientResultException ex)  // from System.ClientModel
```

`Azure.AI.Projects` 2.0.0 GA now throws `ClientResultException` (from `System.ClientModel`, the lower-level client library) instead of `RequestFailedException` (from `Azure.Core`) for HTTP errors like 404.

In FinWise, the `StockSpecializedAgentFactory.CreateAgentAsync()` catch block was updated accordingly. The wrapping behavior is preserved — any agent-not-found error from Foundry is still surfaced as `InvalidOperationException` to callers.

---

## Breaking Change 4: Experimental Diagnostic IDs

Handoff orchestrations and the Foundry agent provider are marked `[Experimental]` in 1.0 GA. With `TreatWarningsAsErrors` enabled, the build fails unless these diagnostics are suppressed.

The experimental IDs are **not** the Semantic Kernel IDs (`SKEXP0001`) — they are MAF-specific:

| Diagnostic ID | Source | Meaning |
|---------------|--------|---------|
| `MAAIW001` | `Microsoft.Agents.AI.Workflows` | Handoff orchestration APIs (e.g., `AgentWorkflowBuilder`) |
| `OPENAI001` | `Microsoft.Agents.AI.Foundry` | Foundry agent provider (`AsAIAgent()`, `FoundryAgent`) |

Suppressed globally in `Directory.Build.props`:

```xml
<NoWarn>$(NoWarn);MAAIW001;OPENAI001</NoWarn>
```

---

## Breaking Change 5: OpenAI Client Accessor

In integration tests that use the Foundry's Responses client:

```csharp
// RC4
using Azure.AI.Projects.OpenAI;
ProjectResponsesClient responsesClient = projectClient.OpenAI
    .GetProjectResponsesClientForAgent(defaultAgent: agentName);

// 1.0 GA
using Azure.AI.Extensions.OpenAI;
ProjectResponsesClient responsesClient = projectClient.ProjectOpenAIClient
    .GetProjectResponsesClientForAgent(defaultAgent: agentName);
```

The namespace changed from `Azure.AI.Projects.OpenAI` → `Azure.AI.Extensions.OpenAI`, and the accessor from `.OpenAI` → `.ProjectOpenAIClient`.

---

## Summary of File Changes

| File | Change |
|------|--------|
| `Directory.Packages.props` | Version bumps for all MAF, Azure SDK, Extensions.AI, and transitive deps |
| `Directory.Build.props` | Added `MAAIW001;OPENAI001` experimental diagnostic suppression |
| `src/FinWise.MultiAgentWorkflow/FinWise.MultiAgentWorkflow.csproj` | `Microsoft.Agents.AI.AzureAI` → `Microsoft.Agents.AI.Foundry` |
| `src/FinWise.MultiAgentWorkflow/Agents/StockSpecializedAgent/StockSpecializedAgentFactory.cs` | Two-step agent resolution, `ClientResultException` catch, `FoundryAgent` intermediate |
| `src/FinWise.MultiAgentWorkflow/AGENTS.md` | Updated technology description (rc/preview → 1.0 GA) |
| `tests/FinWise.MultiAgentWorkflow.UnitTests/StockSpecializedAgentFactoryTests.cs` | Updated comment referencing the new API method name |
| `tests/FinWise.StockAgent.IntegrationTests/StockSpecializedAgentIntegrationTests.cs` | `AgentAdministrationClient`, `ProjectOpenAIClient`, new namespace |

---

## Verification

| Suite | Tests | Result |
|-------|-------|--------|
| Build (`dotnet build FinWise.slnx`) | 10 projects | ✅ 0 warnings, 0 errors |
| Unit tests (MultiAgentWorkflow + McpServer) | 89 | ✅ All passed |
| CosmosDB integration | 10 | ✅ All passed |
| Redis integration | 12 | ✅ All passed |
| MCP Server integration | 8 | ✅ All passed |
| MCP Server container | 9 | ✅ All passed |
| StockAgent integration | 4 | ✅ All passed |

---

## Open Items

- **`Microsoft.Agents.AI.Hosting`** remains on preview (`1.0.0-preview.260402.1`). No GA version available as of 2026-04-04.
- **`Azure.AI.Projects.OpenAI`** remains on `2.0.0-beta.1`. No GA companion to `Azure.AI.Projects` 2.0.0 exists yet.
