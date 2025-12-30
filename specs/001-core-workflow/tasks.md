# Tasks: Core Multi-Agent Workflow

**Input**: Design documents from `specs/001-core-workflow/`  
**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/)

**Organization**: Tasks organized into INCREMENTAL PHASES for simplified POC. Start with simplest in-memory implementation, then add persistence and features progressively. Aligned with FinWise Constitution v2.0.0.

## Format: `- [ ] [ID] [P?] [US#?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[US#]**: User Story reference (US1, US2, US3, US4) from spec.md for traceability
- Include exact file paths in descriptions

---

## Implementation Phase 1: Core Simplified Workflow (In-Memory Only - Single Process) 🎯 FIRST POC

**Goal**: Get the simplest possible multi-agent workflow working with in-memory profile data in a SINGLE PROCESS

**Why this phase**: Validate agent orchestration, handoffs, and context preservation WITHOUT database complexity or multi-process architecture

**What's included**:
- ✅ Three agents (Orchestrator, User Profile, Global Investment Advisor) - ALL in single process
- ✅ AgentWorkflowBuilder with handoffs
- ✅ In-memory profile storage (Dictionary<string, UserProfileDto>) - profile asked every conversation
- ✅ MCP server exposing get_investment_recommendations
- ✅ Basic logging and error handling
- ✅ SINGLE .NET process - no separate MCP servers

**What's deferred to Implementation Phase 2**:
- ❌ PostgreSQL database
- ❌ User Profile MCP Server (separate process)
- ❌ Profile persistence across conversations
- ❌ Database migrations
- ❌ Entity Framework Core

### Setup for Implementation Phase 1

- [ ] T001 Create solution structure: `src/FinWise.Orchestrator/` directory only (single process, no database server)
- [ ] T002 Initialize FinWise.Orchestrator.csproj with .NET 10 and dependencies:
  - Microsoft.Extensions.AI (Agent Framework abstractions)
  - Microsoft.AI.Agents (ChatClientAgent, AgentWorkflowBuilder, InProcessExecution)
  - Microsoft.ModelContextProtocol (MCP server STDIO transport)
  - Serilog.AspNetCore (structured logging)
  - Azure.AI.OpenAI (LLM inference)
  - NO Entity Framework Core, NO Npgsql (deferred to Implementation Phase 2)
- [ ] T003 [P] Configure .editorconfig and StyleCop analyzers at solution root (enforce zero warnings - Constitution I)
- [ ] T004 [P] Setup xUnit test project: FinWise.Orchestrator.Tests with FluentAssertions, Moq (NO Testcontainers in Implementation Phase 1)

**Checkpoint**: Project structure ready, can build and run tests

### Models for Implementation Phase 1 (In-Memory)

- [ ] T005 [P] Create UserProfileDto record in src/FinWise.Orchestrator/Models.cs:
  - Fields: user_identifier, risk_tolerance (enum), investment_goals (string), investment_timeframe (enum)
  - Stored in static Dictionary<string, UserProfileDto> - NO database, NO persistence
  - Profile asked every conversation (no cross-session persistence)
- [ ] T006 [P] Create WorkflowExecutionContext record in src/FinWise.Orchestrator/Models.cs (user_identifier, query, request_time)

**Checkpoint**: Data models ready for in-memory usage

### Infrastructure for Implementation Phase 1

- [ ] T007 Configure Azure OpenAI client in src/FinWise.Orchestrator/Program.cs using environment variables (AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_DEPLOYMENT_NAME)
- [ ] T008 Setup Serilog structured logging in src/FinWise.Orchestrator/Program.cs (JSON output to console, request IDs)
- [ ] T009 Implement basic error handling for MCP server in src/FinWise.Orchestrator/Program.cs (graceful failures)

**Checkpoint**: Infrastructure ready - LLM client configured, logging works

### Core Workflow Implementation

- [ ] T010 [P] [US1] Create Orchestrator Agent in src/FinWise.Orchestrator/Program.cs:
  - ChatClientAgent with system prompt for query routing
  - Agent ID: "orchestrator"
  - Description: "Routes queries to appropriate specialist agent"

