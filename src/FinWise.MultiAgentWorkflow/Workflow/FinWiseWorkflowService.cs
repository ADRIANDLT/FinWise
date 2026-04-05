using FinWise.MultiAgentWorkflow.Agents.AdvisorAgent;
using FinWise.MultiAgentWorkflow.Agents.OrchestratorAgent;
using FinWise.MultiAgentWorkflow.Agents.UserProfileAgent;
using FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStores;
using FinWise.MultiAgentWorkflow.Session;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Serilog;
using Serilog.Context;
using AgentWorkflow = Microsoft.Agents.AI.Workflows.Workflow;

namespace FinWise.MultiAgentWorkflow.Workflow;

/// <summary>
/// Core multi-agent workflow service for FinWise financial advising.
/// Orchestrates the handoff workflow between orchestrator, profile, and advisor agents.
///
/// This class is transport-agnostic: it receives a plain agentSessionId (the MCP Session ID
/// under 008.A) and returns a <see cref="WorkflowResponse"/>.
///
/// Per Microsoft Agent Framework patterns:
/// - AIAgent instances are stateless; all state is preserved in AgentSession
/// - Workflow manages handoffs between agents
/// - AgentSession maintains conversation state across runs
/// </summary>
public class FinWiseWorkflowService
{
    private readonly IChatClient _chatClient;
    private readonly IUserProfileStore _profileStore;
    private readonly AIAgent? _stockAgent;
    private readonly AgentSessionManager _sessionManager;

    public FinWiseWorkflowService(
        IChatClient chatClient,
        IUserProfileStore profileStore,
        AgentSessionStore sessionStore,
        AIAgent? stockAgent)
    {
        ArgumentNullException.ThrowIfNull(chatClient);
        ArgumentNullException.ThrowIfNull(profileStore);
        ArgumentNullException.ThrowIfNull(sessionStore);

        _chatClient = chatClient;
        _profileStore = profileStore;
        _stockAgent = stockAgent;
        _sessionManager = new AgentSessionManager(sessionStore);
    }

