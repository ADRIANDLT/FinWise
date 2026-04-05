# FinWise Architecture — Mermaid Diagrams (v0.3)

**Date:** March 15, 2026  
**Status:** In Progress  
**Previous Version:** [v0.2 Mermaid Diagrams](FinWise-Architecture-Mermaid-Diagram-v0.2.md)

---

## 1. System Context Diagram

```mermaid
graph TB
    subgraph External["External Systems"]
        User["👤 User via AI Assistant<br/>(VS Code / Claude Desktop)"]
        AzureOAI["☁️ Azure OpenAI Service<br/>gpt-4o-mini"]
        CosmosDB["☁️ Azure Cosmos DB<br/>(User Profiles)"]
        Foundry["☁️ Azure AI Foundry<br/>(Stock Specialized Agent)"]
    end

    subgraph Infrastructure["Local Infrastructure (Docker Compose)"]
        subgraph Redis["Redis 7.4"]
            RedisAgentSessions["agentsession:* keys<br/>(Agent conversation state)"]
            RedisMcpInit["mcpinit:* keys<br/>(MCP session migration)"]
        end
    end

    subgraph FinWise["FinWise System"]
        McpServer["FinWise.McpServer<br/>(ASP.NET Core MCP Host)"]
        Workflow["FinWise.MultiAgentWorkflow<br/>(Class Library)"]
    end

    User -->|"MCP Streamable HTTP<br/>localhost:5000"| McpServer
    McpServer -->|"IChatClient"| Workflow
    Workflow -->|"Chat Completions API"| AzureOAI
    Workflow -->|"IUserProfileStore"| CosmosDB
    Workflow -->|"AIAgent (Foundry)"| Foundry
    Workflow -->|"RedisAgentSessionStore"| RedisAgentSessions
    McpServer -->|"RedisSessionMigrationHandler"| RedisMcpInit

    style Foundry fill:#4A90D9,stroke:#2E6DA4,color:#fff
    style Redis fill:#DC382D,stroke:#A52A2A,color:#fff
    style RedisAgentSessions fill:#F5C6C6,stroke:#A52A2A,color:#000
    style RedisMcpInit fill:#F5C6C6,stroke:#A52A2A,color:#000
```

---

## 1.1 System Architecture Overview

