namespace AIResearchAgents.Core.Agents.AIToolsResearchAgent;

/// <summary>
/// Configuration for the AI Tools Research agent, read from environment variables.
/// </summary>
public sealed record AIToolsResearchAgentConfig
{
    public const string DefaultResearchTopic =
        "What are the most viral and trending AI development tool announcements from the LAST 7 DAYS ONLY? " +
        "I want 5-6 bullet points PER section across TWO sections: " +
        "🔥 Product News & Viral Trends: Specific AI dev tool announcements like " +
        "AI coding assistants (GitHub Copilot, Claude Code, Cursor, Windsurf, Cline, Aider), " +
        "AI code generation and review tools (Codex, Devin, Augment Code, Tabnine), " +
        "AI-powered IDEs and extensions, AI spec/doc generation tools, " +
        "parallel code generation tools (Worktrunk, Gas-Town), " +
        "and any new AI coding CLI or IDE tools that just launched or went viral. " +
        "🔄 Emerging Processes & Workflows: New trends in HOW developers use AI tools like " +
        "AI-assisted code review, AI pair programming paradigms, " +
        "prompt engineering for code generation, AI-driven testing workflows, " +
        "vibe coding, parallel code generation workflows, spec-driven AI development. " +
        "EXCLUSIONS: Do NOT include anything about building agents, multi-agent frameworks, " +
        "multi-agent orchestration, agent orchestration SDKs (Semantic Kernel, LangGraph, AutoGen, CrewAI, Microsoft Agent Framework), " +
        "MCP server development, cloud AI platform services (Azure Foundry, Google Vertex, Amazon Bedrock), " +
        "programming language/runtime updates (.NET, Python, Rust, Go), hardware/chip announcements (NVIDIA GPUs, Apple Silicon, TPUs), " +
        "generic DevOps/CI-CD features that aren't AI-powered (non-AI), or general business or enterprise software. "+
        "For each bullet: name the tool/trend, what happened, and the exact date. Include a source citation for each item. " +
        "Do NOT write an essay. Do NOT include history or background. ONLY this week's highlights.";

    public const string DefaultAgentInstructions =
        "You are a concise trend-scout for AI development tools used by software engineers to write, review, test, and ship code. " +
        "SCOPE: You ONLY report on AI development tools used by software engineers to write, review, test, and ship code. " +
        "If a query is outside this scope, REFUSE to answer and state that it's outside your specialization. Suggest rephrasing to focus on AI dev tools. DO NOT attempt to answer off-topic queries. "+
        "STRICT RULES: " +
        "1) Only report items from the LAST 7 DAYS. Ignore anything older. " +
        "2) DEFAULT OUTPUT: Use TWO sections with headers: '🔥 Product News & Viral Trends' and '🔄 Emerging Processes & Workflows'. " +
        "3) Each section SHOULD have 5-6 bullet points when sufficient relevant items exist (target 5-6 per section). " +
        "4) Section 1 (🔥 Product News): Specific AI dev tool announcements, releases, viral moments — coding assistants, AI IDEs, code generation tools, AI-powered testing/review tools. " +
        "LLM model releases ONLY when directly tied to a specific coding tool announcement (e.g., 'Copilot now uses GPT-5' or 'Cursor upgraded to Claude 3.7'), not standalone model launches (e.g., 'OpenAI releases GPT-5'). " +
        "Each bullet: name, what happened, and date. "+
        "5) Section 2 (🔄 Processes & Workflows): New trends in HOW developers use AI tools — AI pair programming, vibe coding, prompt engineering for code, AI-driven testing, spec-driven AI development, parallel code generation. Each bullet: trend name, what's emerging, and date. " +
        "6) NO introductions, NO conclusions, NO history sections, NO broad industry overviews. " +
        "7) Prioritize what is viral and trending RIGHT NOW, not general long-term trends. " +
        "8) EXCLUSIONS — Do NOT report on: agent frameworks or SDKs (Semantic Kernel, LangGraph, AutoGen, CrewAI, Microsoft Agent Framework), " +
        "multi-agent orchestration, MCP server development, cloud AI platform services (Azure Foundry, Google Vertex, Amazon Bedrock), " +
        "programming languages/runtimes (.NET releases, Python updates, Rust news), hardware/chips (NVIDIA GPUs, Apple Silicon, TPUs), " +
        "generic DevOps/CI-CD that isn't AI-powered, or general business/enterprise software. " +
        "These topics belong to a different specialized agent. " +
        "9) FALLBACK: Each section MUST have at least 3 bullet points. If fewer than 5 strong items exist, broaden your search slightly within the AI dev tools scope to find related items. Only after exhausting all options, note it was a quieter week — but STILL provide at least 3 bullets. NEVER leave a section empty or with just a note. Do NOT invent or pad with old news. " +
        "10) FLEXIBILITY: When the research topic is narrowly focused on a specific tool or trend (e.g., a single product), you MAY combine both sections into a single focused section if the topic doesn't naturally split across both categories. " +
        "11) CITATION FORMAT: Use the search tool's default citation markers for all source references. Do NOT generate or fabricate URLs — real URLs are provided by the search tool's annotation system. " +
        "12) FACTUAL ACCURACY: Only include facts that are directly supported by the search results. Do not infer, extrapolate, or fabricate details beyond what the sources explicitly state. If a search result mentions a tool or trend only briefly, report only what it actually says — do not embellish with assumed features, dates, or capabilities.";

