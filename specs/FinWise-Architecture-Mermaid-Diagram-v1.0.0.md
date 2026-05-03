# FinWise Architecture — Mermaid Diagrams (v1.0.0)

**Date:** April 17, 2026  
**Status:** Implemented  
**Previous Version:** [v0.3.1 Mermaid Diagrams](FinWise-Architecture-Mermaid-Diagram-v0.3.1.md)

---

## 1. System Context Diagram

Updated to show Azure cloud data stores as alternatives to local Docker services, and the Azure Container Apps deployment as the fully cloud-native option. The Docker image is published to Docker Hub.

```mermaid
graph TB
    subgraph External["External Systems"]
        User["👤 User via AI Assistant<br/>(VS Code / Claude Desktop)"]
        AzureOAI["☁️ Azure OpenAI Service<br/>gpt-4o-mini"]
        Foundry["☁️ Azure AI Foundry<br/>(Stock Specialized Agent)"]
    end

    subgraph DockerCompose["Docker Compose Network (finwise) — Local"]
        McpContainer["🐳 finwise-mcp-server<br/>ASP.NET Core MCP Host<br/>aspnet:10.0 · non-root<br/>0.0.0.0:5000"]

        subgraph Redis["🐳 finwise-redis · Redis 7.4"]
            RedisAgentSessions["agentsession:* keys<br/>(Agent conversation state)"]
            RedisMcpInit["mcpinit:* keys<br/>(MCP session migration)"]
        end

        CosmosDB["🐳 finwise-cosmosdb-emulator<br/>Azure Cosmos DB Emulator<br/>(User Profiles)"]
    end

    subgraph AzureCloud["☁️ Azure Cloud"]
        ACA["☁️ Azure Container Apps<br/>finwise-mcp-server-container-app<br/>HTTPS · 1–5 replicas · East US 2"]
        AzureRedis["Azure Managed Redis<br/>Balanced B0 · East US<br/>Port 10000 · Plaintext"]
        AzureCosmos["Azure Cosmos DB<br/>Serverless · NoSQL API<br/>FinWise / UserProfiles"]
    end

    subgraph DockerHub["Docker Hub"]
        Image["finwiseproject/<br/>finwise-mcp-server:1.0.0"]
    end

    User -->|"MCP Streamable HTTP<br/>localhost:5000"| McpContainer
    User -->|"MCP over HTTPS<br/>*.azurecontainerapps.io/mcp"| ACA
    McpContainer -->|"Chat Completions API"| AzureOAI
    McpContainer -->|"FoundryAgent (Foundry)"| Foundry
    McpContainer -->|"RedisAgentSessionStore"| RedisAgentSessions
    McpContainer -->|"RedisSessionMigrationHandler"| RedisMcpInit
    McpContainer -->|"CosmosDbUserProfileStore"| CosmosDB

    McpContainer -.->|"OR: Azure Managed Redis<br/>*.redis.azure.net:10000"| AzureRedis
    McpContainer -.->|"OR: Azure Cosmos DB<br/>*.documents.azure.com:443"| AzureCosmos

    Image -->|"Pulled by"| ACA
    ACA -->|"Azure backbone"| AzureRedis
    ACA -->|"Azure backbone"| AzureCosmos
    ACA -->|"Chat Completions API"| AzureOAI
    ACA -->|"FoundryAgent"| Foundry

    style McpContainer fill:#1565C0,stroke:#0D47A1,color:#fff
    style Foundry fill:#4A90D9,stroke:#2E6DA4,color:#fff
    style Redis fill:#DC382D,stroke:#A52A2A,color:#fff
    style RedisAgentSessions fill:#F5C6C6,stroke:#A52A2A,color:#000
    style RedisMcpInit fill:#F5C6C6,stroke:#A52A2A,color:#000
    style CosmosDB fill:#0D47A1,stroke:#0D47A1,color:#fff
    style AzureRedis fill:#DC382D,stroke:#A52A2A,color:#fff
    style AzureCosmos fill:#0D47A1,stroke:#0D47A1,color:#fff
    style AzureCloud fill:#E3F2FD,stroke:#1565C0,color:#000
    style ACA fill:#4A148C,stroke:#7B1FA2,color:#fff
    style Image fill:#FFF3E0,stroke:#E65100,color:#000
```

---

## 2. Four Deployment Modes

Updated from v0.3.1 (was 2 modes). Shows the four supported deployment modes including the new Azure-connected mode and the fully cloud-native Azure Container Apps deployment.