```mermaid
graph TB
    subgraph External["External Clients"]
        VSCode["VS Code<br/>(GitHub Copilot)"]
        Claude["Claude Desktop"]
    end

    subgraph MCP_Transport["MCP Transport Layer"]
        HTTP["Streamable HTTP<br/>(HTTP + SSE)"]
        SessionHeader["MCP-Session-Id<br/>Header"]
    end

    subgraph McpServer["FinWise.McpServer (Thin Host)"]
        McpEndpoint["/mcp Endpoint<br/>ModelContextProtocol.AspNetCore"]
        Tools["Tools/FinWiseTools.cs"]
        T1["🔧 run_finwise_workflow<br/>(query → advice/profile)"]
        T2["🔧 reset_conversation<br/>(clear session)"]
        InfraFolder["Infrastructure/<br/>McpSession, AgentSessionStorage,<br/>UserProfileStorage, AzureOpenAI,<br/>AzureAIFoundry, Logging"]
    end

    subgraph WorkflowLib["FinWise.MultiAgentWorkflow (Class Library — No MCP)"]
        direction TB

        subgraph Orchestration["Workflow/"]
            WFS["FinWiseWorkflowService<br/>ProcessMessageAsync()<br/>ResetSessionAsync()"]
            WR["WorkflowResponse<br/>(Response, AgentSessionId, WasReset)"]
        end

        subgraph AgentBlock["Agents/ (folder-per-agent + .prompt.md + ChatClientAgentOptions)"]
            Orch["🤖 OrchestratorAgent<br/>(Silent Router)<br/>Id='orchestrator_agent'"]
            Profile["🤖 UserProfileAgent<br/>get/set/delete profile tools<br/>Id='profile_agent'"]
            Advisor["🤖 AdvisorAgent<br/>General financial advice<br/>Id='advisor_agent'"]
            Stock["🤖 StockSpecializedAgent<br/>(Azure AI Foundry)<br/>Resolved by name"]
        end

        subgraph Session_Mgmt["Session/"]
            SM["AgentSessionManager<br/>(delegates to SDK store)"]
            SRF["SessionResetFlag<br/>SessionResetToken"]
            CRC["AgentSessionRunContext<br/>AsyncLocal ambient state"]
            SC["AgentSessionConstants<br/>(PROFILE_READY marker)"]
        end

        subgraph Infra_Lib["Infrastructure/"]
            IPS["IUserProfileStore"]
            InMem["InMemoryUserProfileStore"]
            Cosmos["CosmosDbUserProfileStore"]
        end

        subgraph SDK_Session["Session Store (SDK + Redis)"]
            ASS["AgentSessionStore<br/>(abstract class)"]
            IMASS["InMemoryAgentSessionStore<br/>(ConcurrentDictionary)"]
            RASS["RedisAgentSessionStore<br/>(StackExchange.Redis, TTL)"]
            ICS["IClearableSessionStore<br/>(explicit key deletion)"]
        end

        subgraph Domain["DomainModel/"]
            UP["UserProfile<br/>(immutable record)"]
        end
    end

    subgraph AI_Backend["AI Backend"]
        AzureOAI["Azure OpenAI<br/>(IChatClient)<br/>gpt-4o-mini"]
    end

    subgraph Foundry_Backend["Azure AI Foundry"]
        FoundryAgent["Stock Specialized<br/>Investment Agent<br/>Grounded in annual reports:<br/>Apple, Microsoft, Tesla,<br/>Nvidia, Amazon"]
    end

    subgraph Profile_Store["Profile Storage (Docker Compose)"]
        CosmosDB["Azure Cosmos DB Emulator<br/>(persistent profiles)"]
    end

    VSCode -->|"HTTP POST"| HTTP
    Claude -->|"HTTP POST"| HTTP
    HTTP --> SessionHeader
    SessionHeader --> McpEndpoint

    McpEndpoint --> Tools
    Tools --> T1
    Tools --> T2

    T1 -->|"mcpSessionId + query"| WFS
    T2 -->|"mcpSessionId"| WFS

    WFS --> SM
    WFS --> SRF
    WFS --> CRC

    WFS -->|"creates + executes"| Orch
    Orch -->|"handoff_to_profile_agent"| Profile
    Orch -->|"handoff_to_advisor_agent<br/>(only if PROFILE_READY)"| Advisor
    Orch -->|"handoff_to_stock_agent<br/>(only if PROFILE_READY)"| Stock
    Profile -->|"handoff_to_orchestrator"| Orch
    Advisor -->|"handoff_to_orchestrator"| Orch
    Stock -->|"handoff_to_orchestrator"| Orch

    Profile -->|"get/set/delete"| IPS
    IPS --> InMem
    IPS --> Cosmos
    Cosmos --> CosmosDB

    SM --> ASS
    ASS --> IMASS
    ASS --> RASS
    RASS --> RedisAS["agentsession:* keys"]
    InfraFolder -->|"RedisSessionMigrationHandler"| RedisMI["mcpinit:* keys"]

    subgraph RedisDocker["Redis 7.4 (Docker)"]
        RedisAS
        RedisMI
    end
    Orch --> AzureOAI
    Profile --> AzureOAI
    Advisor --> AzureOAI
    Stock -->|"HTTP (managed by SDK)"| FoundryAgent

    classDef external fill:#e1f5fe,stroke:#0288d1,color:#000
    classDef mcp fill:#f3e5f5,stroke:#7b1fa2,color:#000
    classDef workflow fill:#fff3e0,stroke:#ef6c00,color:#000
    classDef agent fill:#fff8e1,stroke:#f9a825,color:#000
    classDef session fill:#e8f5e9,stroke:#388e3c,color:#000
    classDef storage fill:#e0f2f1,stroke:#00897b,color:#000
    classDef ai fill:#ede7f6,stroke:#512da8,color:#000
    classDef domain fill:#fce4ec,stroke:#c62828,color:#000
    classDef foundry fill:#4A90D9,stroke:#2E6DA4,color:#fff
    classDef sdk fill:#E8EAF6,stroke:#3F51B5,color:#000

    class VSCode,Claude external
    class McpEndpoint,Tools,T1,T2,HTTP,SessionHeader,InfraFolder mcp
    class WFS,WR workflow
    class Orch,Profile,Advisor agent
    class Stock foundry
    class SM,SRF,CRC,SC session
    class IPS,InMem,Cosmos,CosmosDB storage
    class AzureOAI ai
    class UP domain
    class ASS,IMASS,RASS,ICS sdk
    class FoundryAgent foundry
```

