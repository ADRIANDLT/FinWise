# InMemory Agent Session Store

This store uses the **built-in `InMemoryAgentSessionStore`** class from the [Microsoft Agents Framework](https://github.com/microsoft/agents) (`Microsoft.Agents.AI.Hosting` namespace) — no custom implementation lives here.

## What it does

`InMemoryAgentSessionStore` persists agent sessions in process memory, keyed by `{agentId}:{conversationId}`. It handles serialization and deserialization of `AgentSession` objects internally via the SDK.

## When it's used

Selected at startup in `Program.cs` when `Redis:Enabled = false` (the default):

```csharp
sessionStore = new InMemoryAgentSessionStore();
```

## Trade-offs

| | In-Memory | Redis |
|---|---|---|
| **Persistence** | Lost on restart | Survives restarts |
| **Scale-out** | Single instance only | Shared across instances |
| **Setup** | Zero — SDK built-in | Requires Redis |

## Suitable for

Local development and single-instance deployments. Use the [`Redis`](../Redis/README.md) store for production.