```mermaid
graph LR
    subgraph OptionA["Option A: Full Docker Stack"]
        direction TB
        A_Client["MCP Client"] -->|"localhost:5000"| A_Container["🐳 finwise-mcp-server<br/>(Docker container)"]
        A_Container -->|"finwise-redis:6379"| A_Redis["🐳 Redis"]
        A_Container -->|"finwise-cosmosdb-emulator:8081"| A_Cosmos["🐳 CosmosDB Emulator"]
        A_Container -->|"HTTPS"| A_AI["☁️ Azure OpenAI"]
        A_Note["Config: appsettings.Docker.json<br/>Secrets: .env file<br/>Start: docker compose up -d"]
    end

    subgraph OptionB["Option B: .NET Host Process"]
        direction TB
        B_Client["MCP Client"] -->|"localhost:5000"| B_Process["⚙️ dotnet run<br/>(host process)"]
        B_Process -->|"localhost:6379"| B_Redis["🐳 Redis"]
        B_Process -->|"localhost:8081"| B_Cosmos["🐳 CosmosDB Emulator"]
        B_Process -->|"HTTPS"| B_AI["☁️ Azure OpenAI"]
        B_Note["Config: appsettings.json<br/>Secrets: system env vars<br/>Start: dotnet run"]
    end

    subgraph OptionC["Option C: Server → Azure DBs (NEW)"]
        direction TB
        C_Client["MCP Client"] -->|"localhost:5000"| C_Container["🐳 finwise-mcp-server<br/>(Docker container)"]
        C_Container -->|"*.redis.azure.net:10000"| C_Redis["☁️ Azure Managed Redis"]
        C_Container -->|"*.documents.azure.com:443"| C_Cosmos["☁️ Azure Cosmos DB<br/>Serverless"]
        C_Container -->|"HTTPS"| C_AI["☁️ Azure OpenAI"]
        C_Note["Config: .env + .env.azure<br/>Start: docker compose -f<br/>docker-compose.finwise.yml<br/>--env-file .env --env-file .env.azure"]
    end

    subgraph OptionD["Option D: Azure Container Apps (Full Cloud, NEW)"]
        direction TB
        D_Client["MCP Client"] -->|"HTTPS<br/>*.azurecontainerapps.io/mcp"| D_ACA["☁️ Azure Container Apps<br/>finwise-mcp-server-container-app<br/>1–5 replicas · East US 2"]
        D_ACA -->|"Azure backbone"| D_Redis["☁️ Azure Managed Redis"]
        D_ACA -->|"Azure backbone"| D_Cosmos["☁️ Azure Cosmos DB<br/>Serverless"]
        D_ACA -->|"Azure backbone"| D_AI["☁️ Azure OpenAI"]
        D_Note["Image: Docker Hub<br/>finwiseproject/finwise-mcp-server:1.0.0<br/>Env vars: ACA configuration"]
    end

    style A_Container fill:#1565C0,stroke:#0D47A1,color:#fff
    style B_Process fill:#E65100,stroke:#BF360C,color:#fff
    style C_Container fill:#1565C0,stroke:#0D47A1,color:#fff
    style A_Redis fill:#DC382D,stroke:#A52A2A,color:#fff
    style B_Redis fill:#DC382D,stroke:#A52A2A,color:#fff
    style C_Redis fill:#DC382D,stroke:#A52A2A,color:#fff
    style A_Cosmos fill:#0D47A1,stroke:#0D47A1,color:#fff
    style B_Cosmos fill:#0D47A1,stroke:#0D47A1,color:#fff
    style C_Cosmos fill:#0D47A1,stroke:#0D47A1,color:#fff
    style D_ACA fill:#4A148C,stroke:#7B1FA2,color:#fff
    style D_Redis fill:#DC382D,stroke:#A52A2A,color:#fff
    style D_Cosmos fill:#0D47A1,stroke:#0D47A1,color:#fff
```

---

## 3. Three-File Docker Compose Architecture

New in v1.0.0. Shows the three compose files, their relationships, and how they enable the three deployment modes.

```mermaid
graph TB
    subgraph ComposeFiles["Docker Compose Files"]
        Infra["docker-compose.infra.yml<br/>────────────────────<br/>finwise-cosmosdb-emulator<br/>finwise-redis<br/>volumes: cosmosdb-data, redis-data"]
        Finwise["docker-compose.finwise.yml<br/>──────────────────────<br/>finwise-mcp-server<br/>(single source of truth)<br/>No depends_on on infra"]
        Main["docker-compose.yml<br/>──────────────────<br/>extends: finwise-mcp-server<br/>include: infra<br/>adds depends_on"]
    end

    subgraph UsageModes["Usage Modes"]
        ModeA["Option A: Full Local Stack<br/>docker compose up -d"]
        ModeB["Option B: Infra Only<br/>docker compose -f<br/>docker-compose.infra.yml up -d<br/>+ dotnet run"]
        ModeC["Option C: Server → Azure<br/>docker compose -f<br/>docker-compose.finwise.yml<br/>--env-file .env<br/>--env-file .env.azure up -d"]
    end

    Main -->|"uses"| Infra
    Main -->|"extends"| Finwise
    ModeA -->|"reads"| Main
    ModeB -->|"reads"| Infra
    ModeC -->|"reads"| Finwise

    style Infra fill:#E8F5E9,stroke:#2E7D32,color:#000
    style Finwise fill:#1565C0,stroke:#0D47A1,color:#fff
    style Main fill:#FFF3E0,stroke:#E65100,color:#000
    style ModeC fill:#E3F2FD,stroke:#1565C0,color:#000
```

---

## 4. Configuration & Environment Flow

Updated from v0.3.1. Shows the full layered configuration precedence chain including `FINWISE_*` env vars, `ForceInMemoryData` master toggle, and the layered `.env` architecture.