    /// <summary>
    /// The Azure AI Foundry project endpoint URL (e.g., "https://my-project.openai.azure.com").
    /// Read from the PROJECT_ENDPOINT environment variable.
    /// </summary>
    public required string ProjectEndpoint { get; init; }

    /// <summary>
    /// The Azure OpenAI model deployment name (e.g., "gpt-4o").
    /// Read from the MODEL_DEPLOYMENT_NAME environment variable.
    /// </summary>
    public required string ModelDeploymentName { get; init; }

    /// <summary>
    /// The Bing connection name configured in Azure AI Foundry (e.g., "bing-grounded-search").
    /// Read from the BING_CONNECTION_NAME environment variable.
    /// </summary>
    public required string BingConnectionName { get; init; }
    public string ResearchTopic { get; init; } = DefaultResearchTopic;
    public string AgentName { get; init; } = "Agentic-Trends-Researcher";
    public string AgentInstructions { get; init; } = DefaultAgentInstructions;

    /// <summary>
    /// Creates a configuration instance by reading from environment variables.
    /// Looks up PROJECT_ENDPOINT, MODEL_DEPLOYMENT_NAME, and BING_CONNECTION_NAME
    /// from process, machine, and user environment variables in that order.
    /// </summary>
    /// <returns>A new <see cref="AIToolsResearchAgentConfig"/> with values from environment variables.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when any required environment variable is missing or empty.
    /// </exception>
    public static AIToolsResearchAgentConfig FromEnvironment()
        => FromEnvironment(ResolveEnvVar);

    internal static AIToolsResearchAgentConfig FromEnvironment(Func<string, string?> envVarProvider)
    {
        var endpoint = GetRequiredEnvVar("PROJECT_ENDPOINT", envVarProvider);
        var modelName = GetRequiredEnvVar("MODEL_DEPLOYMENT_NAME", envVarProvider);
        var bingConnectionName = GetRequiredEnvVar("BING_CONNECTION_NAME", envVarProvider);

        return new AIToolsResearchAgentConfig
        {
            ProjectEndpoint = endpoint,
            ModelDeploymentName = modelName,
            BingConnectionName = bingConnectionName
        };
    }

    internal static string? ResolveEnvVar(string name)
        => Environment.GetEnvironmentVariable(name)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);

    private static string GetRequiredEnvVar(string name, Func<string, string?> envVarProvider)
    {
        var value = envVarProvider(name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"Required environment variable '{name}' is not set or is empty.");
        }
        return value;
    }
}
