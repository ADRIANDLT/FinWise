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

    // Initialize Azure OpenAI client
    var chatClient = Infrastructure.CreateAzureOpenAIChatClient();

    // Initialize in-memory profile store
    var profileStore = new Dictionary<string, UserProfileDto>();
    Log.Information("Initialized in-memory profile store");

    // Create specialist agents using ChatClientAgent
    ChatClientAgent orchestratorAgent = new(chatClient,
        @"You are an orchestrator agent that analyzes user queries and determines which specialist agent should handle them.
Route to 'profile_agent' for queries about user profile, risk tolerance, goals, or timeframe.
Route to 'advisor_agent' for queries about investment advice, stocks, real estate, or recommendations.
Always explain your routing decision briefly.",
        "orchestrator_agent",
        "Routes queries to appropriate specialist agent");

    ChatClientAgent profileAgent = new(chatClient,
        @"You are a user profile agent that collects investment preferences through natural conversation.
Ask ONE question at a time: (1) risk tolerance (Conservative/Moderate/Aggressive), (2) investment goals, (3) timeframe (Short/Medium/Long-term).
Wait for user response before asking the next question. Be conversational and friendly.
After collecting all three, summarize the profile.",
        "profile_agent",
        "Specialist agent for user profile management");

    ChatClientAgent advisorAgent = new(chatClient,
        @"You are an investment advisor agent that provides personalized recommendations.
Consider the user's risk tolerance, investment goals, and timeframe from their profile.
Provide specific, actionable recommendations with clear reasoning.
Explain risks and suggest diversification strategies.
Remind users this is general guidance, not personalized financial advice from a licensed advisor.",
        "advisor_agent",
        "Specialist agent for investment recommendations");

    // Build the handoff workflow
    Workflow workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(orchestratorAgent)
        .WithHandoffs(orchestratorAgent, [profileAgent, advisorAgent])
        .WithHandoffs([profileAgent, advisorAgent], orchestratorAgent)
        .Build();

    Log.Information("FinWise workflow initialized with 3 agents");

    // Create MCP tool wrapper
    async Task<string> GetFinancialAdvice(string query, string userId)
    {
        Log.Information("MCP Tool invoked: get_financial_advice, User: {UserId}, Query: {Query}", userId, query);

        try
        {
            List<ChatMessage> messages = [new ChatMessage(ChatRole.User, query)];
            await using StreamingRun run = await InProcessExecution.StreamAsync(workflow, messages);
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            string? lastAgentId = null;
            string? response = null;

            await foreach (WorkflowEvent evt in run.WatchStreamAsync())
            {
                switch (evt)
                {
                    case ExecutorInvokedEvent invoked:
                        lastAgentId = invoked.ExecutorId;
                        Log.Information("Agent invoked: {AgentId}", lastAgentId);
                        break;
                    case WorkflowOutputEvent output:
                        var outputMessages = output.As<List<ChatMessage>>();
                        if (outputMessages?.Count > 0)
                        {
                            response = outputMessages[^1].Text ?? string.Empty;
                        }
                        break;
                }
            }

            Log.Information("MCP Tool completed successfully");
            return response ?? "No response generated.";
        }
        catch (Exception ex)
        {
            Infrastructure.HandleMcpServerException(ex, "GetFinancialAdvice");
            return "I apologize, but I encountered an error processing your request. Please try again.";
        }
    }

    // Convert to MCP tool
    McpServerTool tool = McpServerTool.Create(
        GetFinancialAdvice,
        new()
        {
            Name = "get_financial_advice",
            Description = "Get personalized financial advice from FinWise multi-agent system. Handles user profile setup and investment recommendations based on risk tolerance, goals, and timeframe."
        });

    // Register the MCP server with StdIO transport
    HostApplicationBuilder builder = Host.CreateEmptyApplicationBuilder(settings: null);
    builder.Services
        .AddMcpServer()
        .WithStdioServerTransport()
        .WithTools([tool]);

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
