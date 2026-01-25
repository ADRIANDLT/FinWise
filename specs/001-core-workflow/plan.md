# Implementation Plan: Core Multi-Agent Workflow

**Branch**: `001-core-workflow` | **Date**: December 30, 2025 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-core-workflow/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/commands/plan.md` for the execution workflow.

## Summary

Implements the baseline multi-agent workflow infrastructure for FinWise, exposing investment advisory capabilities through an MCP server accessible via AI assistants (Claude Desktop, ChatGPT, GitHub Copilot). The system orchestrates three ChatClientAgent instances (Orchestrator, User Profile, Global Advisor) using AgentWorkflowBuilder with handoff relationships, streaming execution via InProcessExecution.

Implementation follows a **two-phase incremental approach**: Feature Implementation Phase 1 establishes core workflow with in-memory profiles usage, Feature Implementation Phase 2 adds user profile persistence with PostgreSQL.

> **Terminology Note**: "Feature Implementation Phase 1" and "Feature Implementation Phase 2" refer to internal implementation phases within this core-workflow feature (v0.1 baseline). These are NOT the same as product versions (v0.1, v0.2, v0.3, etc.) defined in [03-architecture-and-technologies.md](../../03-architecture-and-technologies.md) and [01-idea-vision-scope.md](../../01-idea-vision-scope.md). Both Feature Implementation Phases are part of the v0.1 product release.

---

## Feature Implementation Phase 1: In-Memory Single Process (Baseline POC)

**Goal**: Validate multi-agent orchestration, handoffs, and context preservation WITHOUT database complexity.

### Technical Context

**Language/Version**: C# / .NET 10

**Dependencies**: 
  - Microsoft.Extensions.AI (Agent Framework abstractions)
  - Microsoft.AI.Agents (ChatClientAgent, AgentWorkflowBuilder, InProcessExecution)
  - Microsoft.ModelContextProtocol (MCP server STDIO transport)
  - Serilog.AspNetCore (structured logging)
  - Azure.AI.OpenAI (LLM inference)

**Storage**: In-memory only (`Dictionary<string, UserProfileDto>`) - no persistence across process restarts

**Testing**: xUnit with FluentAssertions, Moq for mocking (in-memory workflow tests only)

**Target Platform**: Windows 11 development workstation, single .NET 10 process (FinWise.Orchestrator only)

**Project Type**: Single-process console application with in-memory storage

**Architecture**:
- Three agents (Orchestrator, User Profile, Global Advisor) in single process
- In-memory profile storage (Dictionary) - profile asked every conversation
- MCP STDIO server for external communication (AI assistant → FinWise)
- NO User Profile MCP Server, NO PostgreSQL, NO multi-process architecture

**Constraints**: 
  - Must use MCP STDIO transport
  - All agents must be "hollow" (simple, LLM-driven, minimal specialized context)
  - Profile storage: Dictionary only - lost on process restart
  - Conversation history: In-memory only (no persistence)
  - Single Program.cs with inline logic

**Scale/Scope**: 
  - Single user (developer/tester/demo)
  - 3 agents total (Orchestrator, User Profile, Global Advisor)
  - Single process, in-memory profiles

**Deliverable**: Working multi-agent workflow accessible via Claude Desktop with profile collection every conversation.

---

## Feature Implementation Phase 2: Persistent Multi-Process (Persistence Added)

**Goal**: Add persistent profile storage so users don't re-enter profile after process restarts.

### Technical Context (Additions to Feature Implementation Phase 1)

**Additional Dependencies**: 
  - MCP SDK for additional custom user profile MCP server
  - Entity Framework Core 10 (database ORM)
  - Npgsql.EntityFrameworkCore.PostgreSQL (PostgreSQL provider)

**Storage**: PostgreSQL 18 (Docker Linux container) - user profiles persistent (risk tolerance, goals, timeframes, questionnaire data)

**Testing**: Add Testcontainers for PostgreSQL integration tests

**Target Platform**: Windows 11 + PostgreSQL Docker container, multi-process (.NET 10 + PostgreSQL)

**Project Type**: Multi-process console application (FinWise.Orchestrator + User Profile MCP Server, both .NET)

**Architecture**:
- Dictionary<string, UserProfileDto> → PostgreSQL database
- Single process → Multi-process (FinWise.Orchestrator + FinWise.UserProfile.McpServer)
- User Profile Agent: Dictionary updates → MCP client calls to User Profile MCP Server
- MCP STDIO for internal communication (FinWise → User Profile MCP Server)

**Constraints**: 
  - Conversation history: Still in-memory only (persistent session/conversation storage deferred to v0.3+)
  - Keep code structure simple (single Program.cs per process, inline logic)

**Scale/Scope**: 
  - Same: 3 agents, single user
  - Multi-process, persistent profiles (survive restarts)

**Deliverable**: Profile persistence across conversations with two-process architecture.

---

## Common Technical Context (Both Phases)

**Language/Version**: C# / .NET 10

**Core Dependencies**: Microsoft Agent Framework, MCP SDK, Azure OpenAI

**Constraints Common to Both**: 
  - Must use MCP STDIO transport
  - All agents must be "hollow" (simple, LLM-driven, minimal specialized context)
  - Keep code structure simple (single Program.cs per process, inline logic)

**Scale/Scope**: 
  - Single user (developer/tester/demo)
  - 3 agents total (Orchestrator, User Profile, Global Advisor)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

### I. Code Quality Standards
**Status**: ✅ PASS (with plan)

- **No deprecated APIs**: Will use .NET 10 current APIs, Microsoft Agent Framework (latest), MCP SDK (latest stable)
- **Code duplication refactored**: Extract repeated logic into local functions within Program.cs (e.g., workflow event handling)
- **Docstrings for complex functions**: Workflow execution and MCP tool functions include XML documentation
- **Comments explain why**: Implementation comments will focus on business logic reasoning (e.g., "why profile agent before advisor") not syntax

**Note**: Principles II (UX Consistency) and III (Performance Requirements) are not applicable to this backend-only feature that exposes functionality through MCP STDIO protocol. The AI assistant client provides the user-facing experience.
- **Domain terminology**: Use investment/finance terms consistently (portfolio, risk tolerance, investment goals, advisor, orchestrator)
- **Simplicity for baseline version**: Inline logic in Program.cs, extract to classes/services in next versions when complexity warrants

### II. User Experience Consistency
**Status**: ✅ PASS

- **Interaction patterns**: User interacts through AI assistant natural language - no custom UI in v0.1/v0.2
- **Financial terminology**: Align with FinWise glossary (to be established): profile, risk tolerance, investment horizon, advisor, recommendation
- **Error messages**: Agent failures will return user-friendly messages (e.g., "Unable to retrieve your profile. Please try again or contact support.")
- **Cross-platform**: MCP STDIO works identically across Claude Desktop, ChatGPT, GitHub Copilot
- **Documentation**: User guide for MCP setup in `/docs/user-guide/mcp-setup.md`
- **Accessibility**: N/A for v0.1 (text-based interaction through AI assistant)

### III. Performance Requirements
**Status**: ✅ PASS

**Both Phases**:
- **Workflow latency**: Full multi-agent workflow <10 seconds total
  - Latency breakdown: Orchestrator routing <1s, Profile agent <3s, Advisor agent <5s, Handoffs <1s total
- **Memory usage**: Process memory <200MB per process
- **Monitoring**: Structured logging with execution times, request IDs, agent transitions

**Phase 1 Specific**:
- **Dictionary access**: <10ms (in-memory lookups)
- **No database queries**: All data in-memory

**Phase 2 Specific**:
- **Database operations**: MCP tool invocations <500ms (via User Profile MCP Server)
- **Database queries**: All queries indexed, no full table scans (user_profiles table <100 rows initially)
- **Performance tests**: Load tests for MCP tool invocations

**Clarification**: Constitution III requires API responses <500ms - applies to MCP tool database operations (Phase 2). Full workflow target <10s allows for multi-agent LLM inference time.

**Violations Requiring Justification**: None

## Quality Gates

*Aligned with FinWise Constitution (v2.0.0)*

**Code Quality**:
- [x] Linting configured: .editorconfig + StyleCop analyzers for C#
- [x] Code style guide: Follow Microsoft C# coding conventions + project-specific rules in `/docs/coding-standards.md`
- [x] Test framework: xUnit with FluentAssertions, Moq, Testcontainers

**Test Coverage**:
- [x] Target coverage: ≥80% per file (orchestrator, agents, MCP servers)
- [x] Test environment: 
  - **Phase 1**: In-memory tests with Moq
  - **Phase 2**: Add Testcontainers for PostgreSQL integration tests

**Performance**:
- [x] Performance targets defined: 
  - **Both phases**: Workflow <10s (multi-agent orchestration), handoffs <1s
  - **Phase 1**: Dictionary access <10ms
  - **Phase 2**: MCP tool database operations <500ms (per Constitution III)
- [x] Performance testing: Custom test harness measuring end-to-end workflow latency + tool invocation timing
- [x] Baseline metrics: Will establish from first successful workflow execution

**User Experience**:
- [x] Terminology aligned: "user profile", "risk tolerance", "investment advisor", "orchestrator", "handoff"
- [x] Error messages: User-friendly fallback responses when agents fail (e.g., "I'm having trouble accessing your profile right now")
- [x] Cross-platform consistency: MCP STDIO protocol ensures identical behavior across AI assistants

**Documentation**:
- [x] User-facing guide planned: `/docs/user-guide/mcp-setup-v01.md` (Claude Desktop configuration)
- [x] API contract documented: MCP tool schemas in `/specs/001-core-workflow/contracts/mcp-tools.json`
- [x] Accessibility: N/A for v0.1 (text-based AI assistant interaction)

## Project Structure

**Detailed file structure and component breakdown**: See [tasks.md](tasks.md) for complete structure after each phase.

### Phase 1 Structure (In-Memory)
```text
src/FinWise.Orchestrator/        # Single process
    Program.cs                    # Agents, workflow, MCP server, Dictionary storage
    Models.cs                     # UserProfileDto, WorkflowExecutionContext
    appsettings.json              # Azure OpenAI config
tests/FinWise.Orchestrator.Tests/ # Integration tests (in-memory)
```

### Phase 2 Additions (Persistence)
```text
src/FinWise.UserProfile.McpServer/  # NEW: Separate process
    Program.cs                       # DbContext, MCP tools, database operations
    UserProfile.cs                   # EF Core entity
    Migrations/                      # EF Core migrations
docker/postgresql/                   # NEW: PostgreSQL container
tests/FinWise.UserProfile.McpServer.Tests/  # NEW: Database integration tests
```

**Rationale**: Simplified single Program.cs per process for v0.1 hollow implementation. Extract to classes/services in v0.2+ when complexity warrants.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

**No violations identified.** All constitutional requirements met without exceptions.
