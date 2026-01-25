# Research Document: Core Multi-Agent Workflow

**Feature**: 001-core-workflow  
**Date**: December 30, 2025  
**Status**: Complete

## Overview

This document captures research decisions for implementing the v0.1 multi-agent workflow. All technical unknowns from the implementation plan have been resolved through analysis of existing documentation, architecture specifications, and .NET ecosystem best practices.

---

## 1. Microsoft Agent Framework: Multi-Agent Orchestration Patterns

### Decision
Use **Microsoft Agent Framework** (formerly Semantic Kernel + AutoGen fusion) for implementing the three agents with sequential handoff and dynamic routing patterns.

### Rationale
- **Native multi-agent support**: Provides `AgentGroupChat`, `SequentialOrchestration`, and `HandoffPattern` abstractions matching the feature spec requirements (FR-003: agent handoff, FR-015: dynamic navigation)
- **MCP integration**: Works seamlessly with `Microsoft.ModelContextProtocol` SDK - agents can act as MCP clients to consume User Profile MCP Server
- **Azure OpenAI compatibility**: Built-in support for Azure OpenAI Service with token usage tracking and retry policies
- **Telemetry and observability**: Native structured logging with OpenTelemetry integration for performance monitoring (Constitution III)
- **Proven in production**: Used in enterprise Microsoft solutions with multi-agent workflows (Copilot Studio, Azure AI)

### Routing Strategy (All Phases)
**Decision**: Use **LLM-only routing** for all phases (simplest implementation). The orchestrator agent's system prompt analyzes user queries to determine which specialist agent to invoke. No keyword matching or custom heuristics needed.

**Rationale**: Simplicity over complexity - LLM-based routing requires zero custom code, handles natural language variations automatically, and scales naturally as new agents are added. The framework manages all routing logic through system prompts.

### Implementation Pattern
```csharp
// Create agents using ChatClientAgent with system prompts
// Orchestrator system prompt performs LLM-based routing analysis
ChatClientAgent orchestratorAgent = new(chatClient,
    "You determine which agent to use based on the user's query. Route to profile agent if profile data needed, advisor agent for investment advice.",
    "orchestrator_agent",
    "Routes queries to appropriate specialist agent");

ChatClientAgent profileAgent = new(chatClient,
    "You collect user investment profile through a multi-turn conversation. Ask ONE question at a time: (1) risk tolerance, (2) investment goals, (3) timeframe. Wait for user response before asking the next question. After collecting all three, signal completion.",
    "profile_agent",
    "Specialist agent for user profile management");

// Multi-turn conversation pattern: Agent asks 1 question per turn, waits for response

ChatClientAgent advisorAgent = new(chatClient,
    "You provide generic investment advice (stocks vs real estate). Use user profile context from handoff.",
    "advisor_agent",
    "Specialist agent for investment recommendations");

// Build workflow with handoff relationships
Workflow workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(orchestratorAgent)
    .WithHandoffs(orchestratorAgent, [profileAgent, advisorAgent])
    .WithHandoffs([profileAgent, advisorAgent], orchestratorAgent)
    .Build();

// Execute workflow with streaming
List<ChatMessage> messages = [new(ChatRole.User, "I need investment advice")];
await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, messages);
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

// Watch for events (agent invocations, handoffs, outputs)
await foreach (WorkflowEvent evt in run.WatchStreamAsync())
{
    switch (evt)
    {
        case ExecutorInvokedEvent invoked:
            // Track which agent is active
            break;
        case WorkflowOutputEvent output:
            // Get final response
            break;
    }
}
```

### Alternatives Considered
- **Semantic Kernel alone**: Lacks native multi-agent handoff patterns (would require custom orchestration logic)
- **Custom agent framework**: Higher implementation cost, no telemetry/observability out-of-box
- **LangChain for .NET**: Immature ecosystem, limited Azure integration

---

## 2. MCP STDIO Transport: Process Communication Architecture

### Decision
Use **MCP STDIO transport** for external communication (all phases) and internal communication (Implementation Phase 2+ only):

