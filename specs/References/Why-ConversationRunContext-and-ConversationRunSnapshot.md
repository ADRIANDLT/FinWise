# Why ConversationRunContext and ConversationRunSnapshot Matter

> **Audience:** Anyone wanting to understand why we need a runtime guardrail for LLM tool calls, and whether it still applies in a stateless, cloud-native architecture, not just a statefull server.

---

## Problem to be solved

| Question | Answer |
|----------|--------|
| **What happened?** | The LLM saved `goals="save for retirement"` and `timeframe="Long-term"` even though the user only provided `risk="Moderate"`. This happened many other times in a similar way. Silent data corruption. |
| **Why didn't prompts stop it?** | Prompts are advisory. The LLM tried to be "helpful" and inferred values from context. |
| **What does `ConversationRunSnapshot` do?** | It gives every tool a read-only photo of the current conversation so the tool can verify "did the user actually say this value?" before persisting. |
| **Is it storage-agnostic?** | Yes. Works the same with in-memory, PostgreSQL, Cosmos DB, or any future store. |
| **Does it work with a stateless MCP server?** | Yes. The snapshot is created per-request; the server never holds global state between calls. |
| **Is there a simpler alternative?** | See the "Alternatives" section. Every simpler idea either keeps the bug or adds more friction elsewhere. |

---

## 1. The Bug (a.k.a. "the save for retirement incident")

A user started a conversation:

```
User: "Give me financial advice"
User: "delatorre@outlook.com"
User: "Moderate"
```

The profile agent should have asked for **goals** and **timeframe**. Instead, the LLM guessed:

```
Risk: Moderate          ← user actually said this
Goals: save for retirement   ← LLM invented this
Timeframe: Long-term         ← LLM invented this
```

The profile store happily saved the invented values. The user never typed "retirement" or "Long-term", but the database said they did. **This is a silent data-corruption bug.**

---

## 2. Why Prompts Alone Failed

We already told the agent in bold letters:

> **"ONLY persist information the user EXPLICITLY gives you. NEVER infer or assume."**

The LLM ignored the instruction because:

1. **LLMs predict the most probable next token.** If "financial advice" usually correlates with "retirement", the model generates it.
2. **Prompts are soft constraints.** They guide behavior; they cannot enforce invariants.
3. **Tools trusted the caller.** `SetProfile(risk, goals, timeframe)` had no way to know whether those strings actually came from user messages.

**Conclusion:** Prompts are necessary but insufficient. We need a runtime guard.

---

## 3. How the Guard Works

Think of `ConversationRunSnapshot` as a sticky note you hand to every tool before it runs:

> "Here's what the user actually said. Check before you write."

### Step-by-step flow

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. MCP request arrives with user query                         │
│ 2. Load conversation history (from memory / Cosmos / wherever) │
│ 3. Push snapshot:                                               │
│      using var scope = ConversationRunContext.Push(snapshot);  │
│ 4. Run workflow (agents call tools)                             │
│    └─ Inside SetProfile:                                        │
│         • Read ConversationRunContext.Current                   │
│         • Search user messages for "Moderate", "retirement"...  │
│         • If NOT found → reject save, return false              │
│         • If found → persist to store                           │
│ 5. Dispose scope (snapshot vanishes)                            │
│ 6. Persist new messages to conversation store                   │
│ 7. Return response to MCP client                                │
└─────────────────────────────────────────────────────────────────┘
```

The snapshot lives only for the duration of that request. The MCP server is stateless between requests.

---

## 4. Stateless Architecture: PostgreSQL + Cosmos DB

**Q: What if profiles live in PostgreSQL and conversations live in Azure Cosmos DB with vector embeddings?**

**A: Nothing changes.** The pattern is storage-agnostic:

| Phase | Current (in-memory) | Future (cloud databases) |
|-------|---------------------|--------------------------|
| Load conversation | `conversationStore.GetAsync()` | Cosmos SDK call |
| Create snapshot | `ConversationRunContext.Push(...)` | Same code |
| Validate in tool | `WasValueProvidedByUser(messages, value)` | Same code |
| Persist profile | `profileStore.SetAsync()` | PostgreSQL call |
| Persist conversation | `conversationStore.SetAsync()` | Cosmos SDK call |

The MCP server process can be ephemeral (e.g., Azure Container Apps scaling to zero). Each request:

1. Loads state from remote stores.
2. Creates a short-lived snapshot.
3. Runs the workflow.
4. Writes back to remote stores.
5. Forgets everything.

**The snapshot is not global state.** It is a per-request, per-async-flow guard that disappears after the response.

---

## 5. Comparison with Industry Frameworks

We reviewed documentation from:

- **Microsoft Agent Framework (.NET)** – recommends combining prompts with programmatic validation layers. Supports shared state and checkpoint patterns for workflows.
- **OpenAI Agents Python SDK** – has first-class **Input Guardrails** and **Output Guardrails** that wrap agents and block bad data before or after execution.

### Key Finding: OpenAI's `@input_guardrail` Pattern

The OpenAI Agents Python SDK provides `@input_guardrail` and `@output_guardrail` decorators that do **exactly** what `ConversationRunSnapshot` does: validate data before/after agent execution to prevent hallucinations from leaking through.

**Reference:** [OpenAI Agents Python SDK – Guardrails Documentation](https://github.com/openai/openai-agents-python/blob/main/docs/guardrails.md)

Here's how OpenAI's pattern looks in Python:

```python
from pydantic import BaseModel
from agents import (
    Agent,
    GuardrailFunctionOutput,
    InputGuardrailTripwireTriggered,
    RunContextWrapper,
    Runner,
    input_guardrail,
)

