# 07 — Testing the MCP Server Live and the Scale-Out Question

_March 22, 2026 — Taking the freshly wired FinWise MCP Server for a live spin, playing the role of end-user, then asking the hard question: can this thing scale?_

---

## The Starting Point

Journal 06 ended with the Redis `AgentSessionStore` plan fully researched and specced. By the time this session began, both commits were already landed: `1d11fe2` brought the baseline in-memory session store from the framework, and `c520148` wired up the Redis implementation. The MCP server was running, the Redis integration tests were green, and everything was ready for the real test — talking to FinWise as a user, not a developer.

---

## Act I — Playing the User

The session opened not with code, but with a question no developer asks:

> **User:** "give me financial advice"

The MCP server responded through the FinWise workflow — ProfileAgent kicked in, asking for an email address. The `PROFILE_READY:` marker pattern, documented since Journal 03, was working end-to-end through Redis session persistence.

> **User:** "adri@outlook.com"

Profile created. The orchestrator handed off to the AdvisorAgent. Profile fields came back: email=adri@outlook.com, risk=conservative, goals=wealth growth, timeframe=short. The advisor responded with tailored guidance — bonds, dividend stocks, low-risk ETFs. Conservative and short-term, just as the profile dictated.

Then the conversation continued naturally:

> **User:** "I want stock buy in advice"

The advisor stayed in character — recommending blue-chip, dividend-paying stocks aligned with the conservative profile. No hallucinated ticker symbols, no aggressive growth plays.

> **User:** "Between tesla and amazon which stock should I buy in?"

This was the interesting one. The advisor correctly identified that _neither_ was ideal for a conservative, short-term investor, but if forced to choose, Amazon's diversified business and stable cash flow made it less risky than Tesla's automotive concentration. The profile-awareness was working — the advisor wasn't just answering generically, it was filtering through the user's risk tolerance.

The follow-ups kept coming:

> **User:** "What about meta?"

Same pattern — high volatility, advertising-dependent revenue, metaverse investment risk. The advisor ranked all three: Amazon > Meta > Tesla for conservative profiles. Consistent reasoning across turns.

Then, a curveball:

> **User:** "Im saying howdy to you in spanish: 'holita'"

¡Holita! The agent handled the social turn gracefully, responding in kind without breaking character.

> **User:** "what was my phrase when I said howdy"

And here was the real test — **session memory**. The agent recalled "holita" from earlier in the conversation. The Redis-backed `AgentSession` was holding the full chat history across turns. The session store wasn't just storing — it was _working_.

---

## Act II — The Stock Agent in Action

> **User:** "Use finwise mcp server and give me the revenue of microsoft in 2024 plus other details"

This triggered the Stock Specialized Agent — the Azure AI Foundry-hosted agent with access to grounding data from annual reports. The response came back rich:

- **Revenue:** $245.1 billion (+16%)
- **Net Income:** $88.1 billion (+22%)
- **Intelligent Cloud:** $105.4 billion (+20%)
- **Microsoft Cloud:** $137.4 billion (+23%)

The hub-and-spoke handoff pattern worked seamlessly — the orchestrator recognized the stock query, routed to the stock agent, and returned the response. No direct agent-to-agent communication, exactly as designed.

> **User:** "now based on my profile should I buy in in tesla or amazon? Give me the facts as to why"

The advisor combined profile awareness (conservative, short-term) with stock knowledge to produce a fact-based comparison. Amazon won on diversification and cash flow stability. The multi-agent architecture was doing what it was designed to do — profile context from one agent informing advice from another, all mediated through the orchestrator.

---

## Act III — The Scale-Out Question

Then the developer hat went back on. Looking at [Program.cs](../src/FinWise.McpServer/Program.cs), lines 124–125:

```csharp
builder.Services.AddSingleton(workflowService);
builder.Services.AddSingleton(new McpSessionMapping());
```

> **User:** "these lines are creating singleton objects. When scaling out the mcp-server in the cloud in azure will have multiple instances... Research and make sure that these singletons impact or do not impact since we have the states userprofile and agentsession in external databases CosmosDB and Redis."

The question cut to the heart of cloud-readiness: can this MCP server sit behind an Azure load balancer with multiple instances?

### The Investigation

A thorough exploration mapped every piece of state in the application:

| Component | State Location | Scale-Out Safe? |
|-----------|---------------|-----------------|
| `FinWiseWorkflowService` | Stateless (holds only references) | ✅ Yes |
| `McpSessionMapping` | **In-memory `ConcurrentDictionary`** | ❌ **Blocker** |
| `RedisAgentSessionStore` | Redis (external) | ✅ Yes |
| `CosmosDbUserProfileStore` | CosmosDB (external) | ✅ Yes |
| `AgentSessionManager` | Stateless wrapper | ✅ Yes |
| `AgentSessionRunContext` | `AsyncLocal` (per-request) | ✅ Yes |
| `SessionResetFlag` | `AsyncLocal` (per-request) | ✅ Yes |

### The Verdict

The good news: the investment in Redis for session storage and CosmosDB for profiles had already externalized the heavy state. `FinWiseWorkflowService` holds only injected references — no conversational state. The `AsyncLocal` fields (`AgentSessionRunContext`, `SessionResetFlag`) are per-async-flow and never cross request boundaries.

The bad news: **`McpSessionMapping` is a scale-out blocker**. It holds a `ConcurrentDictionary<string, string>` mapping MCP session IDs to agent session IDs entirely in memory. In a multi-instance deployment:

1. Request hits **Instance A** → creates mapping `session-abc → agent-xyz`
2. Next request from same client hits **Instance B** → no mapping → new agent session → conversation lost

The fix is clear: move `McpSessionMapping` to Redis, just like the session store. The pattern already exists — the `RedisAgentSessionStore` provides the template.

There's also the MCP transport layer consideration. The `Stateless = false` setting means the MCP SDK maintains per-session state internally. Azure would need **sticky sessions (ARR affinity)** on the load balancer, or the MCP SDK would need distributed session support.

---

## What We Learned

### About the Architecture

- The decision to externalize state to Redis and CosmosDB was the right call — it already solved 90% of the scale-out problem
- `McpSessionMapping` is the last piece of in-memory state blocking horizontal scaling
- The MCP transport's `Stateless = false` adds an infrastructure-level concern (sticky sessions) that code alone can't solve

### About the Workflow

- Playing end-user before asking architecture questions revealed that the system _works_ — session persistence, multi-agent handoffs, profile-aware advice, stock data retrieval all functioned correctly through Redis
- The "holita" recall test was a simple but effective validation of session continuity

### About Testing

- The Redis integration tests passing (`dotnet test tests/FinWise.Redis.IntegrationTests/`) gave confidence before the live test
- End-to-end MCP tool invocation validated the full stack: MCP client → HTTP transport → FinWiseTools → workflow → agents → Redis/CosmosDB → response

---

## What's Next

1. **Move `McpSessionMapping` to Redis** — the last in-memory state blocker. Follow the `RedisAgentSessionStore` pattern
2. **Research MCP SDK distributed session support** — determine if `Stateless = false` requires sticky sessions or if there's a pluggable transport session store
3. **Azure deployment planning** — Container Apps or App Service, ARR affinity configuration, Redis and CosmosDB provisioning

The MCP server works. The agents talk. The sessions persist. Now it needs to scale.

---

_Written: March 22, 2026_
