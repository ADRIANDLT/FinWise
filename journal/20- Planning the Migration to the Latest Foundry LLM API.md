# Planning the Migration to the Latest Foundry LLM API: From Azure OpenAI Service to AIProjectClient + Responses

_April 18–19, 2026_
_The session where we stopped guessing about Foundry SDK shapes and went and read the source._

---

## Where We Were

FinWise had been happily talking to Azure OpenAI through the legacy `Azure.AI.OpenAI` package since the very first version. The composition root in `FinWise.McpServer/Infrastructure/AzureOpenAI/AzureOpenAIChatClientFactory.cs` did exactly two things:

```csharp
var azureClient = new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey));
var chatClient  = azureClient.GetChatClient(deploymentName).AsIChatClient();
```

API key auth. Old-style `https://<resource>.openai.azure.com` endpoint. Chat Completions wire protocol. It worked — but it was the _old_ Azure OpenAI surface, not the new Azure AI Foundry one.

Meanwhile, the StockAgent (a separate, optional Foundry-hosted agent) was already living in the modern world: `AIProjectClient`, service-principal credentials, the new Foundry project endpoint shape (`https://<resource>.services.ai.azure.com/api/projects/<project>`). Two worlds, one app.

The user wanted to bring the Orchestrator, Profile, and Advisor agents into the same world.

---

## The First Question — Did the New Deployment Even Work?

The session opened with a practical poke, not a research question:

> **User:** "I have created a new Azure AI Foundry LLM model deployment (another similar gpt-4o-mini model) but it's in a different Azure AI Foundry resource and different project. I updated the value of these three ENV VARS in the .env file…"

Right after that came a screenshot — the Foundry portal showed the endpoint ending in `/openai/v1`, which is the OpenAI-compatibility surface Foundry exposes for clients that still want to speak Chat Completions. So we tested that path first by running the existing `AzureOpenAIClient` against the new endpoint URL, with key auth, and the full Docker stack up.

That worked. But it raised an uncomfortable question: was Docker Compose actually using our new `.env` value?

> **User:** "Where in the docker-compose files is doing this? 'Docker Compose's `${VAR}` substitution checks host env vars before the .env file'? Or is that a behaviour from the command?"

It wasn't in the compose file. It's built into Docker Compose itself: host environment variables silently win over `.env`. The user had `AZURE_OPENAI_ENDPOINT` set at the Windows Machine level from an earlier session, which is why the old endpoint kept resolving even after the `.env` edit. Five minutes of confusion, one durable lesson.

> **User:** "When finished testing, add that fact about the ENV VARs priority, OS > .env, etc. in the main README.md for the user to know."

Noted. Then the containers came down, and the real work began.

---

## The Pivot — From "Just Make It Work" to "Build the Migration Plan"

The compatibility path proved the new Foundry resource was reachable. But hitting `/openai/v1` from `AzureOpenAIClient` is not really "migrating to Foundry" — it's "using Foundry as if it were Azure OpenAI." The user wanted the real thing.

So began the spec-doc loop. The migration spec at `specs/012-move-to-foundry-llm-api/012-move-to-foundry-llm-api.md` already had a draft (Revision 1), but it was full of ideas borrowed from older Microsoft samples — including a recommendation to use `Azure.AI.Inference`, which the user immediately pushed back on:

> **User:** "Do NOT use/propose Azure.AI.Inference extension if it's deprecated. At the end of the day, I want to use the IChatClient interface compatible with the Microsoft Agent Framework class ChatClientAgent."

Fair. The constraint set crystallized:
- No `Azure.AI.Inference`
- New Foundry **project** (not classic / hub)
- End result must be an `IChatClient` consumable by `Microsoft.Agents.AI.ChatClientAgent`
- Use whatever the **April 2026** SDKs actually want us to use, not what some older blog says

That's when the spec started getting rewritten one revision at a time.

---

## The Research Loop — Six Revisions of "Are We Sure?"

This wasn't a single research pass. It was five days of "are we _really_ sure?" compressed into one long session. Each revision answered one question and surfaced two more.

**Revision 2** removed `Azure.AI.Inference` after the user pointed out it had been deprecated for the new Foundry SDK direction.

