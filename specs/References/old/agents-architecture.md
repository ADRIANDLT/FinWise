

Changes Made:

Workflow Configuration - Removed direct handoffs between ProfileAgent ↔ AdvisorAgent. Now only orchestrator → agents and agents → orchestrator.

AdvisorAgent Prompt - Changed handoff target from profile_agent to orchestrator_agent (2 places).

UserProfileAgent Prompt - Changed STEP 6 to explicitly handoff to orchestrator_agent instead of stopping.

Architecture Now:

User → Orchestrator → ProfileAgent → Orchestrator → AdvisorAgent
                    ↑                              ↓
                    └──────────────────────────────┘
          (Future: Stock Agent, Real Estate Agent, etc.)

The orchestrator routing logic (already in place from the previous edit) handles:

PROFILE_READY: marker → route to advisor_agent
All other cases → route to profile_agent
This means when you add a "Specialized Stock investment agent" in the future:

Add it to the workflow handoff configuration
Update orchestrator routing rules to detect when to route to it
No changes needed to ProfileAgent or AdvisorAgent prompts