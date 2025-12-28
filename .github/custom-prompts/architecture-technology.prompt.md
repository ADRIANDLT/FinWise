---
agent: 'agent'
tools: ['perplexity-ask/perplexity_research', 'perplexity-ask/perplexity_reason', 'perplexity-ask/perplexity_ask', 'context7/*']
description: 'Research an approach to the architecture and technology stack for the project idea.'
---

Perform an indepth analysis of the provided functional specs/vision/scope, simplified directions on architecture and stack/technologies provided in the docs as part of the context of the prompt.

The goal is to research and provide a simplified and high-level document with multiple alternatives for the Agentic solution architecture and related stack and technologies.

Rules:
- Clarify any details that might be helpful before starting to research my idea.
- The document should not be very long, maximum of around 5 pages, but should be detailed enough to provide a good overview of the architecture and technology stack alternatives.
- Use the provided simplified directions as the main context for your research.
- Start your session co-reasoning with me by doing some research using the #perplexity-ask/perplexity_research. 
- Summarize your findings that might be relevant to me before beginning the next step.
- Perform another research loop if asked.

Main TOC or sections:

- Introduction & Objectives
(Introduction & Objectives of this architecture document. Do not get into business requirements or vision/scope since that's defined in another doc)

High-Level Architecture
(Main components, interactions, and system overview)

Technologies, stack & Tools
(Chosen tech stack, frameworks, cloud services, libraries)
For researching technologies, also use the #context7/* tools to get relevant documentation.

Include the following pivots in your research:

- Multiple architecture variants:
    - 1. For initial Application's v0.1 (Simple MCP servers using MCP STDIO with agents running as processes)
    - 2. For Application's v0.2, include an evolved architecture using MCP HTTP towards Dockerized agents and MCP servers using docker-compose.
    - 3. For a cloud version (Application's v.03), supporting deployment into Azure Container Apps.
    - 4. For a scalable version (Application's v.05), supporting deployment into Kubernetes clusters in AKS

- Technology stack (incremental evolution per architecture variant):
  Validate/Invalidate or improve the below proposed technology stack per each architecture variant related to the incremental evolution of the versions of the Application:
    - 1. For initial Application's v0.1 (.NET/C#, local MCP STDIO protocol with MCP SDK for .NET, Microsoft Agent Framework, Azure Foundry OpenAI Service)
    - 2. For Application's v0.2 as local decoupled solution with maybe an additional decoupled agent in Python, using remote MCP HTTP protocol (instead of MCP STDIO), and Docker
    - 3. For cloud version (Application's v.03), using Azure Container Apps
    - 4. For scalable version (Application's v.05), supporting deployment into Kubernetes clusters in AKS

- Differentiators per each technology alternative

WHEN DONE, output to #file:../../specs/03-architecture-and-technologies.md