```mermaid
graph LR
    subgraph Sources["Configuration Sources"]
        Base["appsettings.json<br/>────────────────<br/>ForceInMemoryData: true<br/>CosmosDb:Enabled: false<br/>Redis:Enabled: false<br/>Kestrel: localhost:5000<br/>Redis: localhost:6379<br/>CosmosDb: localhost:8081"]
        Docker["appsettings.Docker.json<br/>────────────────────<br/>ForceInMemoryData: false<br/>CosmosDb:Enabled: true<br/>Redis:Enabled: true<br/>Kestrel: 0.0.0.0:5000<br/>Redis: finwise-redis:6379<br/>CosmosDb: finwise-cosmosdb-emulator:8081"]
        EnvBase[".env (base, git-ignored)<br/>────────────────<br/>AZURE_OPENAI_*<br/>STOCK_AGENT_* (optional)<br/>FINWISE_COSMOSDB_*=local<br/>FINWISE_REDIS_*=local"]
        EnvAzure[".env.azure (overrides)<br/>────────────────────<br/>FINWISE_COSMOSDB_ENDPOINT=<br/>  *.documents.azure.com<br/>FINWISE_COSMOSDB_KEY=<key><br/>FINWISE_REDIS_CONNECTION_<br/>  STRING=*.azure.net:10000"]
    end

    subgraph MasterToggle["Master Toggle"]
        Force["FINWISE_FORCE_IN_MEMORY_DATA<br/>= true → skip all stores<br/>= false → check individual flags"]
    end

    subgraph Container["finwise-mcp-server Container"]
        ASPConfig["ASP.NET Core Configuration<br/>(merged, later overrides earlier)"]
        Factories["Factory Classes<br/>────────────<br/>AgentSessionStoreFactory<br/>UserProfileStoreFactory"]
        Decision{"ForceInMemoryData?"}
        InMemory["All stores: InMemory"]
        CheckFlags["Check Redis:Enabled<br/>+ CosmosDb:Enabled"]
    end

    Base -->|"baked into image"| ASPConfig
    Docker -->|"activated by<br/>ASPNETCORE_ENVIRONMENT=Docker"| ASPConfig
    EnvBase -->|"interpolated by Compose"| ASPConfig
    EnvAzure -->|"layered override<br/>(--env-file .env<br/>--env-file .env.azure)"| ASPConfig
    ASPConfig --> Factories
    Factories --> Decision
    Decision -->|"true"| InMemory
    Decision -->|"false"| CheckFlags
    Force -.->|"short-circuits"| Decision

    style Base fill:#FFF3E0,stroke:#E65100,color:#000
    style Docker fill:#E8F5E9,stroke:#2E7D32,color:#000
    style EnvBase fill:#FCE4EC,stroke:#C62828,color:#000
    style EnvAzure fill:#E3F2FD,stroke:#1565C0,color:#000
    style MasterToggle fill:#FFECB3,stroke:#FF8F00,color:#000
    style Container fill:#F3E5F5,stroke:#7B1FA2,color:#000
```

---

## 5. Docker Compose Service Architecture

Updated from v0.3.1. Shows the three-service Docker Compose stack with health check dependencies and dual data store targets.

```mermaid
graph TB
    subgraph Host["Host Machine"]
        MCP_Clients["MCP Clients<br/>(VS Code, Claude Desktop)"]
        DevTools_Redis["Dev Tools<br/>(RedisInsight, redis-cli)"]
        DevTools_Cosmos["Dev Tools<br/>(Data Explorer)"]
        ContainerTests["Container Tests<br/>(xUnit · SkippableFact)"]
    end

    subgraph DockerNetwork["Docker Compose Network (bridge)"]
        subgraph MCP_Service["finwise-mcp-server"]
            MCP["ASP.NET Core<br/>Kestrel 0.0.0.0:5000<br/>ASPNETCORE_ENVIRONMENT=Docker<br/>USER: app (non-root)"]
            Health["/health → 200 OK"]
            McpEndpoint["/mcp → MCP Protocol"]
            Tools["🔧 run_finwise_workflow<br/>🔧 reset_conversation<br/>🔧 get_storage_info (NEW)"]
        end

        subgraph Redis_Service["finwise-redis"]
            Redis["Redis 7.4 Alpine<br/>volatile-lru eviction<br/>RDB persistence"]
        end

        subgraph Cosmos_Service["finwise-cosmosdb-emulator"]
            Cosmos["CosmosDB Linux Emulator<br/>HTTPS :8081<br/>Data persistence volume"]
        end
    end

    subgraph AzureTargets["☁️ Azure Cloud (Option C)"]
        AzureRedis["Azure Managed Redis<br/>*.redis.azure.net:10000"]
        AzureCosmos["Azure Cosmos DB Serverless<br/>*.documents.azure.com:443"]
    end

    MCP_Clients -->|"localhost:5000"| MCP
    DevTools_Redis -->|"localhost:6379"| Redis
    DevTools_Cosmos -->|"localhost:8081"| Cosmos
    ContainerTests -->|"localhost:5000"| MCP

    MCP -->|"finwise-redis:6379<br/>(Option A)"| Redis
    MCP -->|"finwise-cosmosdb-emulator:8081<br/>(Option A)"| Cosmos
    MCP -.->|"*.azure.net:10000<br/>(Option C)"| AzureRedis
    MCP -.->|"*.azure.com:443<br/>(Option C)"| AzureCosmos
    MCP -->|"Azure OpenAI<br/>(external HTTPS)"| ExtAI["☁️ Azure OpenAI"]

    Redis_Service -.-|"service_healthy"| MCP_Service
    Cosmos_Service -.-|"service_healthy"| MCP_Service

    style MCP_Service fill:#1565C0,stroke:#0D47A1,color:#fff
    style Redis_Service fill:#DC382D,stroke:#A52A2A,color:#fff
    style Cosmos_Service fill:#0D47A1,stroke:#0D47A1,color:#fff
    style ExtAI fill:#7B1FA2,stroke:#4A148C,color:#fff
    style AzureTargets fill:#E3F2FD,stroke:#1565C0,color:#000
    style AzureRedis fill:#DC382D,stroke:#A52A2A,color:#fff
    style AzureCosmos fill:#0D47A1,stroke:#0D47A1,color:#fff
```

