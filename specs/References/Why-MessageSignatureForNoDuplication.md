# Why We Added Message Signature Deduplication

*A short note for future-you (and teammates) about why `Program.cs` now builds a per-message signature before persisting the workflow log.*

---

## Summary
- The workflow runtime can replay past turns whenever it needs more context.
- When that happens, we were saving the same messages multiple times, so the profile flag (`PROFILE_READY: ...`) ended up buried among duplicate entries and the orchestrator stopped noticing it.
- Without that flag, the orchestrator kept bouncing back to the profile agent and never reached the advisor agent.
- We now hash each message by **role + author + text** before storing it. If we have already seen the same signature, we skip it.

---

## The Actual Bug (What Went Wrong)
1. We append the user's latest request to the shared conversation history.
2. We ask the workflow runtime to run the agents. The runtime streams **all** messages it needed **plus** any new ones it produced this turn.
3. Our old logic assumed all streamed messages were new except the first one. That was wrong: the runtime often replays earlier turns so the model can remember context.
4. Because we re-saved those replays over and over (especially `PROFILE_READY`), the conversation history drifted. In some runs the profile marker got duplicated or removed, confusing the orchestrator and advisor.
5. Eventually our "skip the first N messages" rule skipped *everything* the runtime streamed (because duplicates inflated the count). The brand-new `PROFILE_READY` reply never landed at the end of history, so the orchestrator — which mainly checks the latest assistant turns — stopped seeing the marker and kept handing control back to the profile agent.

---

## Why Use A Message Signature?
- **Order-agnostic**: Even if the runtime sends messages in a different order, the signature is the same, so duplicates are recognized.
- **Simple heuristic**: `Role:Author:Text` is enough to describe what matters about a chat message for our persistence layer.
- **Fast lookup**: A `HashSet<string>` gives us O(1) duplicate checks. That keeps the history maintenance cheap even for long conversations.
- **Keeps data intact**: We store the *exact* message objects the workflow produced — nothing dropped, nothing double-saved.

---

## Alternatives We Considered
| Option | What It Does | Pros | Cons |
|--------|---------------|------|------|
| **Skip by index** (old approach) | Assume everything after `existingCount` is new | Easy to code | Breaks as soon as workflow replays turns or reorders messages |
| **Linear scan per message** | For each streamed message, check `conversationHistory.Any(...)` before adding | No extra data structure; very readable | O(N×M) in worst case; still order-sensitive; can miss duplicates if author names are null |
| **GUID-per-message** | Require the workflow to emit unique IDs we can persist | Bulletproof dedupe | Needs changes across multiple libraries; out of scope right now |
| **Signature hash (current)** | Build `Role:Author:Text` string and dedupe with a HashSet | Works with existing runtime behavior; improved performance | Slightly more code; assumes `Text` is stable (true for the Microsoft agent runtime today) |

---

## Why The Simpler Linear Scan Wasn't Enough
- **Performance**: Conversations can hit hundreds of messages. A nested scan becomes noticeable.
- **Missing author info**: Some events don't populate `AuthorName`. The signature treats `null` as an empty string, so we don't accidentally treat system messages as different just because the author is missing.
- **Re-used text**: The linear scan needed several extra condition checks to stay safe. The hash approach keeps everything in one spot and is easier to reason about.

---

## What To Do If The Runtime Changes Later
1. **If messages start containing IDs**: Switch signatures to `message.Id` immediately — that's the cleanest.
2. **If we notice collisions** (same text, different meaning): extend the signature to include timestamp or metadata.
3. **If the workflow ever returns diffs instead of full messages**: keep this file handy and revisit — the idea will still apply, but the signature formula will have to line up with that new format.

---

## Takeaways (THIS IS VERY IMPORTANT TO UNDERSTAND)
- When you see duplicated state, always ask whether the source might be replaying events.
- Dedupe logic should match the *behavior* of the upstream system, not just the shape of the data.
- Favor small, well-documented safety nets (like a signature hash) over assumptions about execution order.

Now you know why that HashSet lives in `Program.cs`, and what to tweak if our agent runtime evolves again. 🚀
