# InMemory Agent Session Store

This store uses the **built-in `InMemoryAgentSessionStore`** class from the [Microsoft Agents Framework](https://github.com/microsoft/agents) (`Microsoft.Agents.AI.Hosting` namespace) — no custom implementation lives here.

## What it does

`InMemoryAgentSessionStore` persists agent sessions in process memory, keyed by `agentsession:{agentId}:{conversationId}`. It handles serialization and deserialization of `AgentSession` objects internally via the SDK.

## How the store is selected

The toggle lives in [`RedisOptions`](../Redis/RedisOptions.cs) and is bound from the `"Redis"` configuration section in `appsettings.json`:

```json
"Redis": {
  "Enabled": true,
  "ConnectionString": "localhost:6379",
  "SessionTtlMinutes": 1440
}
```

| Setting | Config Key | Default (code) | Effect |
|---------|-----------|----------------|--------|
| Use Redis? | `Redis:Enabled` | `false` | `true` → Redis store, `false` → in-memory |
| Connection | `Redis:ConnectionString` | `"localhost:6379"` | Redis endpoint |
| Session TTL | `Redis:SessionTtlMinutes` | `1440` (24 h) | Sliding expiration per session |

At startup, `Program.cs` reads the bound options and picks the store:

```csharp
if (redisOptions.Enabled)
{
    var redis = await ConnectionMultiplexer.ConnectAsync(redisOptions.ConnectionString);
    sessionStore = new RedisAgentSessionStore(redis, ...);
}
else
{
    sessionStore = new InMemoryAgentSessionStore();
}
```

To switch stores, set `Redis:Enabled` to `false` in `appsettings.json` (or via the environment variable `Redis__Enabled=false`).

## Trade-offs

| | In-Memory | Redis |
|---|---|---|
| **Persistence** | Lost on restart | Survives restarts |
| **Scale-out** | Single instance only | Shared across instances |
| **Setup** | Zero — SDK built-in | Requires Redis |

## Suitable for

Local development and single-instance deployments. Use the [`Redis`](../Redis/README.md) store for production.
