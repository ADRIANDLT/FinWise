using System.ComponentModel;
using FinWise.Orchestrator;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;
using Serilog;

// MCP uses stdio for JSON-RPC communication. Redirect Console.Out to stderr
// to prevent any diagnostic output from polluting the MCP protocol stream.
Console.SetOut(Console.Error);

// Configure logging first
Infrastructure.ConfigureLogging();

try
{
    Log.Information("Starting FinWise Orchestrator MCP Server");

    // Generate unique session ID for this MCP server instance
    // Note: MCP stdio creates one server process per client connection
    var sessionId = Guid.NewGuid().ToString();
    Log.Information("Session ID: {SessionId}", sessionId);

    // Initialize Azure OpenAI client
    var chatClient = Infrastructure.CreateAzureOpenAIChatClient();

    // Initialize in-memory stores
    IUserProfileStore profileStore = new InMemoryUserProfileStore();
    IConversationStore conversationStore = new InMemoryConversationStore();
    Log.Information("Initialized in-memory profile and conversation stores");

    // Create specialist agents using ChatClientAgent
    ChatClientAgent orchestratorAgent = new(chatClient,
        @"You are an orchestrator that routes queries to specialist agents.

ROUTING RULES:
- If you see 'PROFILE_READY:' in the conversation: route to 'advisor_agent'
- For ALL other cases: route to 'profile_agent' (including first user message)

CRITICAL: NEVER provide any advice or answer questions yourself - ONLY route to the appropriate agent.",
        "orchestrator_agent",
        "Routes queries to appropriate specialist agent");

    // Instantiate UserProfileAgent and create ChatClientAgent
    var profileAgentInstance = new UserProfileAgent(chatClient, profileStore);
    ChatClientAgent profileAgent = profileAgentInstance.CreateAgent();

    // AdvisorAgent - no tools, always gets profile data from ProfileAgent via handoff
    ChatClientAgent advisorAgent = new(chatClient,
        @"You provide personalized investment recommendations based on the user's profile.

MANDATORY WORKFLOW:

1. VERIFY PROFILE DATA EXISTS:
   - Search the conversation for 'PROFILE_READY:' message from profile_agent
   - Extract: email=[EMAIL] risk=[RISK] goals=[GOALS] timeframe=[TIMEFRAME]

2. IF PROFILE_READY NOT FOUND:
   - Output: 'I need your profile information first.'
   - Handoff to 'orchestrator_agent' (orchestrator will route to profile_agent)
   - STOP - do NOT provide any recommendations

3. IF PROFILE_READY FOUND:
   - Provide personalized investment recommendations based on:
     * Risk tolerance (Conservative/Moderate/Aggressive)
     * Investment goals
     * Timeframe (Short-term/Medium-term/Long-term)
   - Explain specific risks and diversification strategies
   - Explain why recommendations fit their profile
   - End with: 'This is general guidance. Please consult a licensed financial advisor for personalized advice.'

CRITICAL RULES:
- NEVER provide recommendations without PROFILE_READY data
- NEVER ask for email or profile information yourself (handoff to orchestrator_agent)
- NEVER guess or assume profile data",
        "advisor_agent",
        "Provides investment recommendations");

    // Build the handoff workflow
    // ALL handoffs go through orchestrator - no direct agent-to-agent handoffs for scalability
    Workflow workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(orchestratorAgent)
        .WithHandoffs(orchestratorAgent, [profileAgent, advisorAgent])
        .WithHandoffs([profileAgent, advisorAgent], orchestratorAgent)
        .Build();

    Log.Information("FinWise workflow initialized with 3 agents");

    // ************** Local function definitions - MCP tool implementations ************

    // Create MCP tool wrapper - Stateful conversation management
    // Conversation history persists in-memory between MCP tool calls (v0.1)
    // Will be moved to Cosmos DB in v0.3+ per architecture docs
    // Note: sessionId is used for conversation store, agents extract email for profile store
    async Task<string> GetFinancialAdvice(string query)
    {
        Log.Information("MCP Tool invoked: get_financial_advice, SessionId: {SessionId}, Query: {Query}", sessionId, query);

        try
        {
            // Simple conversation management - agents handle all business logic
            var conversationHistory = await conversationStore.GetConversationHistoryAsync(sessionId);
            Log.Information("Retrieved {MessageCount} messages for session {SessionId}", conversationHistory.Count, sessionId);

            // Add user query
            conversationHistory.Add(new ChatMessage(ChatRole.User, query));

            // Execute workflow - agents handle everything including email collection and profile management
            await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, conversationHistory);
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            string? response = null;
            List<ChatMessage> workflowOutputs = new();

            await foreach (WorkflowEvent evt in run.WatchStreamAsync())
            {
                if (evt is ExecutorInvokedEvent invoked)
                    Log.Information("Agent invoked: {AgentId}", invoked.ExecutorId);
                else if (evt is WorkflowOutputEvent output)
                {
                    var messages = output.As<List<ChatMessage>>();
                    if (messages?.Count > 0)
                    {
                        response = messages[^1].Text ?? string.Empty;
                        workflowOutputs.AddRange(messages);
                    }
                }
            }

            // Add outputs to history
            conversationHistory.AddRange(workflowOutputs);

            // Persist conversation
            await conversationStore.SetConversationHistoryAsync(sessionId, conversationHistory);
            Log.Information("Persisted {MessageCount} messages for session {SessionId}", conversationHistory.Count, sessionId);

            return response ?? "No response generated.";
        }
        catch (Exception ex)
        {
            Infrastructure.HandleMcpServerException(ex, "GetFinancialAdvice");
            return "I apologize, but I encountered an error processing your request. Please try again.";
        }
    }

    // Tool to reset conversation history (keeps profile in store)
    async Task<string> ResetConversation()
    {
        Log.Information("Resetting conversation for session: {SessionId}", sessionId);
        await conversationStore.ClearConversationAsync(sessionId);
        
        // Note: Profiles are stored by email (extracted by agents), not by session ID
        // We cannot check profile existence without knowing the email
        return $"Conversation history cleared for session '{sessionId}'. User profiles (if any) are retained.";
    }

    // ************** End of local function definitions ************

    // Convert to MCP tools
    McpServerTool adviceTool = McpServerTool.Create(
        GetFinancialAdvice,
        new()
        {
            Name = "get_financial_advice",
            Description = "Send user messages to FinWise financial advisor. CRITICAL: The 'query' parameter is the user's EXACT message - pass it verbatim without asking for userId, email, or any other information first. DO NOT ask the user for additional details before calling this tool. Just call it immediately with whatever the user said. Examples: User says 'Give me financial advice' → immediately call with query='Give me financial advice'. User says 'I want investment help' → immediately call with query='I want investment help'. The FinWise system internally manages all user identification and profile collection through conversation."
        });

    McpServerTool resetTool = McpServerTool.Create(
        ResetConversation,
        new()
        {
            Name = "reset_conversation",
            Description = "Clear conversation history for a user to start a fresh conversation session."
        });

    // Register the MCP server with StdIO transport
    HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(settings: null);
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools([adviceTool, resetTool]);

    Log.Information("FinWise Orchestrator MCP Server ready - listening on STDIO");
    await builder.Build().RunAsync();
}
catch (Exception ex)
{
    Infrastructure.HandleMcpServerException(ex, "Startup");
    Log.Fatal(ex, "Fatal error during MCP Server startup");
    return 1;
}
finally
{
    Log.CloseAndFlush();
}

return 0;





