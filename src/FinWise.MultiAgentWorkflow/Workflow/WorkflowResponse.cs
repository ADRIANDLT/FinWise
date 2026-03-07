namespace FinWise.MultiAgentWorkflow.Workflow;

/// <summary>
/// Result of processing a user message through the multi-agent workflow.
/// </summary>
/// <param name="Response">The agent's text response to the user.</param>
/// <param name="AgentSessionId">
/// The active agent session identifier after processing.
/// Called <c>conversationId</c> in the SDK's <c>AgentSessionStore</c> — same concept.
/// May differ from the input ID if a reset occurred (see <paramref name="WasReset"/>).
/// </param>
/// <param name="WasReset">Whether the session was reset during this request.</param>
public record WorkflowResponse(string Response, string AgentSessionId, bool WasReset);
