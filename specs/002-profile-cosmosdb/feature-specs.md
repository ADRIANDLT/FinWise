# Feature Specification: Profile Storage Migration to Azure CosmosDB

**Feature Branch**: `002-profile-cosmosdb`
**Created**: January 31, 2026
**Status**: Draft

---

## Overview

This feature migrates the user profile storage from the current in-memory implementation (`InMemoryUserProfileStore`) to Azure CosmosDB, using the CosmosDB emulator running in Docker with Linux containers for local development.

---

## Relationship to Core Workflow

- This feature implements the persistent storage aspect deferred from v0.1 core workflow
- Referenced in [specs/001-core-workflow/spec.md](../001-core-workflow/spec.md) under **Key Entities** → **UserProfile (Persistent Entity)**
- The existing `IUserProfileStore` interface abstraction enables this migration without changes to agent logic

---

## Current Implementation

### Existing Interface
**File**: `src/FinWise.Orchestrator/IUserProfileStore.cs`

```csharp
public interface IUserProfileStore
{
    Task<UserProfileDto?> GetProfileAsync(string userId);
    Task SetProfileAsync(string userId, UserProfileDto profile);
    Task<bool> HasProfileAsync(string userId);
    Task DeleteProfileAsync(string userId);
}
```

### Existing In-Memory Implementation
**File**: `src/FinWise.Orchestrator/InMemoryUserProfileStore.cs`

- Uses `ConcurrentDictionary<string, UserProfileDto>` for thread-safe storage
- Data is lost on application restart
- Suitable for development/testing only

### Data Model
**File**: `src/FinWise.Orchestrator/Models.cs`

```csharp
public record UserProfileDto(
    string UserId,           // Email address
    string? RiskTolerance,   // Nullable - progressive saving
    string? InvestmentGoals,
    string? InvestmentTimeframe
);
```

---

## Target Implementation

### Azure CosmosDB Emulator (Docker Linux)

**Documentation References**:
- Overview: https://learn.microsoft.com/en-us/azure/cosmos-db/emulator
- Development Guide: https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-develop-emulator

### Emulator Configuration

| Property | Value |
|----------|-------|
| Container Type | Linux |
| Default Endpoint | `https://localhost:8081/` |
| Default Key | `C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==` |
| Connection String | `AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;` |
| API | NoSQL |
| Consistency | Session or Strong only |

### Emulator Limitations

- Only supports Session and Strong consistency levels
- Maximum 10 fixed-size containers at 400 RU/s or 5 unlimited-size containers
- Maximum 5 JOIN statements per query
- Item unique identifier limited to 254 characters
- Cannot replicate across regions (single instance only)
- Linux emulator does not support Apple silicon or Microsoft ARM chips (use Windows VM or Linux-based preview emulator)

### CosmosDB Data Model

**Database**: `FinWise`
**Container**: `UserProfiles`
**Partition Key**: `/userId`

```json
{
  "id": "<userId>",
  "userId": "<email>",
  "riskTolerance": "<string|null>",
  "investmentGoals": "<string|null>",
  "investmentTimeframe": "<string|null>",
  "_etag": "<for optimistic concurrency>"
}
```

---

## User Scenarios & Testing

### User Story 1 - Profile Persistence Across Sessions (Priority: P1)

A user creates a profile in one session, restarts the application, and their profile is retained.

**Acceptance Scenarios**:

1. **Given** a user provides profile information, **When** the profile is saved, **Then** it is persisted to CosmosDB
2. **Given** the application restarts, **When** the same user returns, **Then** their previously saved profile is retrieved
3. **Given** a profile exists, **When** the user updates their risk tolerance, **Then** only the changed field is updated (optimistic concurrency)
4. **Given** a concurrent update conflict occurs, **When** saving a profile, **Then** the system handles the conflict gracefully (per spec assumption: prompt user to retry)

### User Story 2 - Docker Emulator Setup (Priority: P1)

Developers can run the CosmosDB emulator locally via Docker.

**Acceptance Scenarios**:

1. **Given** Docker Desktop is installed, **When** running the docker-compose command, **Then** the CosmosDB emulator starts successfully
2. **Given** the emulator is running, **When** the application starts, **Then** it connects to the emulator without errors
3. **Given** TLS/SSL validation issues, **When** connecting from .NET SDK, **Then** the SDK is configured to handle self-signed certificates appropriately

### User Story 3 - Fallback to In-Memory (Priority: P2)

The system can fall back to in-memory storage when CosmosDB is unavailable (for testing/demo).

**Acceptance Scenarios**:

1. **Given** a configuration setting `UseCosmosDb: false`, **When** the application starts, **Then** it uses `InMemoryUserProfileStore`
2. **Given** CosmosDB is configured but unavailable, **When** the application starts, **Then** it logs a warning and optionally falls back to in-memory

---

## Requirements

### Functional Requirements

- **FR-001**: System MUST persist user profiles to Azure CosmosDB using the NoSQL API
- **FR-002**: System MUST support the existing `IUserProfileStore` interface without breaking changes
- **FR-003**: System MUST support optimistic concurrency using ETags for conflict detection
- **FR-004**: System MUST create the database and container if they don't exist on startup
- **FR-005**: System MUST be configurable to use either CosmosDB or in-memory storage
- **FR-006**: System MUST handle CosmosDB connection failures gracefully with appropriate logging
- **FR-007**: Docker compose file MUST be provided for running the CosmosDB emulator locally

### Non-Functional Requirements

- **NFR-001**: Profile operations SHOULD complete within 100ms under normal conditions
- **NFR-002**: Connection string and credentials MUST NOT be committed to source control
- **NFR-003**: TLS/SSL certificate validation MUST be configurable for development vs production