**Revision 3** made the Foundry-native `AIProjectClient` path the primary recommendation and demoted the `/openai/v1/` direct path to a documented fallback. This matched the official Foundry SDK guidance: _"use Foundry SDK for agent scenarios; use OpenAI SDK only when maximum OpenAI compatibility is needed."_

**Revision 4** was the moment everything snapped into place. Until then, the spec was citing third-party blog posts and inferring the call chain. The real verification came from going to the source — literally — and reading `microsoft/agent-framework`'s `Microsoft.Agents.AI.Foundry` 1.1.0 repo on GitHub. There, in `AzureAIProjectChatClientExtensions.cs` line 236-239, was Microsoft's own internal composition for the public `aiProjectClient.AsAIAgent(...)` extension:

```csharp
IChatClient chatClient = aiProjectClient
    .GetProjectOpenAIClient()           // extension from Azure.AI.Extensions.OpenAI
    .GetResponsesClient()               // inherited from OpenAI.OpenAIClient
    .AsIChatClient(modelDeploymentName); // bridge from Microsoft.Extensions.AI.OpenAI
```

That was the canonical chain. Not invented. Not inferred. _The same one Microsoft ships._

This corrected two earlier mistakes in the spec:
- It's `GetProjectOpenAIClient()` — an **extension method** on `AIProjectClient`, not a property like `.ProjectOpenAIClient`
- It's `GetResponsesClient()` — **no model argument**, not `GetProjectResponsesClientForModel(model)` as Revision 1 had assumed

The user also asked an important clarifying question about a related one-liner I had mentioned:

> **User:** "When you say `Microsoft.Agents.AI.Foundry 1.1.0 also exposes a one-liner: aiProjectClient.AsAIAgent(model, instructions, ...)` — is that a Foundry deployed model in foundry cloud? Or is it a custom MAF Agent using a Foundry LLM?"

The honest answer: the `(model, instructions, ...)` overload returns a **client-side `ChatClientAgent`** wrapping a Foundry-deployed _LLM model deployment_ via the Responses API. That's what FinWise's Orchestrator/Profile/Advisor agents are. The other overloads — `AsAIAgent(AgentReference)` and friends — wrap a **server-side Foundry-hosted agent**, like StockAgent. Same method name, two very different things.

So why doesn't FinWise just use the one-liner? Because `FinWise.MultiAgentWorkflow` builds its own `ChatClientAgent` instances per agent in its factories. The McpServer needs to stop one step earlier, at `IChatClient`, so the workflow library stays untouched. That's not a regression — it's preserving the seam.

---

## The "Are You Sure It's 100%?" Pressure

After Revision 4, I reported back at "92% confidence" with two open gaps:
1. Was `GetProjectOpenAIClient()` an extension method or a property? (One README said one thing, the MAF source said another.)
2. What was the exact `using` for `AsIChatClient(string)` on `ResponsesClient`?

The user wasn't satisfied:

> **User:** "Did you fully finished researching and reviewing the document? Is it 100% confirmed?"

Then:

> **User:** "Keep working until is 100% fully confirmed."

So the research went one level deeper — straight into the published API surface files of the actual NuGet packages. Four files closed the gaps:

| Question | Source | Answer |
|---|---|---|
| Is `GetProjectOpenAIClient()` an extension or property? | `Azure/azure-sdk-for-net` → `Azure.AI.Extensions.OpenAI.net8.0.cs` line 778 | **Extension method** on `ClientConnectionProvider` |
| Does `ProjectOpenAIClient` derive from `OpenAIClient`? | same file, line 653 | Yes — `ProjectOpenAIClient : OpenAI.OpenAIClient` |
| Where does `GetResponsesClient()` come from? | `openai/openai-dotnet` → `OpenAI.netstandard2.0.cs` line 34 | Inherited from `OpenAIClient`, returns `ResponsesClient` |
| What namespace and signature for `AsIChatClient(string)`? | `dotnet/extensions` → `OpenAIClientExtensions.cs` line 129 | `public static IChatClient AsIChatClient(this ResponsesClient, string?)` in namespace `Microsoft.Extensions.AI`, marked `[Experimental(AIOpenAIResponses)]` |

Confidence climbed to 100%. Revision 4 was promoted to "implementation-ready."

---

## The Bug I Almost Shipped

