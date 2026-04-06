# FinWise Architecture — Mermaid Diagrams (v0.3.1)

**Date:** April 5, 2026  
**Status:** Implemented  
**Previous Version:** [v0.3 Mermaid Diagrams](FinWise-Architecture-Mermaid-Diagram-v0.3.md)

---

## 1. System Context Diagram

Updated to show the MCP Server running inside a Docker container alongside Redis and the CosmosDB emulator, all within the Docker Compose network.

```mermaid
graph TB
    subgraph External["External Systems"]
        User["👤 User via AI Assistant<br/>(VS Code / Claude Desktop)"]
        AzureOAI["☁️ Azure OpenAI Service<br/>gpt-4o-mini"]
        Foundry["☁️ Azure AI Foundry<br/>(Stock Specialized Agent)"]
    end

    subgraph DockerCompose["Docker Compose Network (finwise)"]
        McpContainer["🐳 finwise-mcp-server<br/>ASP.NET Core MCP Host<br/>aspnet:10.0 · non-root<br/>0.0.0.0:5000"]

        subgraph Redis["🐳 finwise-redis · Redis 7.4"]
            RedisAgentSessions["agentsession:* keys<br/>(Agent conversation state)"]
            RedisMcpInit["mcpinit:* keys<br/>(MCP session migration)"]
        end

        CosmosDB["🐳 finwise-cosmosdb-emulator<br/>Azure Cosmos DB Emulator<br/>(User Profiles)"]
    end

    User -->|"MCP Streamable HTTP<br/>localhost:5000"| McpContainer
    McpContainer -->|"Chat Completions API"| AzureOAI
    McpContainer -->|"FoundryAgent (Foundry)"| Foundry
    McpContainer -->|"RedisAgentSessionStore<br/>redis:6379"| RedisAgentSessions
    McpContainer -->|"RedisSessionMigrationHandler<br/>redis:6379"| RedisMcpInit
    McpContainer -->|"CosmosDbUserProfileStore<br/>cosmosdb-emulator:8081"| CosmosDB

    style McpContainer fill:#1565C0,stroke:#0D47A1,color:#fff
    style Foundry fill:#4A90D9,stroke:#2E6DA4,color:#fff
    style Redis fill:#DC382D,stroke:#A52A2A,color:#fff
    style RedisAgentSessions fill:#F5C6C6,stroke:#A52A2A,color:#000
    style RedisMcpInit fill:#F5C6C6,stroke:#A52A2A,color:#000
    style CosmosDB fill:#0D47A1,stroke:#0D47A1,color:#fff
```

---

## 2. Docker Compose Service Architecture

New in v0.3.1. Shows the three-service Docker Compose stack with health check dependencies, port mappings, and resource limits.

