# Feature: Stock Specialized Agent Integration

> **Created with:** feature-spec skill (`.github/skills/feature-spec/`)
>
> **Location:** `/specs/006-stock-specialized-agent/specs-stock-specialized-agent-funct-and-tech.md`

---

## Functional Specification

### Problem Statement

The FinWise multi-agent workflow currently has two agents: **UserProfileAgent** (profile collection) and **AdvisorAgent** (personalized investment advice). The AdvisorAgent provides general investment guidance but has no access to real company financial data — it cannot answer questions about specific stock fundamentals, annual report details, or company financials.

An Azure Foundry agent (`stock-specialized-investment-agent`) already exists and is **pre-provisioned** in Azure AI Foundry, grounded on actual stock documents (Apple and Microsoft annual reports). This agent needs to be integrated into the existing FinWise multi-agent workflow as a new spoke agent so users can ask stock-specific questions and get answers backed by real financial documents.

### Proposed Solution

Add a **StockSpecializedAgent** as a fourth agent (third spoke) in the existing hub-and-spoke multi-agent workflow. This agent wraps the pre-provisioned Azure Foundry agent and participates in the `AgentWorkflow` handoff mechanism alongside the existing ProfileAgent and AdvisorAgent.

**User interaction flow:**
1. User completes profile (existing flow, unchanged)
2. User asks a stock-specific question (e.g., "What was Apple's revenue in 2024?")
3. Orchestrator routes to StockSpecializedAgent
4. StockSpecializedAgent calls the Azure Foundry agent via the Responses API
5. Foundry agent searches its grounded stock documents and returns an answer
6. Response flows back through the workflow to the user

**No new MCP server. No new CLI.** The agent is added directly into `FinWise.MultiAgentWorkflow` as a new spoke in the existing handoff topology.

### Functional Requirements

- [ ] FR-1 — Users can ask stock-specific questions (company financials, annual reports, revenue, earnings) and receive answers grounded in real financial documents
- [ ] FR-2 — The Orchestrator routes stock/company-specific queries to the StockSpecializedAgent and personalized advice queries to the AdvisorAgent
- [ ] FR-2b — The AdvisorAgent can hand off to the StockSpecializedAgent mid-conversation when the user's question becomes more stock-specific (e.g., asking about a particular company's financials during a general advice session)
- [ ] FR-3 — StockSpecializedAgent is **gated** behind `PROFILE_READY` — users must complete their profile before accessing stock data (consistent with AdvisorAgent gating)
- [ ] FR-4 — The AdvisorAgent continues to handle personalized investment recommendations (behavior unchanged)
- [ ] FR-5 — The StockSpecializedAgent returns the Foundry agent's response (including any document citations) to the user

### User Scenarios

1. **Stock fundamentals query (profile not yet complete):**
   User: "What was Microsoft's net income in 2024?"
   → Orchestrator sees no PROFILE_READY
   → Routes to ProfileAgent first (must complete profile before stock queries)
   → After profile completion, user re-asks → Routes to StockSpecializedAgent

2. **Stock query after profile completion:**
   User has completed profile (PROFILE_READY exists in history)
   User: "How did Apple's revenue grow over the last 3 years?"
   → Orchestrator detects stock-specific intent + PROFILE_READY exists → Routes to StockSpecializedAgent
   → Returns grounded answer from Apple annual reports

3. **Personalized advice (still goes to AdvisorAgent):**
   User: "Based on my risk profile, should I invest in tech stocks?"
   → Orchestrator detects advice intent + PROFILE_READY exists → Routes to AdvisorAgent (unchanged)

4. **Ambiguous query — advice vs. stock data:**
   User: "Tell me about Apple's financial health"
   → Orchestrator routes to StockSpecializedAgent (factual company analysis, not personalized advice)

5. **Advisor-to-stock escalation (mid-conversation):**
   User is in an advice session with AdvisorAgent.
   User: "What was Apple's actual revenue last year?"
   → AdvisorAgent detects stock-specific data request it cannot answer from general knowledge
   → Hands off to StockSpecializedAgent
   → StockSpecializedAgent answers with grounded data from Apple annual reports
   → Hands off back to Orchestrator for next routing decision

