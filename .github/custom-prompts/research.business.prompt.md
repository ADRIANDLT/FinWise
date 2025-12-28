---
agent: 'agent'
tools: ['perplexity-ask/perplexity_research', 'perplexity-ask/perplexity_reason', 'perplexity-ask/perplexity_ask', 'context7/*']
description: 'Research business investment knowledge, fundamental stock investing, and real-estate investing with authoritative, citable sources.'
---
Conduct a comprehensive research analysis of the provided idea, vision-scope, and functionality document, focusing on business investment knowledge, fundamental stock investing, and real-estate investing. The goal is to gather authoritative, high-quality information sources that can be cited, referenced, and used for further extraction of quotes or detailed citations.

Rules:
- Do not tailor the research to any specific individual, investor profile, or personal goals. Keep the research general and universally applicable.
- Begin each session by performing research using perplexity-ask and context7 tools.
- For every insight, concept, framework, or methodology identified, provide:
  - At least one **authoritative source** (books, academic papers, investor letters, reputable reports, government publications, or high-quality articles).
  - A **direct link** to the source or its official catalog/publisher page.
  - The **exact location** of the referenced insight (book: chapter/page/paragraph; paper: section/page; report: section/page; article: heading/paragraph).
- Prioritize well-established, credible, and widely recognized sources in investing, business analysis, and real-estate fundamentals.
- Summaries must be neutral, general, and not tied to any specific investor type.
- Perform additional research loops if requested.

Organize research into the following major sections:

### 1. Business Investment Foundations
- Core principles of business value creation
- Capital allocation fundamentals
- Risk, return, and compounding
- Market structure and economic fundamentals

### 2. Fundamental Stock Investing
- Valuation methods (DCF, comparables, dividend models)
- Financial statement analysis (income statement, balance sheet, cash flow)
- Key ratios and metrics (P/E, ROE, ROIC, margins, leverage)
- Competitive advantage and industry analysis
- Margin of safety and risk management
- Behavioral finance and investor psychology
- Investment thesis development

### 3. Real-Estate Investment Fundamentals
- Types of real-estate investments (residential, commercial, REITs)
- Valuation approaches (cap rates, NOI, cash-on-cash, IRR)
- Financing structures and leverage
- Rental property analysis (cash flow, expenses, vacancy)
- Market cycles and location analysis
- Regulatory, legal, and tax considerations (general, non-jurisdiction-specific)

### 4. Portfolio Construction & Strategy
- Asset allocation frameworks
- Diversification principles
- Correlations between stocks, real estate, and other assets
- Passive vs. active investment approaches
- Long-term vs. tactical strategies

### 5. Due Diligence & Research Methodology
- How to research a stock fundamentally
- How to evaluate a real-estate deal
- Checklists and decision frameworks
- Common red flags and pitfalls

### 6. Case Studies & Historical Examples
- Notable stock investment case studies
- Real-estate investment case studies
- Failures and lessons learned
- All case studies must include source links and citation locations

### 7. Tools, Data Sources & Learning Materials
- Data platforms for stock and real-estate analysis
- Classic books, papers, and investor letters
- Educational resources (courses, lectures, reports)
- Each resource must include a link and citation location

### 8. Behavioral, Psychological & Practical Considerations
- Cognitive biases in investing
- Decision-making frameworks
- Process discipline and documentation

### 9. Implementation & Ongoing Management
- Monitoring investments
- Rebalancing strategies
- Exit strategies for stocks and real estate

For each section:
- Provide a concise summary of key insights.
- List authoritative sources with:
  - Full title and author
  - Link to the source or catalog entry
  - Exact citation location (page, chapter, section, paragraph)
- Include multiple perspectives when relevant.


WHEN DONE, output to #file:../../specs/01.1-research-business.md