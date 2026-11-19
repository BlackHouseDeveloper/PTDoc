# Plan PR 11: Group PHI release information and remove intake revocation controls

**Implemented for the full and visit-specific intake workflows.**

- Requirements: R021, R047
- Original plan status: Ready for Development
- Branch: `codex/plan-11-intake-consent`
- Base: `main`
- Pull request [#166](https://github.com/BlackHouseDeveloper/PTDoc/pull/166)
- [Authoritative plan brief](source/implementation-plan.md#pr-11)
- Review base on 2026-11-18: `a75ca1da3da7c1b5008621866081df8ec950d682` (#165). The October implementation used `6276027251609df64ffbbfa883f66e2d83dd57e4`.

## Current evidence and changes

The Review step groups PHI release authorization, its effective status, authorized contacts and recipient checkboxes. Written revocations appear as read-only history. Both the UI and API use `HasRequiredPhiRecipient`. An active release with populated contacts requires a matching recipient. A previously revoked release can retain its original affirmative flag and submit with an empty recipient list when its required agreements are complete.

Draft normalization preserves recorded revocation keys when legacy flags are false or absent. Recorded acceptance can also be absent. Historical recipient IDs survive when a corresponding contact row is unavailable. Staff and patient-link updates, direct-service saves and offline sync protect stored evidence against stale edits. A caller attempting to discard a recorded revocation must reload. The direct service reads the written-revocation record on resume, and submission retains it in the locked response snapshot. The organizational written-revocation endpoint and its audit trail continue to operate.

### Rebase and feedback

The local tip `5eb273e8` and published tip `ff87501c` had the same Plan 11 source and test patch, with different changelog context. Both tips have local recovery refs. Only the Plan 11 commit was replayed. The changelog conflict retained all entries from main. The ReviewStep conflict retained the required short-visit treatment handler and removed the obsolete revocation-entry handlers. The incremental diff preserves #164’s section navigation and #165’s medical history and functional-limitation work. The visit definitions, route-loading protections, outcome scoring, note and PDF implementations remain on their merged versions.

| Source | Classification | Evidence and disposition |
| --- | --- | --- |
| Recipient selection on a revoked release | Valid | The previous UI and server validators read the historical affirmative flag while the recipient inputs were disabled. Full and Daily HTTP tests now exercise written revocation, stale-save rejection, adding a contact, submission with no recipient and locked-state rejection. [Inline finding](https://github.com/BlackHouseDeveloper/PTDoc/pull/166#discussion_r4376144109) |
| Review summary | Duplicate | It links to the same inline finding. [Published review](https://github.com/BlackHouseDeveloper/PTDoc/pull/166#pullrequestreview-5650487755) |

The published review had one inline thread, one review and zero conversation comments. All feedback pages were exhausted. The public review displayed no Previously missed section. The linked private Copilot session returned 404 in the signed-out browser, so unpublished session-only findings could not be assessed.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

The meeting confirms this bounded layout/control removal. No change to legal consent text or the separate organizational revocation process is included.

## Development specification

### User Problem

Related PHI-release details are separated and the intake exposes unwanted revocation controls.

### Confirmed Requirement

Place release consent with authorized contacts/additional details and remove the revoke controls from this intake workflow.

### Expected Behavior

Patients review the relevant release choice and its contact details together; this intake no longer offers the identified revocation controls.

### Scope

Layout and input binding changes, with compatible display of existing packets.

### Out of Scope

Changing legal consent text, eliminating all organizational revocation processes, deleting historical revocation facts or inventing compliance policy.

### Implementation Guidance

**Technical Recommendation:** reuse IntakeConsentPacket and existing validation; remove only the identified entry controls. Preserve backend interpretation of historical values and make review output truthful.

### Data and Migration Considerations

No destructive migration. Existing revoked records retain their state and evidence; do not convert them to consent by omission.

### Security and Privacy Considerations

**Technical Recommendation:** consent changes require current authorized intake context and existing audit/provenance behavior. Do not log contact details.

### Acceptance Criteria

- [x] **AC1:** Given a patient completing release consent, when reviewing it, then the release choice and authorized-contact details appear together.
- [x] **AC2:** Given a new intake, when reaching consent, then the removed revoke controls are absent.
- [x] **AC3:** Given a historical revoked packet, when viewed after rollout, then its recorded state remains unchanged and is not presented as affirmative consent.

### Rollout and Regression Risks

Omitted controls can accidentally reset booleans. Test serialization defaults and historical records explicitly.

## Historical verification on the October implementation

The following checks predate the rebase and are retained for traceability.

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~Intake' --verbosity minimal -m:1 -p:BuildInParallel=false | PASS: 296 tests, 0 failed, 0 skipped, including new/resumed/submitted compatibility cases. |
| Local Playwright: edit synthetic authorized contact, select PHI recipient with Space, continue/save and reload | PASS: grouped release choice, contact and recipient persist. |
| dotnet format whitespace (Application and Tests, --no-restore --verify-no-changes --include affected C# files) | PASS. |
| python3 workflow.py check 11; git diff --check | PASS: requirements, specification, local links and whitespace. |

## Verification of the rebased head `8eabc212` against November 18 main

Combined-source checks use .NET SDK 8.0.417, the repository workflow’s Release test project and serial xUnit collection setting. GitHub’s PR checks identify the published head.

| Local check | Result |
| --- | --- |
| Release build of the test project and its Web, API and shared dependencies | Passed with zero warnings and errors |
| Affected intake, outcome, sync, note workspace, progress activity and PDF tests | 1,000 passed, 0 failed, 0 skipped |
| CoreCi | 2,721 passed, 0 failed, 0 skipped |
| Full project format verification | Core, Application, Infrastructure, API, UI and Tests all passed |
| iOS Simulator, Android and Mac Catalyst Debug builds | All three passed with zero warnings and errors |
| Synthetic Chromium | 15 passed, 0 failed, 0 skipped. Evaluation, Re-evaluation, Daily, Progress and Discharge at 390, 768 and 1440 px |
| Incremental diff and Git whitespace | Reviewed against current main. Whitespace checks passed |

The verified source and test diff has Git blob hash `05e3bad0fe77ef9c5f002354924cd51dcbf83104` when computed with `git diff --no-ext-diff --binary a75ca1da -- src tests | git hash-object --stdin`. The PR description identifies the published commit and its GitHub Actions results. Local screenshots and the per-scenario pass list are under ignored `output/playwright/pr166-review/`. The original test count below the development specification belongs to October.

```sh
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --configuration Release
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'FullyQualifiedName~Intake|FullyQualifiedName~Outcomes|FullyQualifiedName~PdfIntegrationTests|FullyQualifiedName~SyncClientProtocolTests|FullyQualifiedName~NoteWorkspaceV2ServiceTests|FullyQualifiedName~ProgressActivity' --verbosity normal --logger 'trx;LogFileName=pr166-affected.trx' -- xUnit.ParallelizeTestCollections=false
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'Category=CoreCi' --verbosity normal --logger 'trx;LogFileName=test-results.trx' -- xUnit.ParallelizeTestCollections=false
for project in src/PTDoc.Core src/PTDoc.Application src/PTDoc.Infrastructure src/PTDoc.Api src/PTDoc.UI tests/PTDoc.Tests; do
  dotnet format "$project/${project##*/}.csproj" --verify-no-changes --verbosity diagnostic || exit 1
done
for platform in ios android maccatalyst; do
  dotnet build src/PTDoc.Maui/PTDoc.csproj -f "net8.0-$platform" -c Debug -m:1 -p:BuildInParallel=false -nr:false || exit 1
done
git diff --check origin/main
```

The browser matrix uses a disposable Development SQLite database, local API/Web hosts and synthetic notes. For each visit type, use Space to select release and a named recipient, save, reload and compare the contact and selection IDs. In the full intake, activate Edit medical history with Enter, verify the focused heading, then check the saved medications and both Other fields. For the historical case, apply a written PHI revocation to a packet with no selected recipients. Add a contact in the resumed draft, submit, reload and check the stored flag, key and timestamp alongside the disabled controls. The browser assertions also check the group’s bounds and document width.

## Lite re-review of `8eabc212`

Copilot reported two new findings and marked the earlier recipient-validation finding resolved. [Lite review](https://github.com/BlackHouseDeveloper/PTDoc/pull/166#pullrequestreview-5966642429)

| Finding | Verification and disposition |
| --- | --- |
| Stale saves could flip the recorded acceptance of a revoked consent | Invalid. All supported revocable acceptance fields are nullable booleans. The existing comparison distinguishes JSON `True`, `False` and an absent property (`Undefined`). Nine tests on the reviewed code confirmed that the three identical pairs pass and all six changes are rejected. [Finding](https://github.com/BlackHouseDeveloper/PTDoc/pull/166#discussion_r4622096970) |
| A revoked final attestation could still be submitted | Valid. The original HTTP tests returned 200 for full Evaluation and shortened Daily intakes, and the direct service also submitted the draft. The fix checks the canonical revocation before submission. Staff and verified patient-link requests now receive 400 with a field error. The direct service rejects submission and still allows draft saves. The tests check that the draft stays unlocked and its evidence survives. [Finding](https://github.com/BlackHouseDeveloper/PTDoc/pull/166#discussion_r4622097031) |

On the corrected source, the Release build and the Debug iOS, Android and Mac Catalyst builds succeeded with zero warnings and errors. All 624 intake tests and all 2,733 CoreCi tests passed with zero skips, including the 12 new cases. Format verification passed for Core, Application, Infrastructure, API, UI and Tests.

Synthetic Chromium verification passed 30 of 30 scenarios on the corrected source. At each of 390, 768 and 1440 px, all five visit types passed the existing PHI release scenario and a separate final-attestation revocation scenario. After reload, the accuracy checkbox and Submit were disabled. Directly submitting the same synthetic draft returned a field-specific 400 and preserved the unlocked draft and consent record. The per-scenario results are under ignored `output/playwright/pr166-lite-review/matrix.txt`.

```sh
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'FullyQualifiedName~Intake' --verbosity quiet --logger 'trx;LogFileName=pr166-lite-intake.trx' -- xUnit.ParallelizeTestCollections=false
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'Category=CoreCi' --verbosity normal --logger 'trx;LogFileName=pr166-lite-core.trx' -- xUnit.ParallelizeTestCollections=false
```

## Remaining acceptance work

Client acceptance, native device interaction and screen-reader testing remain open. The clinic policy owner retains responsibility for its separate written-revocation process.
