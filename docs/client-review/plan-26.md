# Plan PR 26: Capture patient personal goals and use them in SMART-goal drafting

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R064
- Original plan status: Ready for Development
- Branch: `codex/plan-26-patient-personal-goals`
- Base: `main`
- Expected GitHub number: #180; actual: [#183](https://github.com/BlackHouseDeveloper/PTDoc/pull/183)
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-26)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Added the optional personal-goal question directly after functional limitations, with an associated label/help text and 2,000-character limit enforced by server normalization. Whitespace and line breaks remain verbatim in stored intake and assessment; old drafts without a goal remain valid. Existing intake seed context retains source ID and submitted/draft attribution. Added dedicated patient-reported goal context to request, service and provider prompt paths, explicitly excluding inferred measurements/targets/timeframes from the patient statement. Existing generated suggestions remain unaccepted until clinician action; pending suggestions are filtered out of persisted clinical goals. No schema migration. Browser review and saved evaluation reload preserve the synthetic goal exactly; mobile screenshot inspected. CI follow-up corrected initializer whitespace using the owning-project formatter. The C# diff changes only initializer spacing/line breaks, confirmed by whitespace-normalized source comparison; feature behavior and prior passing feature tests are unchanged.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

Confirmed wording: optional question after limitations, attributed as the patient’s own words. Client wording acceptance remains a reviewer check; no new clinical target/timeframe rule is introduced.

## Development specification

### User Problem

Patient priorities are not captured alongside limitations and may not guide clinician goal creation.

### Confirmed Requirement

Add a personal-goal question after functional limitations and carry it into clinician-reviewed SMART-goal work.

### Expected Behavior

The patient’s own goal is retained as attributed input and available to the clinician and approved goal-drafting request.

### Scope

Intake capture, review, projection and goal-context plumbing.

### Out of Scope

Automatic clinical targets/timeframes, automatic acceptance or rewriting the patient’s statement as a measured baseline.

### Implementation Guidance

**Technical Recommendation:** preserve verbatim patient input and distinguish it from clinician-authored goals. Add to the existing generation context rather than creating a second goal engine; apply the established AI acceptance flow.

### Data and Migration Considerations

Additive optional intake field and compatible mapping to existing assessment data. Keep old notes with no patient goal valid.

### Security and Privacy Considerations

**Technical Recommendation:** include only authorized encounter data in AI requests and keep generated suggestions outside signed content until reviewed.

### Acceptance Criteria

- [ ] **AC1:** Given a patient completing limitations, when entering a personal goal, then it appears in intake review and the linked clinician assessment after reload.
- [ ] **AC2:** Given goal generation, when context is built, then that personal goal is available as patient-reported context without invented measurements.
- [ ] **AC3:** Given a generated suggestion, when the clinician has not accepted it, then it is not treated as an accepted clinical goal.

### Rollout and Regression Risks

Patient wording may be mistaken for measured clinical fact; preserve attribution.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter 'FullyQualifiedName~IntakeStructuredDataJsonTests&#124;FullyQualifiedName~IntakeServiceTests&#124;FullyQualifiedName~StructuredIntakeComponentsTests&#124;FullyQualifiedName~NoteWorkspaceV2ServiceTests&#124;FullyQualifiedName~NoteWorkspacePayloadMapperTests&#124;FullyQualifiedName~ClinicalGenerationServiceTests&#124;FullyQualifiedName~ClinicalPromptBuilderTests&#124;FullyQualifiedName~AssessmentWorkspaceSectionPrognosisTests&#124;FullyQualifiedName~AiEndpointErrorContractIntegrationTests' --verbosity minimal -m:1 | 196 passed, 0 failed, 0 skipped; includes verbatim persistence, old drafts, length rejection, source intake association, saved note reload, AI context and explicit acceptance. |
| Task-local Playwright CLI: synthetic intake save/review/reload, evaluation prefill/save/reopen, SQLite exact-value inspection, keyboard order and 390/768/1440 viewports | Passed: exact spaces/line break retained in intake and note, review and assessment display, source association, keyboard order and no horizontal overflow. Mobile screenshot inspected; AI calls mocked only. |
| python3 workflow.py check 26; git diff --check | Traceability, local links, sensitive-path and whitespace checks pass. |
| dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --verify-no-changes --verbosity minimal; task-local whitespace-normalized source comparison | PASS: complete owning-project CI formatter; whitespace-only C# correction. Existing feature-test results remain applicable. |

## Remaining acceptance work

AC1–AC3 pass for current evaluation routing and the existing reviewed goal workflow. Browser UI and saved database values agree. Live AI quality/provider validation and full MAUI/assistive-technology testing remain separate acceptance checks. Intake resume/save navigation improvements are owned by Plan 09; this branch uses the existing save path and validation. Coordinate versioned encounter routing with Plan 08 before broader intake types.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