---

## 2. Agent Workflow — Hub-and-Spoke with Profile Gate

```mermaid
graph TB
    subgraph ProfileGate["Code-Enforced Profile Gate"]
        direction TB
        Check{{"IsProfileReady(history)?"}}
        ProfileOnly["availableAgents = [profileAgent]"]
        AllAgents["availableAgents = [profileAgent, advisorAgent, stockAgent]"]
        Check -->|"No PROFILE_READY"| ProfileOnly
        Check -->|"PROFILE_READY found"| AllAgents
    end

    subgraph Workflow["Hub-and-Spoke Handoff Workflow"]
        Orch["🤖 Orchestrator Agent<br/>(Silent Router — Tool Calls Only)<br/>ChatClientAgent"]
        Profile["🤖 Profile Agent<br/>(Collects email, risk, goals, timeframe)<br/>ChatClientAgent + Tools"]
        Advisor["🤖 Advisor Agent<br/>(General Finance: retirement, bonds, budget)<br/>ChatClientAgent"]
        Stock["🤖 Stock Specialized Agent<br/>(Stock picks, analysis, company financials)<br/>AIAgent — Azure AI Foundry"]
    end

    ProfileOnly --> Orch
    AllAgents --> Orch

    Orch -->|"handoff_to_profile_agent<br/>(always available)"| Profile
    Orch -->|"handoff_to_advisor_agent<br/>(only if PROFILE_READY)"| Advisor
    Orch -->|"handoff_to_stock_agent<br/>(only if PROFILE_READY)"| Stock

    Profile -->|"handoff_to_orchestrator"| Orch
    Advisor -->|"handoff_to_orchestrator<br/>(specialized qs)"| Orch
    Stock -->|"handoff_to_orchestrator"| Orch

    Profile -->|"PROFILE_READY:<br/>email, risk, goals, timeframe"| Orch

    style Stock fill:#4A90D9,stroke:#2E6DA4,color:#fff
    style ProfileOnly fill:#FFA07A,stroke:#E08060,color:#000
    style AllAgents fill:#90EE90,stroke:#60C060,color:#000
```

---

## 3. Orchestrator Routing Decision Tree

```mermaid
graph TD
    Start(("User Message"))
    PRCheck{"PROFILE_READY<br/>in message history?"}
    
    Start --> PRCheck
    PRCheck -->|"No"| ProfileOnly["Route to Profile Agent<br/>(only available agent)"]
    PRCheck -->|"Yes"| Intent{"Check User Intent"}
    
    Intent -->|"Stock-related<br/>(buy stocks, stock picks,<br/>company financials,<br/>'what should I invest in')"| StockAgent["🤖 Stock Specialized Agent<br/>(Azure AI Foundry)"]
    Intent -->|"Profile management<br/>(show/update/delete profile)"| ProfileAgent["🤖 Profile Agent"]
    Intent -->|"General finance<br/>(retirement, bonds,<br/>budgeting, savings)"| AdvisorAgent["🤖 Advisor Agent"]
    Intent -->|"Unsupported specialization<br/>(real estate, crypto,<br/>commodities, forex)"| Unsupported["Orchestrator responds directly:<br/>'We don't currently offer<br/>specialized advisory for that area'"]

    style StockAgent fill:#4A90D9,stroke:#2E6DA4,color:#fff
    style Unsupported fill:#FFD700,stroke:#DAA520,color:#000
    style ProfileOnly fill:#FFA07A,stroke:#E08060,color:#000
```

---

## 4. Session Lifecycle (v0.3 — SDK AgentSessionStore + Redis)

