Capstone Project Full Proposal: 
Title - FinWise: A Multi-Agent Investment Assistant for Smarter Financial Decisions

My idea for my project is to create a multi-agent workflow that is specialized in investing. 
This assistant would help any user who desires any sort of tips or assistance on where 
to start investing, where they shouldn’t invest, and also try to predict in a way when to 
remove or invest your funds in different areas. The goal is to make something that feels like 
having a personal investment guide who can break things down simply for beginners, while 
also giving more advanced insights for users who already understand financial markets. 

The agentic workflow would basically work by combining the user's profile with investment alternatives (real state, stock, etc.), fundamental investment strategies and concrete stock values and real-time market data to identify patterns, risks, and opportunities. It wouldn’t just give random suggestions; the idea is that it constantly learns from the data it receives and adjusts its advice accordingly. It would use historical data to see how markets behaved in the past 
and then compare that to live information to try to spot whether something looks like a good 
moment to invest or a moment to be cautious. Initially this will apply to stock market and real-estate in the US, but open for the future to potential additional especialized agents for ETFs, crypto, commodities, etc.
I want to keep this project as realistic as possible, so I don't think that stock trading and crypto trading should be in the scope, but more focus on long-term investments such as fundamentals in stock values.

Another part of the system would be focused on risk management (Another specialized agent for the last versions). 
The system shouldn’t just tell users where to put their money, but also be able to identify when 
something seems too high of a risk for a certain profile, or when diversification is needed. I 
want the assistant to be able to adapt its recommendations based on what type of investor the 
user says they are. For example, a conservative investor shouldn’t get the same suggestions 
as someone who’s more open to risk. This adds more personalization and makes the tool 
more user focused. 

I also want to explore adding sentiment analysis, which basically means that each specialized agent would 
look at news headlines, social media, or market sentiment reports (probably thorugh an MCP server for context, such as Perplexity or any other like Bing/Google) to understand the overall mood around a certain investment. This could give the user a more complete picture beyond just numbers. 

Another important feature (in later versions) will be performing real live trades or stock purchases through additional MCP servers from the market. This will help me evaluate if the agent is improving and what 
parts need adjustment. 

Technical approaches:

For building this, the best place/tool for me to work on the project is through the Azure OpenAI 
Service found in Microsoft Azure Foundry. This gives me access to powerful AI models, 
vector storage, API integration, and a secure environment to run everything. It should make 
it easier to connect data sources, test different models, and deploy the final version of the 
project. However, for easy LLM tasks I could also use local SLM such as Meta Llama.

The architecture and implementation will be based on a multi-agents workflow with multiple 
specialized agents each one specialized on a different business matter or different actions 
and coordinated and orchestrated by another agent. In addition I will provide context and 
content to the agents through MCP servers. Regarding specific technologies I will use 
agentic frameworks (such as Microsoft Agent Framework), local execution based on Docker, 
and for scalability, a deployment version based on Azure services (Azure Container Apps) and Agents and LLM 
models in Azure AI Foundry as I mentioned before. 

Conclusion:

Overall, my goal is to build a practical tool that shows how AI can assist with investing in a 
helpful and responsible way. 

Preliminary Literature Review Section 

References and Background: 
This project is primarily grounded in modern agentic AI design principles and focuses on the 
practical construction of multi-agent systems rather than purely theoretical financial 
modeling. The overall structure of the proposed investment assistant is heavily influenced by 
Building Applications with AI Agents by Dr. Manish Gupta, which presents a clear and 
applied framework for designing agent-based systems that decompose complex problems 
into smaller, specialized components (Gupta, 2025). Key concepts from this work—such as 
defining explicit agent roles, separating planning from execution, incorporating memory and 
context, and orchestrating agent interactions—directly inform the design of the proposed 
multi-agent workflow. In particular, the idea of coordinating specialized agents through an 
orchestrator aligns closely with the intended system architecture, where data ingestion, risk 
evaluation, sentiment analysis, and recommendation generation are handled by distinct 
agents that collaborate toward a shared goal. 