**Implementation Phase 1 (v0.1 baseline)**:
1. **External communication ONLY**: User's AI assistant (Claude Desktop) → FinWise MCP Server (single process)
2. **NO internal communication**: User Profile Agent directly updates Dictionary<string, UserProfileDto> (in-memory, same process)
3. **NO User Profile MCP Server**: Single process only

**Implementation Phase 2 (persistence)**:
1. **External communication**: User's AI assistant (Claude Desktop) → FinWise MCP Server (unchanged)
2. **Internal communication**: FinWise MCP Server → User Profile MCP Server (multi-process via STDIO)

Both processes in Implementation Phase 2 run as separate .NET console applications communicating via stdin/stdout.

### MCP Tool Wrapper Pattern (Both Phases)
```csharp
// MCP uses stdio for JSON-RPC communication. Redirect Console.Out to stderr
// to prevent any diagnostic output from polluting the MCP protocol stream.
Console.SetOut(Console.Error);

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options => 
    options.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();  // Auto-discovers [McpServerTool] methods

await builder.Build().RunAsync();

// Tool class with workflow execution
[McpServerToolType]
public static class WorkflowTools
{
    [McpServerTool(Name = "get_investment_recommendations")]
    [Description("Get personalized investment recommendations based on user profile")]
    public static async Task<string> GetInvestmentRecommendations(
        [Description("The user's investment query")] string query,
        [Description("Unique user identifier")] string userId)
    {
        // Workflow execution (workflow instance injected or created here)
        List<ChatMessage> messages = [new(ChatRole.User, query)];
        await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, messages);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
        
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            if (evt is WorkflowOutputEvent output)
            {
                var outputMessages = output.As<List<ChatMessage>>();
                return outputMessages?[^1].Text ?? string.Empty;
            }
        }
        return string.Empty;
    }
}
```

### Rationale
- **MCP protocol requirement**: STDIO is the standard transport for local MCP servers (spec: FR-011 multi-client accessibility)
- **Implementation Phase 1 simplicity**: Single process with in-memory storage - fastest path to working multi-agent workflow
- **Implementation Phase 2 process isolation**: Separate User Profile MCP Server process provides fault tolerance (if it crashes, main orchestrator remains available)
- **Implementation Phase 2 consistent integration pattern**: Agents use same MCP client code for internal database (User Profile) and future external APIs (market data) - no special-casing
- **Implementation Phase 2 agent-native design**: Agents work with tools ("get user profile") rather than direct database coupling (ORM/LINQ queries)
- **Zero network configuration**: No ports, no HTTP server setup for local development (both phases)
- **Proven pattern**: Used by Block, Bloomberg, and 100+ MCP servers in the ecosystem

### Implementation Pattern - Implementation Phase 1 (In-Memory)
```csharp
// FinWise.Orchestrator/Program.cs - SINGLE PROCESS, in-memory storage
Console.SetOut(Console.Error);

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options => 
    options.LogToStandardErrorThreshold = LogLevel.Trace);

// Configure Azure OpenAI
var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") 
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") 
    ?? throw new InvalidOperationException("AZURE_OPENAI_DEPLOYMENT_NAME is not set.");
var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") 
    ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");

var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
IChatClient chatClient = azureClient.GetChatClient(deploymentName).AsIChatClient();

// In-memory profile storage (Implementation Phase 1 ONLY)
var profileStorage = new Dictionary<string, UserProfileDto>();

// Create agents and workflow
ChatClientAgent orchestratorAgent = new(chatClient, "...", "orchestrator_agent", "...");
ChatClientAgent profileAgent = new(chatClient, "...", "profile_agent", "...");
ChatClientAgent advisorAgent = new(chatClient, "...", "advisor_agent", "...");

Workflow workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(orchestratorAgent)
    .WithHandoffs(orchestratorAgent, [profileAgent, advisorAgent])
    .WithHandoffs([profileAgent, advisorAgent], orchestratorAgent)
    .Build();

// User Profile Agent directly updates Dictionary (NO MCP client calls)
// profileStorage[userId] = new UserProfileDto(userId, riskTolerance, goals, timeframe);

// Register workflow and MCP server
builder.Services.AddSingleton(workflow);
builder.Services.AddSingleton(profileStorage); // Inject Dictionary into agents
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
```