    /// <summary>
    /// Processes a user message through the multi-agent workflow.
    /// Handles session restore, workflow execution, response validation,
    /// session persistence, and post-workflow reset detection.
    /// </summary>
    /// <param name="agentSessionId">
    /// The agent session identifier — the MCP Session ID, used directly as the storage key.
    /// Called <c>conversationId</c> in the SDK's <c>AgentSessionStore</c> — same concept.
    /// </param>
    /// <param name="query">The user's message.</param>
    /// <returns>A <see cref="WorkflowResponse"/> with the agent response, final agentSessionId, and reset flag.</returns>
    public async Task<WorkflowResponse> ProcessMessageAsync(string agentSessionId, string query)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];

        using (LogContext.PushProperty("RequestId", requestId))
        {
            try
            {
                // First create agents with profile-only access (safe default).
                // We'll rebuild with full access after checking message history.
                var (orchestratorAgent, workflow) = CreateAgentsAndWorkflow(agentSessionId, isProfileReady: false);

                // Restore or create AgentSession using Microsoft Agent Framework patterns
                // Messages are stored independently from AgentSession because the SDK's
                // InMemoryChatHistoryProvider service is not reliably restored during deserialization
                var (currentSession, messageHistory) = await _sessionManager.GetOrCreateSessionAsync(orchestratorAgent, agentSessionId);
                Log.Debug("Loaded {Count} messages from session store", messageHistory.Count);

                // Determine if profile is ready — gates access to advisor/stock agents
                bool isProfileReady = AgentSessionConstants.IsProfileReady(messageHistory);
                if (isProfileReady)
                {
                    // Rebuild workflow with full agent access (advisor + stock agents)
                    (orchestratorAgent, workflow) = CreateAgentsAndWorkflow(agentSessionId, isProfileReady: true);
                    // Re-restore session for the new orchestrator agent instance
                    (currentSession, messageHistory) = await _sessionManager.GetOrCreateSessionAsync(orchestratorAgent, agentSessionId);
                }

                Log.Information("======================== REQUEST START ========================");
                Log.Information("ProcessMessage invoked, AgentSessionId: {AgentSessionId}, Query: {Query}", agentSessionId, query);
                Log.Information("Retrieved {MessageCount} messages for session {AgentSessionId}", messageHistory.Count, agentSessionId);

                // Add user query
                messageHistory.Add(new ChatMessage(ChatRole.User, query));

                using var sessionScope = AgentSessionRunContext.Push(
                    new AgentSessionRunSnapshot(agentSessionId, messageHistory));

                // Initialize reset token before workflow execution — tools mutate this shared reference
                var resetToken = SessionResetFlag.Initialize();

                // Execute workflow - per Microsoft Agent Framework workflow patterns
                // Timeout prevents infinite handoff loops (e.g., orchestrator ↔ advisor bouncing)
                using var workflowCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var (response, workflowOutputs, lastRespondingAgent) = await ExecuteWorkflowAsync(workflow, messageHistory, workflowCts.Token);
                AppendUniqueMessages(messageHistory, workflowOutputs);

                // If we got no valid response (only orchestrator talked), that's an error
                if (string.IsNullOrEmpty(response) && workflowOutputs.Count > 0)
                {
                    Log.Error("No valid response from profile_agent or advisor_agent. Orchestrator may have failed to handoff.");
                    response = "I'm having trouble processing your request. Please try again.";
                }

                // Validate: The orchestrator should NEVER produce user-facing text — only tool calls.
                // Any text from the orchestrator is a failed handoff (leaked JSON payload, markdown fence, etc.).
                // Exception: After calling request_session_reset, the orchestrator responds directly —
                // we log this but don't replace it (the reset block below overrides the response anyway).
                if (!string.IsNullOrEmpty(response))
                {
                    var lastOutput = workflowOutputs.LastOrDefault(m => m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text));
                    if (lastOutput?.AuthorName == "orchestrator_agent" || lastRespondingAgent == "orchestrator_agent")
                    {
                        if (resetToken.IsRequested)
                        {
                            Log.Information("Orchestrator emitted reset confirmation text (expected)");
                        }
                        else
                        {
                            Log.Warning("Orchestrator emitted text instead of executing handoff. Response: {Response}",
                                response.Length > 200 ? response[..200] + "..." : response);
                            response = "I'm processing your request. Please try again.";
                        }
                    }
                }

                // Check if the orchestrator's reset tool was called during workflow execution.
                // The token is a mutable reference type — mutations by tools inside the workflow
                // are visible here because AsyncLocal copies references, not objects.
                bool wasReset = resetToken.IsRequested;
                if (wasReset && !isProfileReady)
                {
                    Log.Warning("Ignoring spurious session reset — PROFILE_READY not found in conversation history. {AgentSessionId}", agentSessionId);
                    wasReset = false;
                }

                SessionResetFlag.Clear();
                if (wasReset)
                {
                    // Override any workflow output — the reset is the only thing that matters.
                    // This makes the reset LLM-proof: regardless of what the orchestrator emitted,
                    // the user always sees a consistent reset confirmation.
                    response = "Your session has been reset. Please provide your email address to start a new conversation.";
                    await _sessionManager.ClearSessionAsync(agentSessionId);
                    Log.Information("Session reset via orchestrator tool. Cleared session {AgentSessionId}", agentSessionId);
                }
                else
                {
                    // Only persist if not resetting — reset clears the session
                    await _sessionManager.PersistSessionAsync(agentSessionId, currentSession, orchestratorAgent, messageHistory);

                    string loggedUserId = AgentSessionConstants.ExtractUserIdFromMessageHistory(messageHistory)
                        ?? $"anonymous+{agentSessionId}";
                    Log.Information("Persisted AgentSession with {MessageCount} messages for session {AgentSessionId} (userId: {UserId})",
                        messageHistory.Count, agentSessionId, loggedUserId);
                }

                Log.Information("Request completed successfully");
                return new WorkflowResponse(
                    response ?? "No response generated.",
                    agentSessionId,
                    wasReset);
            }
            catch (OperationCanceledException)
            {
                Log.Warning("Workflow execution timed out for session {AgentSessionId}", agentSessionId);
                return new WorkflowResponse(
                    "The request took too long to process. Please try again or provide your email address to get started.",
                    agentSessionId,
                    WasReset: false);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Request failed");
                return new WorkflowResponse(
                    "I apologize, but I encountered an error processing your request. Please try again.",
                    agentSessionId,
                    WasReset: false);
            }
        }
    }

    /// <summary>
    /// Explicitly resets a session. Clears all state under the same session ID.
    /// The next request with this ID will get a fresh session.
    /// User profiles are retained in the store.
    /// </summary>
    /// <param name="agentSessionId">The agent session to reset.</param>
    public async Task ResetSessionAsync(string agentSessionId)
    {
        Log.Information("Resetting AgentSession for {AgentSessionId}", agentSessionId);
        await _sessionManager.ClearSessionAsync(agentSessionId);
        Log.Information("Session cleared for {AgentSessionId}", agentSessionId);
    }

    /// <summary>
    /// Creates the four-agent handoff workflow.
    /// Strict hub-and-spoke: all agents route exclusively through the orchestrator.
    /// </summary>
    private (AIAgent OrchestratorAgent, AgentWorkflow Workflow) CreateAgentsAndWorkflow(string agentSessionId, bool isProfileReady = false)
    {
        Log.Information("Creating agents for AgentSessionId: {AgentSessionId}", agentSessionId);

        // Create specialist agents using factory pattern (manual DI)
        OrchestratorAgentFactory orchestratorAgtFactory = new(_chatClient);
        ChatClientAgent orchestratorAgent = orchestratorAgtFactory.CreateAgent();

        // ProfileStore is injected but only passed through to the agent factory
        // Profiles are keyed by userId (email address) to enable reuse across sessions
        UserProfileAgentFactory userProfileAgtFactory = new(_chatClient, _profileStore);
        ChatClientAgent profileAgent = userProfileAgtFactory.CreateAgent();

        AdvisorAgentFactory advisorAgtFactory = new(_chatClient);
        ChatClientAgent advisorAgent = advisorAgtFactory.CreateAgent();

        // Build the handoff workflow — strict hub-and-spoke (all agents route through orchestrator)
        // Gate advisor/stock agents behind profile completion to prevent handoff loops.
        // Without PROFILE_READY, the orchestrator can ONLY route to profile_agent.
        // Stock agent is optional — excluded from workflow if not configured.
        AIAgent[] availableAgents = isProfileReady
            ? _stockAgent is not null
                ? [profileAgent, advisorAgent, _stockAgent]
                : [profileAgent, advisorAgent]
            : [profileAgent];

        AgentWorkflow workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(orchestratorAgent)
            .WithHandoffs(orchestratorAgent, availableAgents)
            .WithHandoffs(availableAgents, orchestratorAgent)
            .Build();

        Log.Information("FinWise workflow initialized with {AgentCount} agents for session {AgentSessionId} (ProfileReady: {IsProfileReady})",
            availableAgents.Length + 1, agentSessionId, isProfileReady);

        return (orchestratorAgent, workflow);
    }

    /// <summary>
    /// Executes the agent workflow by streaming events and collecting the response.
    /// Uses InProcessExecution.StreamAsync per Microsoft Agent Framework workflow patterns.
    /// </summary>
    /// <summary>
    /// Maximum number of agent invocations per workflow run.
    /// Prevents infinite handoff loops (e.g., orchestrator ↔ advisor bouncing when no profile exists).
    /// </summary>
    private const int MaxAgentInvocations = 25;

    private static async Task<(string? Response, List<ChatMessage> Outputs, string? LastExecutor)> ExecuteWorkflowAsync(
                                                                                                        AgentWorkflow workflow,
                                                                                                        List<ChatMessage> messageHistory,
                                                                                                        CancellationToken cancellationToken = default)
    {
        await using StreamingRun run = await InProcessExecution.RunStreamingAsync(workflow, messageHistory, cancellationToken: cancellationToken);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        string? response = null;
        string? lastRespondingAgent = null;
        List<ChatMessage> outputs = [];
        int agentInvocationCount = 0;

        await foreach (WorkflowEvent evt in run.WatchStreamAsync().WithCancellation(cancellationToken))
        {
            switch (evt)
            {
                case ExecutorInvokedEvent invoked:
                    agentInvocationCount++;
                    Log.Information("Agent invoked: {AgentId} (invocation {Count}/{Max})",
                        invoked.ExecutorId, agentInvocationCount, MaxAgentInvocations);
                    lastRespondingAgent = invoked.ExecutorId;
                    if (agentInvocationCount >= MaxAgentInvocations)
                    {
                        Log.Warning("Max agent invocations ({Max}) reached — possible handoff loop. Terminating workflow.",
                            MaxAgentInvocations);
                        return (response ?? "I'm having trouble routing your request. Please provide your email address to get started.",
                            outputs, lastRespondingAgent);
                    }
                    break;
                case WorkflowErrorEvent errorEvt:
                    var exception = errorEvt.Data as Exception;
                    Log.Error(exception, "Workflow error occurred");
                    throw exception ?? new InvalidOperationException("Unknown workflow error");
                case ExecutorFailedEvent failedEvt:
                    Log.Error("Executor failed: {ExecutorId} - {Error}", failedEvt.ExecutorId, failedEvt.Data);
                    break;
                case WorkflowOutputEvent output:
                    var messages = output.As<List<ChatMessage>>();
                    if (messages?.Count > 0)
                    {
                        Log.Information("WorkflowOutput received with {Count} messages", messages.Count);

                        foreach (var msg in messages)
                        {
                            Log.Debug("  Message: Role={Role}, Author={Author}, Text={Text}",
                                msg.Role, msg.AuthorName ?? "null",
                                (msg.Text?.Length > 50 ? msg.Text[..50] + "..." : msg.Text) ?? "null");
                        }

                        var lastAssistantText = messages.LastOrDefault(m =>
                            m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text));

                        if (lastAssistantText != null)
                        {
                            var messageText = lastAssistantText.Text ?? string.Empty;
                            var author = lastAssistantText.AuthorName ?? lastRespondingAgent ?? "assistant";

                            Log.Information("Assistant message from {Author}: {Text}", author,
                                messageText.Length > 100 ? messageText[..100] + "..." : messageText);

                            response = messageText;
                        }

                        outputs.AddRange(messages);
                    }
                    break;
            }
        }

        return (response, outputs, lastRespondingAgent);
    }

    /// <summary>
    /// Appends messages from workflow output to message history, skipping duplicates.
    /// Uses role + author + text as the deduplication signature.
    /// </summary>
    internal static void AppendUniqueMessages(List<ChatMessage> messageHistory, List<ChatMessage> newMessages)
    {
        if (newMessages.Count == 0)
        {
            return;
        }

        var existingSignatures = new HashSet<string>(messageHistory.Select(BuildMessageSignature));

        foreach (var message in newMessages)
        {
            var signature = BuildMessageSignature(message);
            if (existingSignatures.Add(signature))
            {
                messageHistory.Add(message);
            }
        }
    }

    /// <summary>
    /// Creates a deduplication signature for a chat message using role, author, and text.
    /// </summary>
    internal static string BuildMessageSignature(ChatMessage message)
    {
        var author = message.AuthorName ?? string.Empty;
        var text = message.Text ?? string.Empty;
        return $"{message.Role}:{author}:{text}";
    }
}
