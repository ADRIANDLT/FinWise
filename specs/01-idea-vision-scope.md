# FinWise: Idea, Vision & Scope Document

## Executive Summary

FinWise is a multi-agent AI investment assistant designed to democratize access to personalized financial guidance. The system leverages agentic workflow architecture to provide intelligent, adaptive investment recommendations for getting started investors. Through specialized AI agents coordinated by an orchestrator, FinWise offers personalized advice initially across stock market fundamentals and residential real estate investments in the United States, tailored to individual risk profiles and investment horizons ranging from several months to several years. However, the system's architecture should be open and extensible to additional specialized investment agents such as ETFs, Crypto currency and other investment areas.

This proof-of-concept project demonstrates how modern AI agent frameworks can transform complex investment decision-making into an accessible, conversational experience while maintaining user control over all financial actions.

---

## Context

### Functionality Summary

FinWise employs a multi-agent workflow architecture where specialized AI agents collaborate to deliver comprehensive investment guidance. The potential agents, to be implemented progressively in the roadmap's versions detailed later in this document, are the following:

- **Orchestrator Agent**: Coordinates all specialized agents, manages workflow, resolves conflicts through user input, and maintains conversation context
- **User Profiling Agent**: Conducts interactive questionnaires to assess risk tolerance (aggressive vs. conservative) and investment goals, creating persistent user profiles stored in a database
- **Global Financial Advisor Agent**: Provides high-level investment direction across asset classes (stocks, real estate, bank accounts, other alternatives)
- **Stock Fundamentals Advisor Agent**: Delivers detailed analysis and recommendations for fundamental stock investments based on financial data and market research
- **Real Estate Investment Advisor Agent**: Offers guidance on residential real estate investment opportunities across the United States
- **Investment Strategy Summarization Agent**: Synthesizes all advice and chat discussions into comprehensive investment strategy summaries that are persisted in the database with date-title identifiers, allowing users to maintain multiple strategy versions over time
- **Risk Management Agent**: Evaluates portfolio composition and alerts users to risk concentrations or profile mismatches
- **Stock Purchase Agent**: Facilitates actual stock purchases based on user approval and coordinated agent recommendations

Some agents leverage external knowledge bases through third-party financial MCP (Model Context Protocol) servers to ensure recommendations are grounded in current market data, financial information, real estate trends, and sentiment analysis from news and social sources.



### Problem to be Solved

**Primary Problem**: Everyday retail investors lack access to personalized, intelligent investment guidance that adapts to their unique risk profiles and goals. Traditional options are either:
- Too expensive (human financial advisors with high minimum investments)
- Too generic (one-size-fits-all advisors)
- Too complex (requiring deep financial expertise to interpret)

**Secondary Problems**:
- Investment beginners don't know where to start or how to assess their own risk tolerance
- Users struggle to decide between different investment options (stocks vs. real estate vs. savings)
- Information overload: too much financial data without context or personalization
- Lack of confidence in investment decisions due to missing explanations of "why" behind recommendations
- Difficulty understanding when portfolio adjustments are needed based on changing market conditions or personal circumstances

### Main Hypothesis

**Core Hypothesis**: A multi-agent AI system specialized in investment guidance can provide retail investors with personalized, adaptive, and explainable investment recommendations that feel like having a knowledgeable financial mentor—combining the accessibility of automated tools with the personalization and educational value of human advisors.

**Supporting Hypotheses**:
1. Users with different risk profiles (aggressive vs. conservative) will make better investment decisions when recommendations are tailored to their specific tolerance levels
2. Breaking down complex investment analysis into specialized agents (global strategy, stock fundamentals, real estate, risk management) produces more accurate and reliable recommendations than a single generalist system. In addition, a more granular system allows a better way for extensibility to additional specialized agents.
3. Providing contextual explanations alongside recommendations increases user understanding, confidence, and engagement with long-term investment strategies
4. Integrating real-time market data and sentiment analysis through MCP servers enables more relevant and timely investment guidance than static recommendation systems
5. Users are more likely to follow through on investment recommendations when they maintain approval authority over actual purchases

### Hypothesis Validation/Invalidation

The following Hypothesis Validation/Invalidation are potential ways that could be done if the project would target real users beyond a PoC (Proof of Concept).

However, metrics and measurement and telemetry are out of scope of this initial project.

**Potential Validation Criteria**:
- User acceptance of investment advice indicating recommendations align with their profiles
- User comprehension showing understanding of investment concepts after system interaction
- Decision confidence levels before and after receiving agent recommendations
- Accuracy of agent recommendations compared to market performance over defined timeframes (it would need significant time to be confirmed)
- User engagement: return usage rates, profile updates, recommendation acceptance rates
- Quality of conflict resolution: user feedback when choosing between competing agent recommendations

**Potential Invalidation Signals**:
- Users consistently reject recommendations aligned with their stated profiles (indicates profiling failure)
- Low comprehension despite explanatory components
- Specialized agents frequently conflict without providing meaningful distinctions (indicates poor agent design)