---

## 6. Docker Image Build Pipeline

Unchanged from v0.3.1. Multi-stage Dockerfile producing a minimal runtime image.

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

## 7. System Architecture Overview

Updated from v0.3.1. Shows three MCP tools (added `get_storage_info`), Azure cloud alternatives for Redis and CosmosDB, and the factory-based store selection.

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

    subgraph DockerCompose["Docker Compose Network / Host Process"]

        subgraph McpServer["finwise-mcp-server (ASP.NET Core)"]
            McpEndpoint["/mcp Endpoint"]
            HealthEndpoint["/health Endpoint"]
            Tools["Tools/FinWiseTools.cs"]
            T1["🔧 run_finwise_workflow"]
            T2["🔧 reset_conversation"]
            T3["🔧 get_storage_info (NEW)"]
            InfraFolder["Infrastructure/<br/>Factories, McpSession,<br/>AzureOpenAI, AzureAIFoundry,<br/>Logging"]

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

        subgraph LocalStores["Local Docker Stores"]
            RedisDocker["🐳 finwise-redis · Redis 7.4<br/>agentsession:* · mcpinit:*"]
            CosmosDocker["🐳 finwise-cosmosdb-emulator<br/>User Profiles"]
        end
    end

    subgraph AzureStores["☁️ Azure Cloud Stores"]
        AzureRedis["Azure Managed Redis<br/>*.redis.azure.net:10000"]
        AzureCosmos["Azure Cosmos DB Serverless<br/>*.documents.azure.com:443"]
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
    Tools --> T3

    T1 --> WFS
    T2 --> WFS

    WFS --> Orch
    Orch --> Profile
    Orch --> Advisor
    Orch --> Stock

    Profile --> IPS
    IPS --> CosmosDocker
    IPS -.->|"OR"| AzureCosmos

    SM --> ASS
    ASS --> RedisDocker
    ASS -.->|"OR"| AzureRedis
    InfraFolder --> RedisDocker

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
    classDef azure fill:#E3F2FD,stroke:#1565C0,color:#000

    class VSCode,Claude external
    class McpEndpoint,Tools,T1,T2,T3,HTTP,SessionHeader,InfraFolder mcp
    class HealthEndpoint health
    class WFS workflow
    class Orch,Profile,Advisor agent
    class Stock foundry
    class SM,SRF session
    class IPS,ASS storage
    class AzureOAI ai
    class FoundryAgent foundry
    class RedisDocker redis
    class CosmosDocker container
    class AzureRedis redis
    class AzureCosmos container
    class AzureStores azure
```

---

## 8. Data Store Selection Flow

New in v1.0.0. Shows the factory-based decision tree that selects between in-memory, local, and Azure data stores at startup.

```mermaid
graph TD
    Start(("Application Startup"))
    ForceCheck{"FINWISE_FORCE_<br/>IN_MEMORY_DATA?"}

    Start --> ForceCheck
    ForceCheck -->|"true"| AllInMemory["All stores: InMemory<br/>(zero infrastructure)"]

    ForceCheck -->|"false"| CosmosCheck{"CosmosDb:Enabled?"}
    CosmosCheck -->|"true"| CosmosFactory["UserProfileStoreFactory<br/>creates CosmosDbUserProfileStore"]
    CosmosCheck -->|"false"| ProfileInMemory["Profiles: InMemoryUserProfileStore"]

    ForceCheck -->|"false"| RedisCheck{"Redis:Enabled?"}
    RedisCheck -->|"true"| RedisFactory["AgentSessionStoreFactory<br/>creates RedisAgentSessionStore<br/>+ RedisSessionMigrationHandler"]
    RedisCheck -->|"false"| SessionInMemory["Sessions: InMemoryAgentSessionStore<br/>MCP Migration: Disabled"]

    CosmosFactory --> EndpointCheck{"AllowInsecureTls?"}
    EndpointCheck -->|"true (emulator)"| EmulatorCosmos["Connect to emulator<br/>LimitToEndpoint=true<br/>Gateway mode"]
    EndpointCheck -->|"false (Azure)"| AzureCosmos["Connect to Azure<br/>Cosmos DB Serverless<br/>Standard TLS"]

    RedisFactory --> ConnStringCheck{"Connection string<br/>contains *.azure.net?"}
    ConnStringCheck -->|"Yes"| AzureRedis["Connect to Azure<br/>Managed Redis<br/>ssl=False required"]
    ConnStringCheck -->|"No"| LocalRedis["Connect to local<br/>Docker Redis"]

    style AllInMemory fill:#F5F5F5,stroke:#9E9E9E,color:#000
    style ProfileInMemory fill:#F5F5F5,stroke:#9E9E9E,color:#000
    style SessionInMemory fill:#F5F5F5,stroke:#9E9E9E,color:#000
    style EmulatorCosmos fill:#0D47A1,stroke:#0D47A1,color:#fff
    style AzureCosmos fill:#0D47A1,stroke:#0D47A1,color:#fff
    style AzureRedis fill:#DC382D,stroke:#A52A2A,color:#fff
    style LocalRedis fill:#DC382D,stroke:#A52A2A,color:#fff