```mermaid
graph TB
    subgraph Host["Host Machine"]
        MCP_Clients["MCP Clients<br/>(VS Code, Claude Desktop)"]
        DevTools_Redis["Dev Tools<br/>(RedisInsight, redis-cli)"]
        DevTools_Cosmos["Dev Tools<br/>(Data Explorer, Azure Data Studio)"]
        ContainerTests["Container Tests<br/>(xUnit · SkippableFact)"]
    end

    subgraph DockerNetwork["Docker Compose Network (bridge)"]
        subgraph MCP_Service["finwise-mcp-server"]
            MCP["ASP.NET Core<br/>Kestrel 0.0.0.0:5000<br/>ASPNETCORE_ENVIRONMENT=Docker<br/>USER: app (non-root)"]
            Health["/health → 200 OK"]
            McpEndpoint["/mcp → MCP Protocol"]
        end

        subgraph Redis_Service["finwise-redis"]
            Redis["Redis 7.4 Alpine<br/>volatile-lru eviction<br/>RDB persistence"]
            RedisHealth["redis-cli ping"]
        end

        subgraph Cosmos_Service["finwise-cosmosdb-emulator"]
            Cosmos["CosmosDB Linux Emulator<br/>HTTPS :8081<br/>Data persistence volume"]
            CosmosHealth["curl -k emulator.pem"]
        end
    end

    MCP_Clients -->|"localhost:5000"| MCP
    DevTools_Redis -->|"localhost:6379"| Redis
    DevTools_Cosmos -->|"localhost:8081"| Cosmos
    ContainerTests -->|"localhost:5000"| MCP

    MCP -->|"redis:6379<br/>(Docker DNS)"| Redis
    MCP -->|"cosmosdb-emulator:8081<br/>(Docker DNS · insecure TLS)"| Cosmos
    MCP -->|"Azure OpenAI<br/>(external HTTPS)"| ExtAI["☁️ Azure OpenAI"]

    Redis_Service -.-|"service_healthy"| MCP_Service
    Cosmos_Service -.-|"service_healthy"| MCP_Service

    style MCP_Service fill:#1565C0,stroke:#0D47A1,color:#fff
    style Redis_Service fill:#DC382D,stroke:#A52A2A,color:#fff
    style Cosmos_Service fill:#0D47A1,stroke:#0D47A1,color:#fff
    style ExtAI fill:#7B1FA2,stroke:#4A148C,color:#fff
```

---

## 3. Dual Deployment Modes

New in v0.3.1. Compares the two supported deployment modes — full Docker stack vs. .NET host process with Docker infrastructure.

```mermaid
graph LR
    subgraph OptionA["Option A: Full Docker Stack"]
        direction TB
        A_Client["MCP Client"] -->|"localhost:5000"| A_Container["🐳 finwise-mcp-server<br/>(Docker container)"]
        A_Container -->|"redis:6379"| A_Redis["🐳 Redis"]
        A_Container -->|"cosmosdb-emulator:8081"| A_Cosmos["🐳 CosmosDB"]
        A_Container -->|"HTTPS"| A_AI["☁️ Azure OpenAI"]
        A_Note["Config: appsettings.Docker.json<br/>Secrets: .env file<br/>Start: docker compose up -d"]
    end

    subgraph OptionB["Option B: .NET Host Process"]
        direction TB
        B_Client["MCP Client"] -->|"localhost:5000"| B_Process["⚙️ dotnet run<br/>(host process)"]
        B_Process -->|"localhost:6379"| B_Redis["🐳 Redis"]
        B_Process -->|"localhost:8081"| B_Cosmos["🐳 CosmosDB"]
        B_Process -->|"HTTPS"| B_AI["☁️ Azure OpenAI"]
        B_Note["Config: appsettings.json<br/>Secrets: system env vars<br/>Start: dotnet run"]
    end

    style A_Container fill:#1565C0,stroke:#0D47A1,color:#fff
    style B_Process fill:#E65100,stroke:#BF360C,color:#fff
    style A_Redis fill:#DC382D,stroke:#A52A2A,color:#fff
    style B_Redis fill:#DC382D,stroke:#A52A2A,color:#fff
    style A_Cosmos fill:#0D47A1,stroke:#0D47A1,color:#fff
    style B_Cosmos fill:#0D47A1,stroke:#0D47A1,color:#fff
```

---

## 4. Docker Image Build Pipeline

New in v0.3.1. Shows the multi-stage Dockerfile producing a minimal runtime image.