### Out of Scope

- Creating or deploying the Azure Foundry agent (already pre-provisioned)
- Bing search grounding — the Foundry agent uses file search on uploaded stock documents only
- Combining stock data + personalized advice in a single response (future: AdvisorAgent could consult stock data)
- Adding new MCP tools — the existing `run_finwise_workflow` tool handles all routing internally
- New MCP server or CLI project
- Stock data APIs or real-time market data

### Open Questions (Functional)

- [x] Should stock queries be gated behind PROFILE_READY? → **Yes**, consistent with all other agents — profile must be complete first
- [ ] Should the orchestrator routing distinguish between "stock data" and "general financial advice" when PROFILE_READY exists? (Proposed: yes — stock-specific queries → StockAgent, personalized recommendations → AdvisorAgent)

---

## Technical Specification

### Architecture Impact

**New spoke agent in hub-and-spoke topology:**

```
                    ┌─────────────────────┐
                    │  OrchestratorAgent   │
                    │  (HUB — silent       │
                    │   router, tool calls)│
                    └──┬──────┬──────┬─────┘
                       │      │      │
              ┌────────┘      │      └────────┐
              ▼               ▼               ▼
        ┌──────────┐   ┌──────────┐   ┌───────────────┐
        │ Profile  │   │ Advisor  │──▶│ Stock         │
        │ Agent    │   │ Agent    │   │ Specialized   │
        │ (SPOKE)  │   │ (SPOKE)  │   │ Agent (SPOKE) │  ← NEW
        └──────────┘   └──────────┘   └──────┬────────┘
                                              │
                          Advisor can hand     ▼
                          off to Stock    ┌─────────────────┐
                          when user asks  │ Azure Foundry    │
                          stock-specific  │ Agent (Responses │
                          questions       │ API — grounded   │
                                          │ on stock docs)   │
                                          └─────────────────┘
```

**Handoff paths:**
- Orchestrator → ProfileAgent, AdvisorAgent, StockSpecializedAgent (hub-to-spoke)
- ProfileAgent, AdvisorAgent, StockSpecializedAgent → Orchestrator (spoke-to-hub)
- **AdvisorAgent → StockSpecializedAgent** (spoke-to-spoke: stock-specific escalation)

**Modified components:**
- `Agents/` — New `StockSpecializedAgent/` folder (factory + prompt, 2 files)
- `Workflow/FinWiseWorkflowService.cs` — Register new agent in handoff topology
- `Agents/OrchestratorAgent/OrchestratorAgent.prompt.md` — Add routing rules for stock queries

**Unchanged components:**
- `Session/` — No changes (session management works with any number of agents)
- `Infrastructure/` — No changes to stores
- `FinWise.McpServer/` — No changes (the MCP tool calls `ProcessMessageAsync` which internally routes)

### Azure Foundry Agent Details (Pre-Provisioned)