Then came one of those quiet moments where the discipline of "go check the source" paid for itself.

**Revision 5** added a second pragma to the spec — `MEAI001` alongside `OPENAI001` — under the theory that the `AsIChatClient(ResponsesClient, string?)` bridge "carries the same `AIOpenAIResponses` experimental diagnostic, which surfaces as `MEAI001` from `Microsoft.Extensions.AI.OpenAI`." It sounded plausible. It was wrong.

When the user came back later and asked one more time —

> **User:** "Check the latest updates in the doc and confirm if 100% of it is right. Show me a summary of the most important conclusions."

— I went and read the actual `DiagnosticIds.cs` file in `dotnet/extensions`. There it was on line 67:

```csharp
internal const string AIOpenAIResponses = "OPENAI001";  // (NOT MEAI001)
```

With an explicit comment: _"These diagnostic IDs are defined by the OpenAI package for its experimental APIs. We use the same IDs so consumers do not need to suppress additional diagnostics."_

`MEAI001` is the constant for unrelated MEAI experimental APIs (image generation, speech-to-text, chat reduction). It is **not** emitted by the Responses bridge. Cross-checked against `Microsoft.Agents.AI.Foundry` 1.1.0 source: zero `MEAI001` references anywhere. Microsoft's own code uses a single `OPENAI001` suppression at the same call site.

**Revision 7** corrected the spec, removed `MEAI001` from every code sample, the migration table, TR-5, and Step 2.1, and added a Revision 7 entry in the history that explicitly marked Revision 5 as superseded with a citation to the source. One pragma, one diagnostic, no superstitious extras.

Lesson: a plausible-sounding claim is not a verified claim. The whole point of the research protocol — "go read the actual source file" — exists precisely to catch the things that _sound right_.

---

## What the Spec Now Says

After seven revisions, the migration spec is a tight, surgical document. The change is concentrated in two files plus a couple of package edits:

**Core migration (one factory swap):**
- New: `src/FinWise.McpServer/Infrastructure/AzureAIFoundry/AzureAIFoundryChatClientFactory.cs`
- Old (delete): `src/FinWise.McpServer/Infrastructure/AzureOpenAI/AzureOpenAIChatClientFactory.cs`
- Composition root: `Program.cs` — single line factory call substitution

**The verified call chain:**
```csharp
#pragma warning disable OPENAI001
IChatClient chat = projectClient
    .GetProjectOpenAIClient()
    .GetResponsesClient()
    .AsIChatClient(modelDeploymentName);
#pragma warning restore OPENAI001
```

**Auth:** Service principal (`ClientSecretCredential`) — reuses the `FINWISE_AZURE_TENANT_ID` / `FINWISE_AZURE_CLIENT_ID` / `FINWISE_AZURE_CLIENT_SECRET` triple already used by `StockAgentFactory`. No more API keys.

**Endpoint:** Foundry project endpoint — `https://<resource>.services.ai.azure.com/api/projects/<project>`. The old `https://<resource>.openai.azure.com[/openai/v1]` becomes a documented fallback only.

**New env vars:** `FINWISE_AZURE_AI_FOUNDRY_PROJECT_ENDPOINT` + `FINWISE_AZURE_AI_FOUNDRY_LLM_DEPLOYMENT_NAME`.

**Package changes:**
- Add `Azure.AI.Extensions.OpenAI` 2.0.0 (GA) — for `GetProjectOpenAIClient()`
- Remove unused `Azure.AI.Projects.OpenAI` 2.0.0-beta.1 (preview, was never referenced)
- Remove `Azure.AI.OpenAI` 2.1.0 from McpServer.csproj after old factory is deleted

**Workflow library:** `FinWise.MultiAgentWorkflow` gets **zero changes**. The `IChatClient` seam is preserved, and that's the whole point.

The spec also gained a **Migration: Before vs After** table grounded in the actual current code (14 rows: factory file, class, usings, auth, env vars, endpoint, SDK type, wire protocol, IChatClient bridge, pragmas, packages, call site, workflow impact, failure mode), plus a **Model Compatibility Matrix** that documents which models work via Responses (GPT-4o, GPT-4.1, o-series, MAI-DS-R1, Grok, Llama 3.3 / Llama-4-Maverick, DeepSeek V3/R1, gpt-oss-120b) versus which need the Chat Completions variant (Mistral, Phi, Cohere) versus which are out of scope entirely (Anthropic Claude — needs a separate factory).

