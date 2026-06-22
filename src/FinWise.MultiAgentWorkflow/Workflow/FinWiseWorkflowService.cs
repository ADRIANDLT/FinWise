using FinWise.MultiAgentWorkflow.Agents.AdvisorAgent;
using FinWise.MultiAgentWorkflow.Agents.OrchestratorAgent;
using FinWise.MultiAgentWorkflow.Agents.UserProfileAgent;
using FinWise.MultiAgentWorkflow.DomainModel;
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
    /// <summary>AuthorName tag for the ephemeral per-turn profile-context message that must never be persisted.</summary>
    internal const string ProfileContextAuthorName = "profile_context";

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
                var orchestratorAgent = CreateOrchestratorAgent();

                // Restore or create AgentSession using Microsoft Agent Framework patterns
                // Messages are stored independently from AgentSession because the SDK's
                // InMemoryChatHistoryProvider service is not reliably restored during deserialization
                var (currentSession, messageHistory) = await _sessionManager.GetOrCreateSessionAsync(orchestratorAgent, agentSessionId);
                Log.Debug("Loaded {Count} messages from session store", messageHistory.Count);

                // Structured profile-ready state (replaces the PROFILE_READY chat-history marker scan)
                var profileState = _sessionManager.GetProfileSessionState(currentSession);
                bool isProfileReady = profileState.ProfileReady;

                // Fallback: migrate legacy sessions that only have the old text marker, no structured state
                if (!isProfileReady && AgentSessionConstants.IsProfileReady(messageHistory))
                {
                    isProfileReady = true;
                    profileState.ProfileReady = true;
                    profileState.UserId ??= AgentSessionConstants.ExtractUserIdFromMessageHistory(messageHistory);
                    Log.Information("Migrated legacy PROFILE_READY marker to structured session state for {AgentSessionId}", agentSessionId);
                }

                var workflow = BuildWorkflow(orchestratorAgent, isProfileReady);

                Log.Information("======================== REQUEST START ========================");
                Log.Information("ProcessMessage invoked, AgentSessionId: {AgentSessionId}, Query: {Query}", agentSessionId, query);
                Log.Information("Retrieved {MessageCount} messages for session {AgentSessionId}", messageHistory.Count, agentSessionId);

                // Add user query
                messageHistory.Add(new ChatMessage(ChatRole.User, query));

                // Deliver the structured profile DATA to downstream agents (Option C).
                // This replaces the old PROFILE_READY chat-history TEXT MARKER as the data channel:
                // the profile is re-loaded fresh from the store on EVERY turn and injected as an
                // EPHEMERAL, per-turn context message that is NOT persisted to messageHistory. Being
                // ephemeral keeps it always reflecting the latest stored profile, and because it rides
                // in the message history it reaches BOTH the local advisor_agent and the remote
                // Azure-AI-Foundry stock agent (whose instructions cannot be injected locally).
                List<ChatMessage> executionMessages = messageHistory;
                if (isProfileReady && !string.IsNullOrWhiteSpace(profileState.UserId))
                {
                    UserProfile? profile = await _profileStore.GetProfileAsync(profileState.UserId);
                    if (profile is not null)
                    {
                        var profileContextMessage = new ChatMessage(ChatRole.System, FormatProfileContext(profile))
                        {
                            AuthorName = ProfileContextAuthorName
                        };
                        executionMessages = [profileContextMessage, .. messageHistory];
                        Log.Information("Injected authoritative profile context for downstream agents (userId: {UserId})", profileState.UserId);
                    }
                }

                using var sessionScope = AgentSessionRunContext.Push(
                    new AgentSessionRunSnapshot(agentSessionId, messageHistory));

                // Initialize reset token before workflow execution — tools mutate this shared reference
                var resetToken = SessionResetFlag.Initialize();

                // Initialize profile-ready token before workflow execution — profile tools mutate this
                // shared reference; seeded with current readiness so it stays ready on later turns
                var profileToken = ProfileReadyFlag.Initialize(isProfileReady, profileState.UserId);

                // Execute workflow - per Microsoft Agent Framework workflow patterns
                // Timeout prevents infinite handoff loops (e.g., orchestrator ↔ advisor bouncing)
                using var workflowCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var (response, workflowOutputs, lastRespondingAgent) = await ExecuteWorkflowAsync(workflow, executionMessages, workflowCts.Token);
                // The workflow echoes input messages back in its outputs; never persist the ephemeral
                // profile-context message (it carries PII and is re-injected fresh each turn).
                AppendUniqueMessages(messageHistory, workflowOutputs.Where(m => !IsEphemeralProfileContext(m)).ToList());

                // If we got no valid response, surface a retryable message.
                // This covers two failure modes:
                //  - Outputs were produced but only by the orchestrator (failed handoff).
                //  - No outputs at all — the orchestrator silently skipped a handoff,
                //    typically when the LLM judges the user's question was already answered
                //    in a prior turn. The user-facing message must look transient so callers
                //    (including E2E test retry logic) treat it as recoverable instead of fatal.
                if (string.IsNullOrEmpty(response))
                {
                    Log.Error("No valid response from profile_agent or advisor_agent (workflow outputs: {Count}). Orchestrator may have failed to handoff.", workflowOutputs.Count);
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
                    Log.Warning("Ignoring spurious session reset — profile not ready in structured session state. {AgentSessionId}", agentSessionId);
                    wasReset = false;
                }

                // Merge the ambient profile-ready flag back into the structured session state.
                // Profile tools mutate the shared token during the run; persist their signal here.
                if (profileToken.IsReady)
                {
                    profileState.ProfileReady = true;
                    if (!string.IsNullOrWhiteSpace(profileToken.UserId))
                    {
                        profileState.UserId = profileToken.UserId;
                    }
                }

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
                    _sessionManager.SetProfileSessionState(currentSession, profileState);
                    await _sessionManager.PersistSessionAsync(agentSessionId, currentSession, orchestratorAgent, messageHistory);

                    string loggedUserId = profileState.UserId ?? $"anonymous+{agentSessionId}";
                    Log.Information("Persisted AgentSession with {MessageCount} messages for session {AgentSessionId} (userId: {UserId})",
                        messageHistory.Count, agentSessionId, loggedUserId);
                }

                Log.Information("Request completed successfully");
                return new WorkflowResponse(
                    response!,
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
            finally
            {
                // Always clear the request-scoped ambient tokens, even on the exception/timeout
                // paths. AsyncLocal mutations don't flow back to the caller, but clearing here
                // guarantees cleanup regardless of how this request exits (success, early return,
                // or exception) and keeps the cleanup robust against future control-flow changes.
                SessionResetFlag.Clear();
                ProfileReadyFlag.Clear();
            }
        }
    }

    /// <summary>
    /// Explicitly resets a session. Clears all state under the same session ID.
    /// The next request with this ID will get a fresh session.
    /// User profiles are retained in the store.
    /// </summary>
    /// <param name="agentSessionId">The agent session to reset.</param>
    /// <returns><c>true</c> if the session was actually cleared by the store;
    /// <c>false</c> if the store does not support clearing (e.g., in-memory).</returns>
    public async Task<bool> ResetSessionAsync(string agentSessionId)
    {
        Log.Information("Resetting AgentSession for {AgentSessionId}", agentSessionId);
        bool cleared = await _sessionManager.ClearSessionAsync(agentSessionId);
        Log.Information("Session reset for {AgentSessionId} (cleared: {Cleared})", agentSessionId, cleared);
        return cleared;
    }

    /// <summary>
    /// Creates the orchestrator agent (the strict hub-and-spoke router).
    /// Its Id is stable via the factory, keeping the session storage key unchanged.
    /// </summary>
    private ChatClientAgent CreateOrchestratorAgent()
    {
        Log.Information("Creating orchestrator agent");

        OrchestratorAgentFactory orchestratorAgtFactory = new(_chatClient);
        return orchestratorAgtFactory.CreateAgent();
    }

    /// <summary>
    /// Builds the handoff workflow around the given orchestrator agent.
    /// Strict hub-and-spoke: all agents route exclusively through the orchestrator.
    /// Advisor/stock agents are gated behind profile completion to prevent handoff loops.
    /// </summary>
    private AgentWorkflow BuildWorkflow(AIAgent orchestratorAgent, bool isProfileReady)
    {
        // ProfileStore is injected but only passed through to the agent factory
        // Profiles are keyed by userId (email address) to enable reuse across sessions
        UserProfileAgentFactory userProfileAgtFactory = new(_chatClient, _profileStore);
        ChatClientAgent profileAgent = userProfileAgtFactory.CreateAgent();

        AdvisorAgentFactory advisorAgtFactory = new(_chatClient);
        ChatClientAgent advisorAgent = advisorAgtFactory.CreateAgent();

        // Build the handoff workflow — strict hub-and-spoke (all agents route through orchestrator)
        // Gate advisor/stock agents behind profile completion to prevent handoff loops.
        // Without profile readiness, the orchestrator can ONLY route to profile_agent.
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

        Log.Information("FinWise workflow initialized with {AgentCount} agents (ProfileReady: {IsProfileReady})",
            availableAgents.Length + 1, isProfileReady);

        Log.Debug("Workflow Mermaid visualization:\n{MermaidDiagram}", workflow.ToMermaidString());

        return workflow;
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

    /// <summary>
    /// Formats a user profile into an authoritative, human-readable context block for downstream agents.
    /// Injected as an ephemeral per-turn message so the advisor and remote stock agent always see the
    /// latest stored profile DATA without re-prompting the user.
    /// </summary>
    /// <param name="profile">The user profile loaded from the profile store.</param>
    /// <returns>A readable profile-context block.</returns>
    internal static string FormatProfileContext(UserProfile profile) =>
        $"""
        CURRENT USER PROFILE (authoritative — loaded from the profile store; use these values, do not ask for them again):
        - Email: {profile.UserId}
        - Risk tolerance: {profile.RiskTolerance ?? "(not specified)"}
        - Investment goals: {profile.InvestmentGoals ?? "(not specified)"}
        - Investment timeframe: {profile.InvestmentTimeframe ?? "(not specified)"}
        """;

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

    /// <summary>True for the ephemeral profile-context message injected per turn — these must be excluded from persisted history.</summary>
    internal static bool IsEphemeralProfileContext(ChatMessage message) =>
        string.Equals(message.AuthorName, ProfileContextAuthorName, StringComparison.Ordinal);

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