### Assumptions

- Docker Desktop is available on developer machines
- Developers have sufficient resources to run the CosmosDB emulator (2GB RAM, 10GB disk)
- Production deployment will use Azure CosmosDB service (not emulator)
- The `Microsoft.Azure.Cosmos` NuGet package will be used for SDK access

---

## Success Criteria

- **SC-001**: All existing unit tests pass without modification (interface compatibility)
- **SC-002**: New integration tests verify CRUD operations against CosmosDB emulator
- **SC-003**: Profile data survives application restart
- **SC-004**: Docker compose starts the emulator with a single command
- **SC-005**: Configuration allows switching between in-memory and CosmosDB implementations
- **SC-006**: Optimistic concurrency conflicts are detected and handled per spec assumptions

---

## Scope

### In Scope

- New `CosmosDbUserProfileStore` implementation of `IUserProfileStore`
- Docker compose configuration for CosmosDB Linux emulator
- Configuration settings for connection string and storage provider selection
- Database/container initialization on startup
- Optimistic concurrency handling with ETags
- Integration tests for CosmosDB operations
- Developer documentation for emulator setup

### Out of Scope

- Production Azure CosmosDB deployment (infrastructure as code)
- Data migration from existing in-memory profiles (profiles are transient by design)
- Multi-region replication
- Change feed processing
- Backup and restore procedures
- Authentication integration with Azure AD/Entra ID

### Dependencies

- Docker Desktop installed on development machines
- `Microsoft.Azure.Cosmos` NuGet package
- Network access to localhost:8081 (emulator endpoint)

---

## Technical Design Notes

### SDK Connection with Emulator

```csharp
// For emulator with self-signed certificates
CosmosClientOptions options = new()
{
    HttpClientFactory = () => new HttpClient(new HttpClientHandler()
    {
        ServerCertificateCustomValidationCallback =
            HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    }),
    ConnectionMode = ConnectionMode.Gateway
};

using CosmosClient client = new(
    accountEndpoint: "https://localhost:8081/",
    authKeyOrResourceToken: "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
    clientOptions: options
);
```

### Database Initialization Pattern

```csharp
Database database = await client.CreateDatabaseIfNotExistsAsync(
    id: "FinWise",
    throughput: 400
);

Container container = await database.CreateContainerIfNotExistsAsync(
    id: "UserProfiles",
    partitionKeyPath: "/userId"
);
```

### Configuration Structure

```json
{
  "CosmosDb": {
    "Enabled": true,
    "Endpoint": "https://localhost:8081/",
    "Key": "<from-user-secrets-or-env>",
    "DatabaseName": "FinWise",
    "ContainerName": "UserProfiles"
  }
}
```

---

## References

- [Azure CosmosDB Emulator Overview](https://learn.microsoft.com/en-us/azure/cosmos-db/emulator)
- [Develop with CosmosDB Emulator](https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-develop-emulator)
- [Core Workflow Specification](../001-core-workflow/spec.md)
- [Existing Interface](../../src/FinWise.Orchestrator/IUserProfileStore.cs)
- [Existing Implementation](../../src/FinWise.Orchestrator/InMemoryUserProfileStore.cs)

---

## PROPOSED IMPLEMENTATION STEPS

> **STATUS**: Awaiting review and approval

`[]` = Pending | `[IN PROGRESS]` = Current | `[COMPLETED]` = Done

### Phase 1: Infrastructure Setup

1. `[COMPLETED]` **Create Docker Compose file** - Add a docker-compose.yml file in the repository root that defines the CosmosDB Linux emulator container with appropriate port mappings (8081) and volume mounts for data persistence.

2. `[COMPLETED]` **Add NuGet package reference** - Add `Microsoft.Azure.Cosmos` package to the FinWise.Orchestrator project.

3. `[COMPLETED]` **Create configuration settings** - Add CosmosDB configuration section to appsettings.json with Enabled flag, Endpoint, DatabaseName, and ContainerName. Use user secrets for the Key in development.

### Phase 2: Implementation

4. `[COMPLETED]` **Create CosmosDB profile document model** - Create a new class that represents the CosmosDB document structure with JSON property mappings, including an Id property and ETag for optimistic concurrency.

5. `[COMPLETED]` **Implement CosmosDbUserProfileStore** - Create a new class implementing `IUserProfileStore` that uses the CosmosDB SDK to perform CRUD operations. Include database/container initialization logic on first use.

6. `[COMPLETED]` **Update dependency injection configuration** - Modify Program.cs (or wherever DI is configured) to register either `CosmosDbUserProfileStore` or `InMemoryUserProfileStore` based on configuration.

7. `[COMPLETED]` **Handle TLS/SSL for emulator** - Configure the CosmosClient to accept self-signed certificates when running against the emulator (development mode only).

### Phase 3: Testing

8. `[COMPLETED]` **Write unit tests** - Create tests for the new CosmosDbUserProfileStore using mocking to verify correct SDK method calls.

9. `[COMPLETED]` **Write integration tests** - Create tests that run against the actual CosmosDB emulator to verify end-to-end functionality.

10. `[COMPLETED]` **Verify existing tests pass** - Run all existing tests to ensure interface compatibility is maintained.

### Phase 4: Documentation

11. `[COMPLETED]` **Update developer documentation** - Add setup instructions for running the CosmosDB emulator and configuring the application.

12. `[COMPLETED]` **Update README** - Add information about the CosmosDB dependency and configuration options.

---

> ✅ **IMPLEMENTATION COMPLETE** - All steps finished.
