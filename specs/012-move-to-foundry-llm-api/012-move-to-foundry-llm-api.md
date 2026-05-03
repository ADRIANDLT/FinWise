# Feature: Migrate LLM Access to Azure AI Foundry Project Responses API

> **Created with:** feature-spec skill (`.github/skills/feature-spec/`)
>
> **Location:** `/specs/012-move-to-foundry-llm-api/012-move-to-foundry-llm-api.md`
>
> **Research date:** April 19, 2026 (Revision 8)
>
> **Research confidence:** 100% for OpenAI-family deployments (GPT-4o, GPT-4.1, o-series) and for other models on Microsoft's Responses-supported list (MAI-DS-R1, Grok, Llama 3.3 / Llama-4-Maverick, DeepSeek V3/R1, gpt-oss-120b). API chain verified against official source on GitHub for `Microsoft.Agents.AI.Foundry` 1.1.0, `Azure.AI.Extensions.OpenAI` 2.0.0, `OpenAI` .NET SDK, and `Microsoft.Extensions.AI.OpenAI` 10.x. See the **Model Compatibility Matrix** below for the non-OpenAI edge cases (Mistral / Phi / Cohere use a Chat Completions variant; Claude is out-of-scope for this factory).

---

## Functional Specification

### Problem Statement

The current LLM integration in FinWise is locked to the legacy **Azure OpenAI service** client path. The composition root (`AzureOpenAIChatClientFactory.cs`) instantiates `AzureOpenAIClient` from the `Azure.AI.OpenAI` package and authenticates with an **API key** (`AzureKeyCredential`):

```csharp
// src/FinWise.McpServer/Infrastructure/AzureOpenAI/AzureOpenAIChatClientFactory.cs (current)
var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
var chatClient  = azureClient.GetChatClient(deploymentName).AsIChatClient();
```

This couples FinWise to:

1. The **older Azure OpenAI resource model** (`https://<resource>.openai.azure.com`) instead of the new Azure AI Foundry project model.
2. **API-key authentication**, instead of the service principal already used by `StockAgentFactory`.
3. The **OpenAI Chat Completions** API on the client side — not the Foundry SDK's preferred Responses-backed path for agent scenarios.

The downstream agents (`OrchestratorAgent`, `AdvisorAgent`, `UserProfileAgent`) are already provider-agnostic: they consume `IChatClient` from `Microsoft.Extensions.AI`, which is then passed into Microsoft Agent Framework's `ChatClientAgent`. The provider lock-in exists **only** in the McpServer composition root, where the `IChatClient` is created.

> **Note on `Azure.AI.Inference`:** This package is **not** referenced anywhere in the FinWise repo (verified by grep). It is therefore not part of the migration discussion — there is nothing to remove and nothing to consider as an alternative.

### Proposed Solution

Replace `AzureOpenAIChatClientFactory` with a new **`AzureAIFoundryChatClientFactory`** that targets the **Azure AI Foundry project endpoint** and produces an `IChatClient` backed by the **Responses API** via `AIProjectClient`:

```csharp
// New factory — follows the exact pattern used internally by Microsoft.Agents.AI.Foundry 1.1.0
var credential    = new ClientSecretCredential(tenantId, clientId, clientSecret);
var projectClient = new AIProjectClient(new Uri(projectEndpoint), credential);

// AsIChatClient(this ResponsesClient, string?) is decorated with
// [Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)], which evaluates to "OPENAI001"
// (verified in dotnet/extensions src/Shared/DiagnosticIds/DiagnosticIds.cs). Microsoft's own
// Microsoft.Agents.AI.Foundry 1.1.0 applies the same single suppression at the same call site.
#pragma warning disable OPENAI001
IChatClient chatClient = projectClient
    .GetProjectOpenAIClient()            // extension from Azure.AI.Extensions.OpenAI
    .GetResponsesClient()                // inherited from OpenAI.OpenAIClient → OpenAI.ResponsesClient
    .AsIChatClient(modelDeploymentName); // bridge from Microsoft.Extensions.AI.OpenAI (namespace Microsoft.Extensions.AI)
#pragma warning restore OPENAI001
```

Why this shape: Foundry-native (aligns with `StockAgentFactory`), preserves the `IChatClient` seam so `FinWise.MultiAgentWorkflow` needs **zero changes**, reuses shared service-principal credentials (no API keys), and uses the exact composition that `Microsoft.Agents.AI.Foundry` 1.1.0 uses internally. The migration is isolated to the McpServer project; a direct `/openai/v1/` `OpenAI.Chat.ChatClient` path is a documented fallback, not the primary design.

### Functional Requirements

- [x] FR-1 — FinWise must be able to connect to a model deployment in Azure AI Foundry via the **Foundry project endpoint** and a **Responses-backed `IChatClient`**
- [x] FR-2 — All existing agent capabilities (Orchestrator, Advisor, UserProfile) must continue to work identically after migration, with no changes required to `ChatClientAgent` construction in `FinWise.MultiAgentWorkflow`
- [x] FR-3 — Environment variable configuration must use `FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT` and `FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME` to clearly indicate the Azure AI Foundry project context
- [x] FR-4 — The deployment name must be required — it specifies which model deployment to invoke
- [x] FR-5 — Docker-based deployment must work with the new environment variables
- [x] FR-6 — Service principal authentication (`ClientSecretCredential`) must be reused from the shared `FINWISE_AZURE_*` env vars, matching the existing `StockAgentFactory` pattern
- [x] FR-7 — The implementation must remain compatible with `Microsoft.Extensions.AI.IChatClient` and Microsoft Agent Framework `ChatClientAgent`

### User Scenarios

1. **Scenario: Developer switches from GPT-4o to another Foundry deployment**
   - *Before*: The app is tied to the older Azure OpenAI-specific factory implementation.
   - *After*: Developer deploys a different model in Azure AI Foundry, updates `FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME` in `.env`, and restarts. The workflow stays unchanged because the downstream contract remains `IChatClient`.

