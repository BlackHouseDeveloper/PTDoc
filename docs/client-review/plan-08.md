# Plan PR 08: Make intake content and repeat encounters follow the destination note type

**Partially implemented** — only the changes and checks listed below are claimed.

- Requirements: R010, R062, R074
- Original plan status: Ready for Development
- Branch: `codex/plan-08-note-type-intake`
- Base: `main`
- Expected GitHub number: #162; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-08)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Reproduced the later-intake failure with the existing real API/OTP integration harness: expected 200, received 403 when a newer intake existed. Token claims already bind intake_id, but the read selected latest-by-patient before checking that claim. The fix filters by that ID before selecting the record. Ordinary answer updates now preserve the issued template version. These are independent prerequisites for repeat encounters; this bounded diff does not implement the note-type matrix or encounter-specific creation. It targets main because these fixes do not consume new Plan 07 contracts. Existing legacy tokens without intake_id retain their previous behavior and require a reviewed compatibility strategy before multiple-encounter activation.

## Dependencies, blockers and owners

- January/clinical owner: Plan 24 needs approved patient instrument versions/scoring; progress intake outcomes cannot be activated from the unapproved PDFs.
- January: provide the exact discharge questionnaire/source for Plan 31; discharge activation remains blocked.
- Engineering: complete encounter/type/version association, safe draft selection/save/submit and matching-note projection using Plans 20/30 and approved outcome contracts; this is remaining implementation, not completed by this prerequisite fix.

## Decisions and options

Engineering: verify exact current repeat-intake paths and deployed SHA before implementation. January supplies discharge content separately; it blocks that type only.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Discharge activation and repeat-encounter criteria.

## Development specification

### User Problem

Patients are shown irrelevant full intake sections and intake responses do not consistently align with the intended note.

### Confirmed Requirement

Full history/pain flow remains for evaluation and re-evaluation. Daily uses its short questionnaire and review; progress/discharge use their questionnaire, outcomes and review. All completed intakes retain a review step.

### Expected Behavior

A new intake is associated with its intended encounter/note type, shows only applicable steps and routes its answers into that note’s matching subjective data.

### Scope

Add explicit note-type-aware step definitions and safe repeat-encounter lifecycle; preserve prior submissions and review history.

### Out of Scope

Removing pain questions from the daily questionnaire, suppressing re-evaluation history, or guessing the missing discharge questionnaire.

### Implementation Guidance

**Technical Recommendation:** share a versioned questionnaire/step definition between intake and destination note. Replace patient-wide locked-intake exclusion with an authorized encounter-specific rule only after tracing delivery/link contracts. Keep already issued links tied to their original schema/version.

### Data and Migration Considerations

Likely additive encounter/type/version linkage and projection changes; assess migrations across configured providers. Historical signed/locked responses must remain renderable. Do not rewrite them to the latest questionnaire.

### Security and Privacy Considerations

**Technical Recommendation:** enforce patient-link scope, note ownership and submission state server-side; a URL type change cannot reveal other questionnaires or records.

### Acceptance Criteria

- [ ] **AC1:** Given a new evaluation or re-evaluation intake, when opened, then full approved history/pain sections and review are available.
- [ ] **AC2:** Given a daily intake, when opened, then its short questionnaire and review appear without the full history/pain modules or new outcome entry.
- [ ] **AC3:** Given a progress intake, when completed, then its questionnaire and approved outcomes reach the matching progress note.
- [ ] **AC4:** Given a prior locked intake and a valid later encounter, when creating the next intake, then the prior record remains unchanged and the new encounter can receive a distinct intake.
- [ ] **AC5:** Given an existing issued link, when the definition changes, then its saved answers still render under the issued version.

### Rollout and Regression Risks

Existing links and clinical projections can regress. Introduce by note type and keep version-compatible rendering.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter 'FullyQualifiedName~Standalone_Intake_Invite_Allows_Draft_Load_Update_And_Submit' --verbosity minimal -m:1 -p:BuildInParallel=false | Before fix: expected regression failure, 403 instead of 200 after creating a later intake. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~Intake&#124;FullyQualifiedName~AuthorizationCoverageTests' --verbosity minimal -m:1 -p:BuildInParallel=false | PASS on independent main base: 299 passed, 0 failed/skipped; Debug test graph compiled. |
| dotnet format whitespace src/PTDoc.Api/PTDoc.Api.csproj --no-restore --include src/PTDoc.Api/Intake/IntakeAccessEndpoints.cs src/PTDoc.Api/Intake/IntakeEndpoints.cs --verify-no-changes --verbosity minimal | PASS |
| dotnet format whitespace tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --include tests/PTDoc.Tests/Integration/EndToEndWorkflowTests.cs --verify-no-changes --verbosity minimal | PASS |
| python3 /private/tmp/ptdoc-client-implementation/workflow.py check 8 | PASS: requirements, sections, links, sensitive paths and whitespace. |

## Remaining acceptance work

Complete the approved evaluation/re-evaluation and daily encounter paths, and retain explicit activation gates for progress outcomes and discharge content. Add encounter-specific persisted context and concurrency rules; update all patient-wide latest/save/submit/seed queries together. Test legacy and new issued links with their original definitions. This PR contains only the verified link/version prerequisites.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
