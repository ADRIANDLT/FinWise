using System.Text.RegularExpressions;
using FinWise.MultiAgentWorkflow.Agents.AdvisorAgent;
using FinWise.MultiAgentWorkflow.Agents.OrchestratorAgent;
using FinWise.MultiAgentWorkflow.Agents.UserProfileAgent;
using FinWise.MultiAgentWorkflow.Infrastructure.AgentSessionStore;
using FinWise.MultiAgentWorkflow.Infrastructure.UserProfileStore;
using FinWise.MultiAgentWorkflow.Session;
using Microsoft.Agents.AI;
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
/// This class is transport-agnostic: it receives a plain agentSessionId and returns
/// a <see cref="WorkflowResponse"/>. The MCP/HTTP session mapping stays in the host.
///
/// Per Microsoft Agent Framework patterns:
/// - AIAgent instances are stateless; all state is preserved in AgentSession
/// - Workflow manages handoffs between agents
/// - AgentSession maintains conversation state across runs
/// </summary>
public class FinWiseWorkflowService
{
    /// <summary>
    /// Detects standalone email addresses in user queries for augmentation.
    /// When a user sends only "user@example.com", we augment it to
    /// "My email address is: user@example.com" so the LLM understands the intent.
    /// </summary>
    private static readonly Regex StandaloneEmailPattern = new(
        @"\b[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Z|a-z]{2,}\b",
        RegexOptions.Compiled);

    private readonly IChatClient _chatClient;
    private readonly IUserProfileStore _profileStore;
    private readonly AgentSessionManager _sessionManager;

    public FinWiseWorkflowService(
        IChatClient chatClient,
        IUserProfileStore profileStore,
        IAgentSessionStore sessionStore)
    {
        _chatClient = chatClient ?? throw new ArgumentNullException(nameof(chatClient));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
        ArgumentNullException.ThrowIfNull(sessionStore);

        _sessionManager = new AgentSessionManager(sessionStore);
    }

    /// <summary>
    /// Processes a user message through the multi-agent workflow.
    /// Handles session restore, reset detection, email augmentation, workflow execution,
    /// response validation, and session persistence.
    /// </summary>
    /// <param name="agentSessionId">
    /// The agent session identifier (caller manages the mapping).
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
                var (orchestratorAgent, workflow) = CreateAgentsAndWorkflow(agentSessionId);

                // Restore or create AgentSession using Microsoft Agent Framework patterns
                AgentSession currentSession = await _sessionManager.GetOrCreateSessionAsync(orchestratorAgent, agentSessionId);

                // Get messages from session's message store (per framework pattern)
                var messageStore = currentSession.GetService<InMemoryChatHistoryProvider>();
                Log.Debug("MessageStore from session: {StoreType}, IsNull: {IsNull}",
                    messageStore?.GetType().Name ?? "null", messageStore == null);
                List<ChatMessage> messageHistory = messageStore?.ToList() ?? [];
                Log.Debug("Loaded {Count} messages from messageStore", messageHistory.Count);

                // Session reset detection:
                // User can explicitly request reset with phrases like "re-identify", "my email is...", etc.
                bool wasReset = false;
                if (messageHistory.Count > 0 && AgentSessionResetEvaluator.ShouldResetSession(messageHistory, query))
                {
                    var previousAgentSessionId = agentSessionId;
                    Log.Information(
                        "Explicit reset requested via query '{Query}'. Previous session {PreviousAgentSessionId} had {MessageCount} messages.",
                        query,
                        previousAgentSessionId,
                        messageHistory.Count);

                    await _sessionManager.ClearSessionAsync(previousAgentSessionId);

                    agentSessionId = Guid.NewGuid().ToString();
                    wasReset = true;

                    (orchestratorAgent, workflow) = CreateAgentsAndWorkflow(agentSessionId);
                    currentSession = await _sessionManager.GetOrCreateSessionAsync(orchestratorAgent, agentSessionId);

                    messageStore = currentSession.GetService<InMemoryChatHistoryProvider>();
                    messageHistory = [];
                    messageStore?.Clear();

                    Log.Information(
                        "Mapped to new session {AgentSessionId} after explicit reset (previous {PreviousAgentSessionId}).",
                        agentSessionId,
                        previousAgentSessionId);
                }

                Log.Information("======================== REQUEST START ========================");
                Log.Information("ProcessMessage invoked, AgentSessionId: {AgentSessionId}, Query: {Query}", agentSessionId, query);
                Log.Information("Retrieved {MessageCount} messages for session {AgentSessionId}", messageHistory.Count, agentSessionId);

                // Extract email from query if present and augment message for better LLM understanding
                var emailMatch = StandaloneEmailPattern.Match(query);
                string userMessage = query;
                if (emailMatch.Success && query.Trim() == emailMatch.Value)
                {
                    // User provided ONLY an email address - augment it so LLM understands context
                    userMessage = $"My email address is: {emailMatch.Value}";
                    Log.Information("Detected standalone email in query. Augmented to: {AugmentedMessage}", userMessage);
                }

