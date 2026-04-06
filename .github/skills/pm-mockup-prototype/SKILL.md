---
name: mockup-prototype
description: 'Generate interactive HTML mockups from a description and iterate on them. Use this skill when the PM wants to create a mockup, prototype, UI exploration, or design comparison.'
---

# Mockup Prototype

End-to-end workflow for creating, iterating, and sharing interactive HTML mockups using GitHub Copilot (for example, via the Copilot CLI or VS Code Agent Mode).

## When to Use This Skill

- User asks to "create a mockup" or "prototype a UI"
- User wants to compare design alternatives side by side
- User wants to share a mockup with colleagues (file is in `my-mockups/`)
- User wants a quick visual exploration of a feature idea

## Phase 1: Generate

### Save location

Mockups are saved in the `my-mockups/` folder at the repository root. Each mockup gets its own named subfolder:

```
my-mockups/<feature-name>/mockup.html
```

Inform the user where the file will be saved. Do not ask which repo to use — the mockup lives in the current working repository.

### Create the mockup

Create a **single self-contained HTML file** with all CSS inline. No external dependencies, no build steps.

### Style guide

Match the visual style of the product being mocked up. If the user specifies a product or brand, replicate its look (colors, spacing, typography). If no specific product is mentioned, use a clean dark theme as a sensible default:

| Token | Value |
|-------|-------|
| Background (page) | `#0d1117` |
| Background (surface) | `#161b22` |
| Background (overlay) | `#21262d` |
| Text (primary) | `#e6edf3` |
| Text (secondary) | `#9198a1` |
| Text (muted) | `#7d8590` |
| Border | `#30363d` |
| Blue | `#4493f8` |
| Green | `#3fb950` |
| Red | `#f85149` |
| Yellow | `#d29922` |
| Purple | `#a371f7` |
| Font stack | `-apple-system, BlinkMacSystemFont, "Segoe UI", "Noto Sans", Helvetica, Arial, sans-serif` |

Use inline SVGs for icons. Do not reference external icon libraries or CDNs.

### Comparing multiple options

When showing alternatives, use a tabbed layout:

```html
<!-- Sticky nav -->
<div class="option-nav">
  <button class="option-btn active" onclick="show('a', this)">A. Option name</button>
  <button class="option-btn" onclick="show('b', this)">B. Option name</button>
</div>

<!-- Each option in its own section -->
<div class="mockup-section active" id="section-a">
  <!-- mockup content -->
  <!-- pros/cons narrative INSIDE this div -->
</div>
<div class="mockup-section" id="section-b">
  <!-- mockup content -->
  <!-- pros/cons narrative INSIDE this div -->
</div>

<script>
function show(id, btn) {
  document.querySelectorAll('.mockup-section').forEach(s => s.classList.remove('active'));
  document.querySelectorAll('.option-btn').forEach(b => b.classList.remove('active'));
  document.getElementById('section-' + id).classList.add('active');
  btn.classList.add('active');
}
</script>
```

Key rules:
- Narrative blocks (pros/cons) go **inside** each `.mockup-section`, not outside
- Only one section visible at a time via CSS (`display:none` / `display:block`)
- Use realistic data, not placeholder text

### Preview locally

After creating the file, always open it in the browser automatically:

```bash
open my-mockups/<feature-name>/mockup.html   # macOS
xdg-open my-mockups/<feature-name>/mockup.html  # Linux
start my-mockups/<feature-name>/mockup.html  # Windows (cmd/PowerShell)
```

## Phase 2: Iterate

All changes stay local in `my-mockups/` (gitignored).

1. Edit the HTML file based on user feedback
2. **Always re-open in browser after each change** so the user can see updates immediately
3. Common iteration patterns:
   - Adding/removing/reordering options
   - Adding pros/cons narrative below each option
   - Tweaking layout, spacing, colors
   - Fixing visual bugs reported via screenshots

## Security

### Content guidelines

- Use **synthetic, fictional data** in all mockups (fake usernames, placeholder repos, made-up org names)
- Do not embed real screenshots of internal tools unless cleared for public sharing
- Do not reference external CDNs, scripts, or stylesheets. All assets must be inline. This prevents supply-chain risks and ensures the file works offline.

### Credential hygiene

- Do not hardcode tokens, passwords, or auth headers in any HTML, CSS, or JS within the mockup.

## Guidelines

- Single self-contained HTML file. No build tools, no npm, no frameworks.
- Save mockups in `my-mockups/<feature-name>/mockup.html`. One file, one location.
- Mockups are local-only (`my-mockups/` is gitignored).
- Always auto-open the local file in browser during iteration.
- Use descriptive folder names (e.g., `sessions-sidebar`, not `mockup-1`).