```mermaid
graph TD
    subgraph Stage1["Stage 1: BUILD (sdk:10.0 ~900 MB)"]
        S1_Copy["COPY project files<br/>Directory.Build.props<br/>Directory.Packages.props<br/>*.csproj"]
        S1_Restore["dotnet restore<br/>(cached layer)"]
        S1_CopySrc["COPY src/<br/>(source code)"]
        S1_Publish["dotnet publish -c Release<br/>→ /app"]

        S1_Copy --> S1_Restore --> S1_CopySrc --> S1_Publish
    end

    subgraph Stage2["Stage 2: RUNTIME (aspnet:10.0 ~220 MB)"]
        S2_Curl["Install curl<br/>(health check tool)"]
        S2_User["USER $APP_UID<br/>(non-root, UID 1654)"]
        S2_App["COPY --from=build /app<br/>Published output only"]
        S2_Entry["ENTRYPOINT<br/>dotnet FinWise.McpServer.dll"]

        S2_Curl --> S2_User --> S2_App --> S2_Entry
    end

    S1_Publish -->|"COPY --from=build"| S2_App

    subgraph FinalImage["Final Image Contents"]
        FI_DLL["FinWise.McpServer.dll<br/>FinWise.MultiAgentWorkflow.dll"]
        FI_Config["appsettings.json<br/>appsettings.Docker.json"]
        FI_Deps["NuGet dependencies"]
        FI_No["✘ No SDK<br/>✘ No source code<br/>✘ No build artifacts"]
    end

    S2_Entry --> FinalImage

    style Stage1 fill:#FFF3E0,stroke:#E65100,color:#000
    style Stage2 fill:#E8F5E9,stroke:#2E7D32,color:#000
    style FinalImage fill:#E3F2FD,stroke:#1565C0,color:#000
```

---

## 5. Configuration & Environment Flow

New in v0.3.1. Shows how configuration is layered for the Docker environment.

```mermaid
graph LR
    subgraph Sources["Configuration Sources"]
        Base["appsettings.json<br/>────────────────<br/>Kestrel: localhost:5000<br/>Redis: localhost:6379<br/>CosmosDb: localhost:8081"]
        Docker["appsettings.Docker.json<br/>────────────────────<br/>Kestrel: 0.0.0.0:5000<br/>Redis: redis:6379<br/>CosmosDb: cosmosdb-emulator:8081"]
        EnvFile[".env (git-ignored)<br/>────────────────<br/>AZURE_OPENAI_ENDPOINT<br/>AZURE_OPENAI_API_KEY<br/>AZURE_OPENAI_DEPLOYMENT_NAME<br/>STOCK_AGENT_* (optional)"]
    end

    subgraph Compose["docker-compose.yml"]
        ComposeEnv["environment:<br/>ASPNETCORE_ENVIRONMENT=Docker<br/>AZURE_OPENAI_*=$&#123;...&#125;"]
    end

    subgraph Container["finwise-mcp-server Container"]
        ASPConfig["ASP.NET Core Configuration<br/>(merged, later overrides earlier)"]
    end

    Base -->|"baked into image"| ASPConfig
    Docker -->|"activated by<br/>ASPNETCORE_ENVIRONMENT=Docker"| ASPConfig
    EnvFile -->|"interpolated by Compose"| ComposeEnv
    ComposeEnv -->|"injected as env vars"| ASPConfig

    style Base fill:#FFF3E0,stroke:#E65100,color:#000
    style Docker fill:#E8F5E9,stroke:#2E7D32,color:#000
    style EnvFile fill:#FCE4EC,stroke:#C62828,color:#000
    style Container fill:#E3F2FD,stroke:#1565C0,color:#000
```

---

## 6. System Architecture Overview