- [ ] T011 [P] [US1] Create User Profile Agent in src/FinWise.Orchestrator/Program.cs:
  - ChatClientAgent with system prompt: "You collect user investment profile through a multi-turn conversation. Ask ONE question at a time: (1) risk tolerance, (2) investment goals, (3) timeframe. Wait for user response before asking the next question."
  - Directly updates static Dictionary<string, UserProfileDto> (NO MCP client calls, NO database)
  - Agent ID: "profile-agent"
  - Description: "Collects user investment profile - in-memory Dictionary storage (Implementation Phase 1)"
  - **Pattern**: Multi-turn conversation - 1 question per turn, waits for response before next question

- [ ] T012 [P] [US1] Create Global Advisor Agent in src/FinWise.Orchestrator/Program.cs:
  - ChatClientAgent with system prompt for investment advice
  - Receives user profile context from handoff
  - Agent ID: "advisor-agent"
  - Description: "Provides investment recommendations based on profile"

- [ ] T013 [US1] Build workflow with AgentWorkflowBuilder in src/FinWise.Orchestrator/Program.cs:
  - CreateHandoffBuilderWith(orchestratorAgent)
  - WithHandoffs(orchestratorAgent, [profileAgent, advisorAgent])
  - WithHandoffs(profileAgent, [advisorAgent])
  - Build() and validate handoff graph

- [ ] T013.1 [US1] Add negative test for invalid workflow configurations in tests/FinWise.Orchestrator.Tests/WorkflowTests.cs:
  - Test: Circular handoff (agent A → agent B → agent A) triggers Build() exception
  - Test: Missing target agent reference triggers Build() exception
  - Test: Invalid handoff destination triggers Build() exception
  - Verify: AgentWorkflowBuilder.Build() throws InvalidOperationException with descriptive message
  - Validates FR-020 (build-time validation preventing circular handoff patterns)

- [ ] T014 [US1] Implement MCP tool: get_investment_recommendations in src/FinWise.Orchestrator/Program.cs:
  - Create WorkflowExecutionContext from input (user_identifier, query)
  - Check static Dictionary<string, UserProfileDto> for existing profile (if not found, User Profile Agent will collect)
  - Execute workflow with InProcessExecution.StreamAsync
  - Watch WorkflowEvent stream (ExecutorInvokedEvent, WorkflowOutputEvent)
  - Log all agent transitions with request_id
  - Return recommendation + agent_workflow array (if include_reasoning=true)

- [ ] T015 [US1] Setup MCP server with STDIO transport in src/FinWise.Orchestrator/Program.cs:
  - Register get_investment_recommendations tool
  - Configure server metadata (name: finwise-orchestrator, version: 0.1.0)

**Checkpoint**: MCP server running, workflow executes end-to-end with in-memory profile

### Testing & Validation Implementation Phase 1

- [ ] T016 [US1] Create integration test in tests/FinWise.Orchestrator.Tests/WorkflowTests.cs:
  - Test 1: "I need investment advice" routes through orchestrator → profile → advisor
  - Test 2: Extended 10+ turn conversation verifying context preservation (SC-008 requirement)
  - Setup: Mock Azure OpenAI client responses
  - Verify: Orchestrator invoked, User Profile Agent invoked (stores in Dictionary), Global Advisor Agent invoked
  - Assert: Profile stored in static Dictionary<string, UserProfileDto>, recommendation returned
  - Verify: agent_workflow contains 3 agents, workflow completes in <10s
  - Verify: 10+ turn conversation maintains full context without loss (validates FR-004, SC-008)
  - NO database, NO MCP client calls, NO multi-process - pure in-memory single process

- [ ] T017 [US1] Add performance logging for workflow execution time (<10s target)

- [ ] T018 [US1] Manual testing with Claude Desktop:
  - Configure mcp.json to point to FinWise.Orchestrator (single .NET process, STDIO transport)
  - Test: "I need investment advice" conversation
  - Verify: User Profile Agent collects profile and stores in Dictionary<string, UserProfileDto>
  - Test: Ask follow-up question in SAME session - profile remembered (Dictionary persists)
  - Test: Restart process - profile lost (no persistence across restarts yet)
  - Verify: Single process only, no User Profile MCP Server running

**Checkpoint Implementation Phase 1 Complete**: ✅ Working multi-agent workflow with in-memory profile data in SINGLE PROCESS

**Validation**: 
- Single .NET process running (FinWise.Orchestrator only)
- Profile stored in Dictionary<string, UserProfileDto> (in-memory)
- Profile survives within process session but lost on restart
- Can have complete investment advice conversation through Claude Desktop
- NO database, NO User Profile MCP Server, NO multi-process architecture yet

---

## Implementation Phase 2: Profile Persistence (PostgreSQL + User Profile MCP Server)