**Measurement Approach** (for hypothetical future evaluation beyond PoC):
- Post-interaction surveys on confidence and understanding
- A/B testing different explanation depths and recommendation formats
- Tracking user profile evolution and investment horizon adherence
- Comparison of recommended vs. actual market performance (historical simulation during development)
- User feedback collection on conflict resolution quality and orchestrator effectiveness

---

## Product Overview

### Core Value Proposition

**"Your Personal Investment Guide—AI-Powered, Always Learning, Always Adapting"**

FinWise transforms the overwhelming world of investing into an accessible, personalized learning and decision-making experience. Unlike generic investment tools or prohibitively expensive human advisors, FinWise:

1. **Personalizes to Your Profile**: Adapts all recommendations to your specific risk tolerance, investment timeline, and financial goals through persistent user profiling
2. **Explains, Not Just Recommends**: Provides educational context and clear rationale for every suggestion, building your investment knowledge over time
3. **Covers Multiple Horizons**: Handles diverse investment timeframes simultaneously—from several months to several years—matching recommendations to your specific goals
4. **Bridges Asset Classes**: Compares and recommends across stocks and real estate (and additional potential investment areas in the future roadmap), helping you understand which investment type suits your current situation
5. **Evolves With You**: Updates your profile as your risk tolerance and circumstances change, ensuring recommendations stay relevant throughout your financial journey
6. **Keeps You in Control**: Requires your explicit approval for any actual financial transactions while providing intelligent guidance to inform those decisions

#### Why This Solution is the Right Next Step

**Market Timing**: The convergence of advanced AI agent frameworks, accessible cloud AI services (Azure AI Foundry), and standardized integration protocols (MCP servers) makes sophisticated multi-agent investment assistants technically feasible for the first time.

**Gap in Market**: Current solutions fall into two extremes:
- **Traditional online advisors**: Automated but inflexible and simple, based on programming rules instead of AI, lacking personalization and comprehensive advisory
- **Human advisors**: Personalized but expensive and inaccessible to most retail investors

FinWise occupies the middle ground: AI-powered intelligence with human-like personalization and educational mentorship, accessible to getting started investors.

**Proven Patterns**: Multi-agent architectures are demonstrating success in complex domains requiring specialized knowledge and coordinated decision-making while allowing extensibility with future specialized agents. Applying this pattern to investment guidance is a great fit.

**User Empowerment**: By combining automation with user approval requirements and educational explanations, FinWise addresses the key concern with AI financial tools: trust. Users learn while being guided, building confidence and competence simultaneously.

### Target Audience

**Primary Users**: Everyday retail investors in the United States who want to make informed investment decisions but lack access to affordable, personalized financial guidance.

**User Segments**:

1. **Investment Beginners** (Primary Focus)
   - Age: 25-45
   - Limited investment knowledge
   - Starting to think about long-term wealth building
   - Intimidated by traditional financial services
   - Seeking education alongside actionable advice

2. **Self-Directed Investors** (Secondary)
   - Some investment experience
   - Want validation and alternative perspectives on their strategies
   - Interested in expanding across multiple investment areas
   - Value having a "second opinion" without paying for full advisory services

**Geographic Scope**: United States (all states) for both stock market and residential real estate recommendations.

**Excluded Audiences** (for this PoC):
- Day traders or active short-term traders
- Professional investors or institutional users
- For the initial versions, users seeking cryptocurrency, commodities, or international market guidance are out of scope (but for potential future expansion)
- Financial advisors using the tool professionally (potential future B2B pivot)

---

## Key Differentiators

1. **Agentic Architecture**: Unlike monolithic traditional simple online advisors, FinWise's multi-agent design allows specialized expertise in different investment domains (stocks, real estate, risk assessment) to work collaboratively and open to extensibility
   
2. **Educational Integration**: Every recommendation includes explanatory context, transforming the tool from a simple advisor into an investment education platform

3. **Profile Persistence & Evolution**: User profiles are stored and evolve over time, creating increasingly personalized experiences rather than treating each interaction independently

4. **Cross-Asset Intelligence**: Compares and recommends across stock market and real estate investments (and other potential areas in the future), helping users understand optimal allocation based on their profile rather than siloing asset classes

5. **User-Controlled Execution**: Maintains human decision-making authority for all decisions and potential transactions while providing AI-powered intelligence to inform those decisions, building trust through transparency

6. **MCP-Powered Context**: Leverages external and internal knowledge bases through standardized MCP servers, ensuring recommendations stay current with user's profile, market data, financial information, and sentiment trends

## Potential Business Model

**Note**: The following business model is hypothetical for this proof-of-concept project. Actual implementation is outside the PoC scope.

**Subscription Tiers**:

**Free Tier** - "FinWise Explorer"
- Access to User Profiling Agent
- Access to Global Financial Advisor Agent (high-level cross-asset recommendations)
- Basic investment education and explanations
- Profile storage and updates
- Limited to general guidance without specialized deep-dives