Evolved from v0.3. The McpServer now runs inside a Docker container. The internal component structure is unchanged; what's new is the container boundary and the Docker DNS-based connections to Redis and CosmosDB.

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

    subgraph DockerCompose["Docker Compose Network"]

        subgraph McpServer["🐳 finwise-mcp-server (ASP.NET Core)"]
            McpEndpoint["/mcp Endpoint"]
            HealthEndpoint["/health Endpoint (NEW)"]
            Tools["Tools/FinWiseTools.cs"]
            T1["🔧 run_finwise_workflow"]
            T2["🔧 reset_conversation"]
            InfraFolder["Infrastructure/<br/>McpSession, AgentSessionStorage,<br/>UserProfileStorage, AzureOpenAI,<br/>AzureAIFoundry (bridge), Logging"]

            subgraph WorkflowLib["FinWise.MultiAgentWorkflow"]
                direction TB

                subgraph Orchestration["Workflow/"]
                    WFS["FinWiseWorkflowService"]
                end

                subgraph AgentBlock["Agents/"]
                    Orch["🤖 OrchestratorAgent"]
                    Profile["🤖 UserProfileAgent"]
                    Advisor["🤖 AdvisorAgent"]
                    Stock["🤖 StockSpecializedAgent<br/>(Azure AI Foundry)"]
                end

                subgraph Session_Mgmt["Session/"]
                    SM["AgentSessionManager"]
                    SRF["SessionResetFlag"]
                end

                subgraph Infra_Lib["Infrastructure/"]
                    IPS["IUserProfileStore"]
                    ASS["AgentSessionStore"]
                end
            end
        end

        subgraph RedisDocker["🐳 finwise-redis · Redis 7.4"]
            RedisAS["agentsession:* keys"]
            RedisMI["mcpinit:* keys"]
        end

        subgraph CosmosDocker["🐳 finwise-cosmosdb-emulator"]
            CosmosDB["User Profiles<br/>(persistent)"]
        end
    end

    subgraph AI_Backend["AI Backend (External)"]
        AzureOAI["Azure OpenAI<br/>gpt-4o-mini"]
    end

    subgraph Foundry_Backend["Azure AI Foundry (External)"]
        FoundryAgent["Stock Specialized<br/>Investment Agent"]
    end

    VSCode -->|"HTTP POST"| HTTP
    Claude -->|"HTTP POST"| HTTP
    HTTP --> SessionHeader
    SessionHeader --> McpEndpoint

    McpEndpoint --> Tools
    Tools --> T1
    Tools --> T2

    T1 --> WFS
    T2 --> WFS

    WFS --> Orch
    Orch --> Profile
    Orch --> Advisor
    Orch --> Stock

    Profile --> IPS
    IPS --> CosmosDB

    SM --> ASS
    ASS --> RedisAS
    InfraFolder --> RedisMI

    Orch --> AzureOAI
    Profile --> AzureOAI
    Advisor --> AzureOAI
    Stock -->|"HTTP (managed by SDK)"| FoundryAgent

    classDef container fill:#1565C0,stroke:#0D47A1,color:#fff
    classDef external fill:#e1f5fe,stroke:#0288d1,color:#000
    classDef mcp fill:#f3e5f5,stroke:#7b1fa2,color:#000
    classDef workflow fill:#fff3e0,stroke:#ef6c00,color:#000
    classDef agent fill:#fff8e1,stroke:#f9a825,color:#000
    classDef session fill:#e8f5e9,stroke:#388e3c,color:#000
    classDef storage fill:#e0f2f1,stroke:#00897b,color:#000
    classDef ai fill:#ede7f6,stroke:#512da8,color:#000
    classDef foundry fill:#4A90D9,stroke:#2E6DA4,color:#fff
    classDef redis fill:#DC382D,stroke:#A52A2A,color:#fff
    classDef health fill:#C8E6C9,stroke:#2E7D32,color:#000

    class VSCode,Claude external
    class McpEndpoint,Tools,T1,T2,HTTP,SessionHeader,InfraFolder mcp
    class HealthEndpoint health
    class WFS workflow
    class Orch,Profile,Advisor agent
    class Stock foundry
    class SM,SRF session
    class IPS,ASS storage
    class AzureOAI ai
    class FoundryAgent foundry
    class RedisDocker,RedisAS,RedisMI redis
    class CosmosDocker,CosmosDB container