2. **Scenario: Existing GPT deployment continues working**
   - *Before*: Works via `AzureOpenAIClient`.
   - *After*: The same deployment works via `AIProjectClient` + Responses while remaining invisible to the workflow layer and end user.

3. **Scenario: Docker Compose deployment**
   - *Before*: Uses `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_DEPLOYMENT_NAME`, `AZURE_OPENAI_API_KEY`.
   - *After*: Uses `FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT` and `FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME`. Authentication uses the shared `FINWISE_AZURE_TENANT_ID`, `FINWISE_AZURE_CLIENT_ID`, and `FINWISE_AZURE_CLIENT_SECRET`. All other infrastructure (Redis, CosmosDB) remains unchanged.

### Out of Scope

- **StockAgent** — Uses its own `AIProjectClient` + `StockSpecializedAgentFactory` path in `Infrastructure/AzureAIFoundry/`, pointing at a **separate** Azure AI Foundry project (`STOCK_AGENT_PROJECT_ENDPOINT`). Not affected by this migration.
- **StockAgent migration to the FinWise Foundry project** — Eventually the Stock Agent may be migrated to the same Foundry project as the LLM, but that is a separate future initiative. Both projects coexist for now with different env vars.
- **FinWise.MultiAgentWorkflow changes** — Library already consumes `IChatClient`; no changes needed.
- **Multiple simultaneous model providers** — This migration supports one configured deployment at a time. Dynamic per-agent model routing is out of scope.
- **Direct `/openai/v1` fallback implementation** — The plain `OpenAI.Chat.ChatClient` approach remains a valid alternative, but it is not the primary design recommended by this spec.
- **`Microsoft.Agents.AI.Foundry` upgrade** — v1.0.0 → v1.1.0 upgrade is a separate task.

### Open Questions (Functional)

- [x] ~~OQ-F1 — Should we support a fallback to the old Azure OpenAI path (e.g., via a feature flag or secondary env vars)? Or is this a clean cutover?~~ **RESOLVED (revised)**: Clean cutover for the **default wiring**. The direct `/openai/v1/` `ChatClient` route remains a documented secondary compatibility option, but it is not the primary/default implementation path.
- [ ] OQ-F2 — Should the model ID be configurable per-agent in the future, or is a single model for all agents sufficient for now?

---

## Technical Specification

### Architecture Impact

**Scope of change: Minimal — McpServer composition root only.**

```
┌──────────────────────────────────────────────────────────────┐
│  FinWise.MultiAgentWorkflow (NO CHANGES)                    │
│  Orchestrator / Advisor / UserProfile                       │
│                consume IChatClient                          │
└───────────────────────────┬──────────────────────────────────┘
                            │
                            ▼
┌──────────────────────────────────────────────────────────────┐
│  FinWise.McpServer (CHANGES HERE)                           │
│                                                              │
│  AzureAIFoundryChatClientFactory (NEW)                      │
│    - reads FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT        │
│    - reads FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME     │
│    - reuses shared FINWISE_AZURE_* credentials              │
│    - new AIProjectClient(projectEndpoint, credential)       │
│    - projectClient                                           │
│        .GetProjectOpenAIClient()  // ext. method            │
│        .GetResponsesClient()                                │
│        .AsIChatClient(modelDeployment)                      │
│    -> IChatClient                                           │
│                                                              │
│  StockAgentFactory (UNCHANGED)                              │
│    - AIProjectClient(stockProjectEndpoint, credential)      │
│    -> StockSpecializedAgentFactory -> AIAgent               │
│                                                              │
│  Program.cs                                                 │
│    - wires new Foundry factory instead of old Azure OpenAI  │
└──────────────────────────────────────────────────────────────┘
```

**Files affected**: ~6 files
**Potential ripple effects**: Unit tests mocking `IChatClient` are unaffected. Integration tests that call real Azure endpoints will need updated env vars.
**Risk level**: Low — the `IChatClient` contract is preserved, the auth pattern (`ClientSecretCredential` + `AIProjectClient`) is already proven in `StockAgentFactory`, and the exact composition (`GetProjectOpenAIClient().GetResponsesClient().AsIChatClient(model)`) is the same one used internally by Microsoft's own `Microsoft.Agents.AI.Foundry` 1.1.0 package.

### Migration: Before vs After

This is the heart of the migration. All changes are concentrated in one file (the factory) plus its single call site in `Program.cs`. Everything else — the workflow library, the agents, the MCP tools, the session storage — is untouched.

