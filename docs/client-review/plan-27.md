# Plan PR 27: Place evaluation assessment narrative after contributing inputs

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R059, R068, R069
- Original plan status: Ready for Development
- Branch: `codex/plan-27-assessment-order`
- Base: `main`
- Expected GitHub number: #181; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-27)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Confirmed R069 layout implemented: assessment narrative now follows all contributing cards, including the functional-limitations editor within AssessmentTab. Reused existing components and tokens without data/schema changes. Browser DOM order and Tab navigation confirm limitations precedes narrative; 390/768/1440 widths have no horizontal overflow. Reopened synthetic note values match storage and full ContentJson SHA-256 is unchanged. R059 inspection identifies Overall symptom frequency (PainFrequency) and Frequency by selected symptom/problem (SymptomFrequencies), whose empty choice uses overall frequency; distinct fields are not proof of unintended duplication. R068 still lacks an exact field/current-versus-prior reproducer. Neither ambiguous report is labelled fixed.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

R069 ordering is confirmed. For R059/R068, January must identify the exact field/label, note type, expected value and observed value in a synthetic or de-identified example. Engineering can then trace that mapping; no unspecified field was removed or reinterpreted.

## Development specification

### User Problem

The assessment narrative appears before the information clinicians use to compose it.

### Confirmed Requirement

Move clinical assessment narrative after its contributing evaluation inputs. Investigate the reported duplicate frequency and previous-versus-current concerns only after identifying their exact fields.

### Expected Behavior

Contributing inputs precede the assessment narrative with saved data unchanged. Unverified fields are not removed speculatively.

### Scope

Layout order and focus/tab sequence; bounded verification tasks for R059/R068.

### Out of Scope

Deleting an unspecified frequency question or changing current/prior values without a reproducer.

### Implementation Guidance

**Technical Recommendation:** reorder composition without rewriting saved payloads. Capture a synthetic field-level example before implementing any additional data fix; split a proven defect into a focused follow-up.

### Data and Migration Considerations

No migration for layout. Any proven mapping fix must preserve existing values and signed records.

### Security and Privacy Considerations

**Technical Recommendation:** maintain existing draft permissions and clinician acceptance controls.

### Acceptance Criteria

- [ ] **AC1:** Given an evaluation assessment, when navigating by keyboard or reading order, then contributing inputs precede the clinical narrative.
- [ ] **AC2:** Given a saved draft, when reopening after the layout change, then all existing values remain unchanged.
- [ ] **AC3:** Given a proposed frequency/current-data fix, when evidence is reviewed, then the exact field, expected value and observed value are documented before code changes.

### Rollout and Regression Risks

A broad cleanup could delete useful clinical data; keep implementation limited to the confirmed ordering.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter 'FullyQualifiedName~AssessmentTab&#124;FullyQualifiedName~AssessmentWorkspaceSectionPrognosisTests&#124;FullyQualifiedName~NoteWorkspacePayloadMapperTests' --verbosity minimal -m:1 | 35 passed, 0 failed, 0 skipped; existing generation/read-only and mapper regressions. No implementation-mirroring layout unit test added. |
| Task-local Playwright CLI: inspect assessment DOM and keyboard order, 390/768/1440 layouts, reopened synthetic note values and stored ContentJson SHA-256 | Passed: narrative last, limitations Tab enters narrative, no horizontal overflow, mobile screenshot inspected, complete saved content hash unchanged. |
| python3 workflow.py check 27; git diff --check | Traceability sections, local links, sensitive-path and whitespace checks pass. |

## Remaining acceptance work

Confirmed layout AC1–AC2 passes; AC3 is enforced as an evidence boundary: no duplicate-frequency/current-prior data fix is proposed without an exact reproducer. Those adjacent reports remain unverified. Client review of ordering and full assistive-technology/device testing remain open.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