```

---

## 7. Agent Workflow — Hub-and-Spoke with Profile Gate

Unchanged from v0.3. Docker does not affect agent orchestration — all container concerns are handled at the infrastructure layer.

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
        Stock["🤖 Stock Specialized Agent<br/>(Stock picks, analysis, company financials)<br/>FoundryAgent — Azure AI Foundry"]
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

## 8. Orchestrator Routing Decision Tree

Unchanged from v0.3.

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

## 9. Session Lifecycle

Unchanged from v0.3. The session flow is identical whether the server runs as a Docker container or a host process — the AgentSessionStore connects to Redis via a connection string, which differs only in hostname (`redis:6379` vs. `localhost:6379`).

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
        SM-->>WF: (AgentSession, List of ChatMessages)
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
    SM->>Store: SaveSessionAsync(agent, id, session)
    
    WF-->>MCP: WorkflowResponse
    MCP-->>Client: MCP tool result (text)
```

---

## 10. CosmosDB Emulator — Dual-Access Pattern

New in v0.3.1. Illustrates the networking challenge and the `LimitToEndpoint` design decision that enables both host and container clients to reach the same emulator.

```mermaid
graph TB
    subgraph Problem["Problem: Endpoint Discovery"]
        Emulator["CosmosDB Emulator<br/>Reports Docker-internal IP<br/>in account metadata"]
        SDK_Default["Cosmos SDK (default)<br/>Discovers endpoints from metadata<br/>Routes to Docker-internal IP"]
        Fail["❌ Connection fails<br/>(IP unreachable from host<br/>or from other containers)"]

        Emulator --> SDK_Default --> Fail
    end

    subgraph Solution["Solution: LimitToEndpoint = true"]
        SDK_Fixed["Cosmos SDK<br/>LimitToEndpoint = true<br/>Ignores metadata addresses"]
        ConnStr["Uses connection string<br/>endpoint ONLY"]

        SDK_Fixed --> ConnStr
    end

    subgraph DualAccess["Dual-Access Pattern"]
        Host["Host Process<br/>(dotnet run)"] -->|"localhost:8081<br/>(port mapping)"| EmulatorOK["🐳 CosmosDB Emulator<br/>:8081"]
        Container["🐳 finwise-mcp-server"] -->|"cosmosdb-emulator:8081<br/>(Docker DNS)"| EmulatorOK
    end

    ConnStr --> DualAccess

    Note1["Conditional: only when<br/>AllowInsecureTls = true<br/>(emulator mode)"]
    Note2["Production CosmosDB<br/>retains multi-region<br/>failover"]

    style Problem fill:#FFEBEE,stroke:#C62828,color:#000
    style Solution fill:#E8F5E9,stroke:#2E7D32,color:#000
    style DualAccess fill:#E3F2FD,stroke:#1565C0,color:#000
    style Fail fill:#EF5350,stroke:#C62828,color:#fff
    style EmulatorOK fill:#66BB6A,stroke:#2E7D32,color:#fff
```

---

## 11. Test Architecture — Shared Base Pattern

New in v0.3.1. Shows the shared test base class library and the two test project categories.

```mermaid
graph TB
    subgraph TestProjects["Test Projects"]
        subgraph Integration["IntegrationTests"]
            E2E["EndToEndMcpTests<br/>6× [Fact]<br/>Server: dotnet run<br/>Fails if server down"]
        end

        subgraph Container["ContainerTests"]
            Dockerized["DockerizedMcpTests<br/>4× [SkippableFact]<br/>Reused MCP protocol tests"]
            DockerSpecific["DockerContainerSpecificTests<br/>5× [SkippableFact]<br/>Docker-only validations"]
        end
    end

    subgraph SharedLib["E2ETestBase (class library — not a test project)"]
        Base["McpEndToEndTestBase<br/>─────────────────────<br/>MCP protocol helpers<br/>SSE streaming reads<br/>Profile setup utilities<br/>Configurable base URL"]
    end

    E2E -->|"inherits"| Base
    Dockerized -->|"inherits"| Base
    DockerSpecific -->|"inherits"| Base

    subgraph Targets["Server Targets"]
        LocalServer["⚙️ dotnet run<br/>localhost:5000"]
        DockerServer["🐳 docker compose<br/>localhost:5000"]
    end

    E2E -.->|"connects to"| LocalServer
    Dockerized -.->|"connects to"| DockerServer
    DockerSpecific -.->|"connects to"| DockerServer

    subgraph HealthCheck["ContainerHealthCheck Utility"]
        Poll["Polling pattern<br/>GET /health every 500ms<br/>Timeout: 5s (quick) / 60s (wait)"]
        Skip["Skip.IfNot(reachable)<br/>→ graceful test skip"]
    end

    Dockerized --> HealthCheck
    DockerSpecific --> HealthCheck

    style Integration fill:#FFF3E0,stroke:#E65100,color:#000
    style Container fill:#E3F2FD,stroke:#1565C0,color:#000
    style SharedLib fill:#E8F5E9,stroke:#2E7D32,color:#000
    style HealthCheck fill:#F3E5F5,stroke:#7B1FA2,color:#000
```