### Implementation Pattern - Implementation Phase 2 (Multi-Process with PostgreSQL)
```csharp
// FinWise.Orchestrator/Program.cs - Main MCP server (Implementation Phase 2)
// Redirect console output to stderr to prevent polluting MCP protocol stream
Console.SetOut(Console.Error);

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(options => 
    options.LogToStandardErrorThreshold = LogLevel.Trace);

// Configure Azure OpenAI
var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") 
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") 
    ?? throw new InvalidOperationException("AZURE_OPENAI_DEPLOYMENT_NAME is not set.");
var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") 
    ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");

var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
IChatClient chatClient = azureClient.GetChatClient(deploymentName).AsIChatClient();

// Create agents and workflow (as shown in Section 1)
ChatClientAgent orchestratorAgent = new(chatClient, "...", "orchestrator_agent", "...");
ChatClientAgent profileAgent = new(chatClient, "...", "profile_agent", "...");
ChatClientAgent advisorAgent = new(chatClient, "...", "advisor_agent", "...");

Workflow workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(orchestratorAgent)
    .WithHandoffs(orchestratorAgent, [profileAgent, advisorAgent])
    .WithHandoffs([profileAgent, advisorAgent], orchestratorAgent)
    .Build();

// Register workflow and MCP server with auto-discovery
builder.Services.AddSingleton(workflow);
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();  // Auto-discovers WorkflowTools class

await builder.Build().RunAsync();

// Tool class - automatically discovered and registered
[McpServerToolType]
public static class WorkflowTools
{
    [McpServerTool(Name = "get_investment_recommendations")]
    [Description("Get personalized investment recommendations based on user profile")]
    public static async Task<string> GetInvestmentRecommendations(
        Workflow workflow,  // Injected from DI
        [Description("The user's investment query")] string query,
        [Description("Unique user identifier")] string userId)
    {
        List<ChatMessage> messages = [new(ChatRole.User, query)];
        await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, messages);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));
        
        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            if (evt is WorkflowOutputEvent output)
            {
                var outputMessages = output.As<List<ChatMessage>>();
                return outputMessages?[^1].Text ?? string.Empty;
            }
        }
        return string.Empty;
    }
}

// User Profile Agent uses MCP client to call User Profile MCP Server (Implementation Phase 2 ONLY)
var clientTransport = new StdioClientTransport(new StdioClientTransportOptions
{
    Name = "User Profile Server",
    Command = "dotnet",
    Arguments = ["run", "--project", "src/FinWise.UserProfile.McpServer"]
});

var mcpClient = await McpClient.CreateAsync(clientTransport);
var result = await mcpClient.CallToolAsync(
    "get_user_profile",
    new Dictionary<string, object?> { ["user_identifier"] = userId });
```

### Alternatives Considered
- **Implementation Phase 1 in-memory Dictionary**: CHOSEN for simplicity - fastest path to working workflow, defers database complexity
- **Direct database access from agents (Implementation Phase 2)**: Violates agent-native design principles, couples agents to schema, harder to test
- **Shared in-process database service (Implementation Phase 2)**: Loses process isolation benefits, no demonstration of MCP dual-role architecture
- **HTTP transport for User Profile MCP Server (Implementation Phase 2)**: Over-engineered for local development, adds unnecessary complexity (ports, HTTP server)

---

## 3. PostgreSQL Schema Design: User Profile Data Model

**Note**: This section applies to **Implementation Phase 2 (persistence) ONLY**. Implementation Phase 1 uses in-memory Dictionary<string, UserProfileDto> with NO database.

### Decision
Use **single `user_profiles` table** with JSON columns for flexible questionnaire data storage (Implementation Phase 2).