```mermaid
sequenceDiagram
    participant Client as MCP Client
    participant MCP as McpServer
    participant WF as WorkflowService
    participant SM as SessionManager
    participant Store as AgentSessionStore<br/>(InMemory or Redis)
    participant Agents as Agent Workflow

    Client->>MCP: POST /mcp (tools/call: run_finwise_workflow)
    MCP->>MCP: Extract MCP-Session-Id via McpSessionAccessor
    MCP->>WF: ProcessMessageAsync(mcpSessionId, query)
    
    WF->>WF: CreateAgentsAndWorkflow(isProfileReady=false)
    WF->>SM: GetOrCreateSessionAsync(agent, agentSessionId)
    SM->>Store: GetSessionAsync(agent, agentSessionId)
    
    alt Session exists
        Store-->>SM: AgentSession (deserialized by SDK)
        SM->>SM: TryGetInMemoryChatHistory()
        SM-->>WF: (AgentSession, List<ChatMessage>)
    else New session
        Store-->>SM: new AgentSession (created by SDK)
        SM-->>WF: (new AgentSession, empty list)
    end

    WF->>WF: IsProfileReady(messageHistory)?
    
    alt Profile ready
        WF->>WF: Rebuild workflow with ALL agents
    end

    WF->>WF: Add user message to history
    WF->>Agents: ExecuteWorkflowAsync(workflow, messages, cancellationToken)
    
    Note over Agents: Hub-and-spoke handoffs<br/>Max 25 invocations<br/>60s timeout<br/>SessionResetFlag for tool-triggered reset

    Agents-->>WF: (response, outputs, lastAgent)
    WF->>WF: AppendUniqueMessages(outputs)
    
    WF->>SM: PersistSessionAsync(id, session, agent, messages)
    SM->>SM: SetInMemoryChatHistory(messages)
    SM->>Store: SaveSessionAsync(agent, id, session)
    
    WF-->>MCP: WorkflowResponse
    MCP-->>Client: MCP tool result (text)
```

---

## 5. Class Diagram — v0.3 Changes Highlighted

```mermaid
classDiagram
    direction TB

    namespace McpServerHost {
        class FinWiseTools {
            +run_finwise_workflow(query) string
            +reset_conversation() string
        }
        class McpSessionAccessor {
            «static»
            +GetSessionId(httpContext) string
        }
        class RedisSessionMigrationHandler {
            «ISessionMigrationHandler»
            +OnSessionInitializedAsync()
            +AllowSessionMigrationAsync()
        }
    }

    namespace WorkflowLayer {
        class FinWiseWorkflowService {
            -IChatClient _chatClient
            -IUserProfileStore _profileStore
            -AgentSessionManager _sessionManager
            -AIAgent? _stockAgent
            -MaxAgentInvocations : int = 25
            +ProcessMessageAsync(agentSessionId, query) WorkflowResponse
            +ResetSessionAsync(agentSessionId)
            -CreateAgentsAndWorkflow(id, isProfileReady) tuple
            -ExecuteWorkflowAsync(workflow, msgs, ct) tuple
        }
        class WorkflowResponse {
            «record»
            +Response : string
            +AgentSessionId : string
            +WasReset : bool
        }
    }

    namespace AgentFactories {
        class OrchestratorAgentFactory {
            +CreateAgent() ChatClientAgent
        }
        class UserProfileAgentFactory {
            +CreateAgent() ChatClientAgent
        }
        class AdvisorAgentFactory {
            +CreateAgent() ChatClientAgent
        }
        class StockSpecializedAgentFactory {
            -AIProjectClient _projectClient
            -string _agentName
            +CreateAgentAsync() AIAgent
        }
    }

    namespace SessionLayer {
        class AgentSessionManager {
            +GetOrCreateSessionAsync(agent, id) tuple~AgentSession+Messages~
            +PersistSessionAsync(id, session, agent, messages)
            +ClearSessionAsync(id)
        }
        class SessionResetFlag {
            «static»
            +Current : SessionResetToken?
            +Initialize() SessionResetToken
            +Clear()
        }
        class SessionResetToken {
            +IsRequested : bool
            +Request()
        }
        class AgentSessionConstants {
            «static»
            +ProfileReadyMarker : string
            +IsProfileReady(history) bool
            +ExtractUserIdFromMessageHistory(history) string?
        }
    }

    namespace InfraLayer {
        class AgentSessionStore {
            «abstract, SDK»
            +SaveSessionAsync(agent, id, session)
            +GetSessionAsync(agent, id) AgentSession
        }
        class InMemoryAgentSessionStore {
            «SDK: Microsoft.Agents.AI.Hosting»
        }
        class RedisAgentSessionStore {
            -IConnectionMultiplexer _redis
            -TimeSpan _ttl
            -string _agentId
            +SaveSessionAsync(agent, id, session)
            +GetSessionAsync(agent, id) AgentSession
            +ClearSessionAsync(conversationId)
        }
        class IClearableSessionStore {
            «interface»
            +ClearSessionAsync(conversationId)
        }
        class IUserProfileStore {
            «interface»
        }
        class InMemoryUserProfileStore
        class CosmosDbUserProfileStore
    }

    %% Wiring
    FinWiseTools --> FinWiseWorkflowService : calls
    FinWiseWorkflowService --> OrchestratorAgentFactory : creates
    FinWiseWorkflowService --> UserProfileAgentFactory : creates
    FinWiseWorkflowService --> AdvisorAgentFactory : creates
    FinWiseWorkflowService --> AgentSessionManager : manages
    FinWiseWorkflowService --> SessionResetFlag : checks
    FinWiseWorkflowService --> AgentSessionConstants : gates profile
    AgentSessionManager --> AgentSessionStore : delegates to
    AgentSessionStore <|-- InMemoryAgentSessionStore : extends (SDK)
    AgentSessionStore <|-- RedisAgentSessionStore : extends
    IClearableSessionStore <|.. RedisAgentSessionStore : implements
    UserProfileAgentFactory --> IUserProfileStore : CRUD
    IUserProfileStore <|.. InMemoryUserProfileStore : implements
    IUserProfileStore <|.. CosmosDbUserProfileStore : implements

    style StockSpecializedAgentFactory fill:#4A90D9,stroke:#2E6DA4,color:#fff
    style RedisAgentSessionStore fill:#DC382D,stroke:#A52A2A,color:#fff
```