---

## 12. Test Pyramid

New in v0.3.1. Shows the full test pyramid with the container test layer added.

```mermaid
graph TB
    subgraph Pyramid["Test Pyramid (v0.3.1)"]
        direction TB
        ContainerE2E["🔝 E2E Container Tests (9)<br/>4 reused MCP protocol + 5 Docker-specific<br/>Validates: Dockerfile, networking, env vars, config overrides"]
        LocalE2E["E2E Local Tests (6)<br/>Full MCP protocol against dotnet run<br/>Validates: profile, sessions, tools, stock handoff"]
        ComponentIntegration["Integration Tests (per-component)<br/>Redis, CosmosDB, Stock Agent (Foundry)<br/>Validates: store implementations against live infrastructure"]
        UnitTests["🔻 Unit Tests (fast, isolated)<br/>MultiAgentWorkflow (~66) + McpServer (~9)<br/>Validates: business logic, session management, routing"]
    end

    ContainerE2E --> LocalE2E --> ComponentIntegration --> UnitTests

    style ContainerE2E fill:#1565C0,stroke:#0D47A1,color:#fff
    style LocalE2E fill:#FFF3E0,stroke:#E65100,color:#000
    style ComponentIntegration fill:#E8F5E9,stroke:#2E7D32,color:#000
    style UnitTests fill:#F5F5F5,stroke:#9E9E9E,color:#000
```

---

## 13. Class Diagram — v0.3.1 Changes Highlighted

Evolved from v0.3. Key changes: `StockSpecializedAgentFactory` now returns `FoundryAgent` (via two-step resolution from `Microsoft.Agents.AI.Foundry`) instead of `AIAgent` directly. The `/health` endpoint was added for Docker support. The core class structure is otherwise unchanged.

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
            -FoundryAgent? _stockAgent
            -MaxAgentInvocations : int = 25
            +ProcessMessageAsync(agentSessionId, query) WorkflowResponse
            +ResetSessionAsync(agentSessionId)
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
            +CreateAgentAsync() FoundryAgent
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

## 14. Stock Specialized Agent — Foundry Integration Detail

Updated from v0.3. The MAF 1.0 GA upgrade changed the agent resolution pattern from a single-step convenience method to an explicit two-step flow: fetch the `ProjectsAgentRecord` from the Azure SDK, then adapt it into a `FoundryAgent` via the bridge package. The Foundry agent still runs in the cloud and is accessed via HTTP regardless of deployment mode.

```mermaid
graph LR
    subgraph FinWise["FinWise.MultiAgentWorkflow"]
        WF["WorkflowService"]
        Factory["StockSpecializedAgentFactory"]
        Agent["FoundryAgent<br/>(adapted from ProjectsAgentRecord)"]
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

    Factory -->|"Step 1: AgentAdministrationClient<br/>.GetAgentAsync(name)"| Project
    Project -->|"Returns ProjectsAgentRecord"| Factory
    Factory -->|"Step 2: AsAIAgent(record)<br/>(bridge: M.A.AI.Foundry)"| Agent
    WF -->|"Workflow handoff"| Agent
    Agent -->|"HTTP (managed by SDK)"| FoundryAgent
    FoundryAgent -->|"Retrieval-augmented"| Grounding

    style FoundryAgent fill:#4A90D9,stroke:#2E6DA4,color:#fff
    style Grounding fill:#F0F8FF,stroke:#B0C4DE,color:#000
```