### Schema Design
```sql
CREATE TABLE user_profiles (
    id UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    user_identifier VARCHAR(255) NOT NULL UNIQUE, -- External user ID from AI assistant
    risk_tolerance VARCHAR(50) NOT NULL CHECK (risk_tolerance IN ('conservative', 'moderate', 'aggressive')),
    investment_goals TEXT[], -- Array: retirement, home_purchase, wealth_building, education
    investment_timeframe VARCHAR(50) NOT NULL CHECK (investment_timeframe IN ('short_term', 'medium_term', 'long_term')),
    questionnaire_responses JSONB, -- Flexible storage for additional questionnaire data
    created_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMP WITH TIME ZONE NOT NULL DEFAULT NOW(),
    version INT NOT NULL DEFAULT 1 -- Optimistic concurrency control
);

CREATE INDEX idx_user_profiles_user_identifier ON user_profiles(user_identifier);
```

### Rationale
- **Simplicity for v0.1**: Single table meets all user profile requirements (risk tolerance, goals, timeframe)
- **Flexible questionnaire storage**: JSONB column allows future questionnaire questions without schema migrations
- **Performance**: Index on `user_identifier` ensures <500ms lookups (Constitution III requirement)
- **Versioning**: `version` column enables optimistic concurrency for profile updates
- **PostgreSQL array type**: Efficient storage for multi-select investment goals

### Implementation via Entity Framework Core
```csharp
public class UserProfile
{
    public Guid Id { get; set; }
    public string UserIdentifier { get; set; } = null!;
    public RiskTolerance RiskTolerance { get; set; }
    public List<InvestmentGoal> InvestmentGoals { get; set; } = new();
    public InvestmentTimeframe InvestmentTimeframe { get; set; }
    public JsonDocument? QuestionnaireResponses { get; set; } // System.Text.Json
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public int Version { get; set; }
}
```

### Alternatives Considered
- **Separate questionnaire table**: Over-normalized for v0.1 scope (<100 users)
- **NoSQL (Cosmos DB)**: Deferred to v0.3+ for conversation history and RAG (per architecture doc)
- **Embedded JSON in SQLite**: PostgreSQL required for v0.2+ Docker Compose compatibility

---

## 4. Agent Routing Logic: LLM-Only Analysis (All Phases)

### Decision
Use **LLM-only routing** for all phases (simplest implementation). The orchestrator agent's system prompt performs query analysis and determines which specialist agent to invoke. No custom routing code or keyword matching needed.

### Routing Implementation
**No custom routing code required** - the Microsoft Agent Framework's `ChatClientAgent` with a well-crafted system prompt handles routing automatically:

```csharp
// Orchestrator system prompt performs LLM-based routing analysis
ChatClientAgent orchestratorAgent = new(chatClient,
    "You are an orchestrator agent that analyzes user queries and determines which specialist agent should handle them. "
    + "Route to 'profile_agent' for queries about user profile, risk tolerance, goals, or timeframe. "
    + "Route to 'advisor_agent' for queries about investment advice, stocks, real estate, or recommendations. "
    + "Always explain your routing decision briefly.",
    "orchestrator_agent",
    "Routes queries to appropriate specialist agent");

// Framework handles routing via LLM inference (no keyword logic needed)
```

### Rationale
- **Simplicity**: Zero custom routing code - system prompt handles all logic
- **Maintainability**: Adding new agents only requires updating orchestrator system prompt
- **Natural language**: Handles query variations automatically without keyword maintenance
- **Extensibility**: Easy to add new agents by updating system prompt (no code changes)
- **Logging**: Framework logs all agent invocations and decisions automatically

### Alternatives Considered
- **Keyword matching + LLM fallback**: Adds complexity for marginal performance gain (200-500ms latency acceptable for POC)
- **Intent detection ML model**: Over-engineered for v0.1, requires training data and MLOps infrastructure
- **Rule-based DSL**: More complex to maintain than simple system prompts for 3-4 agents

---

## 5. Handoff Context Preservation: Framework-Managed Approach

### Decision
Use **Microsoft Agent Framework's built-in context management** - agents share context through the conversation flow automatically via `ChatMessage` accumulation during workflow execution.

### Implementation
The framework automatically passes `ChatMessage` objects between agents during workflow execution. Each agent receives the accumulated conversation history, providing full context without manual state management.