```

---

## 9. Agent Workflow — Hub-and-Spoke with Profile Gate

Unchanged from v0.3.1. Data store selection does not affect agent orchestration.

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

## 10. Orchestrator Routing Decision Tree

Unchanged from v0.3.1.

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

## 11. Session Lifecycle

Unchanged from v0.3.1. The session flow is identical regardless of whether Redis is local Docker or Azure Managed Redis.

```mermaid
sequenceDiagram
    participant Client as MCP Client
    participant MCP as McpServer
    participant WF as WorkflowService
    participant SM as SessionManager
    participant Store as AgentSessionStore<br/>(InMemory / Redis Local / Redis Azure)
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

## 12. Scale-Out Architecture (Azure Container Apps)

New in v1.0.0. Shows the proven 5-replica stateless scale-out pattern deployed to Azure Container Apps with the Docker image from Docker Hub.

```mermaid
graph TB
    subgraph Clients["MCP Clients"]
        C1["VS Code<br/>(GitHub Copilot)"]
        C2["Claude Desktop"]
    end

    subgraph DockerHub["Docker Hub"]
        Image["finwiseproject/<br/>finwise-mcp-server:1.0.0"]
    end

    subgraph ACA["☁️ Azure Container Apps<br/>East US 2 · HTTPS Ingress"]
        R1["🐳 Replica 1"]
        R2["🐳 Replica 2"]
        R3["🐳 Replica 3"]
        R4["🐳 Replica 4"]
        R5["🐳 Replica 5"]
    end

    subgraph SharedStores["☁️ Azure Shared Data Stores"]
        Redis["Azure Managed Redis<br/>agentsession:* keys<br/>mcpinit:* keys<br/>(session state)"]
        Cosmos["Azure Cosmos DB Serverless<br/>FinWise / UserProfiles<br/>(user profiles)"]
    end

    subgraph External["☁️ Azure AI Services"]
        OAI["Azure OpenAI<br/>gpt-4o-mini"]
        Foundry["Azure AI Foundry<br/>(Stock Agent)"]
    end

    Image -->|"Pulled by ACA"| ACA

    C1 -->|"HTTPS<br/>*.azurecontainerapps.io/mcp"| R1
    C1 -->|"Any replica"| R3
    C2 -->|"HTTPS<br/>*.azurecontainerapps.io/mcp"| R2
    C2 -->|"Any replica"| R5

    R1 --> Redis
    R2 --> Redis
    R3 --> Redis
    R4 --> Redis
    R5 --> Redis

    R1 --> Cosmos
    R2 --> Cosmos
    R3 --> Cosmos
    R4 --> Cosmos
    R5 --> Cosmos

    R1 --> OAI
    R3 --> Foundry

    Note1["No session affinity<br/>No sticky sessions<br/>No coordination<br/>Each container is disposable<br/>HTTPS auto-TLS via ACA ingress"]

    style Image fill:#FFF3E0,stroke:#E65100,color:#000
    style ACA fill:#4A148C,stroke:#7B1FA2,color:#fff
    style Redis fill:#DC382D,stroke:#A52A2A,color:#fff
    style Cosmos fill:#0D47A1,stroke:#0D47A1,color:#fff
    style R1 fill:#1565C0,stroke:#0D47A1,color:#fff
    style R2 fill:#1565C0,stroke:#0D47A1,color:#fff
    style R3 fill:#1565C0,stroke:#0D47A1,color:#fff
    style R4 fill:#1565C0,stroke:#0D47A1,color:#fff
    style R5 fill:#1565C0,stroke:#0D47A1,color:#fff
```

---

## 13. CosmosDB Dual-Access Pattern

Unchanged from v0.3.1 for emulator. For Azure Cosmos DB Serverless, `LimitToEndpoint` is not needed — standard endpoint discovery works.

```mermaid
graph TB
    subgraph Problem["Problem: Endpoint Discovery (Emulator)"]
        Emulator["CosmosDB Emulator<br/>Reports Docker-internal IP<br/>in account metadata"]
        SDK_Default["Cosmos SDK (default)<br/>Discovers endpoints from metadata<br/>Routes to Docker-internal IP"]
        Fail["❌ Connection fails<br/>(IP unreachable from host<br/>or from other containers)"]

        Emulator --> SDK_Default --> Fail
    end

    subgraph Solution["Solution: LimitToEndpoint = true (Emulator Only)"]
        SDK_Fixed["Cosmos SDK<br/>LimitToEndpoint = true<br/>Ignores metadata addresses"]
        ConnStr["Uses connection string<br/>endpoint ONLY"]

        SDK_Fixed --> ConnStr
    end

    subgraph DualAccess["Dual-Access Pattern (Emulator)"]
        Host["Host Process<br/>(dotnet run)"] -->|"localhost:8081<br/>(port mapping)"| EmulatorOK["🐳 CosmosDB Emulator<br/>:8081"]
        Container["🐳 finwise-mcp-server"] -->|"finwise-cosmosdb-emulator:8081<br/>(Docker DNS)"| EmulatorOK
    end

    subgraph AzureAccess["Azure Cosmos DB (Standard)"]
        AzureSDK["Cosmos SDK<br/>Standard endpoint discovery<br/>LimitToEndpoint not needed"]
        AzureEndpoint["*.documents.azure.com:443<br/>Standard TLS<br/>No workarounds needed"]
        AzureSDK --> AzureEndpoint
    end

    ConnStr --> DualAccess

    style Problem fill:#FFEBEE,stroke:#C62828,color:#000
    style Solution fill:#E8F5E9,stroke:#2E7D32,color:#000
    style DualAccess fill:#E3F2FD,stroke:#1565C0,color:#000
    style AzureAccess fill:#E3F2FD,stroke:#1565C0,color:#000
    style Fail fill:#EF5350,stroke:#C62828,color:#fff
    style EmulatorOK fill:#66BB6A,stroke:#2E7D32,color:#fff
```