| Aspect | **BEFORE** (current) | **AFTER** (this spec) |
|--------|----------------------|------------------------|
| **Factory file** | `Infrastructure/AzureOpenAI/AzureOpenAIChatClientFactory.cs` | `Infrastructure/AzureAIFoundry/AzureAIFoundryChatClientFactory.cs` |
| **Factory class** | `AzureOpenAIChatClientFactory` (static) | `AzureAIFoundryChatClientFactory` (static) — sits next to `StockAgentFactory` |
| **`using` namespaces** | `Azure;` `Azure.AI.OpenAI;` `Microsoft.Extensions.AI;` | `Azure.AI.Projects;` `Azure.AI.Extensions.OpenAI;` `Azure.Identity;` `Microsoft.Extensions.AI;` `OpenAI.Responses;` |
| **Auth method** | API key (`AzureKeyCredential`) | Service principal (`ClientSecretCredential`) — same as `StockAgentFactory` |
| **Auth env vars** | `AZURE_OPENAI_API_KEY` | `FINWISE_AZURE_TENANT_ID` + `FINWISE_AZURE_CLIENT_ID` + `FINWISE_AZURE_CLIENT_SECRET` (already exist for StockAgent — shared) |
| **Endpoint env var** | `AZURE_OPENAI_ENDPOINT` | `FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT` |
| **Endpoint format** | `https://<resource>.openai.azure.com[/openai/v1]` (Azure OpenAI service) | `https://<resource>.services.ai.azure.com/api/projects/<project>` (Foundry **project** endpoint) |
| **Deployment env var** | `AZURE_OPENAI_DEPLOYMENT_NAME` | `FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME` |
| **SDK client type** | `AzureOpenAIClient` (from `Azure.AI.OpenAI`) | `AIProjectClient` (from `Azure.AI.Projects`) |
| **Wire-protocol API** | OpenAI **Chat Completions** (`/chat/completions`) | OpenAI **Responses** (`/responses`) — Microsoft's recommended default for new agent work |
| **`IChatClient` bridge** | `azureClient.GetChatClient(deployment).AsIChatClient()` (no model arg, uses deployment as model) | `projectClient.GetProjectOpenAIClient().GetResponsesClient().AsIChatClient(modelDeployment)` |
| **Compiler suppressions** | None | `#pragma warning disable OPENAI001` around the `GetResponsesClient().AsIChatClient(...)` call site (single experimental diagnostic — `AIOpenAIResponses` evaluates to `OPENAI001` per `dotnet/extensions` `DiagnosticIds.cs`; matches Microsoft.Agents.AI.Foundry 1.1.0) |
| **NuGet packages added** | — | `Azure.AI.Extensions.OpenAI` 2.0.0 (GA) — provides `GetProjectOpenAIClient()` extension |
| **NuGet packages removed** | — | `Azure.AI.OpenAI` 2.1.0 (after `AzureOpenAIClient` references are gone) and `Azure.AI.Projects.OpenAI` 2.0.0-beta.1 **only in projects where it is no longer referenced** (do not treat it as repo-wide unused; it is still referenced in `FinWise.MultiAgentWorkflow` and `FinWise.StockAgent.IntegrationTests` in the current repo state) |
| **Call site in `Program.cs`** | `var chatClient = AzureOpenAIChatClientFactory.CreateChatClient();` | `var chatClient = AzureAIFoundryChatClientFactory.CreateChatClient();` |
| **`FinWise.MultiAgentWorkflow`** | Receives `IChatClient` → builds `ChatClientAgent` | **Unchanged** — same contract |
| **Failure mode if env vars missing** | `throw InvalidOperationException` per missing var | Same — `throw InvalidOperationException` per missing var (LLM is required, not optional) |

**Future evolution (out of scope):** Once `Microsoft.Agents.AI.Foundry` is upgraded to 1.1.0, McpServer could pass `AIProjectClient` + model name into `FinWise.MultiAgentWorkflow` and let each agent factory call `aiProjectClient.AsAIAgent(model, instructions, ...)` directly — eliminating the manual `IChatClient` plumbing and the experimental pragmas. That refactor reshapes the workflow library's public surface and is tracked as a separate follow-up.

### Research Summary

#### Primary Solution: `AIProjectClient` + Responses-backed `IChatClient`

The latest Foundry SDK guidance distinguishes between two valid access patterns:

- **Foundry SDK** — use for **agents, evaluations, and Foundry-specific features**
- **OpenAI SDK** — use when **maximum OpenAI API compatibility** is required

For FinWise, the Foundry SDK path is the better primary choice because the app already:

- uses **Microsoft Agent Framework**,
- standardizes on **`IChatClient`**,
- already has a Foundry-oriented integration for `StockAgentFactory`, and
- wants to align with **`Azure.AI.Projects` / `AIProjectClient`** rather than a direct raw OpenAI endpoint only.

The current best-fit package set for this migration is:

| Package | Version | Status | Role | Action |
|---------|---------|--------|------|--------|
| `Azure.AI.Projects` | `2.0.0` | **GA** | `AIProjectClient` (Foundry project entry point) | Already in repo — keep |
| `Azure.AI.Extensions.OpenAI` | `2.0.0` | **GA** | Provides `GetProjectOpenAIClient()` extension on `AIProjectClient` | **ADD** to `Directory.Packages.props` and `FinWise.McpServer.csproj` |
| `Microsoft.Extensions.AI.OpenAI` | `10.4.1` (repo) / `10.5.0` (latest GA) | **GA** | Provides `.AsIChatClient(this ResponsesClient, string?)` bridge (namespace `Microsoft.Extensions.AI`) | Already in repo — keep |
| `Microsoft.Extensions.AI` | `10.4.1` | **GA** | `IChatClient` abstraction | Already in repo — keep |
| `Azure.Identity` | `1.20.0` | **GA** | `ClientSecretCredential` | Already in repo — keep |
| `Azure.AI.Projects.OpenAI` | `2.0.0-beta.1` | Preview, still referenced | Older overlapping helper surface | Remove from `Directory.Packages.props` only after updating `src/FinWise.MultiAgentWorkflow/FinWise.MultiAgentWorkflow.csproj` and `tests/FinWise.StockAgent.IntegrationTests/FinWise.StockAgent.IntegrationTests.csproj` to stop referencing it; `Azure.AI.Extensions.OpenAI` GA is the intended replacement |
| `Azure.AI.OpenAI` | `2.1.0` | GA | `AzureOpenAIClient` (Azure OpenAI service) | **REMOVE** from `FinWise.McpServer.csproj` after the old factory is deleted |
| `Microsoft.Agents.AI.Foundry` | `1.0.0` (repo) / `1.1.0` (latest GA) | **GA** | Provides `AIProjectClient.AsAIAgent(...)` one-liner — see "Future evolution" note above | Optional upgrade; not required for this migration |

> **Note on `Azure.AI.Inference`:** Confirmed by `grep` to be absent from the entire FinWise repo. It is not part of this migration in any direction.

#### Recommended Code Pattern

This is the canonical pattern, lifted directly from the official `Microsoft.Agents.AI.Foundry` 1.1.0 source (`AzureAIProjectChatClientExtensions.CreateResponsesChatClientAgent`). The only difference is that we stop at `IChatClient` instead of building a `ChatClientAgent`, because `FinWise.MultiAgentWorkflow` constructs the `ChatClientAgent` itself for each agent.

