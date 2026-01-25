# Tasks: Core Multi-Agent Workflow

**Input**: Design documents from `specs/001-core-workflow/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/  
**Organization**: Tasks organized by PHASE and USER STORY for independent implementation. Aligned with FinWise Constitution v2.0.0.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Exact file paths included in descriptions

## Path Conventions

Based on plan.md structure:
- **Main orchestrator**: `src/FinWise.Orchestrator/`
- **User Profile MCP Server**: `src/FinWise.UserProfile.McpServer/`
- **Tests**: `tests/FinWise.Orchestrator.Tests/`, `tests/FinWise.UserProfile.McpServer.Tests/`
- **Docker**: `docker/postgresql/`
- **Docs**: `docs/user-guide/`, `docs/architecture/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization, quality tooling, and basic structure

- [ ] T001 Create solution structure with two projects: FinWise.Orchestrator and FinWise.UserProfile.McpServer per plan.md
- [ ] T002 Initialize FinWise.Orchestrator.csproj with .NET 10 and dependencies (Microsoft.ModelContextProtocol, Azure.AI.OpenAI, Microsoft.Extensions.Hosting)
- [ ] T003 [P] Initialize FinWise.UserProfile.McpServer.csproj with .NET 10 and dependencies (Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.ModelContextProtocol)
- [ ] T004 [P] Configure .editorconfig with StyleCop analyzers for C# (enforce zero warnings - Constitution I)
- [ ] T005 [P] Setup code quality scanning with .NET analyzers (Code Quality, Code Style, .NET Compiler Platform)
- [ ] T006 [P] Create docker/postgresql/docker-compose.yml for PostgreSQL 18 container (finwise_db database)
- [ ] T007 [P] Create test projects: FinWise.Orchestrator.Tests.csproj and FinWise.UserProfile.McpServer.Tests.csproj with xUnit, FluentAssertions, Moq, Testcontainers
- [ ] T008 Configure Serilog structured logging in both projects (JSON output, console sink, request IDs)
- [ ] T009 [P] Create docs/user-guide/mcp-setup-v01.md skeleton for GitHub Copilot configuration
- [ ] T010 [P] Create docs/architecture/v01-local-mcp-stdio.md skeleton for architecture documentation

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T011 Create UserProfile entity class in src/FinWise.UserProfile.McpServer/UserProfile.cs per data-model.md (Guid Id, string UserIdentifier, enums RiskTolerance/InvestmentTimeframe, string InvestmentGoals, JsonDocument QuestionnaireResponses, DateTimeOffset timestamps, int Version)
- [ ] T012 Create UserProfileDbContext in src/FinWise.UserProfile.McpServer/UserProfileDbContext.cs with EF Core configuration (PostgreSQL provider, user_profiles table mapping, indexes on user_identifier)
- [ ] T013 Create initial EF Core migration for user_profiles table with PostgreSQL schema per data-model.md (CREATE TABLE with CHECK constraints, indexes, triggers for updated_at)
- [ ] T014 Create WorkflowExecutionContext record in src/FinWise.Orchestrator/Models.cs (string UserIdentifier, string Query, DateTimeOffset RequestTime)
- [ ] T015 Create UserProfileDto record in src/FinWise.Orchestrator/Models.cs for User Profile MCP Server responses
- [ ] T016 Setup environment variable configuration in both projects (AZURE_OPENAI_ENDPOINT, AZURE_OPENAI_API_KEY, AZURE_OPENAI_DEPLOYMENT_NAME, PostgreSQL connection string)
- [ ] T017 Create MCP STDIO client helper in src/FinWise.Orchestrator/McpClientHelper.cs for connecting to User Profile MCP Server process
- [ ] T018 Implement error handling base classes in src/FinWise.Orchestrator/ErrorHandling.cs (WorkflowException, AgentException, McpToolException)
- [ ] T019 [P] Setup logging correlation IDs and structured logging format per Constitution III (request_id, agent_id, workflow_step, execution_time)
- [ ] T020 Apply initial migration to PostgreSQL container and verify schema created correctly

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Core Multi-Agent Flow with Context Preservation (Priority: P1) 🎯 MVP