---

## 15. End-to-End Request Flow — Stock Advice (Containerized)

Evolved from v0.3. Shows the same request flow now originating from a containerized MCP server.

```mermaid
sequenceDiagram
    participant User as 👤 User (VS Code)
    participant Docker as 🐳 Docker Network
    participant MCP as finwise-mcp-server
    participant WF as WorkflowService
    participant Orch as 🤖 Orchestrator
    participant Stock as 🤖 Stock Agent (Foundry)
    participant Foundry as ☁️ Azure AI Foundry

    Note over User,Foundry: Assumes PROFILE_READY exists (profile completed earlier)

    User->>Docker: POST localhost:5000/mcp
    Docker->>MCP: Forward to container (0.0.0.0:5000)
    MCP->>WF: ProcessMessageAsync(sessionId, query)
    WF->>WF: IsProfileReady(history) → true
    WF->>WF: CreateAgentsAndWorkflow(isProfileReady=true)
    Note over WF: All 4 agents available

    WF->>Orch: RunStreamingAsync(workflow, messages)
    Orch->>Orch: Detect stock-related query
    Orch->>Stock: handoff_to_stock_agent
    Stock->>Foundry: HTTP (managed by SDK, external)
    Foundry->>Foundry: Retrieve annual reports
    Foundry->>Foundry: Generate grounded response
    Foundry-->>Stock: Stock recommendations
    Stock-->>Orch: handoff_to_orchestrator
    Orch-->>WF: Response with stock advice

    WF->>WF: PersistSession + Messages
    WF-->>MCP: WorkflowResponse
    MCP-->>Docker: MCP tool result (SSE)
    Docker-->>User: Stock recommendations

    Note over User,Foundry: Response grounded in actual annual report data
```

---

## Diagram Index

| # | Diagram | Status | Description |
|---|---------|--------|-------------|
| 1 | System Context | **Updated** | Docker Compose boundary wrapping all three services |
| 2 | Docker Compose Service Architecture | **New** | Three-service stack with health dependencies and port mappings |
| 3 | Dual Deployment Modes | **New** | Option A (full Docker) vs. Option B (.NET host process) |
| 4 | Docker Image Build Pipeline | **New** | Multi-stage Dockerfile: build → runtime |
| 5 | Configuration & Environment Flow | **New** | appsettings layering + .env → Compose → container |
| 6 | System Architecture Overview | **Updated** | Components now inside Docker container boundary |
| 7 | Agent Workflow — Hub-and-Spoke | Unchanged | Profile gate + hub-and-spoke handoffs |
| 8 | Orchestrator Routing Decision Tree | Unchanged | Intent-based routing with profile gate |
| 9 | Session Lifecycle | Unchanged | SDK AgentSessionStore + Redis (same flow, different hostname) |
| 10 | CosmosDB Dual-Access Pattern | **New** | LimitToEndpoint design decision for emulator networking |
| 11 | Test Architecture — Shared Base | **New** | E2ETestBase + IntegrationTests + ContainerTests |
| 12 | Test Pyramid | **New** | Full test layers including container E2E |
| 13 | Class Diagram | **Updated** | `FoundryAgent` replaces `AIAgent` in StockSpecializedAgentFactory |
| 14 | Stock Agent — Foundry Integration | **Updated** | Two-step resolution: `ProjectsAgentRecord` → `FoundryAgent` (MAF 1.0 GA) |
| 15 | E2E Request Flow — Stock Advice | **Updated** | Shows Docker network hop in containerized flow |
