# Plan PR 10: Clarify medical history, medications and functional limitations

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R014, R015, R016, R022, R063
- Original plan status: Ready for Development
- Branch: `codex/plan-10-intake-language`
- Base: `main`
- GitHub PR: [#165](https://github.com/BlackHouseDeveloper/PTDoc/pull/165)
- [Authoritative plan brief](source/implementation-plan.md#pr-10)
- Current main: `f9f33e392ae591af95f227fabe09b8eb2276a751` (2026-11-18). Original October baseline: `6276027251609df64ffbbfa883f66e2d83dd57e4`.

## Current evidence and changes

Existing intake exposed a Comorbidities label, no section-bound Other medication/history fields, and collapsed groups for only the first selected region. Added independent nullable JSON fields, review binding and evaluation seed projection. Existing stored activity values remain untouched because their text participates in selection identity and canonical validation. All four runtime reference/catalog assets are byte-identical to current main, and historical locked review retains original activity text. No EF model change or migration.

## Current review preparation — 2026-11-18

Base: `f9f33e392ae591af95f227fabe09b8eb2276a751` (`origin/main`, squash merge of #164). GitHub confirmed Plans 01–09 and #200–#203 merged; #165 remains open against `main`. Historical October results below do not verify this combined branch.

### Branch reconciliation

The initial checkout was the correct branch with an unfinished merge and local/remote divergence. During read-only inspection, another process finished merge `d28a1cb83ba4a61543262e3a0e73c280a7e86b56`; the user confirmed that process was finished and requested inspection. Its tree was byte-identical to published head `2d67e83c9664c82bb1ed905babc3d38594680c3f`. Local feature commit `d3c61b14` carried the same Plan 10 patch (only changelog whitespace differed). No unique local implementation was discarded.

Recovery refs: `recovery/pr165-local-before-rebase-20261118` → `d28a1cb8`; `recovery/pr165-remote-before-rebase-20261118` → `2d67e83c`. After fetching, `git rebase --onto origin/main 2d67e83c9664c82bb1ed905babc3d38594680c3f codex/plan-10-intake-language` replayed the single local Plan 10 commit, avoiding the duplicate remote feature and already-integrated squash history. Upstream remains `origin/codex/plan-10-intake-language`.

Three conflicts were resolved: retain both changelog histories; combine Plan 09's `IntakeSectionHeading` with Plan 10 instructions; retain body-orientation coverage and both Plan 10 component tests. Other merged behavior remains in the base: encounter scoping, section navigation/focus, isolated heading styles, body-map accessibility, short-visit questionnaires, consent, approved scoring, immutable outcome evidence and source ownership. No workflow, instrument, catalog, EF model or migration change.

### Review findings

Refreshed thread-aware connector data and authenticated GraphQL pagination: one thread with one comment, one review, zero conversation comments; every `hasNextPage` was false. The visible GitHub review contains no Previously missed section. Its private [review session](https://github.com/BlackHouseDeveloper/PTDoc/sessions/502c5ddd-be72-4b82-8997-a106bcd64d14) returned 404 in the available signed-out browser; unpublished/suppressed session-only findings remain inaccessible and are not claimed absent.

| Source | Classification | Evidence and disposition |
| --- | --- | --- |
| [4375919520](https://github.com/BlackHouseDeveloper/PTDoc/pull/165#discussion_r4375919520): submitted review/prefill changes difficulty to inability | Valid, fixed | The original `State.IsLocked` branch used canonical `Activity`, and evaluation seeds used the same original string. Persist an optional per-selection `wordingVersion`; render draft/locked review and Evaluation/Re-evaluation prefill from that version while retaining canonical descriptions. Tests cover normalization, save/submit/reload, locked/unlocked review, historical selections and both encounter evaluation types. |
| [5650139957](https://github.com/BlackHouseDeveloper/PTDoc/pull/165#pullrequestreview-5650139957): same wording defect | Duplicate | Same underlying defect and source thread; handled once. |
| Final downstream audit: display text in the seed description breaks catalog matching and progress duplicate checks | Valid, fixed | Seed and workspace entries retain canonical descriptions plus wording metadata. UI mapping, review, progress rendering and document projection use the version; canonical matching, activity hashes and duplicate rejection remain unchanged. Regression tests exercise checked catalog choices, note round-trips, baseline identity and duplicate rejection. |
| Same review: client-copy/native accessibility acceptance remains | Valid acceptance note | Automated keyboard/responsive checks do not establish client sign-off, VoiceOver or native-device acceptance. Retained as remaining acceptance work; no speculative clinical-policy change. |

The thread remains unresolved, with no reply or new review request. No invalid/outdated code findings were inferred from resolution flags.

### Implementation and compatibility

New selections carry `difficulty-v1`; their canonical `Activity` and stable `Id` remain untouched. The shared Application helper is usable by UI and Infrastructure without crossing architecture boundaries. Missing versions preserve original wording, including when historical selected entries are viewed in the selection card. Unsupported versions are rejected by structured-data validation. Evaluation prefill copies canonical descriptions and wording metadata into new seed entries, retaining selection IDs. Workspace mapping preserves both through save/reload, while note review, assessment/document projection and progress activity rendering use the confirmed display wording. Progress activity hashes and duplicate checks continue to use canonical descriptions. It does not rewrite the intake, catalogs or existing notes; unversioned records retain original wording.

Independent `otherMedications` and `otherMedicalHistory` JSON values retain the 2,000-character and None-consistency checks. HTTP regressions verify rejection before create/update persistence; service tests verify save, submit and fresh database reload with multiple IDs, both Other values, surgery text and activity identity. Full-history Other fields remain inside the existing full-flow review branch; all three short visit types retain their shortened questionnaires.

### Current verification

All commands run from the repository root unless specified. Runtime code was unchanged after the final Release build. Exact command results:

| Command/check | Result |
| --- | --- |
| `dotnet restore tests/PTDoc.Tests/PTDoc.Tests.csproj` | PASS. The first sandboxed attempt stalled and was terminated; the permitted retry completed. |
| `dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --configuration Release` | PASS; 0 warnings/errors; API, UI, Web and test dependency graph. |
| Affected regression command below | PASS; 963 passed, 0 failed/skipped, including the expanded note/progress/document regressions. |
| Current CoreCi workflow command below | PASS; 2,707 passed, 0 failed/skipped; 7m04s. |
| Owning-project/Core CI formatting loop below | PASS; all six projects; 0 files formatted. |
| `dotnet build src/PTDoc.Web/PTDoc.Web.csproj --no-restore --configuration Debug` | PASS; 0 warnings/errors. Refreshes Development's Debug-first static-asset fallback after diagnosing old Debug CSS being served beside fresh Release binaries. No source-style change was required. |
| `python3 /private/tmp/pr165-browser-run.py` | PASS again on corrected binaries; 15/15 scenarios, 0 failed/skipped: five visit types at 390, 768 and 1440 px. |
| `python3 /private/tmp/pr165-prefill-browser.py` | PASS; 2/2 explicit prefill → save → reload → note-review cases, with both Other values, checked catalog choices, confirmed wording and canonical database descriptions/IDs preserved. |
| Compare four runtime JSON assets byte-for-byte with `git show origin/main:<path>` | PASS; workspace catalog, outcome reference data, intake supplemental data and patient outcome questionnaires are identical to current main. Source catalog/scoring directories are also unchanged. |
| `python3 .github/scripts/scan_secret_policy.py` | PASS; 9 tracked configuration files. |
| `git diff --check`; incremental diff against `origin/main` | PASS; reviewed incremental diff and staged whitespace before publication. |

```bash
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'FullyQualifiedName~Intake|FullyQualifiedName~Outcomes|FullyQualifiedName~NoteWorkspaceV2ServiceTests|FullyQualifiedName~NoteWorkspacePayloadMapperTests|FullyQualifiedName~ProgressActivity|FullyQualifiedName~StructuredWorkspaceEditorsTests|FullyQualifiedName~SoapReviewPageTests|FullyQualifiedName~PdfIntegrationTests' --verbosity normal --logger 'trx;LogFileName=pr165-affected.trx' -- xUnit.ParallelizeTestCollections=false

dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter 'Category=CoreCi' --verbosity normal --logger 'trx;LogFileName=test-results.trx' -- xUnit.ParallelizeTestCollections=false

for project in src/PTDoc.Core/PTDoc.Core.csproj src/PTDoc.Application/PTDoc.Application.csproj src/PTDoc.Infrastructure/PTDoc.Infrastructure.csproj src/PTDoc.Api/PTDoc.Api.csproj src/PTDoc.UI/PTDoc.UI.csproj tests/PTDoc.Tests/PTDoc.Tests.csproj; do
  dotnet format "$project" --no-restore --verify-no-changes --verbosity diagnostic
done
```

The first affected run had seven failures in new test setup: four validation assertions targeted the lower-level persistence service rather than the existing API guard; three short-review fixtures omitted the issued visit questionnaire. The next run identified the Progress editor's required service registration. Tests were corrected to exercise the actual HTTP validation boundary and complete short-visit component fixtures. The initial 888-case pass was followed by the expanded passing 963-case run; no production behavior was weakened to satisfy those assertions.

The final browser matrix verifies multiple medications/history selections, independent Other fields, surgery text, two-region activities, 11 initially open groups, keyboard Enter/Space selection and disclosure toggles, body-map front/back state, Review Edit focus, 16px/zero-margin headings, single-row progress and no horizontal overflow. Both full visit types retain exact values after save/reload; all three short flows save/reload without full-history sections. Screenshots at all three widths were visually inspected. Two additional browser cases applied submitted synthetic intakes through the existing clinician review action, saved/reloaded Evaluation and Re-evaluation notes, and checked their Review screen. The existing incomplete-documentation Continue prompt was handled without submitting either note; native reload prompts were accepted only for these disposable records. Note and progress component/HTTP tests cover historical wording and unchanged duplicate prevention. A read-only check of the disposable SQLite database confirms canonical `Unable to` activity values, IDs and `difficulty-v1` survive alongside the Other details and surgery notes. Early harness failures were corrected for duplicate selected-option chips and asynchronous Blazor updates; the final matrix has no failures.

Fresh synthetic browser evidence uses task-owned local API/Web ports 17650/17651, a new SQLite database and disposable Evaluation, Re-evaluation, Daily, Progress and Discharge records. No existing or hosted database is copied. Harnesses/fixtures: `/private/tmp/pr165-*`; ignored browser evidence: `output/playwright/plan10-review/`. Logs: `/private/tmp/pr165-canonical-build.log`, `/private/tmp/pr165-canonical-affected.log`, `/private/tmp/pr165-canonical-coreci.log`, `/private/tmp/pr165-canonical-verify-*.log`. All six remote workflows passed for intermediate head `21e7f876`; that result does not verify the subsequent canonical-value correction. Final exact-head CI is reported in the PR description and handoff.

Not run for this branch: native MAUI publish/install, VoiceOver, hosted-beta QA, Roslynator, SQL Server/PostgreSQL locally, or deployment. Current native build results are recorded below; historical native startup evidence belongs to #164. Local tests have no skips. Remote CI status is reported for the exact published head in the PR description and handoff.

### Native build follow-up — 2026-11-18

Verified source head: `5b1539003f09397b32dcdf0bb1309372f5548f18`, aligned with the local branch, its upstream and GitHub. Base remains `f9f33e392ae591af95f227fabe09b8eb2276a751`. Environment: Apple Silicon macOS, .NET SDK 8.0.417 and Xcode 26.6 (17F113). The MAUI project, platform configuration and SDK pin are unchanged from main. Commands follow `docs/BUILD.md` and `docs/CI.md`; Release builds additionally use the repository's serial-build flags and disable MSBuild node reuse.

| Command | Result |
| --- | --- |
| `dotnet build src/PTDoc.Maui/PTDoc.csproj -f net8.0-ios -c Debug` | PASS; 0 warnings/errors; 22.91s; `iossimulator-arm64` app. |
| `dotnet build src/PTDoc.Maui/PTDoc.csproj -f net8.0-android -c Debug` | PASS; 0 warnings/errors; 44.98s; APK artifacts produced. |
| `dotnet build src/PTDoc.Maui/PTDoc.csproj -f net8.0-maccatalyst -c Debug` | PASS; 0 warnings/errors; 9.56s; `maccatalyst-arm64` app. |
| `dotnet build src/PTDoc.Maui/PTDoc.csproj -f net8.0-ios -c Release -m:1 -p:BuildInParallel=false -nr:false` | PASS; 0 warnings/errors; 13m17.39s; `iossimulator-arm64` app. |
| `dotnet build src/PTDoc.Maui/PTDoc.csproj -f net8.0-android -c Release -m:1 -p:BuildInParallel=false -nr:false` | PASS; 0 warnings/errors; 2m55.40s; APK/AAB produced; APK contains arm64-v8a, armeabi-v7a, x86 and x86_64 libraries. |
| `dotnet build src/PTDoc.Maui/PTDoc.csproj -f net8.0-maccatalyst -c Release -m:1 -p:BuildInParallel=false -nr:false` | PASS; 0 warnings/errors; 1m36.32s; universal x64/arm64 app. |

The first iOS Release attempt lost its process session and temporary log during an environment interruption; no result is claimed for that attempt. The three Debug completions above were observed before the interruption. The iOS Release rerun completed successfully without changing source or build settings. Release logs and a manifest including the observed Debug results are retained under ignored `output/native-builds/pr165/`. Build success does not establish simulator/device installation, native intake interaction, VoiceOver, App Store signing or client acceptance.

No native build failures or warnings were found, and no application, test or build-configuration fix was needed. The follow-up commit changes only this evidence record and the changelog; its runtime source is identical to the tested head. The earlier passing 963 affected tests, 2,707 CoreCi tests and browser evidence remain applicable to that unchanged source. Feedback was refreshed: the same fixed wording finding, duplicate summary and open acceptance note remain; no review thread was changed. Native builds close the platform-build gap in `docs/CI.md`. Technical squash-merge readiness is supported once exact-head CI passes; final copy acceptance and native runtime/screen-reader acceptance remain unverified.

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

- [x] **AC1:** Given medical history or medications with multiple applicable entries, when several are selected and saved, then all survive reload.
- [x] **AC2:** Given an unlisted item, when Other text is entered, then it appears under the correct section in review and the clinician view.
- [x] **AC3:** Given a new intake limitation step, when loaded, then relevant groups are expanded initially and app-authored choices use Difficulty with.
- [x] **AC4:** Given a standardized questionnaire, when these labels change, then its approved item wording is unchanged.

### Rollout and Regression Risks

Changing labels must not orphan historical IDs or alter instrument validity.

## Historical verification — original October implementation

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

January’s acceptance of final plain-language copy and native-device/screen-reader validation remain. Current visit-specific routing is covered by this branch’s tests and browser matrix. No instrument item/scoring change is included.

## Publication and merge boundary

This review preparation authorizes commit/push and a template-based PR description update after verification. No merge, deployment, production-data change, external message, review reply/resolution or new review request is authorized. This is implemented feature work; remaining client/native acceptance is not claimed.