**Goal**: Add persistent profile storage so users don't have to re-enter profile every conversation (or after process restarts)

**Why this phase**: Build on working Implementation Phase 1 single-process workflow, add multi-process architecture with separate User Profile MCP Server for database operations

**Key Architectural Change**: 
- Dictionary<string, UserProfileDto> → PostgreSQL database via User Profile MCP Server (separate process)
- Single process → Multi-process (FinWise.Orchestrator + FinWise.UserProfile.McpServer)
- User Profile Agent changes from Dictionary updates → User Profile Agent with MCP client that calls to User Profile MCP Server

**Key Change**: Dictionary<string, UserProfileDto> → PostgreSQL via User Profile MCP Server (separate process)

**What's added**:
- ✅ PostgreSQL 18 database
- ✅ User Profile MCP Server (separate process)
- ✅ UserProfile entity with EF Core
- ✅ Profile CRUD operations via MCP tools
- ✅ Profile Agent now uses MCP client to save/retrieve profiles

**What remains from Implementation Phase 1**:
- ✅ Three agents (no changes to orchestrator or advisor)
- ✅ Workflow structure (same handoffs)
- ✅ MCP server exposing get_investment_recommendations

### Database Setup

- [ ] T019 Create docker/postgresql/docker-compose.yml for PostgreSQL 18 container
- [ ] T020 Create src/FinWise.UserProfile.McpServer/ directory
- [ ] T021 Initialize FinWise.UserProfile.McpServer.csproj with .NET 10 and dependencies (EF Core 10, Npgsql, Microsoft.ModelContextProtocol, Serilog)
- [ ] T022 Setup xUnit test project: FinWise.UserProfile.McpServer.Tests with Testcontainers

**Checkpoint**: Database infrastructure ready

### User Profile Entity & Database

- [ ] T023 Create UserProfile entity in src/FinWise.UserProfile.McpServer/UserProfile.cs:
  - Fields: Id (UUID), UserIdentifier (string), RiskTolerance (enum), InvestmentGoals (string 1-500 chars), InvestmentTimeframe (enum), QuestionnaireResponses (JSONB nullable), CreatedAt, UpdatedAt, Version
  - Validation: UserIdentifier pattern, InvestmentGoals length

- [ ] T024 Create DbContext in src/FinWise.UserProfile.McpServer/Program.cs:
  - Configure user_profiles table
  - Add indexes (user_identifier unique)
  - Configure optimistic concurrency (Version field)

- [ ] T025 Create EF Core migration for user_profiles table:
  - Run: dotnet ef migrations add InitialCreate
  - Apply: dotnet ef database update (or auto-apply on startup)

**Checkpoint**: Database schema created, can query user_profiles table

### User Profile MCP Server Implementation

- [ ] T026 Implement User Profile MCP Server in src/FinWise.UserProfile.McpServer/Program.cs:
  - MCP server setup with STDIO transport
  - Tool: get_user_profile (queries by user_identifier, returns UserProfileDto)
  - Tool: save_user_profile (creates new UserProfile record)
  - Tool: update_user_profile (updates existing with optimistic concurrency check)
  - Error handling: PROFILE_NOT_FOUND, DATABASE_CONNECTION_ERROR, CONCURRENCY_CONFLICT

- [ ] T027 Implement PostgreSQL connection pooling in src/FinWise.UserProfile.McpServer/Program.cs

- [ ] T028 Add Serilog logging to User Profile MCP Server (structured JSON, log all operations)

**Checkpoint**: User Profile MCP Server running standalone, can save/retrieve profiles

### Integration with Main Workflow

- [ ] T029 Update User Profile Agent in src/FinWise.Orchestrator/Program.cs:
  - Add MCP client connection to User Profile MCP Server (STDIO)
  - Replace in-memory Dictionary with MCP tool calls (get_user_profile, save_user_profile, update_user_profile)
  - Keep same ChatClientAgent system prompt

- [ ] T030 Update in-memory profile storage from Implementation Phase 1:
  - Remove Dictionary<string, UserProfileDto>
  - Profile now retrieved via MCP client from database
  - Profile saved via MCP client to database

- [ ] T031 Add appsettings.json configuration in both projects:
  - src/FinWise.Orchestrator/appsettings.json: Azure OpenAI config (reference env vars)
  - src/FinWise.UserProfile.McpServer/appsettings.json: PostgreSQL connection string

**Checkpoint**: Profile Agent now uses MCP client for persistence, workflow still works end-to-end