From a technical implementation standpoint, the Microsoft Agent Framework serves as the 
main reference for turning these agentic design principles into a working system. The 
framework provides abstractions for agent orchestration, inter-agent communication, and 
tool invocation, allowing the system to scale beyond a single monolithic model into a 
coordinated workflow of multiple agents (Microsoft, 2024a). Its emphasis on modularity and 
extensibility supports iterative development, making it possible to add, modify, or replace 
agents as the project evolves. This is particularly important for an investment-oriented 
system, where different agents may need to reason over different types of data, apply 
different logic, or adapt to changing user profiles and market conditions. 

In addition, Microsoft Azure AI Foundry provides the cloud-based infrastructure and AI 
services required to deploy, evaluate, and validate the multi-agent workflow in a realistic 
environment. Azure AI Foundry enables integration with large language models, 
vector-based memory, external APIs, and monitoring tools, all of which are essential for 
building a production-like agentic system (Microsoft, 2024b). The platform also supports both 
local development and scalable cloud deployment, which aligns with the project’s goal of 
maintaining a realistic development pipeline while allowing experimentation and testing. By 
leveraging Azure AI Foundry alongside the Microsoft Agent Framework, this project bridges 
conceptual agent design with practical deployment considerations, ensuring that the final 
system is not only technically sound but also representative of real-world AI application 
development. 

Also, to ground the project in real investing practice rather than purely academic models, 
Ganar en la bolsa es posible – El Método Ajram by Josef Ajram offers a practical investor’s 
perspective on market behavior and techniques for approaching trading through observation, 
perseverance, and disciplined methods (Ajram, 2011) — especially useful as a contrast to 
AI-based strategies. While Ajram’s book focuses on trading methodology and psychological 
readiness, it reinforces the importance of designing the AI agent to also account for risk 
tolerance, user profile differences, and market rhythms, not just predictive signals, mirroring 
the way human investors structure their decision processes in real life (Ajram, 2011). 

Bibliography: 
Ajram, J. (2011). Ganar en la bolsa es posible – El Método Ajram. Plataforma Editorial. 
Gupta, M. (2025). Book: “Building Applications with AI Agents”. O’Reilly Media. 
Microsoft. (2024a). Microsoft Agent Framework Documentation. Microsoft Learn. 
Microsoft. (2024b). Azure AI Foundry Documentation. Microsoft Learn. 
Initial Execution Plan & Calendar (subject to change): 

Topics to define:

● Decide which markets tol start with (Foundation stock market investment and Real Estate)
● Define in written docs each agent’s role in the multi-agent workflow (e.g., Orchestrator Agent, Global Investment Advisor Agent, Fundamentals Stock values advisor Agent, Real Estate investments agent, Risk Agent, Sotck purchase agent. 
● Identify what data sources will be used for historical and real-time data. 
● Plan how MCP servers will deliver context and content to the agents. 
● Set up Azure AI Foundry workspace + Docker environment for local testing. 


● Research academic and industry papers on: 
○ Agentic frameworks 
○ Multi-agent workflows in finance 
○ Risk modeling and investor profiling 
○ Sentiment analysis for markets 

● Write literature review connecting these concepts to my multi-agent design. 
● Define the system architecture in writing: 
○ How each agent communicates 
○ What the orchestrator agent controls 
○ How MCP servers provide context 

Tasks: 
● Detail the methodology and architecture for: 
○ Data collection and preprocessing 
○ Agent-to-agent communication 
○ Risk assessment logic 
○ Sentiment analysis pipeline 

● Build the first version of the agent workflow: 
○ Implement 2–3 core agents to start 
○ Connect MCP servers to feed external context 

● Build the first demo (even simple): 
○ Workflow runs in local laptop 
○ Orchestrator calls different agents 
○ Agents produce basic investment suggestions 
○ Workflow runs in Docker (later version)

● Integrate Azure-hosted LLMs and test cloud deployment. 
● Write the Results + Evaluation + Discussion sections. 
