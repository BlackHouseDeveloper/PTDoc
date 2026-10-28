# Plan PR 13: Edit provider-directory entries and carry fax details into care teams

**Partially implemented** — only the changes and checks listed below are claimed.

- Requirements: R023, R026
- Original plan status: Ready for Development
- Branch: `codex/plan-13-provider-directory`
- Base: `main`
- Expected GitHub number: #167; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-13)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Implemented active and pending provider edits through the existing directory-maintainer permission. The broader candidate-update endpoint still refuses active edits. Save preserves provider status, unexposed email/address/taxonomy and submission provenance. Stale update/removal requests are rejected; invalid directory fax formats are rejected before persistence. Delete requires an explicit confirmation explaining archive semantics and preserves existing patient relationships and saved intake snapshots.

Primary/referring fax now follows directory selection, same-physician copying, intake draft save/reload and review. Patient-entered new provider candidates receive their fax at projection; existing directory entries and historical clinical evidence are not rewritten. No care-team email was added and no EF migration is required.

The client confirmation of whether Save should activate a new/pending provider remains unanswered. Existing submission/Approve/Reject/merge actions therefore remain; this PR does not claim the full action redesign is complete. There is no bulk activation or permission expansion.

## Dependencies, blockers and owners

- January/product owner: answer the pending Save activation question before replacing Approve/Reject. Existing maintainer roles and separate approval remain unchanged.
- Client acceptance: confirm archive-based Delete satisfies removal; hard deletion is not implemented.

## Decisions and options

Pending question: should an existing directory maintainer saving a new/pending provider make it available for future selection, or should it remain pending until separately approved? No answer has been inferred. Existing permissions and duplicate/merge checks remain authoritative. Archive preserves historical references.

## Development specification

### User Problem

Clinicians need to correct/remove provider entries and reuse fax information instead of repeatedly typing it.

### Confirmed Requirement

Replace the approval/rejection workflow with edit/delete and carry selected provider details, including fax, into care-team intake fields. Email was explicitly declined.

### Expected Behavior

Authorized directory maintainers can edit or remove an entry from future selection; selecting a provider populates the appropriate saved contact/fax details.

### Scope

Directory action redesign, existing-record compatibility and care-team fax propagation.

### Out of Scope

Care-team email, automatic external provider verification or retroactive mutation of signed notes.

### Implementation Guidance

**Technical Recommendation:** reuse existing update contracts. Implement delete as archive/deactivation when an entry has clinical references; confirm that visible behavior satisfies the client. Preserve candidate/merge provenance instead of bypassing server permission checks.

### Data and Migration Considerations

Existing pending/approved entries need a transition rule; referenced entries retain historical names/fax snapshots. Add fax projection only if existing contracts lack it.

### Security and Privacy Considerations

**Technical Recommendation:** restrict maintenance to approved existing roles, validate fax format and clinic visibility, and audit changes without exposing unnecessary patient details.

### Acceptance Criteria

- [ ] **AC1:** Given an authorized maintainer, when correcting a provider and reloading, then the edited details persist.
- [ ] **AC2:** Given a removed referenced provider, when selecting a provider for a new intake, then it is unavailable while old clinical records remain readable.
- [ ] **AC3:** Given selection of a provider with a fax number, when opening intake review, then the correct details are populated and retained.
- [ ] **AC4:** Given an unauthorized user, when directly invoking directory mutation, then the API denies it.

### Rollout and Regression Risks

Hard deletion could break referrals and fax history; transition pending entries deliberately.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~ProviderDirectory&#124;FullyQualifiedName~IntakeServiceTests&#124;FullyQualifiedName~StructuredIntakeComponentsTests&#124;FullyQualifiedName~ProviderAdministrationEditAndArchive&#124;FullyQualifiedName~AuthorizationCoverageTests&#124;FullyQualifiedName~RbacRoleMatrixTests' --verbosity minimal -m:1 | PASS: 148 tests; 0 failed, 0 skipped. Includes compiled API/Web/UI and direct HTTP PT/PTA/Patient denials, foreign-clinic mutations, historical archive preservation, fax selection/review and draft persistence. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~ProviderDirectorySettingsPanelTests' --verbosity minimal -m:1 | PASS: 6 tests; 0 failed, 0 skipped after extending edit/delete to pending rows. Existing status retained; stale errors preserve editor input. |
| dotnet format whitespace <affected Application/Infrastructure/Api/UI/Tests project> --no-restore --include <changed C# files> --verify-no-changes | PASS for all changed C# files. |
| Playwright CLI: local synthetic admin provider edit, validation, reload, intake selection/review/reload, delete/reload; SQLite readback | PASS: invalid fax retained, edited fax persisted, both intake fax values persisted, archive removed provider from selection while saved intake snapshots remained unchanged. Initial intake continuation paused at a missing required body region; corrected the synthetic fixture and completed the flow. |
| python3 workflow.py check 13; git diff --check | PASS: requirement/section/link/path/whitespace checks. |

## Remaining acceptance work

Answer the Save activation question, then implement and verify the approved transition and action labels without weakening duplicate checks. Confirm archive semantics with the client. Native mobile/platform publish and screen-reader testing remain unverified.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