---

## 14. Azure Managed Redis — Connection Pattern

New in v1.0.0. Shows the SSL auto-detection challenge and the required `ssl=False` workaround for Plaintext instances.

```mermaid
graph TB
    subgraph Challenge["Challenge: SSL Auto-Detection"]
        Hostname["Connection string:<br/>*.redis.azure.net:10000"]
        AutoDetect["StackExchange.Redis<br/>detects *.azure.net<br/>→ auto-enables SSL"]
        TLSFail["❌ TLS handshake fails<br/>(instance is Plaintext)"]

        Hostname --> AutoDetect --> TLSFail
    end

    subgraph Fix["Fix: Explicit ssl=False"]
        ConnStr["*.redis.azure.net:10000,<br/>password=key,<br/>ssl=False,<br/>abortConnect=False"]
        Works["✅ Connection succeeds<br/>(Plaintext protocol)"]

        ConnStr --> Works
    end

    subgraph Evolution["Production Evolution Path"]
        Step1["Current (POC)<br/>Plaintext + Access Keys<br/>ssl=False"]
        Step2["Step 1<br/>TLS + Access Keys<br/>ssl=True"]
        Step3["Step 2<br/>TLS + Entra ID<br/>Managed Identity"]

        Step1 --> Step2 --> Step3
    end

    style Challenge fill:#FFEBEE,stroke:#C62828,color:#000
    style Fix fill:#E8F5E9,stroke:#2E7D32,color:#000
    style TLSFail fill:#EF5350,stroke:#C62828,color:#fff
    style Works fill:#66BB6A,stroke:#2E7D32,color:#fff
    style Evolution fill:#E3F2FD,stroke:#1565C0,color:#000
```

---

## 15. Test Architecture — Full Picture

Updated from v0.3.1. Shows xUnit Trait categorization, target-agnostic integration tests, and the new `DockerEnvVarConfigTests`.

```mermaid
graph TB
    subgraph TestProjects["Test Projects"]
        subgraph UnitTests["Unit Tests · Category=Unit"]
            MAW["MultiAgentWorkflow.UnitTests<br/>Workflow, Session, Agents,<br/>Stores, Deduplication"]
            MCP_U["McpServer.UnitTests<br/>McpSession, MigrationHandler"]
        end

        subgraph Integration["Integration · Category=Integration"]
            E2E["EndToEndMcpTests<br/>8× [Fact]<br/>Server: dotnet run"]
            CosmosInt["CosmosDb.IntegrationTests<br/>13× [Fact]<br/>Target: emulator OR Azure"]
            RedisInt["Redis.IntegrationTests<br/>12× [SkippableFact]<br/>Target: local OR Azure"]
            StockInt["StockAgent.IntegrationTests<br/>4× [SkippableFact]"]
        end

        subgraph Container["Container · Category=Container"]
            Dockerized["DockerizedMcpTests<br/>4× [SkippableFact]"]
            DockerSpecific["DockerContainerSpecificTests<br/>5× [SkippableFact]"]
            EnvVar["DockerEnvVarConfigTests (NEW)<br/>2× [SkippableFact]"]
        end
    end

    subgraph SharedLib["E2ETestBase (class library)"]
        Base["McpEndToEndTestBase<br/>MCP protocol helpers<br/>SSE streaming reads<br/>Configurable base URL"]
    end

    E2E -->|"inherits"| Base
    Dockerized -->|"inherits"| Base

    subgraph Targets["Server Targets"]
        LocalServer["⚙️ dotnet run<br/>localhost:5000"]
        DockerServer["🐳 docker compose<br/>localhost:5000"]
    end

    subgraph DataTargets["Data Store Targets"]
        Emulator["🐳 Local Emulators<br/>localhost:8081 / :6379"]
        Azure["☁️ Azure Cloud<br/>*.azure.com / *.azure.net"]
    end

    E2E -.->|"connects to"| LocalServer
    Dockerized -.->|"connects to"| DockerServer
    DockerSpecific -.->|"connects to"| DockerServer
    EnvVar -.->|"connects to"| DockerServer

    CosmosInt -.->|"env vars"| Emulator
    CosmosInt -.->|"env vars"| Azure
    RedisInt -.->|"env vars"| Emulator
    RedisInt -.->|"env vars"| Azure

    subgraph RunSettings[".runsettings Files"]
        Default["test.runsettings<br/>(minimal)"]
        DockerLocal["test.docker-local.runsettings<br/>(all FINWISE_* env vars)"]
    end

    style UnitTests fill:#F5F5F5,stroke:#9E9E9E,color:#000
    style Integration fill:#FFF3E0,stroke:#E65100,color:#000
    style Container fill:#E3F2FD,stroke:#1565C0,color:#000
    style SharedLib fill:#E8F5E9,stroke:#2E7D32,color:#000
    style DataTargets fill:#F3E5F5,stroke:#7B1FA2,color:#000
    style RunSettings fill:#FFECB3,stroke:#FF8F00,color:#000
```