### Testing Implementation Phase 2

- [ ] T032 Create database integration test in tests/FinWise.UserProfile.McpServer.Tests/DatabaseTests.cs:
  - Test: Save profile, retrieve profile, update profile
  - Test: Optimistic concurrency (version conflicts)
  - Use Testcontainers for PostgreSQL

- [ ] T033 Create MCP protocol integration test in tests/FinWise.UserProfile.McpServer.Tests/McpProtocolTests.cs:
  - Use official MCP SDK McpClient with StdioClientTransport (launches server via dotnet run)
  - Test: ListToolsAsync() returns get_user_profile, save_user_profile, update_user_profile
  - Test: CallToolAsync("get_user_profile") with userId returns profile data
  - Test: CallToolAsync("save_user_profile") creates new profile
  - Use FluentAssertions for result validation
  - SDK handles initialize handshake automatically

- [ ] T034 Manual testing with VS Code GH Copilot and/or Claude Desktop:
  - First conversation: Profile collected and saved
  - Second conversation: Profile retrieved automatically (user not asked again)
  - Update profile mid-conversation: Changes persisted

**Checkpoint Implementation Phase 2 Complete**: ✅ Profile persistence working, users don't re-enter profile each time

**Validation**: 
- Profile survives across conversations
- Two processes running (FinWise.Orchestrator + FinWise.UserProfile.McpServer)
- PostgreSQL container running with user_profiles data

---

## Implementation Phase 3: Enhanced Features (Routing + Escalation + Extensibility)

**Goal**: Add all remaining user story features in one integrated phase

**Why this phase**: The workflow needs additional features to reach the functional goals. 

**What's added**:
- ✅ Bidirectional handoffs (advisor → profile for updates)
- ✅ Agent escalation and fallback handling
- ✅ Risk Assessment Agent (extensibility demonstration)
- ✅ Navigation history logging

**Prerequisites**: Implementation Phase 2 complete (persistence working)

### Routing Implementation

**Note**: All routing uses LLM-only approach (orchestrator system prompt) - no custom routing code needed. The framework handles routing automatically through ChatClientAgent system prompts.

- [ ] T035 [US2] Add bidirectional handoff support in workflow in src/FinWise.Orchestrator/Program.cs:
  - Modify: WithHandoffs(advisorAgent, [profileAgent]) enables advisor → profile back-navigation
  - Framework validates no circular patterns at Build() time

- [ ] T036 [US2] Implement navigation history logging in src/FinWise.Orchestrator/Program.cs:
  - Track visited agents from WorkflowEvent stream (ExecutorInvokedEvent)
  - Log navigation pattern based on actual execution path through handoff relationships:
    - profile-only: orchestrator → profile (no advisor needed)
    - advisor-only: orchestrator → advisor (profile already known)
    - sequential: orchestrator → profile → advisor (collect then advise)
    - back-nav: orchestrator → advisor → profile → advisor (return to profile for updates)
  - Include in agent_workflow response array

**Checkpoint**: Routing logic implemented, can route to different agents based on query

### Testing Implementation Phase 3

- [ ] T037 [US2] Create routing integration tests in tests/FinWise.Orchestrator.Tests/RoutingTests.cs:
  - Test: "What is my risk tolerance?" routes ONLY to profile agent
  - Test: "Should I invest in stocks?" routes ONLY to advisor agent
  - Test: "What investments match my profile?" routes profile → advisor sequentially
  - Test: Back-navigation - advisor conversation → "update my risk tolerance" → profile → advisor
  - Verify context preserved across all patterns

- [ ] T038 [US2] Manual testing with Claude Desktop:
  - Test all 4 routing patterns
  - Verify navigation history logged
  - Verify back-navigation updates profile and advisor sees updated context

**Checkpoint**: Routing logic implemented, can route to different agents based on query

### Escalation & Extensibility

- [ ] T039 [US3] Implement escalation handling in advisor agent system prompt:
  - System prompt includes: "If asked about specific stocks (MSFT, AAPL, etc.), escalate by saying 'Detailed stock analysis will be available in v0.2. For now, I can provide general guidance on stocks vs real estate.'"
  - No code changes needed - LLM handles escalation detection and response