**Spec link:** [`specs/012-move-to-foundry-llm-api/012-move-to-foundry-llm-api.md`](../specs/012-move-to-foundry-llm-api/012-move-to-foundry-llm-api.md)

---

## What We Learned

### About the Technology

- **Foundry SDK vs OpenAI SDK is a real choice, not a tomato/tomahto.** Microsoft's official guidance is now explicit: use Foundry SDK (`AIProjectClient`) for _agent_ scenarios; use the OpenAI SDK directly when maximum API compatibility is the priority. FinWise is the first case.
- **`Azure.AI.Inference` is no longer the recommended path** for new Foundry work. The earlier draft of this spec was leaning on it because of older docs; the newer SDK family (`Azure.AI.Projects` + `Azure.AI.Extensions.OpenAI` + `Microsoft.Extensions.AI.OpenAI`) supersedes it for the project-endpoint case.
- **`GetProjectOpenAIClient()` is an extension method, not a property.** This is one of those small details that nobody warns you about until you try to compile. The MAF source uses it; the README in the package shows a property shorthand; both can exist; the extension is the canonical form.
- **`OPENAI001` is the only diagnostic to suppress** at the `AsIChatClient(ResponsesClient, string?)` call site. The OpenAI extensions in `dotnet/extensions` deliberately reuse the OpenAI SDK's diagnostic ID exactly so consumers don't have to manage two pragmas. `MEAI001` belongs to a different family entirely.
- **There's a future one-liner waiting.** When `Microsoft.Agents.AI.Foundry` is upgraded to 1.1.0, McpServer could pass `AIProjectClient` + model name straight into the workflow library and let each agent factory call `aiProjectClient.AsAIAgent(model, instructions, ...)`. That eliminates the manual `IChatClient` plumbing and the pragma. It's a future-evolution note, deliberately out of scope for this migration to keep the change surface tight.

### About the Process

- **Docker Compose's variable precedence rule is a foot-gun.** Host environment variables win over `.env`. If you set anything globally on Windows during an earlier debugging session, your `.env` edits will silently no-op. This earned its own README addition.
- **"Sounds right" is not "is right."** Revision 5 added `MEAI001` based on a plausible-sounding inference. It survived one round of the user asking "are you sure?" because the inference still _sounded_ right. It only got caught when somebody (me, on the next round) actually opened `DiagnosticIds.cs` and read the constant value. The lesson is the lesson the research protocol exists for: don't reason about source, _read_ source.
- **The seam is the architecture.** The reason this migration is small is because `IChatClient` was already the boundary. `FinWise.MultiAgentWorkflow` doesn't know who its LLM provider is and never needs to. Every architectural decision that defended this boundary in earlier sessions is now paying off.
- **Seven revisions on one spec is fine.** Each one closed a real gap. The doc is now ~440 lines, has a verified call chain with line-and-file citations to four official packages, and the implementer can build from it without guessing.

---

## What's Next

Implementation. The spec is implementation-ready. The remaining work is mechanical:

1. **Phase 1** — Update `.env.template` and `docker-compose.finwise.yml` with the new env var names
2. **Phase 2** — Write `AzureAIFoundryChatClientFactory.cs`, swap the call site in `Program.cs`, delete the old factory
3. **Phase 3** — Add `Azure.AI.Extensions.OpenAI` 2.0.0 to central package management; remove the unused beta and the now-unused `Azure.AI.OpenAI`
4. **Phase 4** — Build with zero warnings, run unit tests, run the full Docker stack against the new Foundry project, validate end-to-end
5. **Phase 5** — Update `AGENTS.md`, `README.md`, and architecture spec docs to reflect the new technology

The Coder agent gets a tight Layer-2 context: a verified call chain, explicit `using` directives, the exact pragma to apply, and a Before/After table that shows precisely what changes.

The journey from "we have a Foundry deployment, can it just work?" to "we have a verified, source-grounded migration plan that mirrors what Microsoft ships" took one session and seven revisions. The spec can now be handed off without anyone having to re-derive any of it.

---

_Written: April 19, 2026_
