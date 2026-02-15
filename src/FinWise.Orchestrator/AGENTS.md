# FinWise.Orchestrator - Project Instructions

This is the main MCP server implementing a multi-agent financial advisor workflow. These instructions supplement the [root AGENTS.md](../../AGENTS.md).

---

## Project Overview

FinWise.Orchestrator is an MCP (Model Context Protocol) server that orchestrates a multi-agent workflow for personalized financial advice. It exposes two MCP tools (`get_financial_advice`, `reset_conversation`) via HTTP transport.

## Architecture

### Three-Agent Handoff Workflow

```
┌─────────────────┐
│   MCP Client    │  (VS Code/Claude Desktop via HTTP)
└────────┬────────┘
         │
         ▼
┌─────────────────┐     handoff     ┌────────────────┐
│  Orchestrator   │◄───────────────►│  ProfileAgent  │
│     Agent       │                 │  (tools: get/  │
│  (silent router)│                 │   set_profile) │
└────────┬────────┘                 └────────────────┘
         │
         │ handoff (when PROFILE_READY exists)
         ▼
┌─────────────────┐
│  AdvisorAgent   │
│  (provides      │
│   advice)       │
└─────────────────┘
```

### Key Design Principles

1. **Hub-and-spoke handoffs**: All agents communicate through orchestrator (no direct agent-to-agent)
2. **Stateless agents**: Agent instances are stateless; all state is in `AgentThread`
3. **PROFILE_READY marker**: Signal from ProfileAgent indicating profile is complete
4. **Session detection**: HTTP `MCP-Session-Id` header distinguishes sessions

## File Organization

| File | Purpose |
|------|---------|
| `Program.cs` | Entry point, MCP server setup, workflow creation, tool definitions |
| `AgentThreadManager.cs` | Thread lifecycle, serialization/deserialization, session detection |
| `UserProfileAgent.cs` | ProfileAgent with `get_profile`/`set_profile` tools |
| `Models.cs` | DTOs: `UserProfileDto`, `WorkflowExecutionContext`, `ConversationMetadata` |
| `Infrastructure.cs` | Azure OpenAI client factory, Serilog configuration |
| `SessionResetEvaluator.cs` | Explicit reset trigger detection (e.g., "start over", "my email is...") |
| `InMemoryThreadStore.cs` | `IThreadStore` implementation for thread persistence |
| `InMemoryUserProfileStore.cs` | `IUserProfileStore` implementation for profile persistence |
| `ConversationRunContext.cs` | Ambient context for current conversation (AsyncLocal) |

## Implementation Patterns

### Agent Creation Pattern

Agents are created per-conversation using `ChatClientAgent`:

```csharp
ChatClientAgent profileAgent = new(chatClient, systemPrompt, "profile_agent", "description");
```

### Workflow Building Pattern

Use `AgentWorkflowBuilder` for handoff configuration:

```csharp
Workflow workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(orchestratorAgent)
    .WithHandoffs(orchestratorAgent, [profileAgent, advisorAgent])
    .WithHandoffs([profileAgent, advisorAgent], orchestratorAgent)
    .Build();
```

### Thread Serialization (Microsoft Agent Framework)

```csharp
// Save: Serialize thread for persistence
JsonElement serializedThread = thread.Serialize();

// Load: Deserialize thread to resume
AgentThread resumedThread = agent.DeserializeThread(serializedThread);
```

### PROFILE_READY Marker Format

ProfileAgent outputs this when profile is complete:

```
PROFILE_READY: email=user@example.com risk=Moderate goals=Retirement timeframe=Long-term
```

### Session Management

- HTTP transport provides `MCP-Session-Id` header
- `sessionConversations` dictionary maps session → conversation ID
- New session = new conversation = asks for email
- Same session = continues existing conversation

## Coding Guidelines

### Logging

Use Serilog with `RequestId` context for request tracing:

```csharp
using (LogContext.PushProperty("RequestId", requestId))
{
    Log.Information("Processing request for {ConversationId}", conversationId);
    // ... all logs within this scope include RequestId
}
```

### Error Handling

- Return user-friendly messages from MCP tools (not exceptions)
- Log errors with full context via `Infrastructure.HandleMcpServerException()`
- Validate inputs early; return clear error strings

### Profile Fields

- All profile fields are **free-form text** (no validation or enum constraints)
- Accept any user input; the advisor interprets the values
- Use `WithUpdates()` for incremental profile updates

### Profile Store Access

- **`IUserProfileStore` (in-memory or CosmosDB) MUST only be accessed through `UserProfileAgent`**
- `Program.cs` creates the store instance and injects it into `UserProfileAgent` — it must NOT call store methods directly
- All profile operations (get, set, delete) go through `UserProfileAgent`'s tool methods: `GetProfile`, `SetProfile`, `DeleteProfile`
- External callers (MCP tools, other agents) interact with profiles via the orchestrator workflow, which routes to `UserProfileAgent`

### Agent Prompts

- Orchestrator: Silent router, ONLY makes tool calls, never outputs text
- ProfileAgent: Collects profile incrementally, outputs PROFILE_READY when complete
- AdvisorAgent: Provides personalized advice based on PROFILE_READY data

## Testing

### Unit Tests

Located in `tests/FinWise.Orchestrator.Tests/WorkflowTests.cs`:
- Test DTOs, profile store, incremental profile saving
- Do not require Azure OpenAI (mock-friendly)

### E2E Tests

Located in `tests/FinWise.Orchestrator.Tests/EndToEndMcpTests.cs`:
- Require running server on `http://127.0.0.1:3923`
- Require Azure OpenAI credentials
- Test complete user journeys via MCP protocol

## Common Tasks

### Adding a New Agent

1. Create agent class with system prompt (see `UserProfileAgent.cs`)
2. Add agent to workflow in `createAgentsAndWorkflow` function in `Program.cs`
3. Update orchestrator prompt to include new agent in routing rules
4. Add handoff configuration in `AgentWorkflowBuilder`

### Adding a New MCP Tool

1. Define async function in `Program.cs` (local function pattern)
2. Create `McpServerTool` wrapper with name and description
3. Add to `WithTools([...])` array in MCP server configuration

### Modifying Session Behavior

- Session timeout settings: `AgentThreadManager.SessionTimeoutAfterProfileComplete`
- Reset triggers: Add phrases to `SessionResetEvaluator.ResetTriggers`

## Dependencies

| Package | Purpose |
|---------|---------|
| `Microsoft.Extensions.AI` | AI abstractions (IChatClient) |
| `Microsoft.Extensions.AI.OpenAI` | Azure OpenAI integration |
| `Azure.AI.OpenAI` | Azure OpenAI client |
| `ModelContextProtocol.AspNetCore` | MCP HTTP server |
| `Serilog.AspNetCore` | Structured logging |

Internal project references:
- `Microsoft.Agents.AI` - Core agent abstractions
- `Microsoft.Agents.AI.Workflows` - Workflow orchestration
