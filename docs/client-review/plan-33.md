# Plan PR 33: Verify and remove redundant Settings scrolling

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R002
- Original plan status: Verification Required
- Branch: `codex/plan-33-settings-scrolling`
- Base: `main`
- Expected GitHub number: #187; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-33)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Reproduced on current #154 head 89cb3fc at widths 1440/768/390: outer document heights were 1955/2954/3898 pixels for a 900-pixel viewport while main already scrolled. Three absolutely positioned screen-reader labels used the outer .page as their containing block and escaped the clipped content area. Positioning .settings-page-container makes it their containing block; document height returns to 900 at all widths while main retains its full scroll height. The changed stylesheet is byte-identical on main and #154 before this fix, so this is an independent main-based delta. Compiled CSS was verified against #154; hidden labels and their accessible names remain present.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

The reproduced cause supports a Settings-only containing block. Retain intentional sidebar scrolling and long-form content scrolling. Do not remove accessibility labels or globally hide overflow.

## Development specification

### User Problem

The client sees an unnecessary extra scrollbar on Settings.

### Confirmed Requirement

Remove the redundant Settings scroll container without losing access to content.

### Expected Behavior

Settings has predictable scrolling and all controls remain reachable at supported viewport sizes.

### Scope

Reproduce in the integrated Settings build and change only the responsible layout/overflow rule.

### Out of Scope

Broad application styling cleanup or removing necessary scrolling from long content.

### Implementation Guidance

**Technical Recommendation:** inspect scroll ownership in the actual layout, use existing tokens and test keyboard focus/zoom before changing overflow. Coordinate with #154 to avoid a parallel CSS fix that is overwritten.

### Data and Migration Considerations

No data migration.

### Security and Privacy Considerations

**Technical Recommendation:** keep hidden/offscreen controls accessible only to their authorized roles; layout changes must not reveal restricted panels.

### Acceptance Criteria

- [ ] **AC1:** Given Settings at supported desktop/tablet/phone widths, when content exceeds the viewport, then one intentional page/content scroll path reaches all controls.
- [ ] **AC2:** Given keyboard navigation and zoomed text, when focus moves through Settings, then focused controls remain visible and usable.

### Rollout and Regression Risks

Removing overflow blindly can clip forms; verify against the integrated Settings stack.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet build src/PTDoc.Web/PTDoc.Web.csproj --no-restore --verbosity minimal (89cb3fc plus exact Settings CSS delta) | PASS, 0 warnings and 0 errors. |
| Synthetic Chromium before/after scroll metrics, screenshots, keyboard and 100%/200% root-text matrix at 1440/768/390 | PASS scrollbar cause/fix and keyboard reachability. Normal text has no horizontal overflow; existing 200% phone shared-header overflow independently reproduced and retained as a limitation. |
| python3 workflow.py check 33; git diff --check; compare Settings stylesheet between main and #154 | PASS traceability/links/whitespace; pre-change stylesheet is identical, permitting an independent PR. |

## Remaining acceptance work

Ready for the verified redundant-vertical-scrollbar fix. Full screen-reader and native-device acceptance remain unrun. Separate existing shared-header overflow at 390 pixels with 200% root text remains: approximately 430–434 pixel document width with or without the fix; Settings footer keyboard focus remains visible. That shared-shell text-reflow issue is outside this bounded fix.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