- [ ] T040 [US4] Add Risk Assessment Agent (extensibility demo) in src/FinWise.Orchestrator/Program.cs:
  - Create ChatClientAgent: riskAgent with system prompt "You assess investment risk tolerance through questionnaire"
  - Add to workflow: WithHandoffs(orchestratorAgent, [profileAgent, advisorAgent, riskAgent])
  - Test: Send "Assess my risk" → orchestrator routes to riskAgent
  - Demonstrates adding new agents requires only: new ChatClientAgent + update WithHandoffs (zero orchestrator logic changes)

- [ ] T041 [US3] Create escalation and extensibility tests in tests/FinWise.Orchestrator.Tests/AdvancedTests.cs:
  - Test: "Should I buy MSFT stock?" → advisor escalates gracefully
  - Test: "Assess my risk" → routes to new riskAgent
  - Test: Adding riskAgent required zero orchestrator code changes (extensibility validation)

**Checkpoint Implementation Phase 3 Complete**: ✅ All user stories implemented, 4 agents working, dynamic routing, graceful escalation

**Validation**: Users can ask any type of query without following fixed steps

---

## Implementation Phase 4: Polish & Documentation

**Goal**: Documentation and final polish for demo-ready POC

**Why this phase**: Complete documentation, validate performance, ensure production quality

**What's added**:
- ✅ User guides and architecture documentation
- ✅ Performance validation
- ✅ Error handling verification
- ✅ Code cleanup
- ✅ Quickstart validation

**Prerequisites**: Implementation Phases 1-3 complete (all features working)

### Documentation

- [ ] T042 Create user guide in docs/user-guide/mcp-setup-v01.md:
  - Prerequisites, setup steps, Claude Desktop configuration
  - Example queries and troubleshooting

- [ ] T043 Create architecture diagram in docs/architecture/v01-local-mcp-stdio.md:
  - Component diagram (AI assistant → FinWise → User Profile MCP Server → PostgreSQL)
  - Workflow sequence diagram (orchestrator → profile → advisor)

- [ ] T044 Update README.md: Project overview, quick start, architecture summary

### Performance & Quality

- [ ] T045 Validate performance metrics:
  - Verify workflow <10s, database operations <500ms
  - Run existing performance logging from Implementation Phase 1

- [ ] T046 Verify error handling:
  - Test agent timeout, LLM service errors, database failures
  - Ensure user-friendly error messages

### Code Cleanup

- [ ] T047 Code cleanup:
  - Extract repeated logic to local functions
  - Add XML documentation
  - Ensure zero StyleCop/compiler warnings

- [ ] T048 Run quickstart validation:
  - Follow setup steps from scratch
  - Verify <30 minute setup time
  - Update docs with any clarifications

**Checkpoint Implementation Phase 4 Complete**: ✅ Production-ready POC

**Validation**: Complete documentation, performance validated, clean code

---

## Dependencies & Execution Order

### Phase Dependencies (Sequential for Single Developer with GitHub Copilot)

```
Implementation Phase 1: Core Simplified Workflow (In-Memory)
    ↓ MUST COMPLETE - Foundation
    ↓ Estimated: 1 day
    ↓
Implementation Phase 2: Profile Persistence
    ↓ Add database layer
    ↓ Estimated: 1 day
    ↓
Implementation Phase 3: Enhanced Features (Routing + Escalation + Extensibility)
    ↓ All remaining user story features
    ↓ Estimated: 1 day
    ↓
Implementation Phase 4: Polish & Documentation
    ↓ Final refinement and docs
    ↓ Estimated: 1 day
```

### Recommended Implementation Order (4-Day Sprint with GitHub Copilot)

**Day 1: Core In-Memory Workflow (Implementation Phase 1)**
- T001-T018: Three agents, workflow builder, MCP server
- **Checkpoint**: Can have investment conversations via Claude Desktop
- **Key**: Profile asked every time (in-memory Dictionary)

**Day 2: Profile Persistence (Implementation Phase 2)**
- T019-T034: PostgreSQL setup, User Profile MCP Server, EF Core
- **Checkpoint**: Profile saved across restarts
- **Key**: Two processes running (Orchestrator + User Profile MCP Server)

**Day 3: Enhanced Features (Implementation Phase 3)**
- T035-T041: Bidirectional handoffs, escalation, Risk Assessment Agent, all tests
- **Checkpoint**: 4 agents, dynamic routing, graceful escalation
- **Key**: All user stories implemented

**Day 4: Polish & Documentation (Implementation Phase 4)**
- T042-T047: User guides, architecture docs, performance validation, cleanup
- **Checkpoint**: Production-ready POC
- **Key**: Demo-ready with complete documentation