**Premium Tier** - "FinWise Fundamentals" ($9.99-14.99/month)
- Everything in Free Tier
- Access to Stock Fundamentals Advisor Agent
- Detailed stock analysis and recommendations
- Real-time market data integration
- Enhanced educational content on stock investing

**Professional Tier** - "FinWise Pro" ($24.99-34.99/month)
- Everything in Premium Tier
- Access to Real Estate Investment Advisor Agent
- Access to Risk Management Agent
- Residential real estate investment analysis
- Portfolio risk assessment and diversification recommendations
- Access to Stock Purchase Agent (facilitates actual transactions with user approval)

---

## Functional Requirements

### Sub-module requirements

**FR-1: Multi-Agent Workflow with orchestration/triage agent**
- **Priority**: P1 (High)
- **Rationale**: Multi-agent workflow supporting any number of specialized agents coordinated by an orchestration/triage agent with dynamic routing capabilities
- **High-Level Specification**:
  - System shall coordinate specialized agents through an orchestrator agent on a workflow infrastructure
  - System shall manage conversation context across agent interactions
  - System shall resolve conflicts between specialized agent recommendations by presenting options to user with explanations
  - System shall maintain coherent multi-turn conversations with users
  - System shall route user queries to appropriate specialized agents dynamically based on user intent
  - System shall support dynamic workflows where users can return to previously visited agents (e.g., updating user profile after receiving investment advice)
  - System shall allow non-sequential agent execution, enabling users to switch between agents based on conversation flow rather than fixed steps
  - System shall support escalation scenarios where an agent can request assistance from other specialized agents
  - System shall support fallback mechanisms when an agent cannot handle a user query
  - System shall enable expert handoff where one agent transfers control to a more specialized agent
  - When agents conflict, orchestrator must identify the nature of conflict
  - Orchestrator must present both/all recommendations with full explanations
  - User must be given clear decision options
  - For the baseline implementation before adding the real agents (User profile management agent and Global Investment Advisory agent), implement a hollow workflow with very basic agents with dynamic handoff capabilities.