                // Add user query
                messageHistory.Add(new ChatMessage(ChatRole.User, userMessage));

                using var sessionScope = AgentSessionRunContext.Push(
                    new AgentSessionRunSnapshot(agentSessionId, messageHistory));

                // Execute workflow - per Microsoft Agent Framework workflow patterns
                var (response, workflowOutputs, lastRespondingAgent) = await ExecuteWorkflowAsync(workflow, messageHistory);
                AppendUniqueMessages(messageHistory, workflowOutputs);

                // If we got no valid response (only orchestrator talked), that's an error
                if (string.IsNullOrEmpty(response) && workflowOutputs.Count > 0)
                {
                    Log.Error("No valid response from profile_agent or advisor_agent. Orchestrator may have failed to handoff.");
                    response = "I'm having trouble processing your request. Please try again.";
                }

                // Validate: The orchestrator should NEVER produce user-facing text — only tool calls.
                // Any text from the orchestrator is a failed handoff (leaked JSON payload, markdown fence, etc.).
                if (!string.IsNullOrEmpty(response))
                {
                    var lastOutput = workflowOutputs.LastOrDefault(m => m.Role == ChatRole.Assistant && !string.IsNullOrWhiteSpace(m.Text));
                    if (lastOutput?.AuthorName == "orchestrator_agent" || lastRespondingAgent == "orchestrator_agent")
                    {
                        Log.Warning("Orchestrator emitted text instead of executing handoff. Response: {Response}",
                            response.Length > 200 ? response[..200] + "..." : response);
                        response = "I'm processing your request. Please try again.";
                    }
                }

                // Persist session using Microsoft Agent Framework patterns
                string persistedUserId = AgentSessionConstants.ExtractUserIdFromMessageHistory(messageHistory)
                    ?? $"anonymous+{Guid.NewGuid():N}";

                // Update the message store in the session before serialization
                if (currentSession.GetService<InMemoryChatHistoryProvider>() is InMemoryChatHistoryProvider store)
                {
                    store.Clear();
                    foreach (var msg in messageHistory)
                    {
                        store.Add(msg);
                    }
                }

                await _sessionManager.PersistSessionAsync(agentSessionId, currentSession, orchestratorAgent, persistedUserId, messageHistory.Count);

                Log.Information("Persisted AgentSession with {MessageCount} messages for session {AgentSessionId} (userId: {UserId})",
                    messageHistory.Count, agentSessionId, persistedUserId);

                Log.Information("Request completed successfully");
                return new WorkflowResponse(
                    response ?? "No response generated.",
                    agentSessionId,
                    wasReset);
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
    /// Explicitly resets a session. Clears all state and returns a new agentSessionId.
    /// User profiles are retained in the store.
    /// </summary>
    /// <param name="agentSessionId">The agent session to reset.</param>
    /// <returns>A new agentSessionId for the fresh session.</returns>
    public async Task<string> ResetSessionAsync(string agentSessionId)
    {
        Log.Information("Resetting AgentSession for {AgentSessionId}", agentSessionId);
        await _sessionManager.ClearSessionAsync(agentSessionId);

        var newAgentSessionId = Guid.NewGuid().ToString();
        Log.Information("New session {AgentSessionId} created after reset (previous {PreviousAgentSessionId})",
            newAgentSessionId, agentSessionId);

        return newAgentSessionId;
    }

    /// <summary>
    /// Creates the three-agent handoff workflow.
    /// All handoffs go through orchestrator — no direct agent-to-agent handoffs for scalability.
    /// </summary>
    private (AIAgent OrchestratorAgent, AgentWorkflow Workflow) CreateAgentsAndWorkflow(string agentSessionId)
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

        // Build the handoff workflow
        AgentWorkflow workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(orchestratorAgent)
            .WithHandoffs(orchestratorAgent, [profileAgent, advisorAgent])
            .WithHandoffs([profileAgent, advisorAgent], orchestratorAgent)
            .Build();

        Log.Information("FinWise workflow initialized with 3 agents for session {AgentSessionId}", agentSessionId);

        return (orchestratorAgent, workflow);
    }

    /// <summary>
    /// Executes the agent workflow by streaming events and collecting the response.
    /// Uses InProcessExecution.StreamAsync per Microsoft Agent Framework workflow patterns.
    /// </summary>
    private static async Task<(string? Response, List<ChatMessage> Outputs, string? LastExecutor)> ExecuteWorkflowAsync(
        AgentWorkflow workflow, List<ChatMessage> messageHistory)
    {
        await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, messageHistory);
        await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

        string? response = null;
        string? lastRespondingAgent = null;
        List<ChatMessage> outputs = [];

        await foreach (WorkflowEvent evt in run.WatchStreamAsync())
        {
            switch (evt)
            {
                case ExecutorInvokedEvent invoked:
                    Log.Information("Agent invoked: {AgentId}", invoked.ExecutorId);
                    lastRespondingAgent = invoked.ExecutorId;
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