class MathHomeworkOutput(BaseModel):
    is_math_homework: bool
    reasoning: str

guardrail_agent = Agent(
    name="Guardrail check",
    instructions="Check if the user is asking you to do their math homework.",
    output_type=MathHomeworkOutput,
)

@input_guardrail
async def math_guardrail(
    ctx: RunContextWrapper[None], agent: Agent, input: str | list
) -> GuardrailFunctionOutput:
    result = await Runner.run(guardrail_agent, input, context=ctx.context)
    return GuardrailFunctionOutput(
        output_info=result.final_output,
        tripwire_triggered=result.final_output.is_math_homework,
    )

agent = Agent(
    name="Customer support agent",
    instructions="You help customers with their questions.",
    input_guardrails=[math_guardrail],  # ← Guardrail attached here
)
```

**Our `ConversationRunSnapshot` is the .NET equivalent of this pattern.** The difference is architectural:

| OpenAI Python SDK | FinWise .NET |
|-------------------|--------------|
| Guardrail is a decorator around the agent | Guardrail is inside the tool itself |
| Runs before/after agent turn | Runs when tool is called |
| Raises `TripwireTriggered` exception | Returns `false` and logs warning |
| Uses `RunContextWrapper` for context | Uses `AsyncLocal<ConversationRunSnapshot>` |

Both achieve the same goal: **deterministic validation that prompts cannot guarantee.**

> 💡 **Why inside the tool instead of around the agent?**  
> In our multi-agent handoff workflow, multiple agents share tools. Placing the guard inside `SetProfile` ensures validation happens regardless of which agent calls it, without requiring every agent to have its own guardrail decorator.

### Microsoft Agent Framework (.NET): `GuardrailCallbackMiddleware`

The Microsoft Agent Framework provides a **middleware pattern** for guardrails in C#. This is documented in [ADR-0007: Agent Filtering Middleware](https://github.com/microsoft/agent-framework/blob/main/docs/decisions/0007-agent-filtering-middleware.md).

Here's the official Microsoft pattern:

```csharp
// From Microsoft Agent Framework documentation
internal sealed class GuardrailCallbackMiddleware : CallbackMiddleware<AgentInvokeCallbackContext>
{
    private readonly string[] _forbiddenKeywords = { "harmful", "illegal", "violence" };

    public override async Task OnProcessAsync(
        AgentInvokeCallbackContext context, 
        Func<AgentInvokeCallbackContext, Task> next, 
        CancellationToken cancellationToken)
    {
        // Guardrail: Filter input messages BEFORE agent runs
        context.Messages = this.FilterMessages(context.Messages);
        Console.WriteLine($"Guardrail Middleware - Filtered messages: {new ChatResponse(context.Messages).Text}");

        await next(context);  // Run the agent

        if (!context.IsStreaming)
        {
            // Guardrail: Filter output messages AFTER agent runs
            context.Messages = this.FilterMessages(context.Messages);
        }
    }
}

// Usage: Attach middleware to agent via builder pattern
var agent = persistentAgentsClient.CreateAIAgent(model)
    .AsBuilder()
    .UseCallbacks(config =>
    {
        config.AddCallback(new PiiDetectionMiddleware());
        config.AddCallback(new GuardrailCallbackMiddleware());
    })
    .Build();