**FR-2: User Profile Management**
- **Priority**: P0 (Critical)
- **Rationale**: Profile accuracy is foundational to all personalized recommendations
- **High-Level Specification**:
  - System shall conduct interactive questionnaires to assess user risk tolerance (aggressive, moderate, conservative spectrum)
  - System shall assess investment goals, timeframes (multiple simultaneous horizons from several months to several years), and financial situation
  - System shall generate and store structured user profiles in a database, identified by user ID/username
  - System shall allow users to update their profiles at any time
  - System shall skip questionnaire for returning users with existing profiles
  - System shall adapt profile over time based on user decisions and stated risk tolerance changes (After confirming with the user)
  - Profile database must store data such as: user ID, risk tolerance score, investment goals, timeframes, financial situation, questionnaire responses, creation date, last updated date (TBD: The final profile table template to be filled will be provided in the more detailed feature's specs)
  - Questionnaire must include minimum 8-10 questions covering risk scenarios, investment knowledge, financial goals, and time horizons
  - System must validate profile completeness before allowing access to specialized agents
  - System must support simultaneous recommendations for short-term (several months), medium-term (1-3 years), and long-term (3+ years) goals
  - Each recommendation must specify which investment horizon it addresses
  - Portfolio composition must consider all active horizons
  - Risk assessment must account for different risk tolerances across different timeframes

**FR-3: Global Investment Advisory**
- **Priority**: P1 (High)
- **Rationale**: Core value proposition is personalized guidance matching user risk tolerance
- **High-Level Specification**:
  - System shall provide high-level investment direction across asset classes: stocks, residential real estate, bank savings accounts, and other alternatives
  - System shall tailor recommendations to user risk profile and investment horizons
  - System shall explain rationale for preferring one asset class over another for specific user situations
  - System shall leverage external financial knowledge bases through MCP servers
  - All agent recommendations must reference user profile risk tolerance
  - Conservative profiles: prioritize capital preservation, lower volatility, established investments
  - Moderate profiles: balance growth and preservation, diversified approaches
  - Aggressive profiles: emphasize growth potential, accept higher volatility, emerging opportunities
  - Recommendations outside profile range must be clearly flagged with warnings

**FR-4: Stock Fundamentals Analysis**
- **Priority**: P1 (High)
- **Rationale**: Investment recommendations require current market conditions
- **High-Level Specification**:
  - System shall provide detailed analysis of individual stocks based on fundamental analysis principles
  - System shall recommend long-term stock investments (several months to several years horizon)
  - System shall exclude day-trading and short-term speculation recommendations
  - System shall access real-time and historical stock market data through financial MCP servers
  - System shall provide explanations for stock recommendations grounded in financial metrics
  - The system needs to have live context about specific stock values to be proposed. Sometimes a value might be "expensive" or "cheap" in the current market and might or might not be a good investment opportunity
  - Stock recommendations must use data no older than 24 hours for prices, 1 week for fundamentals

**FR-5: Real Estate Investment Guidance**
- **Priority**: P1 (High)
- **Rationale**: Investment recommendations require current market conditions
- **High-Level Specification**:
  - System shall provide residential real estate investment recommendations across all US states
  - System shall analyze residential property investment opportunities (excludes commercial real estate in initial scope)
  - System shall access real estate market data through specialized MCP servers
  - System shall explain real estate recommendations with market context and investment rationale
  - Real estate recommendations must use data no older than 1 week for prices, 1 month for trends

**FR-6: Investment Strategy Summarization & Persistence**
- **Priority**: P1 (High)
- **Rationale**: Users need coherent strategy summaries to track their evolving investment plans over time
- **High-Level Specification**:
  - System shall synthesize all advisory agent recommendations and chat discussions in a session into a comprehensive investment strategy summary
  - System shall persist strategy summaries in database with unique date-title identifiers
  - System shall allow users to have multiple strategy versions (e.g., different scenarios, time periods, or plan iterations)
  - System shall enable users to retrieve, compare, and review historical strategy summaries
  - Each strategy summary must include: date created, title/label, synthesized recommendations from all agents, user profile snapshot at time of creation, and conversation context
  - Strategy summaries must be queryable by date, title, or keywords
  - Users must be able to archive or delete strategy summaries
  - In future versions of the system, after the system has a custom UI (web app), the user should also be able to directly edit/update any persisted investment strategy.

**FR-7: Risk Management & Portfolio Assessment**
- **Priority**: P3 (Medium)
- **Rationale**: Recommendations should consider existing holdings to avoid duplication or excessive concentration
- **High-Level Specification**:
  - System shall evaluate user portfolio composition for risk alignment with stated profile
  - System shall identify when portfolio becomes misaligned with user risk tolerance
  - System shall recommend diversification strategies when concentration risk is detected
  - System shall alert users to significant risk factors in their investment choices
  - Users should be able to input current portfolio holdings
  - Risk Management Agent must assess portfolio composition before new recommendations
  - System should warn about sector/asset concentration
  - Diversification recommendations should be specific to user's existing positions

**FR-8: Stock Purchase Execution**
- **Priority**: P0 (Critical)
- **Rationale**: Regulatory safety and user trust require explicit consent
- **High-Level Specification**:
  - System shall facilitate stock purchase recommendations based on coordinated agent analysis
  - System shall require explicit user approval before any purchase action
  - System shall present clear purchase recommendations with reasoning and risk assessment
  - System shall confirm transactions with users and provide documentation
  - No financial transaction shall be executed without explicit user approval
  - Approval interface must display: investment details, amount, rationale, risk assessment, and estimated fees
  - User must actively confirm (not default approve)
  - System must log all approval/rejection decisions with timestamps

### Global / cross-cutting requirements

**FR-9: Educational & Explanatory Content**
- **Priority**: P0 (Critical)
- **Rationale**: Trust and user learning depend on transparent reasoning
- **High-Level Specification**:
  - System shall explain "why" behind every recommendation, not just "what" to do
  - System shall provide educational context about investment strategies being suggested
  - System shall help users understand investment concepts relevant to their decisions
  - System shall build user knowledge progressively throughout interactions
  - Every recommendation must include: reasoning (why), risk factors (what could go wrong), and educational context (relevant concepts)
  - Explanations must be tailored to user's demonstrated knowledge level (from profile)
  - Technical terms must be defined on first use
  - Comparisons between options must highlight key differentiating factors

**FR-10: External Knowledge Integration**
- **Priority**: P1 (High)
- **Rationale**: Investment recommendations require current market conditions and external data sources
- **High-Level Specification**:
  - System shall integrate with third-party financial data MCP servers for market data
  - System shall integrate with real estate data MCP servers for property market information
  - System shall integrate with news/sentiment analysis MCP servers for market sentiment context
  - System shall handle MCP server unavailability gracefully with appropriate user messaging
  - Sentiment analysis must use data no older than 48 hours

**FR-11: Multi-Client Accessibility**
- **Priority**: P1 (High)
- **Rationale**: Users should have flexibility in how they access the system
- **High-Level Specification**:
  - System shall be accessible through MCP-compatible clients (Claude, ChatGPT, GitHub Copilot, etc.)
  - System shall support a custom web application interface
  - System shall maintain consistent functionality across different client interfaces

---

## Long-Term Vision

**"Democratizing Financial Wisdom Through Intelligent, Extensible AI Agents"**

FinWise envisions a future where every individual, regardless of income or financial expertise, has access to personalized, intelligent investment guidance that evolves with them throughout their financial journey. The long-term vision extends beyond basic stock and real estate recommendations to create a comprehensive financial ecosystem:

### Future Potential Expansion Areas

**Investment Universe Expansion**:
- **ETFs & Index Funds**: Specialized agent for passive investment strategies and low-cost diversification
- **Cryptocurrency**: Specialized agent for crypto investment guidance with enhanced risk management
- **International Markets**: Global stock and real estate coverage beyond US markets
- **Commodities & Alternatives**: Precious metals, REITs, bonds, and other asset classes
- **Retirement Planning**: 401(k), IRA, and pension optimization agents

**Enhanced Intelligence**:
- **Predictive Analytics**: Machine learning models for market trend prediction and opportunity identification
- **Tax Optimization**: Agent specialized in tax-efficient investment strategies and harvesting
- **Estate Planning**: Long-term wealth transfer and legacy planning guidance
- **Behavioral Finance Integration**: Agents that recognize and help users overcome common investment biases

**Ecosystem Integration**:
- **Multiple Brokerage Integration**: Direct connections to multiple user brokerage accounts for automatic portfolio purchases and seamless execution (TBD: PoC might include a single online broker integration)
- **Financial Institution Partnerships**: Integration with banks, credit unions, and financial platforms
- **Advisor Collaboration Mode**: Tools enabling financial advisors to use FinWise to enhance their services (B2B pivot)
- **Community Learning**: Anonymized insights from successful investment patterns across user base (privacy-preserving)

**Advanced Personalization**:
- **Life Event Adaptation**: Automatic profile adjustments based on major life changes (marriage, children, career changes, retirement)
- **Goal-Based Planning**: Specific goal tracking (home purchase, education funding, retirement) with milestone-based recommendations
- **Emotional Intelligence**: Recognition of user emotional state during market volatility and appropriate supportive guidance
- **Multi-User Accounts**: Joint account management with blended risk profiles for couples/families

**Ultimate Vision**: FinWise becomes the trusted AI companion for every stage of an individual's financial investment evolution thanks to financial intelligence, accessible through conversations.

---

## Scope: Roadmap Versions

### v0.1: Foundational - Core Agentic Workflow (MVP)

**Timeline**: Initial proof-of-concept phase

**Objective**: Establish foundational multi-agent architecture with basic investment guidance capabilities

**Note on incremental delivery**: v0.1 can be delivered in slices. For example, the baseline workflow/orchestration may start with in-memory session state and hollow agents, then add database-backed profile persistence in a follow-up slice.

**Features Implemented**:

1. **Multi-agent workflow support**: Workflow open to any number of agents
1. **Orchestrator Agent**: Like a triage agent, it coordinates workflow, manages conversation context, routes queries
2. **User Profiling Agent**: Conducts questionnaire, assesses risk tolerance (aggressive vs. conservative), stores profiles in database
3. **Global Financial Advisor Agent**: Provides high-level recommendations across asset classes (stocks vs. real estate vs. bank accounts vs. other investments)

**Epic Coverage**:
- **Epic 1: User Onboarding & Profiling**
  - Complete risk assessment questionnaire
  - Input financial situation and goals
  - Set investment timeframes (like investment for three years and expected profit)
  - Profile storage and persistence
  - Profile retrieval for returning users
  - Profile update capability

- **Epic 2: Global Financial advisory directions**
  - Suggestion of investment in stock market or real estate or both. Global directions without specifying concrete values.
  - Comparison across major asset classes
  - Basic explanations for recommendations

**Infrastructure**:
- Profile database implementation
- Integration with at least one financial data MCP server for basic market context
- MCP client compatibility (Test with MCP Inspector, Claude, ChatGPT, or GitHub Copilot)
- Basic orchestration logic for agent coordination

**Success Criteria**:
- Users can complete profiling questionnaire
- Profiles are successfully stored and retrieved
- Users can update their profiles
- System provides differentiated recommendations for aggressive vs. conservative profiles
- Global advisor provides coherent cross-asset guidance

### v0.2: Specialized Advisory - Deep Investment Intelligence

**Objective**: Add specialized agents for detailed stock and real estate analysis

**Agents Added**:

1. **Stock Fundamentals Advisor Agent**: Detailed stock analysis based on fundamental principles, long-term investment recommendations

2. **Real Estate Investment Advisor Agent**: Residential real estate investment guidance across US markets

3. **Investment Strategy Summarization Agent**: Synthesizes all advisory recommendations and conversations into persistent strategy summaries

**Epic Coverage**:
- **Epic 3: Stock Investment Advisory on assets**
  - Discover stock investment opportunities, with concrete values (i.e. specific tech stock values such as MSFT, META in NASDAQ, or values from the industry in DOW JONES, etc.) matching the user's profile
  - Understand why specific investments are recommended
  - Detailed fundamental stock analysis and recommendations
  - Sentiment-aware recommendations incorporating market mood

- **Epic 4: Real Estate Investment Advisory on assets**
  - Discover real estate investment opportunities in concrete states and areas, matching user's profile
  - Understand why specific investments are recommended
  - Residential real estate market analysis across US states
  - Sentiment-aware recommendations incorporating market mood

- **Epic 5: Investment Strategy Summarization**
  - Generate comprehensive strategy summaries from all agent conversations
  - Store multiple strategy versions with date-title identifiers
  - Retrieve and compare historical investment strategies
  - Track strategy evolution over time
  - Review past recommendations and decisions

**Infrastructure Enhancements**:
- Integration with stock market data MCP servers (real-time prices, fundamentals)
- Integration with real estate data MCP servers (residential property data, market trends)
- Integration with news/sentiment analysis MCP servers
- Enhanced orchestrator logic by adding the additional specialized agents
- Cross-agent coordination (Global Advisor → Specialized Advisors workflow)
- Strategy summary database schema and persistence layer
- Strategy summarization logic synthesizing multi-agent conversations

**Success Criteria**:
- Stock agent provides specific stock recommendations with fundamental analysis
- Real estate agent provides property market recommendations with regional context
- Recommendations incorporate current market data and sentiment
- Users can receive specialized deep-dives after global guidance
- Agents provide educational value explaining investment concepts
- Strategy summaries accurately synthesize recommendations from all involved agents
- Users can generate and save comprehensive strategy summaries from conversations
- Users can have and retrieve multiple strategy versions over time

### v0.3: Custom UI experience

**Objective**: Provide a custom UI experience with a web application that allows not just chatting but also provides a content UI area to showcase charts/images and also a background logging surfacing the operations executed by agents, going on under the covers.

**Epic Coverage**:
- **Epic 6: Custom UI application**
  - Interactive chat interface for natural conversation with agents
  - Visual content area to display charts, graphs, and financial data visualizations
  - Background operations log showing agent workflow and decision-making process
  - Profile management dashboard for viewing and updating user preferences
  - Investment recommendations display with explanations and risk indicators
  - Real-time updates as agents process queries and recommendations

**Infrastructure Enhancements**:
- Web application frontend using a SPA framework (React, Vue, or similar framework)
- Backend API to interface with MCP server
- WebSocket or similar technology for real-time agent operation visibility
- Charting library integration for financial data visualization
- Session management for multi-turn conversations

**Success Criteria**:
- Users can interact with all agents through intuitive web interface
- Visual charts enhance understanding of recommendations
- Background log provides transparency into agent orchestration
- Application works seamlessly across different screen sizes
- User experience is smooth with responsive feedback

### v0.4: Risk analysis and stock purchase execution - Active Portfolio Management

**Objective**: Enable portfolio risk management and facilitated stock purchases with user approval

**Agents Added**:

1. **Risk Management Agent**: Portfolio risk assessment, diversification recommendations, alerts for profile misalignment
1. **Stock Purchase Agent**: Facilitates actual stock purchases based on coordinated recommendations, requires user approval

**Epic Coverage**:
- **Epic 7: Risk Management**
  - Risks on recommendations (P1):
    - Receive diversification recommendations
    - Recommendations risk assessment aligned with user profile
    - Concentration risk detection and diversification guidance

  - Risks on Portfolio (P2)
    - Understand current portfolio risks and health
    - Track portfolio composition
    - Portfolio risk assessment aligned with user profile

    **Infrastructure Enhancements**:
    - Risk calculation engine

    **Success Criteria**:
    - Risk agent accurately identifies recommendations or portfolio risks and misalignments with user's profile
    - Users understand risk implications before approving transactions

  
- **Epic 8: Purchase Execution & Monitoring**
  - User reviews stock purchase recommendations
  - Approve or reject proposed transactions
  - Receive transaction confirmations


    **Infrastructure Enhancements**:
    - Coordinated multi-agent purchase recommendations (Global → Stock Fundamentals → Risk → Purchase workflow)
    - Portfolio storage and tracking system
    - Integration with brokerage APIs for stock purchase execution (user approval required)
    - Transaction logging and confirmation system

    **Success Criteria**:
    - Purchase recommendations require and receive explicit user approval
    - Multi-agent coordination produces coherent purchase advice
    - System maintains transaction audit trail

### Open to Growth Criteria

**Additional Specialized Agents** (Future versions beyond v0.3):

**Potential v0.4+**:
- **Crypto Investment Advisor Agent**: Cryptocurrency investment guidance with specialized risk considerations
- **ETF & Index Fund Advisor Agent**: Passive investment strategy recommendations
- **Tax Optimization Agent**: Tax-efficient investment strategies and tax-loss harvesting
- **Retirement Planning Agent**: 401(k), IRA, and long-term retirement optimization

**Expansion Considerations**:
- Each new agent requires dedicated MCP server integration for domain-specific data
- Orchestrator complexity grows with agent count; may require hierarchical orchestration
- User interface must scale to present multiple specialized perspectives coherently
- Risk management must expand to cover new asset classes and investment types

**Trigger Criteria for New Agents**:
- User demand signals from feedback and feature requests
- Market opportunity in underserved investment categories
- Availability of reliable MCP servers for required data
- Clear differentiation from existing agent capabilities

### Out of Scope

**Explicitly Excluded from Roadmap** (for the initial implemented proof-of-concept):

**Security & Authentication**:
- User authentication and authorization systems
- Secure credential storage and management
- Multi-factor authentication
- Role-based access control
- Data encryption at rest and in transit
- **Rationale**: PoC focuses on functional agent architecture; production security is separate concern needed for production-ready products

**Regulatory & Compliance**:
- SEC/FINRA regulatory compliance
- Formal investment advisor registration
- Legal disclaimers and terms of service
- Audit logging for regulatory purposes
- Compliance monitoring and reporting
- **Rationale**: PoC is educational/demonstrative; not a registered financial service

**Advanced Analytics & Backtesting**:
- Historical performance simulation
- Backtesting recommendation strategies
- Benchmark performance comparison
- Portfolio performance tracking against market indices
- Simulated market environments for testing
- **Rationale**: Requires extensive historical data infrastructure; beyond PoC scope

**Cost & Fee Analysis**:
- Transaction cost calculation
- Tax impact analysis (capital gains, etc.)
- Fee comparison across brokerages
- Total cost of ownership calculations
- **Rationale**: Adds significant complexity; not core to agentic workflow demonstration

**Portfolio Monitoring & Alerts**:
- Continuous portfolio performance monitoring
- Automated alerts for significant portfolio drift
- Real-time market event notifications
- Performance benchmarking
- **Rationale**: Requires production-grade monitoring infrastructure

**Mobile Applications**:
- Native iOS or Android applications
- Mobile-optimized interfaces beyond responsive web
- Push notifications
- **Rationale**: Web application sufficient for PoC; mobile requires separate development effort

**Social Features**:
- Community forums or discussion boards
- Sharing investment strategies with other users
- Social learning from user behavior
- **Rationale**: Not in initial roadmap; potential future consideration

**International Scope**:
- Markets outside United States
- Multi-currency support
- International tax considerations
- Non-US real estate markets
- **Rationale**: US-only scope simplifies data requirements and regulatory complexity

**Day Trading & Short-Term Speculation**:
- Intraday trading recommendations
- Technical analysis for short-term trades
- Options, futures, or derivatives trading
- **Rationale**: Focus is long-term fundamental investing (several months to years)

**Commercial Real Estate**:
- Commercial property investment guidance
- REITs analysis (potential future addition)
- **Rationale**: Residential real estate only for initial scope

### Tradeoffs in Prototype

**Accepted Limitations for PoC**:

1. **No Authentication/Authorization**: 
   - **Tradeoff**: Users not securely identified; profiles identifiable only by username/ID
   - **Justification**: Enables focus on agent architecture without security infrastructure overhead
   - **Future**: Production requires OAuth, secure sessions, encrypted storage

2. **Simplified MCP Server Integration**:
   - **Tradeoff**: May use free tiers with rate limits; limited error handling for API failures
   - **Justification**: Demonstrates integration patterns without operational cost management
   - **Future**: Production requires paid tiers, comprehensive error handling, fallback strategies

3. **Manual Conflict Resolution**:
   - **Tradeoff**: Orchestrator asks user to decide when agents conflict, rather than intelligent conflict resolution
   - **Justification**: Keeps orchestrator logic simple while maintaining transparency
   - **Future**: Could add ML-based conflict resolution or weighted agent confidence scores

4. **Limited Portfolio Tracking**:
   - **Tradeoff**: Portfolio composition may be user-reported rather than brokerage-integrated
   - **Justification**: Avoids complex brokerage API integrations for PoC
   - **Future**: Direct brokerage integration for automatic portfolio sync

5. **No Real-Money Testing**:
   - **Tradeoff**: Cannot validate recommendation quality against actual market performance during PoC
   - **Justification**: PoC demonstrates architecture, not investment accuracy
   - **Future**: Extended beta with paper trading or small real-money pilot

6. **Basic Web UI**:
   - **Tradeoff**: Web application may have minimal styling and basic UX
   - **Justification**: Focus on functional agent interactions, not visual polish
   - **Future**: Production UI/UX design and development

7. **US-Only Scope**:
   - **Tradeoff**: Limits potential user base and investment universe
   - **Justification**: US market data is most accessible; simplifies regulatory considerations
   - **Future**: International expansion with localized agents and data sources

8. **No Offline Capability**:
   - **Tradeoff**: Requires internet connection for all functionality
   - **Justification**: Agent coordination and MCP servers require connectivity
   - **Future**: Could cache educational content and profile data for offline viewing

---

## Risk Assessment

### Technical Risks

**TR-1: MCP Server Reliability & Availability**
- **Risk**: Third-party financial and real estate MCP servers may have downtime, rate limits, or data quality issues
- **Impact**: High - System cannot provide current recommendations without external data
- **Likelihood**: Medium
- **Mitigation**:
  - Use multiple redundant MCP servers when available
  - Set user expectations about online requirements
  - Build robust error handling for API failures

**TR-2: Agent Orchestration Complexity**
- **Risk**: Coordinating multiple specialized agents with potentially conflicting recommendations is complex
- **Impact**: High - Poor orchestration leads to incoherent or contradictory user experience
- **Likelihood**: Medium-High
- **Mitigation**:
  - Start simple with v0.1 (only 3 agents) and add complexity gradually
  - Use a comprenhensive agentic framework that supports multi-agent orchestration "out of the box"
  - Implement clear agent communication protocols
  - Use user-driven conflict resolution for v0.1-v0.3
  - Extensive testing of multi-agent workflows

**TR-3: Profile Accuracy & Evolution**
- **Risk**: User-reported risk tolerance may not reflect actual behavior; profiles may become stale
- **Impact**: Medium - Misaligned recommendations reduce value and trust
- **Likelihood**: Medium
- **Mitigation**:
  - Encourage regular profile updates
  - Track recommendation acceptance/rejection to detect profile drift
  - Prompt users to review profile after major life events
  - Validate profile through scenario-based questions

**TR-4: Data Freshness & Quality**
- **Risk**: Market data delays or errors could lead to recommendations based on outdated or incorrect information
- **Impact**: High - Could result in poor investment decisions
- **Likelihood**: Low-Medium
- **Mitigation**:
  - Implement data freshness validation checks
  - Use reputable MCP server sources

**TR-5: Explanation Quality & Comprehension**
- **Risk**: AI-generated explanations may be too technical, too vague, or misleading
- **Impact**: Medium - Reduces educational value and user trust
- **Likelihood**: Medium
- **Mitigation**:
  - Extensive prompt engineering for explanation generation
  - User testing with target audience for comprehension
  - Iterative refinement based on feedback

**TR-6: Scalability of Agent Framework**
- **Risk**: As more specialized agents are added, orchestration and coordination become unwieldy
- **Impact**: Medium - Limits future expansion
- **Likelihood**: Low (in v0.1-v0.3), High (in future versions)
- **Mitigation**:
  - Design modular, loosely coupled agent architecture
  - Regular architecture reviews as system grows
  - Performance testing with increasing agent counts

### Potential Business Risks

**BR-1: Regulatory & Legal Exposure**
- **Risk**: Providing investment recommendations may trigger regulatory requirements (SEC, FINRA) or legal liability
- **Impact**: Very High - Could shut down project or incur legal costs
- **Likelihood**: Medium (if positioned as advice), Low (if clearly educational/PoC)
- **Mitigation**:
  - Explicit disclaimers: "not financial advice," "educational purposes only"
  - Position as PoC/research project, not production financial service
  - User approval required for all transactions
  - Consult legal counsel before any commercial deployment

**BR-2: User Trust & Adoption**
- **Risk**: Users may not trust AI with financial decisions, especially for complex topics like investing
- **Impact**: High - Low adoption makes project less valuable
- **Likelihood**: Medium
- **Mitigation**:
  - Transparent explanations for all recommendations
  - User maintains control (approval required)
  - Educational approach builds confidence gradually
  - Start with conservative recommendations to build track record

**BR-3: Competitive Landscape**
- **Risk**: Established traditional online advisors and fintech companies have significant resources and market presence
- **Impact**: High - Difficult to differentiate and gain market share
- **Likelihood**: High (if commercialized)
- **Mitigation**:
  - Focus on educational value and explainability as differentiators
  - Target underserved segments (beginners, educational focus)
  - Multi-agent architecture as technical moat
  - PoC phase allows validation before competing directly

**BR-5: Data Privacy Concerns**
- **Risk**: Users hesitant to share financial information due to privacy concerns
- **Impact**: Medium - Limits user engagement and profile depth
- **Likelihood**: High (if commercialized)
- **Mitigation**:
  - Transparent data usage policies
  - Minimize data collection to essential elements
  - Allow anonymous/pseudonymous usage for PoC
  - Future: Implement robust data protection and user control

**BR-6: Monetization Viability**
- **Risk**: Subscription model may not generate sufficient revenue or users may not pay for AI investment advice
- **Impact**: High - Unsustainable business model
- **Likelihood**: Medium (hypothetical for PoC, but important consideration)
- **Mitigation**:
  - Free tier attracts users and demonstrates value
  - Premium features (specialized agents) provide clear incremental value
  - Competitive pricing compared to human advisors
  - PoC validates willingness to pay before significant investment

**BR-7: Market Volatility Impact**
- **Risk**: Severe market downturns may discredit AI recommendations if users lose money
- **Impact**: High - User churn and reputation damage
- **Likelihood**: Low-Medium (market cycles inevitable)
- **Mitigation**:
  - Long-term investment focus reduces short-term volatility impact
  - Risk-aligned recommendations prevent over-exposure
  - Educational content prepares users for market cycles
  - Clear disclaimers about inherent investment risk

---

## Contact Information

**Project Lead**: De La Torre 
**Institution**: IE University 
**Email**: adelatorre.ieu2022@student.ie.edu  
**Project Repository**: https://github.com/ADRIANDLT/FinWise  

---

## Document Control

**Document Version**: v0.1 
**Last Updated**: December 23, 2025  

**Version History**:
- v0.1 (December 23, 2025): Initial idea and vision scope document created based on project proposal and requirements clarification

**Related Documents**:
- [00-AdrianDLTCapstoneProjectProposal.md](./00-AdrianDLTCapstoneProjectProposal.md) - Original project proposal
- [Architecture and Technical Specifications] - To be created separately

---

*This document represents the functional vision and scope for FinWise. Technical architecture, implementation details, and specific technology choices will be documented separately. This is a proof-of-concept project for educational and demonstrative purposes, not a production financial service.*