**Total Time**: 4 days for complete feature-rich POC

### Minimum Viable Product (MVP) Scopes

**Minimal MVP** = Implementation Phase 1 only (1 day)
- ✅ Three agents with in-memory profile
- ✅ Basic workflow execution
- ✅ Can demo via Claude Desktop
- ❌ No persistence (profile asked every time)

**Recommended MVP** = Implementation Phase 1 + Implementation Phase 2 (2 days)
- ✅ Three agents with persistent profile
- ✅ Database storage working
- ✅ Professional POC quality
- ✅ Ready for stakeholder demo

**Full Feature Set** = Implementation Phases 1-3 (3 days)
- ✅ All user stories implemented (routing, escalation, extensibility)
- ✅ 4 agents working (Orchestrator, Profile, Advisor, Risk)
- ⏳ Documentation minimal

**Production-Ready** = Implementation Phases 1-4 (4 days)
- ✅ Complete feature set
- ✅ Full documentation
- ✅ Performance validated
- ✅ Demo-ready POC

---

## Success Criteria Mapping

| Success Criterion | Implementation Phase | Tasks | Validation |
|-------------------|----------------------|-------|------------|
| SC-001: Multi-agent coordination | Implementation Phase 1 | T010-T016 | Integration test T016 |
| SC-002: Routing to correct agents | Implementation Phase 3 | T037 | Routing tests T037 |
| SC-003: Context preservation | Implementation Phase 1 | T013-T016 | Verify in T016 |
| SC-004: Workflow <10s | Implementation Phase 4 | T045 | Performance validation T045 |
| SC-005: Complete logging | Implementation Phase 4 | T047 | Verify in all tests |
| SC-006: Graceful failures | Implementation Phase 4 | T046 | Failure tests T046 |
| SC-007: Add agent <30min | Implementation Phase 3 | T040 | Risk Agent in T040 |
| SC-008: 10-turn history | Implementation Phase 1 | T014 | Extended conversation |
| SC-009: MCP client access | Implementation Phase 1 | T002, T015 | Manual test T018 |
| SC-010: Conflict presentation | Implementation Phase 3 | T039 | Escalation test T041 |
| SC-011: Back-navigation | Implementation Phase 3 | T035, T037 | Back-nav test T037 |
| SC-012: 3 navigation patterns | Implementation Phase 3 | T036-T037 | All routing tests T037 |
| SC-013: Escalation context | Implementation Phase 3 | T039-T041 | Escalation test T041 |
| SC-014: Circular prevention | Implementation Phase 3 | T038, T040 | Circular test T040 |
| SC-015: 5+ agent switches | Implementation Phase 3 | T040 | Extended nav test T040 |

---

## Quality Checklist (Per Constitution v2.0.0)

**Before marking any task complete, verify**:

### I. Code Quality (Constitution I)
- [ ] Zero StyleCop warnings
- [ ] Zero compiler warnings
- [ ] XML documentation for complex functions (workflow, MCP tools)
- [ ] Comments explain "why" not "what"
- [ ] No code duplication (extract to local functions if >3 lines repeated)
- [ ] Domain terminology consistent (profile, advisor, orchestrator, handoff)

### II. User Experience (Constitution II)
- [ ] Error messages user-friendly (no stack traces or technical jargon)
- [ ] Terminology aligned with FinWise glossary
- [ ] MCP tools work identically across all clients (Claude, ChatGPT, GHCP)

### III. Performance (Constitution III)
- [ ] MCP tool database operations <500ms (log actual times)
- [ ] Full workflow <10s (log actual times)
- [ ] All database queries use indexed columns
- [ ] Connection pooling configured
- [ ] No full table scans

**Validation**: Run all integration tests before marking phase complete

---

## Development Tips for Single Developer

### Implementation Phase 1 (In-Memory) Tips

**Why start here**:
- ✅ No Docker/PostgreSQL complexity
- ✅ Fast iteration on agent prompts
- ✅ Validate workflow orchestration first
- ✅ Can demo basic functionality quickly

**Testing approach**:
1. Start FinWise.Orchestrator process
2. Configure Claude Desktop mcp.json
3. Ask: "I want investment advice"
4. Verify: Profile agent asks questions → Advisor provides advice
5. Close/restart: Profile asked again (expected - in-memory)

**Success indicator**: Full conversation works end-to-end before adding database

### Implementation Phase 2 (Persistence) Tips