---

## 6. Stock Specialized Agent — Foundry Integration Detail

```mermaid
graph LR
    subgraph FinWise["FinWise.MultiAgentWorkflow"]
        WF["WorkflowService"]
        Factory["StockSpecializedAgentFactory"]
        Agent["AIAgent<br/>(resolved by name)"]
    end

    subgraph Foundry["Azure AI Foundry"]
        Project["AI Project"]
        FoundryAgent["stock-specialized-<br/>investment-agent"]
        subgraph Grounding["Grounding Data (Annual Reports)"]
            Apple["📊 Apple"]
            MSFT["📊 Microsoft"]
            Tesla["📊 Tesla"]
            Nvidia["📊 Nvidia"]
            Amazon["📊 Amazon"]
        end
    end

    Factory -->|"AIProjectClient<br/>.GetAIAgentAsync(name)"| Project
    Project -->|"Resolves"| FoundryAgent
    FoundryAgent -->|"Returns AIAgent"| Agent
    WF -->|"Workflow handoff"| Agent
    Agent -->|"HTTP (managed by SDK)"| FoundryAgent
    FoundryAgent -->|"Retrieval-augmented"| Grounding

    style FoundryAgent fill:#4A90D9,stroke:#2E6DA4,color:#fff
    style Grounding fill:#F0F8FF,stroke:#B0C4DE
```

---

## 7. End-to-End Request Flow — Stock Advice

```mermaid
sequenceDiagram
    participant User as 👤 User (VS Code)
    participant MCP as MCP Server
    participant WF as WorkflowService
    participant Orch as 🤖 Orchestrator
    participant Stock as 🤖 Stock Agent (Foundry)
    participant Foundry as ☁️ Azure AI Foundry

    Note over User,Foundry: Assumes PROFILE_READY exists (profile completed earlier)

    User->>MCP: "Which stocks should I buy<br/>for aggressive short-term gains?"
    MCP->>WF: ProcessMessageAsync(sessionId, query)
    WF->>WF: IsProfileReady(history) → true
    WF->>WF: CreateAgentsAndWorkflow(isProfileReady=true)
    Note over WF: All 4 agents available

    WF->>Orch: RunStreamingAsync(workflow, messages)
    Orch->>Orch: Detect stock-related query
    Orch->>Stock: handoff_to_stock_agent
    Stock->>Foundry: HTTP (managed by SDK)
    Foundry->>Foundry: Retrieve annual reports<br/>(Apple, Microsoft, Tesla, Nvidia, Amazon)
    Foundry->>Foundry: Generate grounded response
    Foundry-->>Stock: Stock recommendations
    Stock-->>Orch: handoff_to_orchestrator
    Orch-->>WF: Response with stock advice

    WF->>WF: PersistSession + Messages
    WF-->>MCP: WorkflowResponse
    MCP-->>User: "Based on your aggressive profile,<br/>consider TSLA, NVDA, MSFT..."

    Note over User,Foundry: Response grounded in actual annual report data
```