---

## 16. Test Pyramid

Updated from v0.3.1. Expanded test counts and new env var config layer.

```mermaid
graph TB
    subgraph Pyramid["Test Pyramid (v1.0.0)"]
        direction TB
        ContainerE2E["🔝 E2E Container Tests (11)<br/>4 reused MCP + 5 Docker-specific + 2 env var config<br/>Validates: Dockerfile, networking, env vars, FINWISE_* pipeline"]
        LocalE2E["E2E Local Tests (8)<br/>Full MCP protocol against dotnet run<br/>Validates: profile, sessions, tools, stock handoff, storage info"]
        ComponentIntegration["Integration Tests (per-component, target-agnostic)<br/>Redis (12), CosmosDB (13), Stock Agent (4)<br/>Validates: store implementations against emulator OR Azure"]
        UnitTests["🔻 Unit Tests (fast, isolated)<br/>MultiAgentWorkflow + McpServer<br/>Validates: business logic, session management, routing, deduplication"]
    end

    ContainerE2E --> LocalE2E --> ComponentIntegration --> UnitTests

    style ContainerE2E fill:#1565C0,stroke:#0D47A1,color:#fff
    style LocalE2E fill:#FFF3E0,stroke:#E65100,color:#000
    style ComponentIntegration fill:#E8F5E9,stroke:#2E7D32,color:#000
    style UnitTests fill:#F5F5F5,stroke:#9E9E9E,color:#000
```

---

## 17. Class Diagram — v1.0.0 Changes Highlighted

Updated from v0.3.1. Key changes: `get_storage_info` tool added to `FinWiseTools`, factory classes now include `FINWISE_*` env var override logic, `CosmosDbUserProfileStore` no longer specifies throughput.

```mermaid
classDiagram
    direction TB

    namespace McpServerHost {
        class FinWiseTools {
            +run_finwise_workflow(query) string
            +reset_conversation() string
            +get_storage_info() string [NEW]
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
        class AgentSessionStoreFactory {
            «static» [UPDATED]
            +CreateSessionStoreAsync(config) tuple
            -ApplyEnvironmentOverrides(options)
            -IsForceInMemoryDataEnabled(config) bool
        }
        class UserProfileStoreFactory {
            «static» [UPDATED]
            +CreateProfileStore(config) IUserProfileStore
            -ApplyEnvironmentOverrides(options)
            -IsForceInMemoryDataEnabled(config) bool
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
        class CosmosDbUserProfileStore {
            [UPDATED: no throughput param]
        }
        class RedisOptions {
            +Enabled : bool
            +ConnectionString : string
            +SessionTtlMinutes : int
        }
        class CosmosDbOptions {
            +Enabled : bool
            +Endpoint : string
            +Key : string
            +DatabaseName : string
            +ContainerName : string
            +AllowInsecureTls : bool
        }
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
    AgentSessionStoreFactory --> AgentSessionStore : creates
    AgentSessionStoreFactory --> RedisOptions : reads
    UserProfileStoreFactory --> IUserProfileStore : creates
    UserProfileStoreFactory --> CosmosDbOptions : reads

    style StockSpecializedAgentFactory fill:#4A90D9,stroke:#2E6DA4,color:#fff
    style RedisAgentSessionStore fill:#DC382D,stroke:#A52A2A,color:#fff
    style AgentSessionStoreFactory fill:#FFF3E0,stroke:#E65100,color:#000
    style UserProfileStoreFactory fill:#FFF3E0,stroke:#E65100,color:#000
```

---

## 18. Stock Specialized Agent — Foundry Integration Detail

Unchanged from v0.3.1.

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

## 19. End-to-End Request Flow — Stock Advice (Azure Container Apps)

Updated from v0.3.1. Shows the request flow through Azure Container Apps with HTTPS ingress, connecting to Azure cloud data stores.

```mermaid
sequenceDiagram
    participant User as 👤 User (VS Code)
    participant ACA as ☁️ Azure Container Apps<br/>(HTTPS Ingress)
    participant MCP as 🐳 finwise-mcp-server<br/>(Replica N)
    participant WF as WorkflowService
    participant Orch as 🤖 Orchestrator
    participant Stock as 🤖 Stock Agent (Foundry)
    participant Foundry as ☁️ Azure AI Foundry
    participant Redis as ☁️ Azure Managed Redis
    participant Cosmos as ☁️ Azure Cosmos DB

    Note over User,Cosmos: Assumes PROFILE_READY exists (profile completed earlier)

    User->>ACA: HTTPS *.azurecontainerapps.io/mcp
    ACA->>MCP: Route to any replica (no affinity)
    MCP->>WF: ProcessMessageAsync(sessionId, query)
    WF->>Redis: GetSessionAsync (*.azure.net:10000)
    Redis-->>WF: AgentSession + messages
    WF->>WF: IsProfileReady(history) → true
    WF->>WF: CreateAgentsAndWorkflow(isProfileReady=true)
    Note over WF: All 4 agents available

    WF->>Orch: RunStreamingAsync(workflow, messages)
    Orch->>Orch: Detect stock-related query
    Orch->>Stock: handoff_to_stock_agent
    Stock->>Foundry: HTTP (managed by SDK, external)
    Foundry->>Foundry: Retrieve annual reports
    Foundry-->>Stock: Stock recommendations
    Stock-->>Orch: handoff_to_orchestrator
    Orch-->>WF: Response with stock advice

    WF->>Redis: SaveSessionAsync (*.azure.net:10000)
    WF-->>MCP: WorkflowResponse
    MCP-->>ACA: MCP tool result
    ACA-->>User: Stock recommendations (SSE over HTTPS)

    Note over User,Cosmos: Next request may hit a different replica —<br/>session is in Azure Redis, profile is in Azure Cosmos DB
```

