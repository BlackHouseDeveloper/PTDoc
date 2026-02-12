# AI Instructions Refactoring - Change Log

**Date:** February 12, 2026  
**Scope:** `.github/copilot-instructions.md`  
**Lines Reduced:** ~720 lines → ~280 lines (61% reduction)

---

## Executive Summary

The AI coding assistant instructions have been refactored from a 720-line prescriptive document to a 280-line task-focused reference guide. The new structure emphasizes **relevance-first referencing**, reducing cognitive overhead while maintaining critical technical guardrails.

### Key Improvements

1. **"Working Agreement" section** - Establishes task-driven workflow upfront
2. **"Doc Map" with decision tree** - Clear "when to consult / when not to" guidance  
3. **61% reduction in verbosity** - Removed redundant examples and explanations
4. **No forced reading** - Docs consulted only when relevant to task
5. **Quick reference format** - Tables, checklists, and command snippets

---

## Files Changed

### Modified
- `.github/copilot-instructions.md` - Complete restructure (720 → 280 lines)

### No Changes Required
- `README.md` - Already well-structured for onboarding
- `docs/ARCHITECTURE.md` - Solid reference docs (consulted when needed)
- `docs/Blazor-Context.md` - Detailed lifecycle guide (not duplicated)
- Other `docs/*` files - Preserved as specialized references

---

## Contradictions Found & Resolutions

### 1. **"Always Consult" vs. "Reference When Needed"**

**Conflict:**
- Old instructions: *"Always consult `docs/context/ptdoc-figma-make-prototype-v5-context.md` FIRST"*
- This forced agents to open a 1,492-line document for every task

**Resolution:**
- Removed "always consult" mandate
- Added Doc Map with explicit "Use when" / "Skip when" guidance
- Figma context now consulted **only** when implementing UI from design specs

**Rationale:** Most tasks (e.g., adding a service, fixing a bug, writing tests) don't need design specs. Forcing the agent to read 1,492 lines for every task wasted tokens and time.

---

### 2. **Verbosity vs. Quick Reference**

**Conflict:**
- Old instructions included full code examples for DTOs, API endpoints, test patterns
- Examples totaled ~200 lines, duplicating what's in actual codebase

**Resolution:**
- Removed code examples from instructions  
- Kept **bullet rules** only (e.g., "Use `[EditorRequired]` for required params")
- Agents can reference existing code patterns in the repo directly

**Rationale:** The best examples are already in the codebase. Instructions should provide **decision rules**, not boilerplate code.

---

### 3. **Testing Guidance Overload**

**Conflict:**
- Old instructions had 60+ lines on testing patterns with bUnit, Moq, WebApplicationFactory examples
- These are standard .NET testing practices, not PTDoc-specific

**Resolution:**
- Removed generic testing examples
- Kept only **PTDoc-specific** test guidance (e.g., platform-specific builds, test organization)
- Agents already know how to write xUnit tests

**Rationale:** Trust agents' .NET knowledge. Only document **deviations** from standard practices.

---

### 4. **Platform Detection Code Duplication**

**Conflict:**
- Old instructions included MAUI context detection code with try-catch blocks
- This was speculative code, not currently used in the project

**Resolution:**
- Removed detection code example
- Kept rule: **"Use design tokens for all styling, support light/dark themes"**
- Platform-specific styling deferred until actually needed

**Rationale:** Don't document hypothetical patterns. Add guidance when a real use case emerges.

---

### 5. **Configuration Examples**

**Conflict:**
- Old instructions showed `appsettings.json` JWT structure with placeholder values
- This duplicated what's already in the actual config files

**Resolution:**
- Removed config file examples
- Kept rules: **"Never use placeholders in production"**, **"Min 32 chars for signing key"**
- Agents can read actual `appsettings.json` files in the repo

**Rationale:** Instructions should enforce **policies**, not replicate file contents.

---

## New Structure Breakdown

### 1. Working Agreement (New)
```markdown
**Before starting any task:**
1. Restate the task in 1-2 lines
2. Identify 0-3 relevant docs (only if needed)
3. Use existing patterns
4. Small commits
5. Don't refactor unrelated code
```

**Purpose:** Establish task-driven workflow upfront, reduce scope creep.

---

### 2. Doc Map (New)
```markdown
### Setup & Running
- [README.md] - Use when: setup issues | Skip when: writing code

### Architecture & Boundaries
- [ARCHITECTURE.md] - Use when: layer changes | Skip when: UI-only work
```

**Purpose:** Clear signposting for when to consult each doc.

---

### 3. Quick Reference (Condensed)

**Before:**
- 200+ lines of Blazor rules, lifecycle explanations, code examples

**After:**
- 30-line table of most common rules
- Bullet lists only
- No code examples

**Purpose:** Fast lookups for critical constraints (PascalCase naming, parameter immutability, lifecycle hooks).

---

