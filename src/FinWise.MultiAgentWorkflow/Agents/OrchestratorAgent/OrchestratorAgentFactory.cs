using Microsoft.Extensions.AI;
using Microsoft.Agents.AI;

namespace FinWise.MultiAgentWorkflow.Agents.OrchestratorAgent;

public class OrchestratorAgentFactory
{
    private readonly IChatClient _chatClient;

    public string Prompt { get; } = LoadPrompt();

    // Prompt loaded from embedded .prompt.md resource for editor-friendly editing,
    // clean git diffs on prompt changes, and separation of prompt engineering from C# code.
    private static string LoadPrompt()
    {
        var assembly = typeof(OrchestratorAgentFactory).Assembly;
        const string resourceName = "FinWise.MultiAgentWorkflow.Agents.OrchestratorAgent.OrchestratorAgent.prompt.md";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public string Name => "orchestrator_agent";
    public string Description => "Silent router - calls handoff functions only, never outputs text";

    public OrchestratorAgentFactory(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public ChatClientAgent CreateAgent()
    {
        return new ChatClientAgent(_chatClient, Prompt, Name, Description);
    }
}