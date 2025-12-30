# Feature Specification: Core Multi-Agent Workflow

**Feature Branch**: `001-core-workflow`  
**Created**: December 26, 2025  
**Status**: Draft  

## Glossary

**Hollow Agent**: A simplified or placeholder agent implementation that demonstrates workflow mechanics without full production capabilities. For example, the "hollow Global Advisor Agent" provides generic investment advice ("consider stocks vs. real estate") without accessing real market data or performing deep analysis. Hollow agents validate orchestration patterns while deferring complex domain logic to future iterations.

---

## Relationship to the Vision Doc

- This feature primarily implements **FR-1** (Multi-Agent Workflow with orchestration/triage agent) from [specs/01-idea-vision-scope.md](../01-idea-vision-scope.md).
- It also aligns with **FR-11** (Multi-Client Accessibility) by exposing the workflow through an MCP server.
- Profile persistence (database), deep investment logic, and external market/real-estate data are intentionally **out of scope** for this baseline feature.
- The architecture MUST be extensible to support additional agent types introduced in the vision (e.g., the **Investment Strategy Summarization Agent**, **Risk Management Agent**, and their associated functional requirements) in later versions, even though those agents and their persistence concerns are **not implemented** in this feature.
- **Risk Management Agent** is explicitly deferred to **v0.4** as specified in [01-idea-vision-scope.md](../01-idea-vision-scope.md#v04-risk-analysis-and-stock-purchase-execution---active-portfolio-management). This v0.1 baseline focuses on core workflow orchestration without portfolio risk assessment capabilities.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Core Multi-Agent Flow with Context Preservation (Priority: P1)

A user initiates a conversation with FinWise. The orchestrator receives the user's query, chooses which specialized agent should act next, and manages handoffs between the hollow user profile agent and hollow global investment advisor agent. Conversation context is preserved across handoffs, and the active specialized agent responds directly to the user.

**Why this priority**: This validates the core multi-agent workflow (routing + handoff + context) that everything else builds on.

**Independent Test**: Send a simple query like "I want investment advice" and verify:
- Orchestrator routes to profile agent when needed
- Profile agent gathers context and signals completion
- Orchestrator hands off to advisor agent with profile context
- Advisor agent responds directly to the user
- Conversation history includes both agents' turns

**Acceptance Scenarios**:

1. **Given** a user starts a new conversation, **When** they ask "I need investment advice", **Then** the orchestrator receives the query and initiates the workflow
2. **Given** the orchestrator analyzes the query, **When** basic user context is missing, **Then** it routes to the hollow user profile agent first
3. **Given** the hollow user profile agent is interacting with the user, **When** it completes gathering basic information, **Then** it signals completion to the orchestrator
4. **Given** the profile agent has signaled completion, **When** the orchestrator receives the signal, **Then** it hands off to the hollow global investment advisor agent with the collected context
5. **Given** the advisor agent starts after handoff, **When** it formulates investment advice, **Then** the advisor agent responds directly to the user (not the orchestrator compiling responses)
6. **Given** the advisor agent is active, **When** the user asks a follow-up question, **Then** the advisor can reference information collected earlier in the session
7. **Given** a multi-turn conversation with handoffs, **When** the user reviews the conversation history, **Then** all agent interactions and transitions are visible and ordered

---

### User Story 2 - Dynamic Routing and Navigation (Priority: P1)

During a conversation, the user can move between agents based on current needs. The orchestrator dynamically routes queries (profile vs. investment) and supports revisiting previously visited agents (e.g., update profile after receiving advice). Handoff and context-preservation mechanics are covered by **User Story 1**.

**Why this priority**: Users do not follow fixed sequential steps; navigation and routing make the workflow feel natural and reduce repetition.

**Independent Test**: Exercise at least three patterns:
- Query routes directly to one agent (profile-only or advisor-only)
- Sequential route (profile then advisor)
- Back-navigation (advisor → profile update → advisor)

**Acceptance Scenarios**:

1. **Given** a user asks "What is my risk tolerance?", **When** the orchestrator analyzes the query, **Then** it routes only to the hollow user profile agent
2. **Given** a user asks "Should I invest in stocks or real estate?", **When** the orchestrator analyzes the query, **Then** it routes only to the hollow global investment advisor agent
3. **Given** a user asks "What investments match my profile?", **When** the orchestrator analyzes the query, **Then** it routes to both profile and advisor agents in sequence (profile before advisor)
4. **Given** a user has completed interaction with the advisor agent, **When** they say "I want to update my risk tolerance", **Then** the orchestrator routes back to the hollow user profile agent
5. **Given** the user profile is updated mid-conversation, **When** the user asks for investment advice again, **Then** the orchestrator routes to the advisor agent with the updated profile context
6. **Given** a user is in the middle of a conversation with any agent, **When** they ask a question relevant to a different agent's expertise, **Then** the orchestrator routes the request to the appropriate agent and records the routing decision reason


---

### User Story 3 - Agent Escalation and Fallback (Priority: P2)

When a specialized agent cannot fully handle a user query or needs additional expertise, it can escalate to the orchestrator for rerouting to a more specialized agent. For example, the hollow global investment advisor may receive questions about specific stock values that require deeper analysis than its general guidance capability. Expert handoff scenarios allow smooth transitions when deeper specialization is needed.

**Why this priority**: Demonstrates intelligent agent cooperation and improves response quality, though basic routing can work initially without this.

**Independent Test**: Can be tested by giving the hollow global investment advisor agent a query about a specific stock (e.g., "Should I buy MSFT?") and verifying it escalates to the orchestrator, which explains that detailed stock analysis will be available in v0.2.

**Acceptance Scenarios**:

1. **Given** the hollow global investment advisor agent receives a question about a specific stock value (e.g., "Should I invest in MSFT stock?"), **When** it recognizes this requires detailed stock analysis outside its global advisory capability, **Then** it requests escalation to the orchestrator
2. **Given** an agent has escalated a query, **When** the orchestrator receives the escalation, **Then** it identifies the appropriate expert agent (in PoC, it may inform user that detailed stock analysis will be available in v0.2) and performs appropriate action
3. **Given** an expert handoff is performed, **When** the new agent takes over, **Then** it receives full context about why the handoff occurred and what the user needs
4. **Given** an agent cannot handle a query and no other agent is suitable, **When** the fallback mechanism triggers, **Then** the orchestrator provides a helpful message explaining the limitation and offers alternatives (e.g., "Detailed stock analysis will be available in the next version")
5. **Given** multiple escalations occur in a conversation, **When** the session completes, **Then** all escalation paths and decisions are logged for transparency

---

### User Story 4 - Extensible Agent Registration (Priority: P2)

The workflow infrastructure allows new specialized agents to be added without modifying the core orchestration logic. A developer can register a new specialized agent by providing its capabilities and integration points.

**Why this priority**: Critical for long-term extensibility but not required for initial PoC functionality.

**Independent Test**: Can be tested by adding a third hollow agent (e.g., a hollow risk agent) and verifying the orchestrator can incorporate it without code changes to orchestration core.

**Acceptance Scenarios**:

1. **Given** a new specialized agent is developed, **When** it is registered with the workflow system, **Then** the orchestrator becomes aware of its capabilities
2. **Given** a new agent is registered, **When** a user query matches its capabilities, **Then** the orchestrator can route requests to it
3. **Given** multiple agents are registered, **When** the orchestrator receives a query, **Then** it can coordinate any combination of available agents
4. **Given** an agent is added or removed, **When** the system restarts, **Then** the workflow adapts without requiring orchestrator code changes

---

### Edge Cases

- What happens when a specialized agent fails or times out during workflow execution? (System should gracefully handle failures and inform user)
- How does the system handle ambiguous queries that could route to multiple agents? (Orchestrator makes best determination or asks user for clarification)
- What happens if agents provide conflicting information? (Orchestrator presents both perspectives with explanations per FR-1)
- How does the system handle very long conversation histories? (Context management strategy needed - may summarize or trim older context)
- What happens when no specialized agents match a user query? (Orchestrator provides helpful error message or general guidance)
- What happens when a user rapidly switches between agents (e.g., profile → advisor → profile → advisor)? (System maintains coherent context across all transitions)
- How does the system handle circular escalations where agent A escalates to agent B, which tries to escalate back to agent A? (Orchestrator detects and breaks circular patterns, potentially involving user for clarification)
- How does the system handle concurrent requests from the same user? (Queue or reject concurrent requests to maintain conversation coherence)

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a workflow infrastructure that supports coordination of multiple specialized agents
- **FR-002**: System MUST include an orchestrator/triage agent that receives user queries and determines agent routing (orchestrator focuses on routing, not response generation)
- **FR-003**: System MUST support agent handoff, allowing one agent to transfer control to another while preserving conversation context
- **FR-004**: System MUST maintain conversation history across all agent interactions within a session
- **FR-005**: System MUST allow specialized agents to be registered with the workflow system. **v0.1**: Manual agent registration via code (configure `ChatClientAgent` and add to `AgentWorkflowBuilder`). **v0.2+**: Dynamic registration without modifying orchestrator core logic (plugin architecture).
- **FR-006**: Orchestrator MUST be able to route user queries to appropriate specialized agent(s) based on dynamic query analysis
- **FR-007**: Specialized agents MUST respond directly to users when they have the answer (orchestrator manages routing, not response compilation)
- **FR-008**: System MUST implement a hollow user profile agent for PoC that simulates basic profile gathering (asks 3 questions: risk tolerance, investment goals, timeframe)
- **FR-009**: System MUST implement a hollow global investment advisor agent for PoC that simulates basic investment advice (provides generic stock vs. real estate guidance)
- **FR-010**: System MUST handle agent failures gracefully, informing users when an agent cannot complete its task
- **FR-011**: System MUST be accessible through MCP-compatible clients as defined in 'FR-11: Multi-Client Accessibility' of the vision document
- **FR-012**: System MUST log all agent interactions, decisions, handoffs, and routing changes for debugging and transparency
- **FR-013**: Orchestrator MUST present both agent perspectives when specialized agents provide conflicting information (this is the exception where orchestrator does respond - to present conflicts)
- **FR-014**: System MUST support multi-turn conversations where users can ask follow-up questions within the same session
- **FR-015**: System MUST support dynamic agent navigation (dynamic handoff), allowing users to move between agents based on current needs, return to previously visited agents to update information, and switch agents flexibly rather than following fixed sequential steps
- **FR-016**: System MUST enable agents to escalate queries to other agents when they encounter requests outside their capability, including expert handoff scenarios where one agent transfers control to a more specialized agent with full context preservation
- **FR-017**: System MUST provide fallback mechanisms when an agent cannot handle a query and no suitable alternative agent exists
- **FR-018**: System MUST detect and prevent circular escalation patterns (agent A → agent B → agent A) by tracking escalation paths
- **FR-019**: Orchestrator MUST maintain complete conversation state including visited agents, current agent, and navigation history
- **FR-020**: System MUST prevent circular handoff patterns via build-time workflow validation (framework's `AgentWorkflowBuilder` enforces valid handoff graph, invalid configurations rejected during `Build()` call)

### Assumptions

- The baseline implementation will support dynamic workflow navigation, allowing users to move between agents flexibly rather than following fixed sequential steps
- While parallel agent execution is deferred, the system will support non-sequential agent switching and revisiting
- Hollow agents will use an actual AI inference (Language Model) for the baseline v0.1 implementation, even when they might lack specialized context, though.
- Context preservation will be managed in-memory for the baseline implementation; persistent storage (like user profile persistence, investment recomendation summary with investment strategy per session and user's portfolio persistence) will be addressed in later iterations or versions of the application.
- **Conversation history scope (v0.1)**: Framework maintains message history during a single workflow execution only (within one MCP tool call). Each MCP tool invocation is stateless - no persistent conversation history across multiple tool calls. Persistent session management deferred to v0.2+.
- **Routing strategy (all phases)**: The orchestrator will use LLM-only routing (ChatClientAgent system prompts analyze queries to determine which agent to invoke). This is the simplest implementation with zero custom heuristics.
- Initial implementation will support single-user, single-session scenarios; concurrent multi-user support is deferred
- MCP server integration will be demonstrated but hollow agents won't require external data sources initially
- Escalation and fallback mechanisms will be implemented with simple heuristics; advanced AI-based decision making is deferred
- The system will track basic navigation history to prevent simple circular patterns; complex loop detection is deferred

### Key Entities

- **Agent**: Specialized component in the workflow with specific capabilities (profile management, investment advice, etc.). Implemented as framework `ChatClientAgent` instances with system prompts and tool configurations. Key configuration: agent ID, system prompt, description.

- **UserProfile** (Persistent Entity): User investment preferences stored in PostgreSQL. Key attributes: user identifier, risk tolerance, investment goals (string), investment timeframe, questionnaire responses (JSONB), version (optimistic concurrency).

- **WorkflowExecutionContext** (Transient): Per-request context for MCP tool invocations. Key attributes: user identifier, query, request timestamp. Discarded after workflow completes.

**Framework-Managed Entities** (v0.1 - not persisted):
- **Conversation State**: Managed by framework's `StreamingRun` during workflow execution
- **Agent Messages**: Managed by framework's `ChatMessage` accumulation
- **Agent Handoffs**: Configured via `AgentWorkflowBuilder.WithHandoffs()` at build time
- **Navigation History**: Tracked by framework's `WorkflowEvent` stream

**Future Entities** (v0.2+):
- **Conversation Session**: Persistent conversation history (deferred - v0.1 is stateless per MCP tool call)

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can initiate a conversation and receive a response that demonstrates coordination between at least two specialized agents (hollow profile agent and hollow advisor agent)
- **SC-002**: The orchestrator successfully routes all defined test queries to the appropriate specialized agent(s) based on query content
- **SC-003**: Context from the profile agent is successfully passed to and accessible by the advisor agent in all defined test scenarios
- **SC-004**: The system completes a full multi-agent workflow (user query → orchestrator → profile agent → advisor agent → user response) in under 10 seconds for the hollow implementation
- **SC-005**: All agent interactions, handoffs, routing changes, and decisions are logged with sufficient detail to understand the workflow execution path
- **SC-006**: The system gracefully handles at least 3 different types of agent failure scenarios (timeout, error, unavailable) without crashing
- **SC-007**: A developer can add a third hollow specialized agent and have it integrated into workflows within 30 minutes without modifying orchestrator core logic
- **SC-008**: The system maintains conversation history for at least 10 turns in a single session without losing context
- **SC-009**: The system successfully exposes the multi-agent workflow through at least one MCP-compatible client (Claude Desktop, ChatGPT, or GitHub Copilot)
- **SC-010**: When agents provide conflicting information, the orchestrator presents both perspectives in all defined conflict test cases
- **SC-011**: A user can return to the profile agent after receiving investment advice, update their profile, and receive revised recommendations in all defined test scenarios
- **SC-012**: The system supports at least 3 different dynamic navigation patterns (sequential, back-navigation, non-sequential jumps) without losing context
- **SC-013**: When an agent escalates a query, the target agent receives complete context about the escalation reason in all defined escalation test cases
- **SC-014**: The system detects and prevents at least 2 types of circular escalation patterns without requiring manual intervention
- **SC-015**: A user can switch between agents at least 5 times in a single conversation while maintaining coherent context throughout

## Scope & Dependencies

### In Scope

- Baseline workflow infrastructure supporting multiple specialized agents with dynamic routing
- Orchestrator/triage agent implementation with dynamic query routing and agent navigation
- Two hollow specialized agents (user profile and global investment advisor)
- Agent handoff mechanism with context preservation
- Dynamic workflow navigation allowing users to return to previous agents or switch between agents flexibly
- Agent escalation mechanisms for handling queries outside an agent's capability
- Fallback mechanisms when no suitable agent can handle a query
- Expert handoff scenarios with full context transfer
- Circular escalation detection and prevention
- Conversation session management with navigation history tracking
- Basic error handling and logging including all routing decisions
- MCP server interface for client accessibility
- Agent registration system for extensibility

### Out of Scope (Deferred to Future Iterations)

- Parallel agent execution (baseline supports dynamic sequential navigation only)
- Persistent storage of conversation history (baseline is in-memory)
- Domain-accurate investment logic and data-grounded recommendations (baseline agents will be LLM-driven, but intentionally "hollow" by probably lacking specialized context)
- Multi-user concurrent session support
- Agent conflict resolution beyond presenting both perspectives
- Performance optimization for high-volume requests
- Integration with actual external data sources (MCP servers for market data, etc.)
- Authentication and authorization
- Advanced context summarization for long conversations

### Dependencies

- **MCP Protocol Understanding**: Team must understand MCP server/client architecture to expose workflow properly
- **Agent Framework Selection**: Decision on which .NET agent framework to use (Microsoft Agent Framework, Semantic Kernel, or custom)
- **Logging Infrastructure**: Basic logging framework must be available (can be simple console logging for PoC)

### Constraints

- Must be implemented in .NET (C#) to align with existing codebase
- Must follow MCP protocol standards for client compatibility
- Hollow agents must be simple enough to implement quickly (2-3 days each max)
- Initial implementation should prioritize clarity and extensibility over performance
- Must not require external paid services for the PoC (free-tier APIs acceptable)