### 4. Decision Checklist (New)
```markdown
**Consult docs when:**
- [ ] Adding services across architecture layers
- [ ] Component not rendering
- [ ] Database schema changes

**Don't consult docs for:**
- Standard .NET patterns you already know
- Basic component creation
- Simple CRUD following existing patterns
```

**Purpose:** Preemptive filtering to prevent unnecessary doc reads.

---

### 5. Common Pitfalls Table (New Format)
```markdown
| Problem | Likely Cause | Fix |
|---------|-------------|-----|
| Component invisible | Lowercase name | PascalCase + `_Imports.razor` |
| Blank during load | No loading indicator | Add `@if (isLoading)` |
```

**Purpose:** Fast diagnostic reference, avoids long explanations.

---

## Removed Content (Safe Deletions)

### Generic .NET Knowledge
- ✂️ bUnit component testing boilerplate
- ✂️ Moq service mocking examples
- ✂️ WebApplicationFactory integration test setup
- ✂️ Standard DTO/record syntax examples

**Justification:** Agents already know .NET testing/DTO patterns. Only document **PTDoc deviations**.

---

### Speculative Code
- ✂️ MAUI context detection with `IJSRuntime`
- ✂️ Touch-optimized CSS examples (not yet used)
- ✂️ PersistentComponentState prerendering pattern (future optimization)

**Justification:** Don't document hypothetical solutions. Add when actually needed.

---

### Duplicated Content
- ✂️ Full `appsettings.json` JWT structure
- ✂️ API endpoint mapping examples (already in `PTDoc.Api/`)
- ✂️ EF migration commands (already in `EF_MIGRATIONS.md`)

**Justification:** Instructions should **point to** references, not duplicate them.

---

## Preserved Critical Rules

### Architecture (Non-Negotiable)
- ✅ Core has zero dependencies
- ✅ Never reference Infrastructure from Application
- ✅ All reusable components in `PTDoc.UI`

### Blazor (Most Common Errors)
- ✅ PascalCase component naming
- ✅ Never mutate `[Parameter]` properties
- ✅ Always show loading state for async operations
- ✅ Use `OnAfterRenderAsync(firstRender)` for JS interop

### Healthcare (Compliance)
- ✅ HIPAA audit trails for patient data
- ✅ WCAG 2.1 AA accessibility mandatory
- ✅ 15min inactivity + 8hr absolute session limits

### Platform-Specific (Critical Differences)
- ✅ Android emulator: `http://10.0.2.2:5170`
- ✅ MAUI uses `SecureStorageTokenStore` (JWT)
- ✅ Web uses cookie-based auth

---

## Metrics

| Aspect | Before | After | Change |
|--------|--------|-------|--------|
| **Total Lines** | 720 | 280 | **-61%** |
| **Code Examples** | 15 blocks | 0 blocks | **-100%** |
| **Doc References** | Mandatory first read | On-demand only | **Contextual** |
| **Decision Points** | Implicit | Explicit checklist | **+Clarity** |
| **Sections** | 12 nested | 6 flat | **-50%** |

---

## Migration Notes

### For AI Agents
- **No behavioral changes** - All critical rules preserved
- **Faster task startup** - No mandatory 1,492-line doc read
- **Clearer when to ask** - Use decision checklist to determine if human input needed

### For Developers
- **Instructions as quick reference** - Not a tutorial
- **Docs structure unchanged** - All detailed guides still in `docs/`
- **README unchanged** - Still the onboarding entry point

### For Future Updates
- **Add only deviations** - Don't document standard .NET practices
- **Link, don't duplicate** - Point to docs, don't copy content
- **Test the checklist** - New rules should fit "Use when / Skip when" format

---

## Success Criteria Met

- ✅ **Relevance-first referencing** - Doc Map with "Use when" guidance
- ✅ **Clear doc map** - 8 docs with decision tree
- ✅ **Reduced verbosity** - 61% line reduction
- ✅ **Task-driven** - Working Agreement section
- ✅ **No forced reading** - Docs consulted contextually only

---

## Appendix: Style Comparison

### Old Style (Prescriptive)
```markdown
**Canonical Context Source**

**PRIMARY REFERENCE:** Always consult `docs/context/ptdoc-figma-make-prototype-v5-context.md` 
FIRST for all PTDoc v5 implementation decisions. This consolidated document is the single 
source of truth for:
- Design system (colors, typography, components)
- UI architecture and page specifications
...
[200 more lines of examples and explanations]
```

### New Style (Task-Focused)
```markdown
### UI Implementation
- **[ptdoc-figma-make-prototype-v5-context.md]** - Design system, component specs
  - **Use when:** Implementing UI from Figma, design tokens
  - **Skip when:** Backend logic, database migrations
  - **Figma Link:** [Prototype v5](https://...)
```

**Difference:** Old style mandated reading everything. New style signals **when** to consult.

---

**Next Steps:**
1. Monitor agent performance with new instructions
2. Gather feedback on missing critical guidance
3. Iterate on Doc Map based on actual usage patterns

**Contact:** GitHub Copilot for Enterprise - PTDoc Project
