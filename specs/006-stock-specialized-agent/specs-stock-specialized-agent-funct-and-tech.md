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
- `Agents/` — New `StockSpecializedAgent/` folder (factory + prompt + runner)
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
| Authentication | `DefaultAzureCredential` / `AzureCliCredential` (same as AgenticGuru PoC) |

### Dependencies

- **Internal**: `FinWise.MultiAgentWorkflow` (existing project — where the agent lives)
- **External (new NuGet packages)**:
  - `Azure.AI.Projects` — Azure Foundry agent SDK (used for `AIProjectClient`, agent discovery)
  - `Azure.AI.Projects.OpenAI` — OpenAI Responses API client for Foundry agents
  - `Azure.Identity` — Azure credential management (may already be transitive; verify)
- **External (existing)**: `Microsoft.Agents.AI`, `Microsoft.Agents.AI.Workflows`

### Technical Requirements

- [ ] TR-1 — New `Azure.AI.Projects` and `Azure.AI.Projects.OpenAI` packages added to `Directory.Packages.props` and `FinWise.MultiAgentWorkflow.csproj`
- [ ] TR-2 — `StockSpecializedAgentFactory` follows existing factory pattern (folder-per-agent, embedded `.prompt.md`, receives dependencies via constructor)
- [ ] TR-3 — Foundry agent invoked via Azure.AI.Projects SDK (Responses API), following the AgenticGuru PoC `AIToolsResearchAgentRunner` pattern
- [ ] TR-4 — Configuration via environment variables: `STOCK_AGENT_PROJECT_ENDPOINT`, `STOCK_AGENT_NAME` (same pattern as AgenticGuru's `AIToolsResearchAgentConfig.FromEnvironment()`)
- [ ] TR-5 — Authentication via `TokenCredential` (injected, supports `DefaultAzureCredential` / `AzureCliCredential`)
- [ ] TR-6 — The agent participates in `AgentWorkflow` handoffs (orchestrator can hand off to it, it can hand off back)
- [ ] TR-6b — AdvisorAgent has a direct handoff path to StockSpecializedAgent for stock-specific escalation (spoke-to-spoke, in addition to spoke-to-hub)
- [ ] TR-7 — Zero warnings (TreatWarningsAsErrors enforced)
- [ ] TR-8 — Unit tests for the new factory and runner

### Implementation Approach: Tool-Based Wrapper

The StockSpecializedAgent is a `ChatClientAgent` (same as existing agents) backed by the regular `IChatClient` (Azure OpenAI). It has a **single tool** — `query_stock_documents(query)` — that calls the pre-provisioned Foundry agent via the Responses API and returns its response.

**Why a tool-based wrapper (not a raw IChatClient adapter)?**
- Follows the existing pattern (UserProfileAgent has tools — `get_profile`, `set_profile`, `delete_profile`)
- The `AgentWorkflow` handoff mechanism expects `ChatClientAgent` instances
- The local agent's prompt can add preamble/formatting instructions
- Simpler to implement and test — the Foundry call is isolated in a tool function
- The trade-off (extra LLM call for the wrapper) is acceptable for v0.2 POC scope

**Component layout:**

```
src/FinWise.MultiAgentWorkflow/
  Agents/
    StockSpecializedAgent/
      StockSpecializedAgent.prompt.md     ← System prompt for the wrapper agent
      StockSpecializedAgentFactory.cs     ← Factory: creates ChatClientAgent + tool
      FoundryStockAgentRunner.cs          ← SDK wrapper: calls Foundry Responses API
      FoundryStockAgentConfig.cs          ← Config record (env vars)
```

**Data flow:**

```
Orchestrator ──handoff_to_stock_agent──▶ StockSpecializedAgent (ChatClientAgent)
                                              │
                                              │ LLM decides to call tool
                                              ▼
                                        query_stock_documents(query)
                                              │
                                              │ Azure.AI.Projects SDK
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
| `STOCK_AGENT_PROJECT_ENDPOINT` | Azure AI Foundry project endpoint | `https://ai-foundry-cesardl.services.ai.azure.com/api/projects/DemoProject/` |
| `STOCK_AGENT_NAME` | Foundry agent application name | `stock-specialized-investment-agent` |

These are read by `FoundryStockAgentConfig.FromEnvironment()` and injected via the composition root (`Program.cs`).

### Alternatives Considered

| Approach | Pros | Cons | Decision |
|----------|------|------|----------|
| **Tool-based wrapper** (chosen) | Follows existing patterns, simple, testable | Extra LLM call per query | ✅ Best fit for v0.2 POC |
| **Custom IChatClient adapter** | Single LLM call, cleaner | Complex to implement, `IChatClient` contract is chat-oriented while Foundry uses Responses API | Deferred — consider for v0.3 optimization |
| **Custom AIAgent implementation** | Full control, no double-LLM | Bypasses framework abstractions, more code | Deferred — only if framework limitations block tool approach |

### Open Questions (Technical)

- [ ] Are `Azure.AI.Projects` / `Azure.AI.Projects.OpenAI` package versions compatible with .NET 10? (AgenticGuru uses `1.2.0-beta.5` / `1.0.0-beta.5` on .NET 10 — likely yes)
- [ ] Should `TokenCredential` be registered in `Program.cs` as a shared singleton (used by both Foundry calls and Azure OpenAI)? Or kept separate?

---

## Feature-Specific Context

### Requirements & Constraints

- **Pre-provisioned Foundry agent**: `stock-specialized-investment-agent` v2 already exists in Azure AI Foundry — no agent creation/deployment needed
- **No Bing grounding** — the Foundry agent uses file search on uploaded stock documents only
- **No new MCP server or CLI** — integration is purely within `FinWise.MultiAgentWorkflow`
- **Profile gating required** — stock queries gated behind PROFILE_READY, consistent with all other agents
- **Follow AgenticGuru PoC pattern** — `AIProjectClient` → `GetProjectResponsesClientForAgent()` → `CreateResponseAsync()` for calling the Foundry agent
- **Follow existing factory pattern** — folder-per-agent with `.prompt.md`, factory class, embedded resources

### Implementation Guidance

- **SDK pattern**: Mirror `AIToolsResearchAgentRunner` from AgenticGuru PoC for Foundry agent communication
- **Config pattern**: Mirror `AIToolsResearchAgentConfig.FromEnvironment()` for environment variable resolution
- **Agent factory pattern**: Mirror `AdvisorAgentFactory` / `UserProfileAgentFactory` for agent creation
- **Credentials**: Use `TokenCredential` injection (supports `AzureCliCredential` for local dev, `DefaultAzureCredential` for production)
- **Agent discovery**: Use get-or-fetch pattern (get existing agent by name, no creation)

---

## PROPOSED IMPLEMENTATION STEPS

> **Status tags**: `[COMPLETED]` | `[IN PROGRESS]` | `[]` (pending)
>
> Agent updates these steps as work progresses. Never proceed to next step without approval.

### Phase 1: Infrastructure & Configuration

- [] Step 1.1 — Add `Azure.AI.Projects` and `Azure.AI.Projects.OpenAI` and `Azure.Identity` (if not already transitive) package references to `Directory.Packages.props` and `FinWise.MultiAgentWorkflow.csproj`
- [] Step 1.2 — Create `FoundryStockAgentConfig.cs` in `Agents/StockSpecializedAgent/` — a sealed record that reads `STOCK_AGENT_PROJECT_ENDPOINT` and `STOCK_AGENT_NAME` from environment variables, following the `AIToolsResearchAgentConfig` pattern from the AgenticGuru PoC
- [] Step 1.3 — Create `FoundryStockAgentRunner.cs` in `Agents/StockSpecializedAgent/` — a class that uses `AIProjectClient` to fetch the existing Foundry agent by name and invoke it via the Responses API (`GetProjectResponsesClientForAgent` → `CreateResponseAsync`). Follows the `AIToolsResearchAgentRunner` pattern: factory method `CreateFromConfig(config, credential)`, a `RunAsync(query)` method that returns the Foundry agent's text response. Includes annotation/citation text extraction from the response.

### Phase 2: Agent Factory & Prompt

- [] Step 2.1 — Create `StockSpecializedAgent.prompt.md` in `Agents/StockSpecializedAgent/` — system prompt for the wrapper agent. Instructs the agent to always use the `query_stock_documents` tool for any stock-related query and to relay the tool's response (including citations) to the user. The prompt should also instruct it to hand off back to the orchestrator when done.
- [] Step 2.2 — Create `StockSpecializedAgentFactory.cs` in `Agents/StockSpecializedAgent/` — follows existing factory pattern. Receives `IChatClient`, `FoundryStockAgentConfig`, and `TokenCredential` via constructor. Exposes `Name` ("stock_specialized_agent") and `Description`. The `CreateAgent()` method builds a `ChatClientAgent` with the embedded prompt and registers a `query_stock_documents` tool. The tool internally creates a `FoundryStockAgentRunner` and calls `RunAsync(query)`.

### Phase 3: Workflow Integration

- [] Step 3.1 — Update `OrchestratorAgent.prompt.md` to add routing rules for stock-specific queries. Stock queries route to `stock_specialized_agent` only when PROFILE_READY exists. The routing remains: (1) no PROFILE_READY → profile agent, (2) stock-specific → stock agent, (3) profile intent → profile agent, (4) advice intent → advisor agent.
- [] Step 3.2 — Update `AdvisorAgent.prompt.md` to add a handoff rule: when the user asks stock-specific data questions (company financials, annual report figures), hand off to `stock_specialized_agent` instead of trying to answer from general knowledge.
- [] Step 3.3 — Update `FinWiseWorkflowService.cs` to create the `StockSpecializedAgentFactory` and register the stock agent in the handoff topology. The constructor receives `FoundryStockAgentConfig` and `TokenCredential` as additional dependencies. In `CreateAgentsAndWorkflow()`: (a) add the stock agent to the orchestrator's handoff list, (b) register the stock agent → orchestrator handoff, (c) register the advisor agent → stock agent handoff (spoke-to-spoke).
- [] Step 3.4 — Update `Program.cs` (composition root) to create `FoundryStockAgentConfig.FromEnvironment()`, resolve `TokenCredential`, and pass both to `FinWiseWorkflowService`.

### Phase 4: Testing

- [] Step 4.1 — Write unit tests for `FoundryStockAgentConfig.FromEnvironment()` (validates required env vars, default values)
- [] Step 4.2 — Write unit tests for `StockSpecializedAgentFactory.CreateAgent()` (verifies agent name, description, tool registration)
- [] Step 4.3 — Write unit tests for `FoundryStockAgentRunner` (mock `AIProjectClient` to verify correct Foundry API calls and response extraction)
- [] Step 4.4 — Update existing orchestrator routing tests (if any) to cover stock-specific query routing

### Phase 5: Documentation & Cleanup

- [] Step 5.1 — Add `STOCK_AGENT_PROJECT_ENDPOINT` and `STOCK_AGENT_NAME` to `appsettings.Development.json` example or environment setup documentation
- [] Step 5.2 — Update architecture diagrams in specs to reflect 4-agent topology

---

## Learnings & Notes

> Capture insights discovered during implementation for future reference.

### Patterns Discovered

- AgenticGuru PoC provides a proven pattern for calling Azure Foundry agents via `Azure.AI.Projects` SDK (get-or-create agent → Responses API → extract annotated text)
- The `AgentWorkflowBuilder.WithHandoffs()` API makes adding new spokes straightforward — just add to the orchestrator's handoff list
- Spoke-to-spoke handoffs are supported by the framework — `WithHandoffs(advisorAgent, [stockAgent])` enables direct escalation without routing through the hub

### Issues Encountered

- (To be captured during implementation)

### Notes for Future Work

- **v0.3 optimization**: Consider replacing the tool-based wrapper with a custom `IChatClient` adapter to eliminate the double-LLM call overhead
- **Cross-agent consultation**: Future versions could let the AdvisorAgent consult the StockSpecializedAgent for data-backed personalized recommendations
- **Additional companies**: The Foundry agent's file search can be expanded with more company documents without any code changes