**Goal**: Implement complete multi-agent workflow with orchestrator routing queries to profile and advisor agents, preserving context across handoffs

**Independent Test**: Send query "I want investment advice", verify orchestrator routes to profile agent (if no profile), then advisor agent with context, final response from advisor includes profile information

### Implementation for User Story 1

- [ ] T021 [P] [US1] Create ChatClientAgent for Orchestrator in src/FinWise.Orchestrator/Program.cs with system prompt per research.md decision 1 ("determine which agent to use, route to profile or advisor")
- [ ] T022 [P] [US1] Create ChatClientAgent for User Profile Agent in src/FinWise.Orchestrator/Program.cs with system prompt ("collect risk tolerance, goals, timeframe using MCP tools")
- [ ] T023 [P] [US1] Create ChatClientAgent for Global Advisor Agent in src/FinWise.Orchestrator/Program.cs with system prompt ("provide generic stock vs real estate advice based on profile context")
- [ ] T024 [US1] Build AgentWorkflowBuilder with handoff configuration in src/FinWise.Orchestrator/Program.cs (orchestrator → [profile, advisor], [profile, advisor] → orchestrator per research.md decision 1)
- [ ] T025 [US1] Implement get_investment_recommendations MCP tool wrapper in src/FinWise.Orchestrator/Program.cs per contracts/mcp-tools.json (executes workflow with InProcessExecution.StreamAsync, watches WorkflowEvent stream)
- [ ] T026 [US1] Implement WorkflowEvent stream monitoring in src/FinWise.Orchestrator/Program.cs (ExecutorInvokedEvent, WorkflowOutputEvent, WorkflowErrorEvent logging per data-model.md framework types)
- [ ] T027 [US1] Implement get_user_profile MCP tool in src/FinWise.UserProfile.McpServer/Program.cs (query PostgreSQL via EF Core, return UserProfileDto per contracts/user-profile-mcp.json)
- [ ] T028 [US1] Implement save_user_profile MCP tool in src/FinWise.UserProfile.McpServer/Program.cs (insert new UserProfile entity with validation, handle concurrency via version field)
- [ ] T029 [US1] Wire User Profile Agent to call User Profile MCP Server via STDIO client for profile retrieval during workflow
- [ ] T030 [US1] Implement profile context passing during orchestrator → advisor handoff (orchestrator retrieves profile via MCP, framework passes as ChatMessage to advisor)
- [ ] T031 [US1] Add conversation history accumulation validation (verify framework's ChatMessage list grows with each agent turn)
- [ ] T032 [US1] Add logging for all agent transitions, handoffs, and routing decisions (log ExecutorInvokedEvent agent IDs, handoff reasons, final WorkflowOutputEvent)
- [ ] T033 [US1] Register FinWise MCP Server with STDIO transport in src/FinWise.Orchestrator/Program.cs (StdioMcpServer with get_investment_recommendations tool)
- [ ] T034 [US1] Register User Profile MCP Server with STDIO transport in src/FinWise.UserProfile.McpServer/Program.cs (get/save user profile tools)
- [ ] T035 [US1] Add validation and error handling for workflow timeouts (10s target per plan.md quality gates, return WorkflowException if exceeded)
- [ ] T036 [US1] Add validation for profile not found scenario (orchestrator routes to profile agent when get_user_profile returns null)

**Checkpoint**: At this point, User Story 1 should be fully functional - test end-to-end workflow with profile creation and investment advice

---

## Phase 4: User Story 2 - Dynamic Routing and Navigation (Priority: P1)

**Goal**: Support flexible navigation patterns - direct routing (profile-only, advisor-only), sequential routing (profile → advisor), and back-navigation (advisor → profile update → advisor)

**Independent Test**: Exercise 3 patterns: (1) "What is my risk tolerance?" routes to profile only, (2) "Should I invest in stocks?" routes to advisor only, (3) "I want to update my profile" after advice → routes back to profile → then advisor with new context

### Implementation for User Story 2

- [ ] T037 [P] [US2] Implement query analysis logic in Orchestrator agent (keyword matching: "profile"/"risk tolerance" → profile agent, "invest"/"stocks"/"real estate" → advisor agent, per research.md decision 4)
- [ ] T038 [US2] Implement LLM fallback routing for ambiguous queries in Orchestrator (when keyword matching fails, use LLM to classify query per research.md decision 4)
- [ ] T039 [US2] Add routing decision logging with reasoning (log keyword matches, LLM classification results, agent selection per FR-012)
- [ ] T040 [US2] Implement update_user_profile MCP tool in src/FinWise.UserProfile.McpServer/Program.cs per contracts/user-profile-mcp.json (update existing UserProfile with optimistic concurrency check)
- [ ] T041 [US2] Implement update_user_profile MCP tool wrapper in src/FinWise.Orchestrator/Program.cs (routes to User Profile Agent workflow)
- [ ] T042 [US2] Add support for back-navigation pattern (user asks to update profile after receiving advice → orchestrator routes to profile agent → profile agent calls update_user_profile → orchestrator re-routes to advisor with updated profile)
- [ ] T043 [US2] Add validation for profile update with version conflict (catch EF Core DbUpdateConcurrencyException, return CONCURRENCY_ERROR per contracts/user-profile-mcp.json)
- [ ] T044 [US2] Add navigation history tracking in WorkflowEvent stream monitoring (track sequence of ExecutorInvokedEvent for transparency)
- [ ] T045 [US2] Add logging for all routing decisions including reasons (keyword match, LLM classification, direct request) per FR-012

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently - test all 3 navigation patterns

---

## Phase 5: User Story 3 - Agent Escalation and Fallback (Priority: P2)

**Goal**: Enable agents to escalate queries outside their capability (e.g., specific stock analysis) to orchestrator, with graceful fallback when no agent can handle request

**Independent Test**: Ask Global Advisor "Should I buy MSFT stock?" and verify it escalates to orchestrator, which returns helpful message explaining v0.2 will have detailed stock analysis

### Implementation for User Story 3

- [ ] T046 [P] [US3] Update Global Advisor agent system prompt to recognize out-of-scope queries requiring escalation (specific stock tickers, real estate properties, market timing)
- [ ] T047 [US3] Implement escalation signaling mechanism in Global Advisor agent (agent returns specific message pattern indicating escalation needed, e.g., "ESCALATE: requires detailed stock analysis")
- [ ] T048 [US3] Implement escalation detection in WorkflowEvent stream monitoring (detect escalation signal in WorkflowOutputEvent, log escalation reason)
- [ ] T049 [US3] Implement fallback response generation in orchestrator (when escalation detected and no suitable agent exists, return user-friendly message per research.md decision 7 tier 4)
- [ ] T050 [US3] Add escalation context preservation (when agent escalates, orchestrator receives full context about query and why escalation occurred per FR-016)
- [ ] T051 [US3] Add logging for all escalation events (agent ID, escalation reason, fallback action taken per FR-012)
- [ ] T052 [US3] Add error handling for agent failures with retry logic (Azure OpenAI transient errors, exponential backoff per research.md decision 7 tier 1)
- [ ] T053 [US3] Add error handling for database failures with graceful degradation (User Profile MCP Server unavailable → use empty profile + warn user per research.md decision 7 tier 4)
- [ ] T054 [US3] Add validation for AGENT_FAILURE error response per contracts/mcp-tools.json (LLM timeout or service error)

**Checkpoint**: All escalation and fallback scenarios should work - test specific stock query triggers proper fallback message

---

## Phase 6: User Story 4 - Extensible Agent Registration (Priority: P2)

**Goal**: Demonstrate workflow infrastructure allows new agents to be added without modifying orchestrator core logic (add hollow risk assessment agent as proof)

**Independent Test**: Add third hollow agent (Risk Assessment Agent), verify orchestrator can route to it without changes to core orchestration code, only configuration changes

### Implementation for User Story 4

- [ ] T055 [P] [US4] Create ChatClientAgent for Risk Assessment Agent in src/FinWise.Orchestrator/Program.cs with system prompt ("analyze user risk tolerance and provide personalized risk assessment")
- [ ] T056 [US4] Update AgentWorkflowBuilder handoff configuration to include risk agent (orchestrator → [profile, advisor, risk], [profile, advisor, risk] → orchestrator)
- [ ] T057 [US4] Update Orchestrator query analysis to recognize risk-related queries (keywords: "risk assessment", "risk analysis", "how risky")
- [ ] T058 [US4] Update routing decision logging to include risk agent routing
- [ ] T059 [US4] Add documentation comment in Program.cs showing how to add new agents (ChatClientAgent creation + handoff config + routing keywords = complete integration)
- [ ] T060 [US4] Validate no changes needed to core workflow execution logic (InProcessExecution.StreamAsync, WorkflowEvent monitoring unchanged)

**Checkpoint**: Risk Assessment Agent should be fully functional - demonstrates extensibility without core changes

---

## Phase 7: Edge Cases and Circular Prevention (Priority: P2)

**Goal**: Handle all edge cases from spec.md including circular escalation detection, long conversation histories, conflicting information, concurrent requests

**Independent Test**: Attempt circular escalation pattern, verify framework prevents it at build time; test 10+ turn conversation maintains context

### Implementation for User Story Edge Cases

- [ ] T061 [P] Add circular handoff prevention validation (verify AgentWorkflowBuilder.Build() rejects invalid circular configurations per FR-020, research.md decision 6)
- [ ] T062 [P] Add conversation history length monitoring (track ChatMessage accumulation, log warning if >10 turns in single workflow per edge case requirement)
- [ ] T063 [P] Implement conflicting information handling in orchestrator (when multiple agents provide different answers, orchestrator presents both with reasoning per FR-013)
- [ ] T064 [P] Add concurrent request handling (reject or queue concurrent MCP tool invocations from same user_identifier to maintain conversation coherence per edge case requirement)
- [ ] T065 Add ambiguous query handling (when keyword matching and LLM both uncertain, orchestrator asks user for clarification per edge case requirement)
- [ ] T066 Add "no suitable agent" fallback (when query matches no agent capabilities, orchestrator provides helpful error message per edge case requirement)
- [ ] T067 Add rapid agent switching validation (test profile → advisor → profile → advisor maintains coherent context per edge case requirement)
- [ ] T068 Add agent timeout handling (if agent exceeds 5s response time, trigger graceful failure per edge case requirement)

**Checkpoint**: All edge cases handled gracefully without crashes

---

## Phase 8: Testing and Validation

**Purpose**: Comprehensive testing to validate all success criteria from spec.md

- [ ] T069 [P] Create integration test for SC-001 in tests/FinWise.Orchestrator.Tests/WorkflowTests.cs (user initiates conversation, receives response from at least 2 agents)
- [ ] T070 [P] Create integration test for SC-002 in tests/FinWise.Orchestrator.Tests/WorkflowTests.cs (orchestrator routes all test queries to correct agents)
- [ ] T071 [P] Create integration test for SC-003 in tests/FinWise.Orchestrator.Tests/WorkflowTests.cs (profile context passed to advisor agent successfully)
- [ ] T072 [P] Create performance test for SC-004 in tests/FinWise.Orchestrator.Tests/PerformanceTests.cs (full workflow completes in <10s)
- [ ] T073 [P] Create integration test for SC-006 in tests/FinWise.Orchestrator.Tests/WorkflowTests.cs (3 failure scenarios handled gracefully: timeout, error, unavailable)
- [ ] T074 [P] Create integration test for SC-007 in tests/FinWise.Orchestrator.Tests/ExtensibilityTests.cs (add third agent, integrate in <30min equivalent)
- [ ] T075 [P] Create integration test for SC-008 in tests/FinWise.Orchestrator.Tests/WorkflowTests.cs (10+ turn conversation maintains context)
- [ ] T076 [P] Create integration test for SC-011 in tests/FinWise.Orchestrator.Tests/WorkflowTests.cs (user updates profile after advice, receives revised recommendations)
- [ ] T077 [P] Create integration test for SC-012 in tests/FinWise.Orchestrator.Tests/WorkflowTests.cs (3 navigation patterns: sequential, back-navigation, non-sequential)
- [ ] T078 [P] Create integration test for SC-013 in tests/FinWise.Orchestrator.Tests/WorkflowTests.cs (escalated agent receives complete context)
- [ ] T079 [P] Create integration test for SC-014 in tests/FinWise.Orchestrator.Tests/WorkflowTests.cs (2 circular escalation patterns prevented)
- [ ] T080 [P] Create integration test for SC-015 in tests/FinWise.Orchestrator.Tests/WorkflowTests.cs (5+ agent switches maintain coherent context)
- [ ] T081 [P] Create database integration tests in tests/FinWise.UserProfile.McpServer.Tests/DatabaseTests.cs using Testcontainers (CRUD operations, concurrency, validation)
- [ ] T082 Run all tests and verify 100% pass rate for success criteria SC-001 through SC-015
- [ ] T083 Run performance tests and verify <500ms MCP tool operations, <10s workflow latency per plan.md quality gates
- [ ] T084 Run linting and code quality checks with zero warnings per Constitution I

---

## Phase 9: Documentation and Polish

**Purpose**: Complete documentation, finalize user guides, code cleanup

- [ ] T085 [P] Complete docs/user-guide/mcp-setup-v01.md with GitHub Copilot configuration instructions per quickstart.md
- [ ] T086 [P] Complete docs/architecture/v01-local-mcp-stdio.md with architecture diagrams and component overview
- [ ] T087 [P] Add XML documentation comments to all public methods in both projects per Constitution I
- [ ] T088 [P] Create README.md in repository root with quick start, architecture overview, and links to detailed docs
- [ ] T089 Code cleanup: Extract repeated workflow event handling logic to local functions in Program.cs
- [ ] T090 Code cleanup: Extract MCP client connection logic to shared McpClientHelper.cs
- [ ] T091 Add troubleshooting section to docs/user-guide/mcp-setup-v01.md (common errors: PostgreSQL not running, Azure OpenAI key invalid, MCP server not found)
- [ ] T092 Validate quickstart.md instructions by following step-by-step on clean Windows environment
- [ ] T093 [P] Add performance monitoring dashboard configuration (Serilog + Seq or Application Insights for structured log analysis)
- [ ] T094 Final validation: Run full workflow end-to-end and verify all logging, error handling, and success criteria

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Story 1 (Phase 3)**: Depends on Foundational completion - Core workflow MUST work first
- **User Story 2 (Phase 4)**: Depends on User Story 1 completion - Navigation builds on core workflow
- **User Story 3 (Phase 5)**: Depends on User Story 1 completion - Escalation builds on core workflow (can run in parallel with US2 if staffed)
- **User Story 4 (Phase 6)**: Depends on User Story 1 completion - Extensibility builds on core workflow (can run in parallel with US2/US3 if staffed)
- **Edge Cases (Phase 7)**: Depends on User Stories 1-4 completion - Tests edge behavior across all stories
- **Testing (Phase 8)**: Depends on all User Stories completion - Validates all functionality
- **Polish (Phase 9)**: Depends on Testing completion - Final cleanup and docs

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories - **CRITICAL MVP**
- **User Story 2 (P1)**: Depends on User Story 1 - Navigation extends core workflow routing
- **User Story 3 (P2)**: Depends on User Story 1 - Escalation uses core workflow patterns (can run parallel to US2 if staffed)
- **User Story 4 (P2)**: Depends on User Story 1 - Extensibility proves core workflow design (can run parallel to US2/US3 if staffed)

### Within Each User Story

**User Story 1** (Core Workflow):
1. Create all three ChatClientAgent instances in parallel (T021, T022, T023)
2. Build AgentWorkflowBuilder with handoffs (T024 - depends on agents)
3. Implement MCP tool wrappers and User Profile MCP Server in parallel (T025-T028)
4. Wire agents to MCP clients and implement handoffs (T029-T032)
5. Register MCP servers (T033-T034)
6. Add validation and error handling (T035-T036)

**User Story 2** (Navigation):
1. Implement routing logic in parallel (T037, T038)
2. Add update_user_profile in parallel (T040, T041)
3. Implement back-navigation and validation (T042-T045)

**User Story 3** (Escalation):
1. Update agent prompts and implement escalation signaling in parallel (T046, T047)
2. Implement detection and fallback (T048-T051)
3. Add error handling and validation (T052-T054)

**User Story 4** (Extensibility):
1. Create risk agent and update configuration in parallel (T055, T056)
2. Update routing and documentation (T057-T060)

### Parallel Opportunities

**Phase 1 (Setup)**: All tasks T003-T010 marked [P] can run in parallel after T001-T002

**Phase 2 (Foundational)**: T019 can run in parallel with T011-T018

**Phase 3 (User Story 1)**: 
- T021, T022, T023 (create agents) - parallel
- T025, T026, T027, T028 (MCP implementations) - parallel after T024
- T033, T034 (register servers) - parallel

**Phase 4 (User Story 2)**: T037, T038, T040, T041 can run in parallel

**Phase 5 (User Story 3)**: T046, T047, T052, T053 can run in parallel

**Phase 6 (User Story 4)**: T055, T056 can run in parallel

**Phase 7 (Edge Cases)**: All tasks T061-T064 can run in parallel

**Phase 8 (Testing)**: All integration tests T069-T081 can run in parallel

**Phase 9 (Polish)**: All documentation tasks T085-T088, T091, T093 can run in parallel

---

## Parallel Example: User Story 1 Core Implementation

```powershell
# Launch all agent creation tasks in parallel:
# Terminal 1: Create Orchestrator agent (T021) in Program.cs
# Terminal 2: Create User Profile agent (T022) in Program.cs  
# Terminal 3: Create Global Advisor agent (T023) in Program.cs

# Then launch MCP implementations in parallel:
# Terminal 1: Implement get_investment_recommendations wrapper (T025)
# Terminal 2: Implement get_user_profile MCP tool (T027)
# Terminal 3: Implement save_user_profile MCP tool (T028)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only - Fastest Path to Value)

1. Complete Phase 1: Setup (T001-T010)
2. Complete Phase 2: Foundational (T011-T020) - **CRITICAL BLOCKING PHASE**
3. Complete Phase 3: User Story 1 (T021-T036)
4. **STOP and VALIDATE**: Test end-to-end workflow independently
   - User asks for investment advice
   - Orchestrator routes to profile agent (if no profile exists)
   - Profile agent gathers info via conversation
   - Profile agent saves to PostgreSQL via User Profile MCP Server
   - Orchestrator hands off to advisor agent with profile context
   - Advisor provides generic stock vs real estate advice
   - Verify conversation history maintained throughout
5. Deploy/demo if ready - **THIS IS YOUR MINIMUM VIABLE PRODUCT**

**Tasks for MVP**: T001-T036 (36 tasks total)

### Incremental Delivery (Recommended for v0.1)

1. Complete Setup + Foundational (T001-T020) → Foundation ready
2. Add User Story 1 (T021-T036) → Test independently → **Deploy MVP Demo!**
3. Add User Story 2 (T037-T045) → Test navigation patterns → **Deploy Enhanced Demo**
4. Add User Story 3 (T046-T054) → Test escalation → **Deploy with Escalation**
5. Add User Story 4 (T055-T060) → Prove extensibility → **Deploy Complete v0.1 Feature Set**
6. Add Edge Cases (T061-T068) → Harden production readiness
7. Complete Testing (T069-T084) → Validate all success criteria
8. Polish and Documentation (T085-T094) → **Deploy Production-Ready v0.1**

Each increment adds value without breaking previous functionality.

### Parallel Team Strategy (If 3+ Developers Available)

**After Foundational Phase Complete**:

**Week 1**:
- Developer A: User Story 1 (T021-T036) - **HIGHEST PRIORITY**
- Developer B: Setup documentation (T085-T086)
- Developer C: Database integration tests (T081)

**Week 2** (after US1 complete):
- Developer A: User Story 2 (T037-T045)
- Developer B: User Story 3 (T046-T054)
- Developer C: User Story 4 (T055-T060)

**Week 3**:
- Developer A: Edge Cases (T061-T068)
- Developer B: Success Criteria Tests (T069-T080)
- Developer C: Polish and final docs (T087-T094)

---

## Success Criteria Mapping

Each task maps to spec.md success criteria:

- **SC-001** (2 agents coordinate): T021-T036 (User Story 1)
- **SC-002** (routing correct): T037-T039 (User Story 2 routing)
- **SC-003** (context preserved): T030-T031 (US1 handoff)
- **SC-004** (workflow <10s): T035, T072 (performance validation)
- **SC-005** (logging complete): T032, T039, T045, T051 (all logging tasks)
- **SC-006** (3 failure types): T052-T053, T073 (error handling)
- **SC-007** (add agent <30min): T055-T060, T074 (extensibility)
- **SC-008** (10+ turns): T031, T075 (conversation history)
- **SC-009** (MCP client access): T033-T034, T092 (MCP server registration)
- **SC-010** (conflicts presented): T063 (conflicting info handling)
- **SC-011** (back-navigation): T042, T076 (update profile flow)
- **SC-012** (3 navigation patterns): T037-T045, T077 (routing)
- **SC-013** (escalation context): T050, T078 (escalation context)
- **SC-014** (2 circular patterns): T061, T079 (circular prevention)
- **SC-015** (5+ switches): T067, T080 (rapid switching)

**Validation**: Run T082 to verify all 15 success criteria tests pass.

---

## Notes

- **[P] tasks**: Different files or independent components, no blocking dependencies
- **[Story] label**: Maps task to specific user story for traceability and independent delivery
- **Each user story independently completable**: Can test and deploy US1 without US2/US3/US4
- **Verify tests fail before implementation**: Especially T069-T080 (write assertions first)
- **Commit after logical groups**: Each completed task or small batch of [P] tasks
- **Stop at checkpoints**: Validate story works independently before proceeding
- **MVP = User Story 1**: Everything else is enhancement - ship early if needed
- **Constitution alignment**: All tasks enforce Code Quality (I), UX Consistency (II), Performance (III)

---

## Total Task Count

- **Setup**: 10 tasks (T001-T010)
- **Foundational**: 10 tasks (T011-T020) - **BLOCKING**
- **User Story 1**: 16 tasks (T021-T036) - **MVP CRITICAL**
- **User Story 2**: 9 tasks (T037-T045)
- **User Story 3**: 9 tasks (T046-T054)
- **User Story 4**: 6 tasks (T055-T060)
- **Edge Cases**: 8 tasks (T061-T068)
- **Testing**: 16 tasks (T069-T084)
- **Polish**: 10 tasks (T085-T094)

**Total**: 94 tasks  
**MVP (minimal)**: 36 tasks (Setup + Foundational + US1)  
**Feature Complete**: 60 tasks (+ US2, US3, US4, Edge Cases)  
**Production Ready**: 94 tasks (+ Testing, Polish)