```

The framework also defines filter interfaces for function-level interception:

```csharp
// Intercept agent runs
interface IAgentRunFilter
{
    Task OnRunAsync(AgentRunContext context, Func<AgentRunContext, Task> next, CancellationToken ct);
}

// Intercept function/tool calls specifically
interface IAgentFunctionCallFilter
{
    Task OnFunctionCallAsync(AgentFunctionCallContext context, Func<AgentFunctionCallContext, Task> next, CancellationToken ct);
}
```

**How our approach compares to Microsoft's middleware:**

| Microsoft Agent Framework | FinWise Implementation |
|---------------------------|------------------------|
| `GuardrailCallbackMiddleware` wraps agent | Validation inside `SetProfile` tool |
| Uses `AgentInvokeCallbackContext` | Uses `ConversationRunSnapshot` via `AsyncLocal` |
| Filters messages before/after agent turn | Validates tool arguments before persistence |
| Decorator pattern: `.UseCallbacks()` | Ambient context pattern: `ConversationRunContext.Push()` |
| Middleware chain for multiple concerns | Single-purpose guard in the critical tool |

**Why we chose tool-level validation instead of middleware:**
1. Our workflow uses handoffs between multiple agents—middleware would need to be attached to each agent.
2. The `SetProfile` tool is the single point where data gets persisted—guarding it directly is simpler.
3. `AsyncLocal` provides ambient context without polluting agent construction code.

---

## 6. Alternative Approaches and Why We Rejected Them

| Idea | Sounds simpler because... | Fails because... |
|------|---------------------------|------------------|
| **"Write better prompts"** | No code changes | LLM still hallucinates; we tested it |
| **"LLM sends full history in tool call"** | No ambient context needed | Token bloat; LLM can misquote or cherry-pick |
| **"Explicit `messages` parameter on every tool"** | Obvious data flow | Pollutes every tool signature; duplication |
| **"Database trigger rejects bad rows"** | Validation close to storage | DB has no conversation context to compare against |
| **"Separate validation agent"** | Separation of concerns | Same concept, more latency, more tokens |

`ConversationRunContext` is **~60 lines of code** that:

- Does not pollute tool signatures.
- Does not require LLM cooperation.
- Works with any storage backend.
- Adds ~0 ms overhead (in-memory substring search).

---

## 7. When This Pattern Really Shines

- **Parallel workflows:** Multiple async branches can run concurrently; `AsyncLocal` isolates each branch.
- **Compliance:** Add more checks (PII detection, profanity filtering) without changing tool signatures.
- **Audit logging:** The snapshot contains the exact messages that led to a tool call—useful for debugging.
- **Stateless scaling:** The pattern does not require sticky sessions or in-memory caches.

---

## 8. Key Takeaways

1. **Prompts are guidance, not guarantees.** LLMs will infer plausible values even when told not to.
2. **Tools must validate inputs.** If a tool persists data, it should verify the data came from the user.
3. **`ConversationRunSnapshot` is a per-request, storage-agnostic guardrail.** It does not add global state.
4. **The pattern survives migration to PostgreSQL, Cosmos DB, or any cloud backend.** Each request loads history, creates a snapshot, runs, and forgets.
5. **Removing the guard re-opens the bug.** Without it, the LLM can again invent profile data.

---

## 9. Code Size Summary

| File | Lines | Purpose |
|------|-------|---------|
| `ConversationRunSnapshot` (record) | 1 | Holds conversationId + messages |
| `ConversationRunContext` (static class) | ~50 | `AsyncLocal` + `Push`/`Dispose` pattern |
| Validation in `SetProfile` | ~30 | `WasValueProvidedByUser` checks |
| Injection in `Program.cs` | 2 | `using var scope = ...` |

**Total: ~83 lines** to prevent silent data corruption in a financial application. That is a good trade-off.

---

## 10. Final Thoughts

This guardrail is the **minimum viable defense** against LLM hallucinations leaking into persistent storage. It is simple, fast, storage-agnostic, and stateless-friendly. Every alternative either reintroduces the bug or adds more complexity elsewhere.

For a system that handles personal finance data, trusting the LLM to "just follow the prompt" is not an option. We need a deterministic check, and `ConversationRunSnapshot` provides it with the smallest possible footprint.
