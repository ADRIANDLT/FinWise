# Redis Setup Guide

This guide explains how to set up Redis for local development session storage.

## Prerequisites

- [Docker Desktop](https://www.docker.com/products/docker-desktop/) installed and running
- Port 6379 available on localhost

## Quick Start

### 1. Start Redis

From the repository root, run:

```
docker compose up -d redis
```

### 2. Verify Redis is Running

```
docker exec -it finwise-redis redis-cli ping
# Should return: PONG
```

### 3. Configure the Application

Redis is configured in `appsettings.json`:

```json
{
  "Redis": {
    "Enabled": true,
    "ConnectionString": "localhost:6379",
    "SessionTtlMinutes": 1440
  }
}
```

To disable Redis and use in-memory storage, set `Enabled: false`.

### 4. Run the Application

```
dotnet run --project src/FinWise.McpServer/ --urls http://localhost:5000
```

## Connection Details

| Property | Value |
|----------|-------|
| Host | `localhost` |
| Port | `6379` |
| Connection String | `localhost:6379` |

## Useful Redis CLI Commands

```
docker exec -it finwise-redis redis-cli
KEYS *              # List all keys
GET <key>           # Get value
TTL <key>           # Check TTL in seconds
DBSIZE              # Count keys
FLUSHDB             # Clear all keys
INFO memory         # Memory usage stats
```

## Common Commands

```
# Start Redis only
docker compose up -d redis

# Stop Redis
docker compose stop redis

# View logs
docker compose logs -f redis

# Restart
docker compose restart redis

# Remove data (fresh start)
docker compose down -v
```

## Troubleshooting

| Issue | Resolution |
|-------|------------|
| Connection refused on port 6379 | Ensure Docker is running and Redis container is up: `docker compose ps` |
| Data lost after restart | Check that the `redis-data` volume exists: `docker volume ls`. The `--save 60 1` flag enables RDB persistence. |
| "READONLY" errors | Container may be in a bad state. Restart: `docker compose restart redis` |
| Session not restored | Verify `Redis:Enabled` is `true` in config. Check logs for "Using Redis session store" at startup. |