### Rationale
- **Framework-managed**: `AgentWorkflowBuilder` defines handoff relationships declaratively
- **Automatic context flow**: Messages accumulate as workflow executes, providing context to subsequent agents
- **No manual state tracking**: Framework's `StreamingRun` manages execution state internally
- **Conversation continuity**: FR-004 met through framework's `ChatMessage` history during workflow execution
- **Debugging**: `WorkflowEvent` stream provides observability without custom serialization
- **Performance**: Framework-optimized handoffs meet <1s target

### Alternatives Considered
- **Custom HandoffContext object**: Over-engineered - framework provides this functionality
- **Database-backed handoff state**: Not needed for v0.1 stateless MCP tool invocations
- **Message queue**: Not needed for in-process workflow execution

---

## 6. Circular Escalation Prevention: Build-Time Configuration

### Decision
Use **Microsoft Agent Framework's workflow configuration** to prevent circular handoffs at build time rather than runtime detection.

### Implementation
The `AgentWorkflowBuilder.WithHandoffs()` method defines valid agent transitions at build time. Only configured paths are allowed during execution - attempting invalid transitions is prevented by the framework's design.

### Rationale
- **Build-time validation**: `AgentWorkflowBuilder` prevents invalid handoff configurations before execution
- **Declarative approach**: Handoff graph defined explicitly, no runtime cycle detection needed
- **Framework-enforced**: Invalid transitions cannot occur if not configured in builder
- **Meets requirements**: FR-018 (circular escalations prevented) through workflow design
- **Simplicity**: No custom navigation tracking or stack management needed
- **Performance**: Zero runtime overhead for cycle detection

### Alternatives Considered
- **Runtime cycle detection with NavigationHistoryService**: Over-engineered - framework prevents invalid handoffs
- **Full graph cycle detection**: Not needed when handoffs are declaratively configured
- **No prevention**: Framework design makes this impossible

---

## 7. Error Handling and Fallback Mechanisms

### Decision
Implement **tiered fallback strategy** with graceful degradation:

1. **Agent-level errors**: Retry with exponential backoff (Azure OpenAI transient failures)
2. **Routing failures**: Orchestrator fallback message when no agent matches
3. **Handoff failures**: Return control to orchestrator with error context
4. **Database failures**: Return cached/default data if available, otherwise user-friendly error

### Implementation
Agents use try-catch blocks for Azure OpenAI exceptions. Transient failures trigger exponential backoff retry. Persistent failures return user-friendly error messages via `ChatMessage` responses. All errors are logged with structured context for debugging.

### Rationale
- **User experience**: Never expose raw exceptions to users (Constitution II: user-friendly errors)
- **Reliability**: Transient failure retry improves success rate for Azure OpenAI calls
- **Observability**: All errors logged with context for debugging (FR-012 logging requirement)
- **Graceful degradation**: System remains available even if one agent fails (FR-010)

### Alternatives Considered
- **No error handling**: Violates FR-010 and Constitution II
- **Circuit breaker pattern**: Over-engineered for v0.1 single-user scale
- **Generic fallback agent**: Not needed with only 3 specialized agents

---

## 8. Testing Strategy: Unit, Integration, and End-to-End

### Decision
Implement **three-tier testing pyramid**:

1. **Unit tests** (60%): Agent logic, routing algorithms, handoff service (mocked dependencies)
2. **Integration tests** (30%): Database operations with Testcontainers, MCP client/server communication
3. **End-to-end tests** (10%): Full workflow simulation with real PostgreSQL and MCP STDIO

### Tools and Frameworks
- **xUnit**: .NET standard testing framework with parallel execution support
- **FluentAssertions**: Readable test assertions (`result.Should().BeSuccess()`)
- **Moq**: Mocking LLM services and MCP clients for unit tests
- **Testcontainers**: Spin up real PostgreSQL containers for integration tests
- **Custom test harness**: Measure end-to-end workflow latency (<10s target)