```csharp
using Azure.AI.Projects;
using Azure.AI.Extensions.OpenAI; // GetProjectOpenAIClient() extension
using Azure.Identity;
using Microsoft.Extensions.AI;    // IChatClient

var credential    = new ClientSecretCredential(tenantId, clientId, clientSecret);
var projectClient = new AIProjectClient(new Uri(projectEndpoint), credential);

#pragma warning disable OPENAI001 // Experimental: AsIChatClient(ResponsesClient, string?) — AIOpenAIResponses → "OPENAI001"
IChatClient chatClient = projectClient
    .GetProjectOpenAIClient()           // returns ProjectOpenAIClient (: OpenAI.OpenAIClient)
    .GetResponsesClient()               // returns OpenAI.ResponsesClient (inherited)
    .AsIChatClient(modelDeploymentName);
#pragma warning restore OPENAI001

return chatClient; // consumed by FinWise.MultiAgentWorkflow → ChatClientAgent (unchanged)
```

**Key points:**
- `projectEndpoint` is the **Foundry project endpoint** (`https://<resource>.services.ai.azure.com/api/projects/<project>`), not the Azure OpenAI service endpoint.
- The model deployment name is supplied **once**, to `.AsIChatClient(modelDeploymentName)`. `GetResponsesClient()` takes no arguments.
- `GetProjectOpenAIClient()` is an **extension method** on `AIProjectClient`. An equivalent `projectClient.ProjectOpenAIClient` property shorthand exists but we prefer the method form to match Microsoft's internal composition.
- The `OPENAI001` pragma is **expected and required**. The `AsIChatClient(this ResponsesClient, string?)` overload is decorated with `[Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)]`, and per `dotnet/extensions` `src/Shared/DiagnosticIds/DiagnosticIds.cs` the constant `AIOpenAIResponses` evaluates to **`"OPENAI001"`** (the OpenAI .NET SDK's diagnostic ID, deliberately reused so consumers don't need a second pragma). `MEAI001` is a different, unrelated diagnostic for other Microsoft.Extensions.AI experimental APIs (image generation, speech-to-text, etc.) — it is **not** emitted by the Responses bridge and does not need to be suppressed at this call site.
- Auth: service principal via `ClientSecretCredential` is primary (reuses shared `FINWISE_AZURE_*` vars). `DefaultAzureCredential` is a supported alternative. RBAC: service principal needs **Azure AI User** role on the Foundry resource.

#### Model Compatibility Matrix

The `.GetResponsesClient().AsIChatClient(modelDeployment)` chain works for every model on Microsoft's documented Responses-supported list. Models outside that list require a Chat Completions variant of the chain or fall out of scope entirely. FinWise defaults to an OpenAI deployment, so the primary path works unchanged; this matrix documents the edges so future readers don't assume "any deployment name works" unconditionally.

| Model family (April 2026) | Responses API | Chat Completions | Code chain needed |
|---|---|---|---|
| Azure OpenAI (GPT-4o, GPT-4.1, o-series) | ✅ | ✅ | Spec default (`GetResponsesClient().AsIChatClient(model)`) |
| Microsoft MAI-DS-R1, xAI Grok, Meta Llama 3.3 / Llama-4-Maverick, DeepSeek V3/R1, gpt-oss-120b | ✅ | ✅ | Spec default — change only `FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME` |
| Mistral, Phi-4 family, Cohere, Fireworks partners (Kimi / MiniMax / Qwen3 / GLM) | ❌ | ✅ (via `/openai/v1/`) | Swap to `GetChatClient(model).AsIChatClient()` on the same `ProjectOpenAIClient` — no new packages |
| Anthropic Claude (Opus 4.7 GA on Foundry) | ❌ | ❌ | **Out of scope.** Requires `Anthropic.Foundry` + `Microsoft.Agents.AI.Anthropic` and a separate factory. Not reachable via `AIProjectClient`. |
| Hub-based (classic) / managed-compute deployments | ❌ | varies | Requires legacy `Azure.AI.Inference` path. Not applicable — FinWise uses a non-classic Foundry project. |

If a future deployment falls into the Chat Completions-only row, the factory should branch on deployment name and call `.GetChatClient(modelDeployment).AsIChatClient()` instead. This is the same shape the current `AzureOpenAIChatClientFactory` already uses and requires no new NuGet packages.

#### Research Sources