---

## 20. Layered .env Architecture

New in v1.0.0. Shows the `.env` file layering pattern that enables switching between local and Azure data stores.

```mermaid
graph TB
    subgraph EnvFiles[".env File Layering"]
        EnvBase[".env (base)<br/>────────────────<br/>AZURE_OPENAI_*=...<br/>STOCK_AGENT_*=...<br/>FINWISE_COSMOSDB_ENDPOINT=<br/>  https://localhost:8081/<br/>FINWISE_REDIS_CONNECTION_STRING=<br/>  localhost:6379"]
        EnvAzure[".env.azure (overrides)<br/>──────────────────<br/>FINWISE_COSMOSDB_ENDPOINT=<br/>  https://*.documents.azure.com<br/>FINWISE_COSMOSDB_KEY=<br/>  <azure-primary-key><br/>FINWISE_REDIS_CONNECTION_STRING=<br/>  *.azure.net:10000,password=..."]
    end

    subgraph Compose["Docker Compose v2.24+"]
        Cmd["docker compose -f<br/>docker-compose.finwise.yml<br/>--env-file .env<br/>--env-file .env.azure<br/>up -d"]
        Merge["Later files override<br/>earlier files"]
    end

    subgraph Result["Effective Environment"]
        Eff["AZURE_OPENAI_*=... (from .env)<br/>STOCK_AGENT_*=... (from .env)<br/>FINWISE_COSMOSDB_ENDPOINT=<br/>  *.documents.azure.com (from .env.azure)<br/>FINWISE_REDIS_CONNECTION_STRING=<br/>  *.azure.net:10000 (from .env.azure)"]
    end

    EnvBase -->|"first"| Cmd
    EnvAzure -->|"second (overrides)"| Cmd
    Cmd --> Merge --> Result

    subgraph GitTracking["Git Status"]
        Tracked[".env.azure.template ✅<br/>(placeholder values, tracked)"]
        Ignored[".env ❌<br/>.env.azure ❌<br/>(secrets, git-ignored)"]
    end

    style EnvBase fill:#FCE4EC,stroke:#C62828,color:#000
    style EnvAzure fill:#E3F2FD,stroke:#1565C0,color:#000
    style Result fill:#E8F5E9,stroke:#2E7D32,color:#000
    style Tracked fill:#E8F5E9,stroke:#2E7D32,color:#000
    style Ignored fill:#FCE4EC,stroke:#C62828,color:#000
```

---

## Diagram Index

| # | Diagram | Status | Description |
|---|---------|--------|-------------|
| 1 | System Context | **Updated** | Added Azure cloud stores, Azure Container Apps, Docker Hub image |
| 2 | Four Deployment Modes | **Updated** | Was 2 modes — added Option C (server → Azure DBs) + Option D (Azure Container Apps) |
| 3 | Three-File Docker Compose Architecture | **New** | `finwise.yml` + `infra.yml` + `docker-compose.yml` relationships |
| 4 | Configuration & Environment Flow | **Updated** | Full layered config with `FINWISE_*` env vars and master toggle |
| 5 | Docker Compose Service Architecture | **Updated** | Added Azure cloud targets and `get_storage_info` tool |
| 6 | Docker Image Build Pipeline | Unchanged | Multi-stage Dockerfile: build → runtime |
| 7 | System Architecture Overview | **Updated** | 3 MCP tools, Azure cloud store alternatives |
| 8 | Data Store Selection Flow | **New** | Factory decision tree: ForceInMemory → check flags → select store |
| 9 | Agent Workflow — Hub-and-Spoke | Unchanged | Profile gate + hub-and-spoke handoffs |
| 10 | Orchestrator Routing Decision Tree | Unchanged | Intent-based routing with profile gate |
| 11 | Session Lifecycle | Unchanged | Same flow, store abstraction handles local/Azure transparently |
| 12 | Scale-Out Architecture | **New** | 5 replicas in Azure Container Apps → shared Azure Redis + Cosmos DB, Docker Hub image |
| 13 | CosmosDB Dual-Access Pattern | **Updated** | Added Azure Cosmos DB (no workaround needed) |
| 14 | Azure Managed Redis Connection | **New** | SSL auto-detection challenge + `ssl=False` fix |
| 15 | Test Architecture — Full Picture | **Updated** | Trait categorization, target-agnostic tests, `.runsettings` |
| 16 | Test Pyramid | **Updated** | Expanded counts, env var config tests |
| 17 | Class Diagram | **Updated** | Factory classes, `get_storage_info`, Options classes |
| 18 | Stock Agent — Foundry Integration | Unchanged | Two-step resolution via MAF 1.0 GA |
| 19 | E2E Request Flow — Stock Advice | **Updated** | Azure Container Apps HTTPS ingress, multi-replica routing |
| 20 | Layered .env Architecture | **New** | `.env` + `.env.azure` layering pattern |