### Example Test Structure
```csharp
// Unit test: Orchestrator routing logic
[Fact]
public async Task AnalyzeQueryAsync_InvestmentKeyword_RoutesToAdvisorAgent()
{
    var orchestrator = new OrchestratorAgent(_mockAgentRegistry, _mockHandoffService);
    var decision = await orchestrator.AnalyzeQueryAsync("Should I invest in stocks?");
    
    decision.TargetAgent.Should().Be(AgentType.GlobalAdvisor);
    decision.Reason.Should().Contain("Investment advice query");
}

// Integration test: User Profile MCP Server database operations
[Fact]
public async Task GetUserProfileTool_ExistingUser_ReturnsProfile()
{
    await using var container = new PostgreSqlBuilder().Build();
    await container.StartAsync();
    
    var dbContext = CreateDbContext(container.GetConnectionString());
    var tool = new GetUserProfileTool(dbContext);
    
    var result = await tool.InvokeAsync(new { userId = "test-user-123" });
    result.RiskTolerance.Should().Be("moderate");
}

// MCP protocol integration test: Using official MCP SDK McpClient
[Fact]
public async Task McpServer_ListAndCallTools_ReturnsValidResponses()
{
    // Launch MCP server as separate process using official SDK transport
    var transport = new StdioClientTransport(new StdioClientTransportOptions
    {
        Name = "User Profile Server",
        Command = "dotnet",
        Arguments = ["run", "--project", "src/FinWise.UserProfile.McpServer"],
        WorkingDirectory = Path.GetFullPath("../../..")
    });

    // Create client (SDK handles initialize handshake automatically)
    var client = await McpClient.CreateAsync(transport);

    // List tools using SDK method
    IList<McpClientTool> tools = await client.ListToolsAsync();
    tools.Should().HaveCountGreaterThan(0);
    tools.Select(t => t.Name).Should().Contain(new[] 
    { 
        "get_user_profile", 
        "save_user_profile", 
        "update_user_profile" 
    });

    // Call tool using SDK method
    var result = await client.CallToolAsync(
        "get_user_profile",
        new Dictionary<string, object?> { ["userId"] = "test-123" },
        cancellationToken: CancellationToken.None);

    result.Content.Should().NotBeEmpty();
    result.Content.First(c => c.Type == "text").Text.Should().NotBeNullOrEmpty();

    await client.DisposeAsync();
}

// End-to-end test: Full multi-agent workflow
[Fact]
public async Task FullWorkflow_UserQuery_CompletesInUnder10Seconds()
{
    var stopwatch = Stopwatch.StartNew();
    var orchestrator = CreateRealOrchestrator(); // No mocks
    
    var response = await orchestrator.ProcessUserQueryAsync("I need investment advice");
    
    stopwatch.Stop();
    stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    response.Should().Contain("investment"); // Advisor agent responded
}

```

### Key Testing Insights from Official MCP SDK

**MCP Integration Tests** (using official SDK):
- **McpClient with StdioClientTransport**: Official SDK pattern for launching and connecting to MCP servers
- **Automatic protocol handling**: SDK handles initialize handshake, request ID management, JSON-RPC formatting
- **Strongly-typed API**: `ListToolsAsync()`, `CallToolAsync()` return typed results (no manual JSON parsing)
- **Clean resource management**: `await client.DisposeAsync()` properly shuts down server process
- **FluentAssertions**: Standard for readable test assertions on SDK results
- **Process isolation**: Each test launches fresh MCP server instance (no shared state)
- **Build configuration**: UseAppHost=false in csproj to avoid Windows file locks during rapid testing

### Rationale
- **Constitution compliance**: ≥80% code coverage target (Quality Gates)
- **Fast feedback**: Unit tests run in <1 second for local TDD workflow
- **Real environment**: Testcontainers ensure integration tests match production PostgreSQL behavior
- **Performance validation**: End-to-end tests enforce <10s workflow latency requirement

---

## 9. Logging and Observability: Structured JSON Logging

### Decision
Use **Microsoft.Extensions.Logging with Serilog** for structured JSON logging with contextual enrichers.