**Prerequisites**:
- Implementation Phase 1 working perfectly
- Docker Desktop installed and running

**Migration strategy**:
1. Keep Implementation Phase 1 code working while adding Implementation Phase 2
2. Create User Profile MCP Server in separate directory
3. Test database operations standalone
4. Modify Profile Agent to use MCP client instead of Dictionary
5. Run both processes (FinWise + User Profile MCP Server)

**Testing approach**:
1. Start PostgreSQL: `docker-compose up -d`
2. Start User Profile MCP Server (separate terminal)
3. Start FinWise.Orchestrator (separate terminal)
4. First conversation: Profile saved to database
5. Close/restart: Profile retrieved (NOT asked again)

**Success indicator**: Profile survives process restarts

### Implementation Phase 3+ Tips

**Prerequisite**: Implementation Phase 2 fully working

**Approach**: Each phase extends existing code
- Implementation Phase 3: Add routing keywords to orchestrator
- Implementation Phase 4: Add escalation detection to advisor
- Implementation Phase 5: Add new agent to workflow
- Implementation Phase 6: Documentation and cleanup

**Testing**: Regression test all previous phases when adding new features

### Time Management

**With GitHub Copilot** (single developer, first-time with framework):
- Implementation Phase 1: 1 day (Copilot accelerates agent creation, workflow setup)
- Implementation Phase 2: 1 day (Copilot handles EF Core boilerplate)
- Implementation Phase 3: 1 day (Copilot generates routing logic, tests)
- Implementation Phase 4: 1 day (Copilot assists with documentation)

**Total**: 4 days for complete feature-rich POC

**Why so fast**: GitHub Copilot dramatically reduces boilerplate code, generates test scaffolding, and suggests idiomatic patterns for Microsoft Agent Framework

### Commit Strategy

**After each task**:
```bash
git add .
git commit -m "T001: Create solution structure"
git push
```

**Benefits**:
- Easy rollback if task goes wrong
- Track progress clearly
- Can share incremental progress

### Common Pitfalls to Avoid

1. **Don't skip Implementation Phase 1**: Tempting to start with database, but in-memory version validates workflow first
2. **Don't optimize prematurely**: Get it working, then make it fast
3. **Don't test manually only**: Write integration tests for each phase
4. **Don't ignore warnings**: Zero StyleCop warnings from the start (easier than fixing later)
5. **Don't hardcode config**: Use environment variables for Azure OpenAI from day 1

---

## File Structure After Each Phase

### After Implementation Phase 1 (In-Memory)

```
FinWise-orchestrator-mcp/
├── src/
│   └── FinWise.Orchestrator/
│       ├── Program.cs                 # Agents, workflow, MCP server, in-memory Dictionary
│       ├── Models.cs                  # UserProfileDto, WorkflowExecutionContext
│       ├── appsettings.json           # Azure OpenAI config (env var references)
│       └── FinWise.Orchestrator.csproj
│
├── tests/
│   └── FinWise.Orchestrator.Tests/
│       ├── WorkflowTests.cs           # End-to-end test (in-memory)
│       └── FinWise.Orchestrator.Tests.csproj
│
├── .editorconfig
└── FinWise.sln
```

**Files**: ~6 files
**Lines of Code**: ~800 lines (including tests)

### After Implementation Phase 2 (Persistence Added)

```
FinWise-orchestrator-mcp/
├── src/
│   ├── FinWise.Orchestrator/
│   │   ├── Program.cs                 # Modified: Profile Agent uses MCP client
│   │   ├── Models.cs                  # Same as Implementation Phase 1
│   │   ├── appsettings.json           # Same as Implementation Phase 1
│   │   └── FinWise.Orchestrator.csproj
│   │
│   └── FinWise.UserProfile.McpServer/
│       ├── Program.cs                 # NEW: DbContext, MCP server, tools
│       ├── UserProfile.cs             # NEW: EF Core entity
│       ├── appsettings.json           # NEW: PostgreSQL connection
│       ├── Migrations/                # NEW: EF Core migrations
│       └── FinWise.UserProfile.McpServer.csproj
│
├── tests/
│   ├── FinWise.Orchestrator.Tests/
│   │   ├── WorkflowTests.cs           # Modified: Uses Testcontainers
│   │   └── FinWise.Orchestrator.Tests.csproj
│   │
│   └── FinWise.UserProfile.McpServer.Tests/
│       ├── DatabaseTests.cs           # NEW: Database integration tests
│       └── FinWise.UserProfile.McpServer.Tests.csproj
│
├── docker/
│   └── postgresql/
│       └── docker-compose.yml         # NEW: PostgreSQL container
│
├── .editorconfig
└── FinWise.sln
```