| Property | Value |
|----------|-------|
| Agent application | `stock-specialized-investment-agent` |
| Agent version | 2 |
| Project endpoint | `https://ai-foundry-cesardl.services.ai.azure.com/api/projects/DemoProject/` |
| Responses API endpoint | `https://ai-foundry-cesardl.services.ai.azure.com/api/projects/DemoProject/applications/stock-specialized-investment-agent/protocols/openai/responses?api-version=2025-11-15-preview` |
| Grounding | File search on stock documents (Apple, Microsoft annual reports) |
| Authentication | **API key** via `api-key` HTTP header (see [Research: API Key Auth](#research-api-key-authentication)) |

### Dependencies

- **Internal**: `FinWise.MultiAgentWorkflow` (existing project — where the agent lives)
- **External (new NuGet packages)**: None — the Foundry agent is called directly via `HttpClient` (built-in) with `System.Text.Json` (built-in). No `Azure.AI.Projects`, `Azure.AI.Projects.OpenAI`, or `Azure.Identity` packages needed.
- **External (existing)**: `Microsoft.Agents.AI`, `Microsoft.Agents.AI.Workflows`

### Technical Requirements

- [ ] TR-1 — No new NuGet packages required — `HttpClient` and `System.Text.Json` are built-in. Remove any unused Azure.AI.Projects references if previously added.
- [ ] TR-2 — `StockSpecializedAgentFactory` follows the same 2-file layout as AdvisorAgent/OrchestratorAgent (factory `.cs` + `.prompt.md` only — no separate config or runner files). Foundry HTTP call logic and config are embedded in the factory.
- [ ] TR-3 — Foundry agent invoked via direct HTTP POST to the Responses API endpoint with `api-key` header (not via `AIProjectClient` SDK — that SDK does not support API key auth)
- [ ] TR-4 — Configuration via environment variables: `STOCK_AGENT_RESPONSES_ENDPOINT` (full Responses API URL) and `STOCK_AGENT_API_KEY` (API key for the Azure AI Services resource) — read directly in the factory constructor.
- [ ] TR-5 — Authentication via API key (`api-key` HTTP header), stored in environment variable `STOCK_AGENT_API_KEY`. No `TokenCredential` or `Azure.Identity` dependency.
- [ ] TR-6 — The agent participates in `AgentWorkflow` handoffs (orchestrator can hand off to it, it can hand off back)
- [ ] TR-6b — AdvisorAgent has a direct handoff path to StockSpecializedAgent for stock-specific escalation (spoke-to-spoke, in addition to spoke-to-hub)
- [ ] TR-7 — Zero warnings (TreatWarningsAsErrors enforced)
- [ ] TR-8 — Unit tests for the new factory (config validation, agent creation, tool HTTP call)

### Implementation Approach: Tool-Based Wrapper

The StockSpecializedAgent is a `ChatClientAgent` (same as existing agents) backed by the regular `IChatClient` (Azure OpenAI). It has a **single tool** — `query_stock_documents(query)` — that calls the pre-provisioned Foundry agent via the Responses API and returns its response.

**Why a tool-based wrapper (not a raw IChatClient adapter)?**
- Follows the existing pattern (UserProfileAgent has tools — `get_profile`, `set_profile`, `delete_profile`)
- The `AgentWorkflow` handoff mechanism expects `ChatClientAgent` instances
- The local agent's prompt can add preamble/formatting instructions
- Simpler to implement and test — the Foundry call is isolated in a tool function
- The trade-off (extra LLM call for the wrapper) is acceptable for v0.2 POC scope

**Component layout (2 files — matches AdvisorAgent / OrchestratorAgent pattern):**

```
src/FinWise.MultiAgentWorkflow/
  Agents/
    StockSpecializedAgent/
      StockSpecializedAgent.prompt.md     ← System prompt for the wrapper agent
      StockSpecializedAgentFactory.cs     ← Factory: creates ChatClientAgent + tool + embeds HTTP runner & config
```

The Foundry HTTP call logic and configuration (env var reading, `HttpClient` POST, response parsing) are embedded directly in the factory class — no separate runner or config files. This mirrors how `UserProfileAgentFactory` embeds its profile tools and store access.

**Data flow:**

```
Orchestrator ──handoff_to_stock_agent──▶ StockSpecializedAgent (ChatClientAgent)
                                              │
                                              │ LLM decides to call tool
                                              ▼
                                        query_stock_documents(query)
                                              │
                                              │ HttpClient + api-key header
                                              ▼
                                        Foundry Agent (Responses API)
                                              │
                                              │ File search on stock docs
                                              ▼
                                        Response with citations
                                              │
                                              ▼
                                        StockSpecializedAgent formats response
                                              │
                                              │ handoff_to_orchestrator_agent
                                              ▼
                                        Orchestrator returns to user
```

### Orchestrator Routing Updates

The `OrchestratorAgent.prompt.md` decision tree expands from 2-spoke to 3-spoke:

```
STEP 1: Search entire history for PROFILE_READY marker

IF PROFILE_READY NOT FOUND:
  → ALWAYS route to profile_agent (unchanged — no exceptions)

IF PROFILE_READY FOUND:
  Check user intent:
  ├─ Stock-specific query (company financials, annual reports, revenue,
  │  earnings, stock fundamentals) → stock_specialized_agent
  ├─ Profile-related intent (show, update, delete) → profile_agent (unchanged)
  └─ Personalized advice/recommendation → advisor_agent (unchanged)
```

**Key change**: Stock-specific queries are a new intent category routed after `PROFILE_READY` is confirmed — consistent gating with AdvisorAgent.

### AdvisorAgent Handoff to StockSpecializedAgent

The AdvisorAgent gets a **direct handoff** to StockSpecializedAgent. When a user asks a stock-specific data question during an advice session (e.g., "What was Apple's actual revenue?"), the AdvisorAgent recognizes it cannot provide grounded company data and hands off to the stock agent.

```
AdvisorAgent decision:
  IF user asks stock-specific data question (company revenue, earnings,
     annual report figures, financial statements):
    → handoff_to_stock_specialized_agent
  ELSE:
    → Continue providing personalized advice, or
    → handoff_to_orchestrator_agent when done
```

This is a **spoke-to-spoke** handoff (the only one in the topology). It avoids an unnecessary round-trip through the orchestrator when the intent is clear.

### Configuration

Environment variables (following AgenticGuru pattern):

| Variable | Description | Example |
|----------|-------------|---------|
| `STOCK_AGENT_RESPONSES_ENDPOINT` | Full Responses API URL for the Foundry agent | `https://ai-foundry-cesardl.services.ai.azure.com/api/projects/DemoProject/applications/stock-specialized-investment-agent/protocols/openai/responses?api-version=2025-11-15-preview` |
| `STOCK_AGENT_API_KEY` | API key for the Azure AI Services resource | (from Azure Portal → AI Services resource → Keys and Endpoint) |

These are read directly by the `StockSpecializedAgentFactory` constructor and used to configure the embedded HTTP call logic.

### Alternatives Considered

| Approach | Pros | Cons | Decision |
|----------|------|------|----------|
| **Tool-based wrapper + direct HTTP** (chosen) | Follows existing patterns, simple, testable, no extra SDK dependencies, API key auth | Extra LLM call per query | ✅ Best fit for v0.2 POC |
| **Tool-based wrapper + Azure.AI.Projects SDK** | Higher-level SDK, retry policies | Does NOT support API key auth (only `TokenCredential`), adds 3 NuGet dependencies | ❌ Rejected — API key auth required |
| **Custom IChatClient adapter** | Single LLM call, cleaner | Complex to implement, `IChatClient` contract is chat-oriented while Foundry uses Responses API | Deferred — consider for v0.3 optimization |
| **Custom AIAgent implementation** | Full control, no double-LLM | Bypasses framework abstractions, more code | Deferred — only if framework limitations block tool approach |

### Open Questions (Technical)

- [x] Are `Azure.AI.Projects` / `Azure.AI.Projects.OpenAI` package versions compatible with .NET 10? → **Moot** — these packages are no longer used. The Foundry agent is called directly via `HttpClient`.
- [x] Should `TokenCredential` be registered in `Program.cs` as a shared singleton? → **Moot** — API key auth is used instead of `TokenCredential`. No `Azure.Identity` dependency.
- [ ] Should `HttpClient` be a shared singleton via `IHttpClientFactory` or a simple `new HttpClient()`? (Recommend `IHttpClientFactory` for proper connection pooling, but `Program.cs` currently uses manual DI — a singleton `HttpClient` is acceptable for POC)

---

## Feature-Specific Context

### Requirements & Constraints

- **Pre-provisioned Foundry agent**: `stock-specialized-investment-agent` v2 already exists in Azure AI Foundry — no agent creation/deployment needed
- **No Bing grounding** — the Foundry agent uses file search on uploaded stock documents only
- **No new MCP server or CLI** — integration is purely within `FinWise.MultiAgentWorkflow`
- **Profile gating required** — stock queries gated behind PROFILE_READY, consistent with all other agents
- **Follow AgenticGuru PoC pattern** — Adapted: same Responses API semantics, but using direct `HttpClient` + `api-key` header instead of `AIProjectClient` SDK (SDK does not support API key auth)
- **Follow existing factory pattern** — folder-per-agent with `.prompt.md`, factory class, embedded resources

### Implementation Guidance

- **2-file layout**: Match the `AdvisorAgentFactory` / `OrchestratorAgentFactory` pattern exactly — one factory `.cs` file + one `.prompt.md` file per agent folder. No separate config or runner classes.
- **HTTP call pattern**: The factory embeds the Foundry HTTP call directly (as a tool method, like `UserProfileAgentFactory` embeds profile tools). Mirror the `AIToolsResearchAgentRunner` semantics from the AgenticGuru PoC, but replace `AIProjectClient` SDK calls with direct `HttpClient` POST to the Responses API endpoint. Send JSON body with `input` field and parse the response's `output` array for text content and annotations.
- **Config pattern**: Environment variables (`STOCK_AGENT_RESPONSES_ENDPOINT`, `STOCK_AGENT_API_KEY`) are read directly in the factory constructor — no separate config class.
- **Agent factory pattern**: Mirror `AdvisorAgentFactory` for agent creation, `UserProfileAgentFactory` for tool registration.
- **Credentials**: API key from environment variable — passed via `api-key` HTTP header on every request. Store securely; never log or commit the key.
- **Agent endpoint**: The Responses API URL is pre-configured (env var), no runtime agent discovery needed

---

## PROPOSED IMPLEMENTATION STEPS

> **Status tags**: `[COMPLETED]` | `[IN PROGRESS]` | `[]` (pending)
>
> Agent updates these steps as work progresses. Never proceed to next step without approval.

### Phase 1: Verify Prerequisites

- [] Step 1.1 — No new NuGet packages required. Verify `FinWise.MultiAgentWorkflow.csproj` already references `System.Text.Json` (or it's implicitly available via the framework). No changes to `Directory.Packages.props`.

### Phase 2: Agent Factory & Prompt (2 files)

- [] Step 2.1 — Create `StockSpecializedAgent.prompt.md` in `Agents/StockSpecializedAgent/` — system prompt for the wrapper agent. Instructs the agent to always use the `query_stock_documents` tool for any stock-related query and to relay the tool's response (including citations) to the user. The prompt should also instruct it to hand off back to the orchestrator when done.
- [] Step 2.2 — Create `StockSpecializedAgentFactory.cs` in `Agents/StockSpecializedAgent/` — follows the same 2-file layout as AdvisorAgent/OrchestratorAgent. Receives `IChatClient` and `HttpClient` via constructor. Reads `STOCK_AGENT_RESPONSES_ENDPOINT` and `STOCK_AGENT_API_KEY` from environment variables (config logic embedded in the factory, no separate config class). Exposes `Name` ("stock_specialized_agent") and `Description`. The `CreateAgent()` method builds a `ChatClientAgent` with the embedded prompt and registers a `query_stock_documents` tool. The tool method (embedded in the factory, like `UserProfileAgentFactory`'s profile tools) uses `HttpClient` to POST to the Foundry Responses API with `api-key` header, parses the response, and returns the text with citations.

### Phase 3: Workflow Integration

- [] Step 3.1 — Update `OrchestratorAgent.prompt.md` to add routing rules for stock-specific queries. Stock queries route to `stock_specialized_agent` only when PROFILE_READY exists. The routing remains: (1) no PROFILE_READY → profile agent, (2) stock-specific → stock agent, (3) profile intent → profile agent, (4) advice intent → advisor agent.
- [] Step 3.2 — Update `AdvisorAgent.prompt.md` to add a handoff rule: when the user asks stock-specific data questions (company financials, annual report figures), hand off to `stock_specialized_agent` instead of trying to answer from general knowledge.
- [] Step 3.3 — Update `FinWiseWorkflowService.cs` to create the `StockSpecializedAgentFactory` and register the stock agent in the handoff topology. The constructor receives `HttpClient` as an additional dependency. In `CreateAgentsAndWorkflow()`: (a) add the stock agent to the orchestrator's handoff list, (b) register the stock agent → orchestrator handoff, (c) register the advisor agent → stock agent handoff (spoke-to-spoke).
- [] Step 3.4 — Update `Program.cs` (composition root) to create/configure `HttpClient` and pass it to `FinWiseWorkflowService`.

### Phase 4: Testing

- [] Step 4.1 — Write unit tests for `StockSpecializedAgentFactory` config validation (env vars required, missing throws)
- [] Step 4.2 — Write unit tests for `StockSpecializedAgentFactory.CreateAgent()` (verifies agent name, description, tool registration)
- [] Step 4.3 — Write unit tests for the `query_stock_documents` tool method (mock `HttpClient` via `HttpMessageHandler` to verify correct HTTP request format, `api-key` header, and response parsing)
- [] Step 4.4 — Update existing orchestrator routing tests (if any) to cover stock-specific query routing

### Phase 5: Documentation & Cleanup

- [] Step 5.1 — Add `STOCK_AGENT_RESPONSES_ENDPOINT` and `STOCK_AGENT_API_KEY` to `appsettings.Development.json` example or environment setup documentation
- [] Step 5.2 — Update architecture diagrams in specs to reflect 4-agent topology

---

## Learnings & Notes

> Capture insights discovered during implementation for future reference.

### Patterns Discovered

- AgenticGuru PoC provides a proven pattern for calling Azure Foundry agents via `Azure.AI.Projects` SDK (get-or-create agent → Responses API → extract annotated text) — adapted for direct HTTP calls
- The `AgentWorkflowBuilder.WithHandoffs()` API makes adding new spokes straightforward — just add to the orchestrator's handoff list
- Spoke-to-spoke handoffs are supported by the framework — `WithHandoffs(advisorAgent, [stockAgent])` enables direct escalation without routing through the hub

### Research: API Key Authentication

**Finding**: The `Azure.AI.Projects` SDK (`AIProjectClient`) does **NOT** support API key authentication. Its constructors accept only `AuthenticationTokenProvider` (GA) or `TokenCredential` (beta) — no `AzureKeyCredential` variant exists.

**However**, the Azure OpenAI Responses API (which the Foundry agent exposes at `.../protocols/openai/responses`) supports two authentication methods:
1. **API key**: via `api-key` HTTP header
2. **Microsoft Entra ID**: via `Authorization: Bearer <token>` header

This is confirmed in the [Azure OpenAI REST API reference](https://learn.microsoft.com/en-us/azure/foundry/openai/reference#authentication) and the [Responses API how-to guide](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/responses).

**Decision**: Bypass the `Azure.AI.Projects` SDK entirely. Call the Foundry agent's Responses API endpoint directly via `HttpClient` with the `api-key` header. This:
- Eliminates 3 NuGet dependencies (`Azure.AI.Projects`, `Azure.AI.Projects.OpenAI`, `Azure.Identity`)
- Simplifies the implementation (single HTTP POST, JSON parsing)
- Uses the same Responses API semantics as the SDK (same request/response format)
- API key is stored as environment variable `STOCK_AGENT_API_KEY`

### Issues Encountered

- (To be captured during implementation)

### Notes for Future Work

- **v0.3 optimization**: Consider replacing the tool-based wrapper with a custom `IChatClient` adapter to eliminate the double-LLM call overhead
- **Cross-agent consultation**: Future versions could let the AdvisorAgent consult the StockSpecializedAgent for data-backed personalized recommendations
- **Additional companies**: The Foundry agent's file search can be expanded with more company documents without any code changes
