---
agent: agent
tools:
  - perplexity-ask/perplexity_research
  - perplexity-ask/perplexity_reason
  - perplexity-ask/perplexity_ask
  - context7/*
description: Research an idea from a technical perspective, with potential architecture, design and technologies.
---

Perform an indepth technical analysis of the provided idea, vision-scope and functionality document:

Rules:
- Clarify any details that might be helpful before starting to research my idea.
- Start your session with me by doing some research using the perplexity-ask and context7 tools. 
- Look for information that may inform potential architecture and design approaches, technology choices, potential preferred languages and frameworks, and other technical considerations.
- Summarize your findings that might be relevant to me before beginning the next step.
- Perform another research loop if asked.

Include the following pivots in your research (Group in major sections):
-Logical architecture
-Software architecture
-Data architecture
-Data flow
-User experience
-Technical requirements
-Design patterns
-Technology stack
-APIs
-SDKs  
-Scalability
-Security
-Performance
-Maintainability
-Extensibility
-Integration
-Testing
-Deployment
-Operations
-Documentation
-Community
-Case studies
-Examples
-Open source projects


WHEN DONE, output to #file:../../specs/02.1-research-technical.md