### Log Structure
```json
{
  "timestamp": "2025-12-30T14:32:15.123Z",
  "level": "Information",
  "message": "Agent handoff completed",
  "properties": {
    "sessionId": "550e8400-e29b-41d4-a716-446655440000",
    "sourceAgent": "UserProfile",
    "targetAgent": "GlobalAdvisor",
    "handoffReason": "Profile collection complete",
    "executionTimeMs": 245,
    "userId": "user-123"
  }
}
```

### Implementation
```csharp
// Program.cs - Serilog configuration
builder.Host.UseSerilog((context, config) => config
    .WriteTo.Console(new JsonFormatter())
    .WriteTo.File(new JsonFormatter(), "logs/finwise-.json", rollingInterval: RollingInterval.Day)
    .Enrich.WithProperty("Application", "FinWise.Orchestrator")
    .Enrich.WithProperty("Version", "0.1.0")
);

// Usage in services
_logger.LogInformation(
    "Agent handoff from {SourceAgent} to {TargetAgent}: {Reason}",
    handoff.SourceAgent, handoff.TargetAgent, handoff.Reason
);
```

### Rationale
- **Constitution requirement**: Structured logs with request IDs, user IDs, execution time (Performance & Scalability Standards)
- **Debugging**: JSON format enables easy log querying in production (grep, jq, log aggregators)
- **Performance tracking**: Execution time logged for every operation (<10s workflow, <500ms MCP tools)
- **Compliance**: FR-012 (log all agent interactions, decisions, handoffs, routing)

---

## 10. Azure OpenAI Configuration: Model Selection and Cost Management

### Decision
Use configuration based on environment variables for the LLM model.

### Model Selection
- **All Agents**: Use same deployment (configured via `AZURE_OPENAI_DEPLOYMENT_NAME` environment variable)


### Cost Management
```csharp
var endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") 
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
var deploymentName = Environment.GetEnvironmentVariable("AZURE_OPENAI_DEPLOYMENT_NAME") 
    ?? throw new InvalidOperationException("AZURE_OPENAI_DEPLOYMENT_NAME is not set.");
var apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_API_KEY") 
    ?? throw new InvalidOperationException("AZURE_OPENAI_API_KEY is not set.");

// Create Azure OpenAI client
var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
IChatClient chatClient = azureClient.GetChatClient(deploymentName).AsIChatClient();
```

### Rationale
- **Environment-driven**: All LLM configuration via environment variables (endpoint, deployment, API key) for flexibility
- **Deployment-agnostic**: Works with any Azure OpenAI deployment (GPT-4o-mini recommended for cost)
- **Performance**: Fast models achieve <1s latency for typical agent responses (supports <10s workflow target)

### Alternatives Considered
- **Local LLM (Ollama, LM Studio)**: Requires GPU, slower inference, lower quality for reasoning tasks
- **Hardcoded configuration**: Less flexible than environment variables, harder to test/deploy

---

## Summary of Research Decisions

| Topic | Decision | Key Rationale |
|-------|----------|---------------|
| **Multi-agent framework** | Microsoft Agent Framework | Native handoff patterns, Azure integration, telemetry |
| **MCP transport** | STDIO (both external and internal) | Process isolation, consistent integration, zero network config |
| **Database schema** | Single `user_profiles` table with JSONB | Simple for v0.1, flexible, indexed for <500ms lookups |
| **Routing logic** | LLM-only (orchestrator system prompt) | Simplest implementation, zero custom code, scales automatically |
| **Handoff context** | Framework's ChatMessage flow | Automatic context passing, framework-managed |
| **Circular prevention** | Build-time workflow configuration | Framework validates handoffs, zero runtime overhead |
| **Error handling** | Tiered fallback with retry | User-friendly errors, graceful degradation, logged |
| **Testing strategy** | Unit (60%) + Integration (30%) + E2E (10%) | ≥80% coverage, Testcontainers, <10s validation |
| **Logging** | Serilog JSON with enrichers | Structured, queryable, performance tracking |
| **LLM configuration** | Environment variables (deployment-agnostic) | Flexible, testable, supports any Azure OpenAI model |

All decisions align with the v0.1 architecture from `specs/03-architecture-and-technologies.md` and satisfy Constitutional requirements. No unknowns remain - ready for Implementation Phase 1 design.