**Files**: ~15 files
**Lines of Code**: ~1,500 lines (including tests, migrations)

### After Implementation Phase 4 (Complete)

```
FinWise-orchestrator-mcp/
├── src/                              # Same as Implementation Phase 2, with routing/escalation logic added
├── tests/                            # Extended with RoutingTests, EscalationTests, ExtensibilityTests
├── docker/                           # Same as Implementation Phase 2
├── docs/
│   ├── user-guide/
│   │   ├── mcp-setup-v01.md          # NEW: Setup guide
│   │   └── add-new-agent.md          # NEW: Agent registration guide
│   │
│   └── architecture/
│       └── v01-local-mcp-stdio.md    # NEW: Architecture diagrams
│
├── .editorconfig
├── README.md                         # UPDATED: Complete project overview
└── FinWise.sln
```

**Files**: ~22 files
**Lines of Code**: ~2,500 lines (including tests, docs, comments)

---

## Summary

This task breakdown provides a **4-day implementation plan** for a complete, production-ready POC:

**Total Tasks**: 47 tasks across 4 implementation phases (18 Implementation Phase 1, 16 Implementation Phase 2, 7 Implementation Phase 3, 6 Implementation Phase 4)

**Implementation Phase 1** (Day 1, 18 tasks): In-memory three-agent workflow
**Implementation Phase 2** (Day 2, 16 tasks): PostgreSQL persistence
**Implementation Phase 3** (Day 3, 7 tasks): Routing + Escalation + Risk Agent
**Implementation Phase 4** (Day 4, 7 tasks): Documentation + Polish

**Key Success Factor**: GitHub Copilot significantly accelerates development by:
- Generating agent system prompts
- Creating EF Core entities and DbContext
- Writing test scaffolding
- Suggesting workflow patterns
- Generating documentation

**Deliverable**: Feature-complete POC with 4 agents, persistent profiles, dynamic routing, graceful escalation, and comprehensive documentation.

---

## Ready to Start?

### Prerequisites Checklist

- [ ] .NET 10 SDK installed (`dotnet --version` shows 10.x)
- [ ] GitHub Copilot enabled in VS Code
- [ ] VS Code with C# Dev Kit extension
- [ ] Docker Desktop installed and running (for Implementation Phase 2+)
- [ ] Azure OpenAI Service access (endpoint, API key, deployment name)
- [ ] Claude Desktop or compatible MCP client
- [ ] Git for version control

### Environment Variables

Create `.env` file (gitignored) or set in system:

```bash
AZURE_OPENAI_ENDPOINT=https://your-resource.openai.azure.com/
AZURE_OPENAI_API_KEY=your-api-key-here
AZURE_OPENAI_DEPLOYMENT_NAME=gpt-4
```

### First Command

```bash
# Create solution directory
mkdir -p src/FinWise.Orchestrator
cd FinWise-orchestrator-mcp

# Start with T001
# Task: Create solution structure
```

### Recommended Reading Before Starting

1. [Microsoft Agent Framework Docs](https://learn.microsoft.com/en-us/agent-framework/)
2. [MCP Protocol Specification](https://modelcontextprotocol.io/)
3. [plan.md](plan.md) - Review simplified architecture
4. [data-model.md](data-model.md) - Understand framework-managed vs custom types

---

## Summary

This task breakdown reorganizes implementation into **4 incremental implementation phases**:

1. **Implementation Phase 1** (1 day): In-memory core workflow - Simplest possible MVP
2. **Implementation Phase 2** (1 day): Add PostgreSQL persistence - Professional POC
3. **Implementation Phase 3** (1 day): Dynamic routing + Escalation + Risk Agent - Natural conversations with graceful limitations
4. **Implementation Phase 4** (1 day): Polish - Documentation & production readiness

**Key Innovation**: Implementation Phase 1 delivers working multi-agent workflow WITHOUT database complexity, validating core orchestration first.

**Minimum for Stakeholder Demo**: Implementation Phase 1 + Implementation Phase 2 (2 days)

**Full Feature-Complete POC**: Implementation Phases 1-3 (3 days)

**Production-Ready**: All implementation phases (4 days)

Good luck! 🚀

Start with **T001** and work sequentially through Implementation Phase 1 first.
