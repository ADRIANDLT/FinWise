# CosmosDB Setup Guide

This guide explains how to set up the Azure CosmosDB emulator for local development.

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) installed and running
- Minimum 3GB RAM available for the emulator container
- Port 8081 available on localhost

## Quick Start

### 1. Start the CosmosDB Emulator

From the repository root, run:

```powershell
docker compose up -d
```

This starts the CosmosDB Linux emulator in a Docker container.

### 2. Verify Emulator is Running

Wait about 60 seconds for the emulator to initialize, then check:

```powershell
# Check container status
docker compose ps

# View logs
docker compose logs -f cosmosdb-emulator
```

The emulator is ready when you can access the Data Explorer at:
- **URL**: https://localhost:8081/_explorer/index.html

> **Note**: Your browser will show a security warning because the emulator uses a self-signed certificate. Accept the warning to proceed.

### 3. Configure the Application

The application is already configured for the emulator in `appsettings.Development.json`:

```json
{
  "CosmosDb": {
    "Enabled": true,
    "Key": "C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
    "AllowInsecureTls": true
  }
}
```

To disable CosmosDB and use in-memory storage instead, set `Enabled: false` in your configuration.

### 4. Run the Application

```powershell
dotnet run --project src/FinWise.Orchestrator/ --urls http://127.0.0.1:3923
```

The application will automatically create the `FinWise` database and `UserProfiles` container on first use.

## Connection Details

| Property | Value |
|----------|-------|
| Endpoint | `https://localhost:8081/` |
| Key | `C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==` |
| Database | `FinWise` |
| Container | `UserProfiles` |
| Partition Key | `/userId` |

## Common Commands

```powershell
# Start emulator
docker compose up -d

# Stop emulator
docker compose down

# View logs
docker compose logs -f cosmosdb-emulator

# Restart emulator
docker compose restart

# Remove emulator and data
docker compose down -v
```

## Running Tests

### Unit Tests (No Emulator Required)

```powershell
dotnet test tests/FinWise.Orchestrator.Tests --filter "Category!=Integration&FullyQualifiedName!~EndToEndMcpTests"
```

### Integration Tests (Requires Emulator)

```powershell
# Start emulator first
docker compose up -d

# Wait for emulator to be ready (about 60 seconds)

# Run integration tests
dotnet test tests/FinWise.Orchestrator.Tests --filter "Category=Integration"
```

## Troubleshooting

### Emulator Won't Start

1. Ensure Docker Desktop is running
2. Check if port 8081 is available:
   ```powershell
   netstat -an | findstr 8081
   ```
3. Ensure sufficient memory (3GB minimum)

### Connection Refused

1. Wait 60+ seconds after starting the container
2. Check emulator health:
   ```powershell
   docker compose ps
   ```
3. Verify the endpoint is accessible:
   ```powershell
   curl -k https://localhost:8081/
   ```

### SSL/TLS Errors

The emulator uses a self-signed certificate. The application is configured to accept this in development mode (`AllowInsecureTls: true`). 

**Never enable `AllowInsecureTls` in production.**

### Data Persistence

Data is persisted in a Docker volume (`cosmosdb-data`). To reset all data:

```powershell
docker compose down -v
docker compose up -d
```

## Switching Between In-Memory and CosmosDB

Edit `appsettings.json` or `appsettings.Development.json`:

```json
{
  "CosmosDb": {
    "Enabled": false  // Use in-memory storage
  }
}
```

Or:

```json
{
  "CosmosDb": {
    "Enabled": true   // Use CosmosDB
  }
}
```

## Production Configuration

For production, configure these environment variables:

| Variable | Description |
|----------|-------------|
| `CosmosDb__Enabled` | `true` |
| `CosmosDb__Endpoint` | Your Azure CosmosDB endpoint |
| `CosmosDb__Key` | Your Azure CosmosDB key (use Azure Key Vault) |
| `CosmosDb__AllowInsecureTls` | `false` (always) |

## References

- [Azure CosmosDB Emulator Documentation](https://learn.microsoft.com/en-us/azure/cosmos-db/emulator)
- [Develop with CosmosDB Emulator](https://learn.microsoft.com/en-us/azure/cosmos-db/how-to-develop-emulator)