1. [`Microsoft.Agents.AI.Foundry` 1.1.0 source — `AzureAIProjectChatClientExtensions`](https://github.com/microsoft/agent-framework/blob/main/dotnet/src/Microsoft.Agents.AI.Foundry/AzureAIProjectChatClientExtensions.cs) — canonical reference for the call chain.
2. [Azure AI Foundry SDK overview](https://learn.microsoft.com/en-us/azure/ai-foundry/how-to/develop/sdk-overview) — Foundry SDK vs direct OpenAI SDK guidance.
3. [`Azure.AI.Projects` 2.0.0 README](https://www.nuget.org/packages/Azure.AI.Projects) and [`Azure.AI.Extensions.OpenAI` 2.0.0 README](https://www.nuget.org/packages/Azure.AI.Extensions.OpenAI) — `AIProjectClient` + `GetProjectOpenAIClient()` extension.
4. [Foundry Responses-supported model list](https://learn.microsoft.com/azure/foundry/foundry-models/how-to/generate-responses) and [`/openai/v1/` migration guide](https://learn.microsoft.com/azure/foundry/how-to/model-inference-to-openai-migration) — basis for the Model Compatibility Matrix.
5. [Claude on Foundry how-to](https://learn.microsoft.com/azure/foundry/foundry-models/how-to/use-foundry-models-claude) — confirms Claude requires `Anthropic.Foundry` + `Microsoft.Agents.AI.Anthropic`, not `AIProjectClient`.
6. NuGet version indexes — confirmed GA status of `Azure.AI.Projects` 2.0.0, `Azure.AI.Extensions.OpenAI` 2.0.0, `Microsoft.Agents.AI` 1.1.0, `Microsoft.Extensions.AI.OpenAI` 10.5.0.

### Dependencies

#### Package Changes for `FinWise.McpServer.csproj`

```diff
  <PackageReference Include="Azure.AI.Projects" />              <!-- keep: AIProjectClient -->
  <PackageReference Include="Azure.Identity" />                 <!-- keep: ClientSecretCredential -->
  <PackageReference Include="Microsoft.Extensions.AI" />        <!-- keep: IChatClient abstraction -->
  <PackageReference Include="Microsoft.Extensions.AI.OpenAI" /> <!-- keep: AsIChatClient(modelId) bridge -->
+ <PackageReference Include="Azure.AI.Extensions.OpenAI" />     <!-- ADD: GetProjectOpenAIClient() ext -->
- <PackageReference Include="Azure.AI.OpenAI" />                <!-- REMOVE after old factory deleted -->
```

#### Package Changes for `Directory.Packages.props`

```diff
+ <PackageVersion Include="Azure.AI.Extensions.OpenAI" Version="2.0.0" />
- <PackageVersion Include="Azure.AI.Projects.OpenAI" Version="2.0.0-beta.1" />  <!-- unused, replaced by GA above -->
- <PackageVersion Include="Azure.AI.OpenAI" Version="2.1.0" />                  <!-- if removed from McpServer -->
```

**Rationale:**

- `Azure.AI.Extensions.OpenAI` 2.0.0 (**GA**) is the package that provides the `GetProjectOpenAIClient()` extension on `AIProjectClient`. It is the modern, supported helper surface.
- `Azure.AI.Projects.OpenAI` 2.0.0-beta.1 is currently in `Directory.Packages.props` but **not referenced** by any project. It is an older preview-only overlapping surface that is superseded by `Azure.AI.Extensions.OpenAI` GA. Safe to remove.
- `Azure.AI.OpenAI` is only needed by the soon-to-be-deleted `AzureOpenAIChatClientFactory`. Once the old factory is removed, the package reference can be removed too.
- `Microsoft.Agents.AI.Foundry` 1.0.0 **IS referenced** by `src/FinWise.McpServer/Infrastructure/AzureAIFoundry/StockAgentFactory.cs` (used to construct the StockAgent specialized agent). Kept as-is in this migration. A future upgrade to 1.1.0 (which introduces the `aiProjectClient.AsAIAgent(...)` one-liner) is tracked under "Notes for Future Work".

#### Packages NOT Affected

- Redis, CosmosDB, Serilog, and MCP packages
- `Microsoft.Agents.AI` and `Microsoft.Agents.AI.Workflows` (consumed by `FinWise.MultiAgentWorkflow`, no version change needed)

### Technical Requirements

- [x] TR-1 — New `AzureAIFoundryChatClientFactory` must produce an `IChatClient` via the chain `AIProjectClient` → `.GetProjectOpenAIClient()` → `.GetResponsesClient()` → `.AsIChatClient(modelDeployment)`.
- [x] TR-2 — Factory must authenticate with `ClientSecretCredential` using `FINWISE_AZURE_TENANT_ID`, `FINWISE_AZURE_CLIENT_ID`, and `FINWISE_AZURE_CLIENT_SECRET` (shared with `StockAgentFactory`).
- [x] TR-3 — Factory must validate all five required env vars (endpoint, deployment name, tenant, client, secret) and throw `InvalidOperationException` per missing variable with a clear message — matching the throw-per-variable pattern of the existing `AzureOpenAIChatClientFactory`.
- [x] TR-4 — Factory must log env var status (SET / NOT SET) using Serilog without exposing values — matching the `StockAgentFactory` pattern.
- [x] TR-5 — Solution must build with zero warnings (`TreatWarningsAsErrors` is enabled). Use a localized `#pragma warning disable OPENAI001` / `restore OPENAI001` around the `GetResponsesClient().AsIChatClient(modelDeployment)` call site. The `AsIChatClient(this ResponsesClient, string?)` overload carries `[Experimental(DiagnosticIds.Experiments.AIOpenAIResponses)]`, and per `dotnet/extensions` `DiagnosticIds.cs` the `AIOpenAIResponses` constant evaluates to `"OPENAI001"` (deliberately reusing the OpenAI SDK's diagnostic ID, so a single pragma covers both source attributes). Microsoft's own `Microsoft.Agents.AI.Foundry` 1.1.0 package applies the same single suppression at the same call site.
- [x] TR-6 — All existing unit tests must pass without modification (the `IChatClient` seam is preserved). Verified: 89/89 unit tests pass (78 MultiAgentWorkflow + 11 McpServer).
- [x] TR-7 — Docker Compose configuration must pass `FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT` and `FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME` to the container; `FINWISE_AZURE_*` auth vars are already passed.
- [x] TR-8 — Add `Azure.AI.Extensions.OpenAI` 2.0.0 to `Directory.Packages.props` and `FinWise.McpServer.csproj`. Remove unused `Azure.AI.Projects.OpenAI` 2.0.0-beta.1 from `Directory.Packages.props` **and from `FinWise.MultiAgentWorkflow.csproj` and `tests/FinWise.StockAgent.IntegrationTests/*.csproj`** where stale references existed. Remove `Azure.AI.OpenAI` from `FinWise.McpServer.csproj` and `Directory.Packages.props` once the old factory is deleted.

### Open Questions (Technical)

- [x] OQ-T4 — **Resolved (Revision 8)**: `Azure.AI.OpenAI` was removed from `FinWise.McpServer.csproj` and `Directory.Packages.props` after the legacy `AzureOpenAIChatClientFactory` was deleted. Verified by grep — no remaining references to `AzureOpenAIClient` or `Azure.AI.OpenAI` in the codebase.

*(OQ-T1, OQ-T2, OQ-T3 resolved in earlier revisions — see Revision History.)*

---

## Feature-Specific Context

### Requirements & Constraints

**From `AGENTS.md` (root)**:
- Hub-and-spoke agent architecture — this migration does not affect agent routing
- Use env vars for secrets and credentials — maintained with new variable names
- Never commit secrets or `.env` files — only `.env.*.template` files are tracked
- Conventional Commits format for all commits

**From `src/FinWise.McpServer/AGENTS.md`**:
- `Program.cs` is the composition root — no 3rd-party DI container
- `TreatWarningsAsErrors` — zero warnings required
- McpServer is a thin host; all business logic is in MultiAgentWorkflow

**From `src/FinWise.MultiAgentWorkflow/AGENTS.md`**:
- LLM-provider-agnostic — receives `IChatClient` (this migration preserves this)
- Never reference MCP packages or types
- Zero changes expected in this project

### Implementation Guidance

- **Follow existing `StockAgentFactory` pattern** for env var reading and SET/NOT SET logging. For error handling, follow `AzureOpenAIChatClientFactory` — throw `InvalidOperationException` per missing variable (the LLM client is required, unlike the optional StockAgent which returns `null`).
- **Use `AIProjectClient` with the Foundry project endpoint** and obtain the model client through the current Responses-backed helper path.
- **Bridge to `IChatClient`** via `.AsIChatClient(modelDeployment)` from `Microsoft.Extensions.AI.OpenAI`; keep raw Responses-specific types inside the composition root.
- **Factory lives in `Infrastructure/AzureAIFoundry/`** alongside `StockAgentFactory.cs` — both are Azure AI Foundry concerns.
- **Match existing logging style**: Use `Log.Information` from Serilog, log env var status as SET/NOT SET.
- **C# class name**: `AzureAIFoundryChatClientFactory` (generic, not model-specific or stock-specific).

---

## PROPOSED IMPLEMENTATION STEPS

> **Status tags**: `[COMPLETED]` | `[IN PROGRESS]` | `[]` (pending)
>
> Agent updates these steps as work progresses. Never proceed to next step without approval.

### Phase 1: Configuration Setup

- [COMPLETED] **Step 1.1** — Update `.env.template` to replace the old Azure OpenAI environment variables section with the new Azure AI Foundry ones. Replace `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_DEPLOYMENT_NAME`, and `AZURE_OPENAI_API_KEY` with `FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT` (format: `https://<resource>.services.ai.azure.com/api/projects/<project>`) and `FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME`. Keep the shared auth vars (`FINWISE_AZURE_TENANT_ID`, `FINWISE_AZURE_CLIENT_ID`, `FINWISE_AZURE_CLIENT_SECRET`) and do not duplicate them. If the repo still documents `/openai/v1/`, clarify that it is an **alternative fallback path**, not the primary one for this implementation.

- [COMPLETED] **Step 1.2** — Update `docker-compose.finwise.yml` to replace the old `AZURE_OPENAI_*` environment variables with `FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT` and `FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME`. Verify the shared auth vars (`FINWISE_AZURE_TENANT_ID`, `FINWISE_AZURE_CLIENT_ID`, `FINWISE_AZURE_CLIENT_SECRET`) are already passed through to the container.

### Phase 2: Factory Migration

- [COMPLETED] **Step 2.1** — Create `AzureAIFoundryChatClientFactory.cs` in the existing `src/FinWise.McpServer/Infrastructure/AzureAIFoundry/` folder (same folder as `StockAgentFactory.cs`). The class should be a static class with a static `CreateChatClient()` method that returns `IChatClient`. It should:
  - (a) Read `FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT` and `FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME` from environment variables.
  - (b) Read the shared auth vars `FINWISE_AZURE_TENANT_ID`, `FINWISE_AZURE_CLIENT_ID`, `FINWISE_AZURE_CLIENT_SECRET`.
  - (c) Validate that ALL required env vars are set (endpoint, deployment name, and all three auth vars). Throw `InvalidOperationException` per missing variable with clear messages (matching `AzureOpenAIChatClientFactory` pattern — NOT `StockAgentFactory` which returns `null` for optional services).
  - (d) Log env var status as SET/NOT SET using Serilog (never log actual values).
  - (e) Create a `ClientSecretCredential(tenantId, clientId, clientSecret)`.
  - (f) Create an `AIProjectClient(new Uri(projectEndpoint), credential)`.
  - (g) Obtain the Responses-backed client and bridge to `IChatClient`:
    ```csharp
    #pragma warning disable OPENAI001 // experimental: AsIChatClient(ResponsesClient, string?) — AIOpenAIResponses → "OPENAI001"
    IChatClient chatClient = projectClient
        .GetProjectOpenAIClient()           // ext. method from Azure.AI.Extensions.OpenAI
        .GetResponsesClient()               // OpenAI.ResponsesClient (inherited)
        .AsIChatClient(modelDeployment);    // bridge from Microsoft.Extensions.AI.OpenAI
    #pragma warning restore OPENAI001
    ```
  - (h) Return that `IChatClient`.

- [COMPLETED] **Step 2.2** — Update `Program.cs` to call `AzureAIFoundryChatClientFactory.CreateChatClient()` instead of `AzureOpenAIChatClientFactory.CreateChatClient()`. Remove the `using FinWise.McpServer.Infrastructure.AzureOpenAI;` statement (the `FinWise.McpServer.Infrastructure.AzureAIFoundry` namespace is already imported for `StockAgentFactory`). Two lines change: one factory call substitution, one using removal.

- [COMPLETED] **Step 2.3** — Remove the old factory from the **default wiring**. If the direct `/openai/v1/` path is still needed temporarily as a documented compatibility fallback, keep it clearly secondary/non-default; otherwise delete `src/FinWise.McpServer/Infrastructure/AzureOpenAI/AzureOpenAIChatClientFactory.cs` and remove the `Infrastructure/AzureOpenAI/` folder after validation.

### Phase 3: Package Alignment and Cleanup

- [COMPLETED] **Step 3.1** — Add `Azure.AI.Extensions.OpenAI` 2.0.0 (GA) to `Directory.Packages.props` and add `<PackageReference Include="Azure.AI.Extensions.OpenAI" />` to `FinWise.McpServer.csproj`. This package provides the `GetProjectOpenAIClient()` extension on `AIProjectClient`.

- [COMPLETED] **Step 3.2** — Remove the unused `Azure.AI.Projects.OpenAI` 2.0.0-beta.1 entry from `Directory.Packages.props`. Verify by `grep` that no `.csproj` references it (it is not referenced today; it is superseded by the GA `Azure.AI.Extensions.OpenAI`).

- [COMPLETED] **Step 3.3** — Remove `<PackageReference Include="Azure.AI.OpenAI" />` from `FinWise.McpServer.csproj` and the corresponding `PackageVersion` from `Directory.Packages.props` once the old `AzureOpenAIChatClientFactory` is deleted. Verify by `grep` that no remaining code references `AzureOpenAIClient` or `Azure.AI.OpenAI`.

- [NOT APPLIED — package is referenced by `StockAgentFactory`; kept] **Step 3.4** *(originally proposed cleanup, reversed)* — Investigation during implementation showed `Microsoft.Agents.AI.Foundry` 1.0.0 IS legitimately referenced by `src/FinWise.McpServer/Infrastructure/AzureAIFoundry/StockAgentFactory.cs` (used to construct the StockAgent specialized agent). The earlier "unreferenced" claim was incorrect. Package KEPT as-is. A future upgrade to 1.1.0 (which introduces the `aiProjectClient.AsAIAgent(...)` one-liner) is tracked under "Notes for Future Work".

### Phase 4: Testing & Validation

- [COMPLETED] **Step 4.1** — Build the entire solution (`dotnet build FinWise.slnx`) and verify zero errors and zero warnings.

- [COMPLETED] **Step 4.2** — Run all unit tests (`dotnet test tests/FinWise.MultiAgentWorkflow.UnitTests/` and `dotnet test tests/FinWise.McpServer.UnitTests/`) and verify they all pass without any modifications. Result: 89/89 (78 + 11). Integration/E2E: 48/48 (Cosmos 13 + Redis 12 + StockAgent 4 + McpServer integration 8 + Container 11). Total: 137/137.

- [COMPLETED] **Step 4.3**— Integration test: Run the MCP server locally with a real Azure AI Foundry project endpoint using the new environment variables (`FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT`, `FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME`, and the shared `FINWISE_AZURE_*` auth vars). Verify end-to-end conversation flow (profile collection -> advisor recommendations) works correctly.

- [SKIPPED — out of scope, optional] **Step 4.4** — (Optional, if a non-OpenAI model is deployed) Test with a non-OpenAI model that is on Microsoft's **Responses-supported list** — e.g., Llama 3.3, Llama-4-Maverick, DeepSeek V3/R1, Grok-4, or MAI-DS-R1. For these, change only `FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME` in `.env` and agents should work unchanged. **Do NOT use Mistral, Phi, or Cohere for this test** — they do not support the Responses API and would require the Chat Completions variant of the chain (see Model Compatibility Matrix). Testing them is a separate effort that belongs to a future per-model-family branch, not this migration.

### Phase 5: Documentation

- [COMPLETED] **Step 5.1** — Update `src/FinWise.McpServer/AGENTS.md` to change the Technology line from "Azure OpenAI" to "Azure AI Foundry (via `AIProjectClient` + Responses-backed `IChatClient`)".

- [COMPLETED] **Step 5.2** — Update `README.md` setup instructions to reference the new environment variable names and the Azure AI Foundry project endpoint format (`https://<resource>.services.ai.azure.com/api/projects/<project>`). If the direct `/openai/v1/` path is documented, label it as an alternative compatibility option rather than the primary design.

- [ ] **Step 5.3** *(deferred)* — Update architecture spec documents (e.g., `specs/05-architecture-and-technologies-v1.0.0.md`) if they reference Azure OpenAI specifically. Verified deferred: `specs/05-architecture-and-technologies-v1.0.0.md` still references `AzureOpenAIChatClientFactory` and "Azure OpenAI" in the diagrams — to be addressed in a follow-up doc-only PR.

---

## Learnings & Notes

> Capture insights discovered during implementation for future reference.

### Patterns Discovered

- **Centralized .NET versioning** via `<FinWiseVersion>1.0.1</FinWiseVersion>` in `Directory.Build.props` is the single source of truth that propagates `<Version>` to every project. Individual `.csproj` files must NOT redeclare `<Version>` — doing so silently shadows the central value and creates drift across artifacts.
- **Removing dead `Azure.AI.Projects.OpenAI` 2.0.0-beta.1** eliminated a colliding `GetProjectOpenAIClient()` extension method, allowing the spec-canonical parameterless `.GetProjectOpenAIClient()` chain to compile cleanly without disambiguation workarounds (no `ProjectOpenAIClientOptions()` overload selection needed).
- **Single `OPENAI001` pragma at the call site is sufficient.** `MEAI001` is unrelated and is not emitted by `AsIChatClient(this ResponsesClient, string?)` — confirmed against `dotnet/extensions` source and `Microsoft.Agents.AI.Foundry` 1.1.0 (which suppresses only `OPENAI001` at the same call site).

### Issues Encountered

- **Misread of the Foundry-overlay package surface.** Initial implementation kept `Azure.AI.Projects.OpenAI 2.0.0-beta.1` and added a `ProjectOpenAIClientOptions()` disambiguation workaround based on a misread that the package was needed. A grep for `using Azure.AI.Projects.OpenAI;` returned zero hits — the package was completely dead. Removing it eliminated the extension-method collision and allowed reversion to the spec-canonical chain.
- **Versioning drift from per-csproj overrides.** First version bump added `<Version>1.0.1</Version>` to individual `.csproj` files, missing the centralized `<FinWiseVersion>` in `Directory.Build.props`. Cleaned up by removing csproj overrides and bumping only the central prop. Future bumps must update the central prop only.

### Notes for Future Work

- **StockAgent migration to same Foundry project**: Currently the LLM factory will target the FinWise Foundry project (`FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT`) and the StockAgent may still target a separate project (`STOCK_AGENT_PROJECT_ENDPOINT`). A future initiative could consolidate both into a single Foundry project, simplifying env var management.
- **Per-agent model routing**: If different agents need different models (e.g., Orchestrator on GPT-4o, Advisor on Llama 3.3), this would require passing different `IChatClient` instances to different agents — a larger refactor of `FinWiseWorkflowService`.
- **Direct OpenAI SDK fallback**: If a future requirement needs the raw `/openai/v1/` OpenAI surface instead of the Foundry SDK, FinWise can still introduce a secondary `ChatClient` factory without changing the workflow layer.
- **`Microsoft.Agents.AI.Foundry` upgrade**: v1.0.0 → v1.1.0 upgrade available. Separate task.
- **Versioning discipline**: Tag `1.0.1` published to Docker Hub. Future bumps must update all 4 files listed in the new `## Versioning` section of root `AGENTS.md`.

---

## Research Revision History

### Revision 8 — April 19, 2026 (implementation complete)

Spec implemented end-to-end. Image `finwiseproject/finwise-mcp-server:1.0.1` published to Docker Hub. All 137 tests pass (89 unit + 48 integration/E2E). Two corrections vs the plan:
- `Microsoft.Agents.AI.Foundry 1.0.0` was KEPT (not removed). Spec Step 3.4 and the Dependencies Rationale incorrectly claimed it was unreferenced; it is actually used by `StockAgentFactory`. Both updated.
- `Azure.AI.Projects.OpenAI 2.0.0-beta.1` was confirmed dead (zero `using` references) and removed not only from `Directory.Packages.props` but also from `FinWise.MultiAgentWorkflow.csproj` and `tests/FinWise.StockAgent.IntegrationTests/*.csproj` where stale references existed.

Versioning: centralized via `<FinWiseVersion>` in `Directory.Build.props` (single source of truth). Documented in root `AGENTS.md` `## Versioning` section.

### Revision 7 — April 19, 2026

**Correction**: Removed the `MEAI001` pragma added in Revision 5. Verified directly against `dotnet/extensions` source (`src/Shared/DiagnosticIds/DiagnosticIds.cs`, line 67): `AIOpenAIResponses = "OPENAI001"` (NOT `MEAI001`). The OpenAI extensions deliberately reuse the OpenAI SDK's diagnostic ID so that a single `OPENAI001` suppression covers both source attributes. `MEAI001` is a separate diagnostic ID for unrelated MEAI experimental APIs (image generation, speech-to-text, chat reduction, etc.) and is not emitted by `AsIChatClient(this ResponsesClient, string?)`. Cross-checked: `Microsoft.Agents.AI.Foundry` 1.1.0 source contains zero references to `MEAI001`.

### Revision 6 — April 19, 2026

Added **Model Compatibility Matrix** to the Research Summary after verifying (against Microsoft Learn, April 2026) that the `GetResponsesClient().AsIChatClient(model)` chain only works for the Responses-supported model list. Mistral / Phi / Cohere need a Chat Completions variant (`GetChatClient(model).AsIChatClient()` on the same `ProjectOpenAIClient` — no new packages). Claude is available on Foundry but requires a separate factory (`Anthropic.Foundry` + `Microsoft.Agents.AI.Anthropic`) and is out of scope. Step 4.4 now restricts the non-OpenAI test to Responses-supported models only. Also trimmed duplication from Revisions 1–5 (repeated tables in Authentication / Endpoint URL / Env Var / Implementation Considerations sections, verbose revision-history tables) without removing any implementation-relevant content.

### Revision 5 — April 19, 2026 (superseded by Revision 7)

Added `MEAI001` to the pragma alongside `OPENAI001`. **This was incorrect** — the `AsIChatClient(ResponsesClient, string?)` bridge surfaces only as `OPENAI001`, not `MEAI001`. Corrected in Revision 7. Also documented the `projectClient.ProjectOpenAIClient` property shorthand as an equivalent alternative; added optional Step 3.4 to remove the unused `Microsoft.Agents.AI.Foundry` 1.0.0 entry from `Directory.Packages.props`.

### Revision 4 — April 19, 2026

Final source-level validation against the official `Microsoft.Agents.AI.Foundry` 1.1.0 code (`AzureAIProjectChatClientExtensions.CreateResponsesChatClientAgent`). Corrected the call chain to `projectClient.GetProjectOpenAIClient().GetResponsesClient().AsIChatClient(modelDeployment)` (extension method, not property; no model arg on `GetResponsesClient`). Selected `Azure.AI.Extensions.OpenAI` 2.0.0 GA as the helper package; dropped the unused `Azure.AI.Projects.OpenAI` 2.0.0-beta.1. Reduced `Azure.AI.Inference` discussion to a single reference (package is absent from the repo). Added the **Migration: Before vs After** comparison table.

### Earlier Revisions (superseded)

- **Revision 3** (April 19, 2026): Made the Foundry-native path primary; demoted `/openai/v1/` to fallback.
- **Revision 2** (April 18-19, 2026): Pivoted away from `Azure.AI.Inference` after discovering its deprecation.
- **Revision 1** (April 18, 2026): Original draft proposed `Azure.AI.Inference` + `Microsoft.Extensions.AI.AzureAIInference`. Fully invalidated.
