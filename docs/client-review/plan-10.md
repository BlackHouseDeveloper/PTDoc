# Plan PR 10: Clarify medical history, medications and functional limitations

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R014, R015, R016, R022, R063
- Original plan status: Ready for Development
- Branch: `codex/plan-10-intake-language`
- Base: `main`
- Expected GitHub number: #164; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-10)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Existing intake exposed a Comorbidities label, no section-bound Other medication/history fields, and collapsed groups for only the first selected region. Added independent nullable JSON fields, review binding and evaluation seed projection. Existing stored activity values remain untouched because their text participates in selection identity and canonical validation. Both runtime catalog assets are byte-identical to main, and historical locked review retains original activity text. No EF model change or migration.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

Copy and Other placement await ordinary client acceptance; no unapproved clinical catalog or scoring decision was inferred.

## Development specification

### User Problem

Clinical jargon and collapsed options make intake choices harder to understand and complete.

### Confirmed Requirement

Use Past Medical History, make multiple selection clear, provide Other/free entry, expand limitation groups initially, and use Difficulty with in app-authored limitation wording.

### Expected Behavior

Patients see understandable labels, can choose multiple applicable entries, supply a missing item and scan expanded body-region limitation choices.

### Scope

Labels/instructions, Other value binding, default expanded state and app-authored limitation copy.

### Out of Scope

Rewording validated outcome instruments, changing diagnoses or adding duplicate employment/imaging fields.

### Implementation Guidance

**Technical Recommendation:** retain stable catalog IDs while changing presentation text. Use explicit multi-select instructions and bind Other text to its section. Reuse runtime WorkspaceReferenceCatalog rather than treating clinicrefdata notes as production data.

### Data and Migration Considerations

Preserve existing selected IDs and historical free text. Map renamed labels without replacing stored clinical meaning.

### Security and Privacy Considerations

**Technical Recommendation:** treat free text as patient health information; avoid analytics payloads and validate normal text limits server-side.

### Acceptance Criteria

- [ ] **AC1:** Given medical history or medications with multiple applicable entries, when several are selected and saved, then all survive reload.
- [ ] **AC2:** Given an unlisted item, when Other text is entered, then it appears under the correct section in review and the clinician view.
- [ ] **AC3:** Given a new intake limitation step, when loaded, then relevant groups are expanded initially and app-authored choices use Difficulty with.
- [ ] **AC4:** Given a standardized questionnaire, when these labels change, then its approved item wording is unchanged.

### Rollout and Regression Risks

Changing labels must not orphan historical IDs or alter instrument validity.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~Intake&#124;FullyQualifiedName~NoteWorkspaceV2ServiceTests' --verbosity minimal -m:1 -p:BuildInParallel=false | PASS: 325 passed, 0 failed/skipped. |
| dotnet format whitespace src/PTDoc.Application/PTDoc.Application.csproj --no-restore --include src/PTDoc.Application/Intake/IntakeStructuredDataJson.cs src/PTDoc.Application/Intake/IntakeStructuredDataModels.cs --verify-no-changes --verbosity minimal | PASS |
| dotnet format whitespace src/PTDoc.Infrastructure/PTDoc.Infrastructure.csproj --no-restore --include src/PTDoc.Infrastructure/Notes/Workspace/NoteWorkspaceV2Service.cs --verify-no-changes --verbosity minimal | PASS |
| dotnet format whitespace src/PTDoc.UI/PTDoc.UI.csproj --no-restore --include src/PTDoc.UI/Components/Intake/Models/FunctionalLimitationDisplayText.cs src/PTDoc.UI/Components/Intake/Models/IntakeSupplementalSelectionCanonicalizer.cs --verify-no-changes --verbosity minimal | PASS |
| dotnet format whitespace tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --include tests/PTDoc.Tests/Application/IntakeStructuredDataJsonTests.cs tests/PTDoc.Tests/UI/Intake/StructuredIntakeComponentsTests.cs tests/PTDoc.Tests/Intake/IntakeServiceTests.cs tests/PTDoc.Tests/Notes/Workspace/NoteWorkspaceV2ServiceTests.cs --verify-no-changes --verbosity minimal | PASS |
| python3 /private/tmp/ptdoc-client-implementation/browser10.py | PASS: actual local browser save/reload retained both Other strings and two medications; native Space/Enter selection; 11 activity groups initially expanded. Task-local Playwright CLI script; reproduce via synthetic Medical History intake → select two medications and fill Other fields → Continue → reload → Back. |
| Compare WorkspaceReferenceCatalog.json and OutcomeMeasureReferenceData.json bytes with git show 6276027:<path> | PASS: both assets unchanged; SHA-256 16283f87f17b32ad42257c926f310f5d058e58e54a44220bb1b5f3467dacfc6a and bb390fcbd37d9466c53d8bb6e25caa6abe12cd22b507ec820c86b8cae1d0e4ea. |
| python3 /private/tmp/ptdoc-client-implementation/workflow.py check 10 | PASS: requirements, sections, local links, sensitive paths and git diff --check. |

## Remaining acceptance work

January’s acceptance of final plain-language copy and native-device/screen-reader validation remain. Recheck the new optional JSON fields when future note-type routing is integrated. No instrument item/scoring change is included.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
