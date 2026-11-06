# Plan PR 08: Make intake content and repeat encounters follow the destination note type

> **2026-11-05 review split:** The combined implementation below is preserved in four incremental layers above PR 163. Use the [review stack](plan-08-review-stack.md) for current layer boundaries, verification, publication status and reconciliation with PRs 176/187/180. Earlier single-branch status statements are historical.

**Questionnaire and patient outcome implementation complete locally; the functional checkpoint passed 2,455/2,455 CoreCi tests and focused Web/PDF verification. The subsequent styling correction passed both Release builds and 113 focused UI tests; provider/native release checks remain.** The current 2026-11-04 checkpoints below supersede earlier dependency and implementation-status statements.

- Requirements: R010, R062, R074
- Original plan status: Ready for Development
- Branch: `codex/plan-08-note-type-intake`
- Base: `main` (includes merged Plans 07 and 06)
- Expected GitHub number: #162; actual: [#163](https://github.com/BlackHouseDeveloper/PTDoc/pull/163)
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-08)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current implementation checkpoint — 2026-11-04

The owner approved Plan 08 and confirmed authority over the client decisions. The owner specified: **the questionnaire for every intake must match the Subjective section of its corresponding visit/SOAP note**. This supplies the Discharge patient-questionnaire source: the existing Discharge Subjective component. Discharge reason and recommendations remain clinician-owned. The owner subsequently chose patient selection from relevant body-region measures, delegated missing-answer policy, and approved corrected official HOOS/FAAM sources. Those decisions supersede the earlier request for further owner-provided scoring rules.

The owner subsequently supplied the two client outcome-measure ZIP archives. All 16 PDF hashes match the existing inventory. The [source matrix and owner decisions](outcome-approval-matrix.md#owner-decisions-2026-11-04) record exact source hashes, instrument-specific formulas/completion thresholds, the selection map, and corrected HOOS/FAAM provenance. All 13 patient-reported instruments are enabled in the local implementation. Four clinician-administered tests remain separate Plan 36 work.

### Rebase and publication

PR 163 was rebased onto `a787e848219abff50b63462681b3bf603d27f93b` (`main`, including merged PR 161). Its former history contained duplicated Plan 08 commits and the already-merged Plan 07 foundation. Replayed the unique Plan 08 changes plus the review-fix delta, preserving main's profile provenance/reconciliation, failed-save recovery, organization/subscription models and all five earlier review fixes. Provider snapshots retain main's definitions plus the encounter association. The rebased head `cd0810f255346fa266a7e3f96cbe42fd6d0b678f` was pushed to `codex/plan-08-note-type-intake` with an exact lease against `ec33089e9a49465ee561b26cb529dc5e9ef2ec7c`.

New implementation is **uncommitted** in `/Users/calvinccarter/.codex/worktrees/eb23/PTDoc`, branch `codex/pr-163-completion`. The original checkout remains untouched. The remote PR currently contains the rebase only; the new work requires owner-confirmed build/test results before committing under `AGENTS.md`.

### Implemented locally

- Consume the existing Daily questionnaire (Plan 20 / PR 176), Progress activity foundation (Plan 30 / PR 187), and versioned outcome contracts (Plan 23 / PR 180), including their regressions. These are integrated changes in this worktree; no other PR is merged, rebased, closed or published.
- Issue immutable `daily-subjective-v1`, `progress-subjective-v1`, and `discharge-subjective-v1` encounter questionnaires alongside unchanged `evaluation-full-v1`. The saved destination controls the type. Repeat visits get separate answers and fresh consent; historical submissions and links remain pinned to their own records.
- Reuse the actual Daily, Progress and Discharge Subjective components in patient/staff intake and read-only review. Short visits have questionnaire → review/consent navigation, without full history/insurance/pain-map modules. Daily collects no new outcome measures. Evaluation and Re-evaluation keep full history, pain and review.
- Save short answers in `IntakeForm.ResponseJson`; no additional EF columns or migration are needed beyond PR 163's existing encounter association. Reject wrong type/version, invalid pain/percentage/choice values, changed issued activity baselines and forged catalog choices. Missing current pain is distinct from explicitly recorded zero. No clinician plan or assessment is accepted in the short-answer contract.
- Pin Progress baseline and full relevant activity choices when issuing the intake. Patient links use that snapshot instead of calling staff note/catalog APIs. Newly difficult selections use stable catalog identities; earlier signed notes are untouched.
- Preserve chart demographics, insurance, legacy outcome evidence and clinician-only fields during short-intake saves/submission. Clinicians explicitly review and copy submitted patient answers into the matching editable draft. Daily questionnaire, Progress activity/percentage and Discharge report fields travel with the common pain data; objective findings, assessment, discharge reason and plan remain intact. The note retains intake ID, definition ID and answer-version provenance.
- Retain exact-record token scope, patient ownership, dual intake/note-read authorization, optimistic concurrency and PostgreSQL timestamp precision. Also discard delayed submit results after navigation to another encounter.
- Implement patient-selected, body-region-filtered questionnaires for all 13 patient instruments, with partial-save/resume, explicit zero/N/A, source-defined omission thresholds, optional unscored fields and server-only calculations. HOOS and FAAM have corrected source versions and independently scored subscales; no combined total is invented. Daily has no new outcome collection.
- Persist issued choices, responses and definition/source/scorer evidence in intake JSON. Copy selected outcomes from the locked matching intake into the editable note, display them read-only, and export completed/partial scores with provenance. Workspace/generic/sync writes reject wrong-encounter sources and evidence removal; legacy manual/aggregate results remain separate. No additional outcome migration.
- Correct issues found during live verification: two-step short-visit headers, partial-save actions, visible missing-pain validation, explicit treatment consent, fresh optional consent defaults, scoped outcome layouts and stable form models that retain keyboard focus. Existing issued answers/consents are preserved.
- Add source-boundary, typical-value, missing/N/A, partial-domain, tampered-total/choice, escaped-JSON-key, optional-zero, patient-link, locked-source, UI and PDF regressions. The CoreCi coverage was executed during the owner-authorized verification below.

### Current dependency feedback

Retrieved thread-aware feedback and full discussion from PRs 176, 180 and 187. PR 176 has no feedback. All four unique dependency findings below are verified/actionable and fixed locally; summary bullets duplicate these comments, with no additional suppressed/Previously missed finding in the returned review bodies. No GitHub replies or thread resolutions were made.

| Source | Current evidence and local remediation |
| --- | --- |
| [PR 180 / 4379541152](https://github.com/BlackHouseDeveloper/PTDoc/pull/180#discussion_r4379541152) | Registry validation dereferenced null items/options before rejecting them. Guard null elements and keep unrelated valid packages active; add both regression cases. |
| [PR 187 / 4380503858](https://github.com/BlackHouseDeveloper/PTDoc/pull/187#discussion_r4380503858) | Two activity source lookups required `NoteStatus.Signed`, unlike `CarryForwardService`'s status/signature/timestamp predicate. Align both and cover Progress/Daily/Discharge with either signature indicator. |
| [PR 187 / 4380503942](https://github.com/BlackHouseDeveloper/PTDoc/pull/187#discussion_r4380503942) | First-save fallback promoted current newly difficult selections into their own baseline. Derive that fallback from recorded functional limitations only; add a canonical-label first-save regression. |
| [PR 187 / 4380504012](https://github.com/BlackHouseDeveloper/PTDoc/pull/187#discussion_r4380504012) | Generic note creation let activity/percentage validation exceptions escape. Return `NoteOperationResponse.Errors` through existing HTTP 422 handling; add an HTTP regression. |

PR 163's refreshed inventory still contains five resolved findings, all addressed in the rebased source: patient ownership, seed read permissions, pending-save identity, PostgreSQL precision and cached invite identity. Its old summary is a duplicate historical reference, not five new defects.

### Acceptance and remaining work

| Criterion | Current status |
| --- | --- |
| AC1 Evaluation/Re-evaluation full history/pain/review | Existing implementation retained; CoreCi regressions pass and the Evaluation lumbar outcome step was inspected in the browser. |
| AC2 Daily short questionnaire/review without new outcomes | Shared Subjective content, partial save, review/consent and keyboard regressions pass; Daily browser inspection confirms no new outcomes. |
| AC3 Progress questionnaire and approved outcomes in matching note | Questionnaire/activity projection and all 13 patient instruments implemented locally: patient selection, automatic scoring, partial persistence, explicit note copy, read-only review and PDF. Plans 24/25 patient paths are incorporated. CoreCi coverage passes; browser verification covers partial scores, save/reload, locked submission, matching-note copy and read-only provenance; the exported PDF was visually inspected. |
| AC4 distinct later encounter without rewriting locked history | Extended to all supported types; API/OTP CoreCi regressions pass. Separate disposable encounter fixtures were created without rewriting the locked hip intake. |
| AC5 issued record/definition remains readable | Existing Evaluation definition retained; new types use fixed version IDs and pinned catalog snapshots. Version/type/ownership CoreCi regressions pass. Provider-specific release checks remain outstanding. |
| Discharge questionnaire source decision | Resolved by owner: use corresponding Subjective component. Patient-report projection implemented; reason/recommendation editing stays with clinicians. Plan 31's structured recommendation vocabulary is still separate work. |

After this combined implementation is accepted, reconcile PRs 176/180/187 against the landed code rather than replaying duplicate foundations. Plans 24/25 should reconcile their patient-questionnaire and note-projection acceptance against this implementation; clinician-result collection/projection, prior-result comparison and attributable correction workflows remain separate. Plan 31 should adopt the owner's Subjective-source decision; its recommendation vocabulary remains separate work. This session does not authorize merging or closing those PRs.

### Styling correction checkpoint — 2026-11-04 (Eastern)

Following the owner's styling feedback, align the new UI with the existing `BasicInfoCard`, `FunctionalLimitationsCard`, `ReviewStep` secondary actions and shared form styles. The questionnaire cards now use the same desktop padding (24px), corner radius (10px), heading (16px/medium), field label (14px) and 44px minimum control height, all through existing tokens. Add the previously missing isolated `OutcomeMeasuresStep` stylesheet; its prior-score panel no longer depends on another component's isolated styles. Patient outcome results, Progress activity controls and the note's read-only source panel use the existing surface, border, text and information-callout tokens.

Replace undefined new button classes with the existing primary button and matching secondary styles. Reuse `IntakeFooterActions` and `PainProgressBar` for short visits. The full intake retains five desktop labels; on narrow screens it shows the current step and count without mid-word wrapping. Short visits retain two labels and half/full progress. No clinical scoring or answer contract changes in this pass.

Both `dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Release` and `dotnet build src/PTDoc.Web/PTDoc.Web.csproj --configuration Release` pass with zero warnings/errors. The following final-build regression command passes **113/113**, zero skipped:

```bash
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "FullyQualifiedName~PTDoc.Tests.UI.Intake|FullyQualifiedName~PTDoc.Tests.UI.Notes.ProgressActivityTests|FullyQualifiedName~PTDoc.Tests.UI.Pages.NoteWorkspacePageTests" --verbosity normal
```

Headed Chrome against the same disposable SQLite fixtures confirms editable outcomes in light/dark themes at 390/768/1440 pixels, with no horizontal overflow; read-only copied outcomes retain dark input/foreground colors and disabled controls. Compare rendered properties with the existing Basic Information card, inspect the Evaluation and short-visit headers, and exercise review/edit using the existing 48px secondary button. Local evidence is ignored under `output/playwright/plan08-style-verification/`; build/test logs are `/tmp/ptdoc-plan08-style-build.log`, `/tmp/ptdoc-plan08-style-web-build.log` and `/tmp/ptdoc-plan08-style-tests.log`. `git diff --check` passes. Temporary app hosts and the task browser are stopped after verification. No implementation commit, push, deployment or GitHub review write occurred.

The full CoreCi and PDF results below are the preceding functional checkpoint, not a repeated full-suite claim for this styling correction.

### Functional verification checkpoint — 2026-11-04 (Eastern; artifacts stamped 2026-11-05 UTC)

The owner explicitly authorized running the three requested terminal commands and visual verification outside the usual sandbox. This overrides the normal `AGENTS.md` requirement to have the owner run builds/tests. It does not supply the separate confirmation required before an implementation commit.

| Command / check | Result |
| --- | --- |
| `dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --configuration Release` | PASS, zero warnings/errors after the final functional change. |
| `dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --configuration Release --filter "Category=CoreCi" --verbosity normal` | PASS, 2,455/2,455, zero skipped; final exact-command run completed in 3m56s. |
| `dotnet build src/PTDoc.Web/PTDoc.Web.csproj --configuration Release` | PASS, zero warnings/errors after the final functional/style changes. |
| `git diff --check` and `python3 .github/scripts/scan_secret_policy.py` | PASS. |
| Headed Chrome, isolated loopback API/Web, disposable SQLite | Focused checks below passed; no hosted environment or real patient data used. |

Run history: the first run found five test-fixture/assertion issues (three short-visit save mocks/async handlers, one absent body-region fixture, and PDF extraction whitespace). Correcting those produced 2,449/2,449 passes. Added partial-save/validation coverage produced 2,452/2,452 passes. After the final keyboard-focus fix, all three new typing cases passed; the 2,455-case run had one unrelated existing two-second autosave cancellation timeout (`DraftAutosaveServiceTests.DiscardPendingChanges_CancelsActiveSave_AndPreventsFurtherWritesUntilReset`, line 225). The exact-command repeat after stopping the browser and temporary app hosts passed that test unchanged in 393 ms, but exposed a randomized PDF fixture containing `ff`: the extraction library omits the font ligature. The PDF provenance fixture now uses a fixed numeric UUID while retaining the exact-source-ID assertion. The final rebuilt suite passed 2,455/2,455 in 3m56s, including the exact PDF source-ID assertion and the autosave cancellation test. No timeout or autosave production code was changed.

Focused browser evidence:

- Evaluation retains its full flow and offers ODI, Modified ODI and Roland–Morris for lumbar context. Daily shows its corresponding Subjective questions and no new outcome selector. Progress shoulder/hip/foot and Discharge wrist show the expected distinct measure choices, with none selected by default.
- QuickDASH: nine responses persist with no score; ten responses produce 50%, retained after reload. Progress retains explicitly recorded zero pain, 12.5% improvement and keyboard-entered text.
- Corrected HOOS: 40 controls including loaded-leg wording; five pain responses produce 75 points while other subscales stay incomplete. Draft save accepts missing current pain; review displays the required explicit-pain message. With zero pain and required consent, submission locks the form.
- Corrected FAAM: eight sports questions including low-impact activity; seven scored responses plus one N/A produce a 50% sports-only result. N/A and an optional numeric zero survive reload; keyboard Tab moves between the optional rating controls.
- Clinician review/copy attaches the locked hip intake only to its matching Progress draft. Save/restart/reload retains the score, source date and corrected definition version. All 40 copied HOOS controls are disabled. Local API export returns a three-page PDF whose score, incomplete-subscale notice and source evidence were inspected visually.
- Discharge omits clinician-owned reason/recommendations and retains zero pain, a whole-number improvement value and goal text. Its existing percentage contract remains whole-number; Progress supports fractional percentages.
- New short-intake required and optional consents start unselected. Treatment consent has an explicit checkbox; submission stays disabled until required agreements are granted. Existing issued consents are not reset.
- Actual multi-character keyboard entry retains focus and complete text in Daily, Progress and Discharge after the form-model fix. Responsive outcome captures at 390/768/1440 pixels show separate full-width controls without horizontal overflow. Final action buttons use the existing primary style.

Evidence is ignored under `output/playwright/plan08-verification/`, including `quickdash-final-*.png`, `faam-*.png`, `hoos-readonly-*.png`, and `hoos-progress-note.pdf`. Local build logs: `/tmp/ptdoc-plan08-tests-build.log`, `/tmp/ptdoc-plan08-web-build.log`; final repeat log: `/tmp/ptdoc-plan08-coreci-release-final.log`. Temporary app hosts and the task browser are stopped.

Remaining release coverage: owning-project CI formatting; all three EF provider model-drift checks; SQL Server/PostgreSQL migration/OTP regressions (including timestamp precision); native UI acceptance; the complete public-link OTP browser journey and exhaustive all-instrument browser matrix. CoreCi does cover scoped OTP/API, stale-version/ownership, scorer, UI and PDF behavior, but it is not a claim that these additional release checks ran. No live database, hosted beta, production fixture or deployment was modified.

## Historical implementation evidence

## Current evidence and changes

The branch now consumes Plan 07 profile-prefill contracts. A saved draft Evaluation or Re-evaluation receives one unique intake associated with its note, patient, clinic and immutable evaluation-full-v1 definition. Earlier locked intakes do not prevent later encounters and are never reused as new answers or consent. Staff and verified patient-link updates/submits require the current answer version; unsupported definitions, legacy sync writes and finalized/addendum destinations are rejected. Legacy patient-wide routes exclude encounter-bound forms, and old links remain pinned to their issued record. Submitted answers are offered only to the matching editable note, with full-intake review and explicit acceptance. Copying preserves existing measurement body-region context and manual assessment/plan fields. Browser testing exposed sync acknowledgements advancing answer versions; the production metadata interceptor now leaves encounter answer versions unchanged for sync-only acknowledgements. Three-provider additive migrations are included; SQL Server/Postgres snapshots materialize the old dynamic helper to avoid duplicate model entities, while unrelated generated index operations are excluded. CI follow-up removed a SQL Server downgrade operation that incorrectly recreated an already-existing NoteTemplates unique index. New provider-operation regressions require downgrade to remove only the encounter association and preserve unrelated schema. Infrastructure formatting was corrected with the owning-project CI formatter.

## Historical dependencies, blockers and owners (superseded by current checkpoint)

- User merge checkpoint: accept Plans 07/08 and Plan 20 before integrating the Daily questionnaire into the shared encounter-intake contract; no duplicate foundation commits or multi-parent stack.
- January/clinical approver: approve exact versions, scoring, missing-answer rules, reproduction rights and fixtures for all selected patient instruments (Plan 24); Progress integration also consumes Plan 30.
- January: identify the exact discharge questionnaire/source for Plan 31. Discharge intake remains disabled.

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
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --filter 'FullyQualifiedName~Intake&#124;FullyQualifiedName~NoteWorkspaceV2ServiceTests&#124;FullyQualifiedName~NoteWorkspacePageTests&#124;FullyQualifiedName~SendIntakeModalTests&#124;FullyQualifiedName~SyncClientProtocolTests' --verbosity minimal | PASS: 413 passed; 0 failed/skipped. Includes real OTP scope, concurrent writes, production sync interceptor, note copy preservation and legacy regressions. |
| dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --verbosity minimal | PASS: API/Web/shared test graph, 0 warnings/errors; subsequent focused test compilation also passed. |
| EF_PROVIDER=<sqlite&#124;sqlserver&#124;postgres> dotnet ef migrations has-pending-model-changes --no-build -p src/PTDoc.Infrastructure.Migrations.<Provider> -s src/PTDoc.Api | PASS: all three provider models match their snapshots. No SQL Server/Postgres live database was used. |
| SQLite migration upgrade → populated downgrade → upgrade | PASS: historical answers, IDs, consent, lock/review timestamps, dependent FK record and indexes preserved; new fields remain unset on legacy rows. |
| Synthetic browser + persisted database checks | PASS: full Evaluation history/pain/review/save/submit/lock; explicit matching-note copy; existing measurements and manual assessment/plan preserved; distinct Re-evaluation with fresh consent; earlier submitted intake unchanged. |
| dotnet format whitespace (changed C# files); git diff --check | PASS on formatted changed source; final whitespace/document check before commit. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~Intake&#124;FullyQualifiedName~NoteWorkspaceV2ServiceTests&#124;FullyQualifiedName~NoteWorkspacePageTests&#124;FullyQualifiedName~SendIntakeModalTests&#124;FullyQualifiedName~SyncClientProtocolTests' --verbosity minimal | PASS 415, including SQL Server/PostgreSQL encounter migration operation regressions. |
| dotnet format src/PTDoc.Infrastructure/PTDoc.Infrastructure.csproj --verify-no-changes --verbosity minimal | PASS: exact owning-project CI format check after correction. |

## Remaining acceptance work

AC1 and the Evaluation/Re-evaluation portions of AC4/AC5 have synthetic service/API/OTP/browser evidence. AC2 Daily and AC3 Progress remain unimplemented in this branch pending the explicit merge checkpoints and instrument gates above; Discharge remains gated by Plan 31. Roll out the additive database migration before the new API, keep unsupported encounter types disabled, and validate the combined release and native clients. SQL Server/Postgres live database upgrade/downgrade and MAUI builds were not run. Export encounter associations before any downgrade; older schemas cannot represent those three added fields. Clinical/client acceptance remains open.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.


## Current PR 163 re-verification — 2026-10-29

This section supersedes the open-thread, pending-merge and pending-remediation statuses in the historical review below. Local `HEAD` and the remote PR head both equal `2a0f8a846987d1d4304d11b8aa71bf5a3ba7de78`; the base is `ee9a0066d11b135841e7da33643d53ab954a4a76`. The fixes and regression coverage are already present in that commit. There are no unmerged index entries or active `MERGE_HEAD`, and GitHub reports the PR as mergeable.

### Complete refreshed inventory

Retrieved the current PR, thread-aware feedback and full discussion through the GitHub connector, then independently checked GraphQL review bodies, minimized-comment metadata and every outer/nested pagination boundary. The complete inventory is **5 threads, 10 inline comments (5 findings + 5 closure replies), 6 review submissions and 0 conversation comments**. All five threads are resolved; GitHub marks three as outdated. None of the ten comments is minimized/suppressed. Full review bodies, including collapsed sections, contain **no suppressed or Previously missed findings**. The five later review bodies are empty. This covers feedback exposed by GitHub, not unpublished reviewer output.

Against the current code, **all 5 unique findings are outdated/already addressed**; there are **0 remaining valid/actionable, 0 invalid and 0 ambiguous findings**. This classification reflects implementation evidence, independently of GitHub's outdated flag. The original review's five linked bullets are **duplicate references** to these same five findings; its “5 open issues” label is historical. The five closure replies describe the fixes and introduce no additional findings.

### Current classification and evidence

Paths and line references below refer to the current checkout. Each finding was valid at reviewed head `7b8dc4e7`; comparison with `2a0f8a8` confirms its corrective change. No further implementation change is required.

| Finding / thread | Classification and GitHub state | Current implementation, contract and regression evidence |
| --- | --- | --- |
| [4406603000: patient ownership](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#discussion_r4406603000), `PRRT_kwDOQt-faM6xVvv5` | **Outdated — addressed**. Resolved; GitHub outdated=false. | `src/PTDoc.Api/Intake/IntakeEndpoints.cs:279` checks `IPatientContextAccessor.GetCurrentPatientId()` before querying the requested patient/note for a Patient caller. The accessor rejects an unmapped authenticated patient, and `src/PTDoc.Api/Program.cs:710` maps provisioning failures to 403. This closes the ownership gap left by the clinic filter while retaining the `IntakeRead` role contract. `tests/PTDoc.Tests/Integration/EncounterIntakeIntegrationTests.cs:18` covers own-patient access, another actual same-clinic patient, unmapped-patient rejection and staff access. |
| [4406603059: intake-seed authorization](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#discussion_r4406603059), `PRRT_kwDOQt-faM6xVvwc` | **Outdated — addressed**. Resolved; GitHub outdated=true. | `src/PTDoc.Api/Notes/NoteWorkspaceV2Endpoints.cs:23` requires both `NoteRead` and `IntakeRead` on the reported encounter-seed route. The policies in `src/PTDoc.Application/Services/IRoleService.cs:175` and `:184` permit Billing to read notes but exclude it from source intake. `EncounterIntakeIntegrationTests.cs:48` tests seven roles and confirms ordinary Billing note access remains allowed. |
| [4406603110: pending-save encounter race](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#discussion_r4406603110), `PRRT_kwDOQt-faM6xVvw1` | **Outdated — addressed**. Resolved; GitHub outdated=true. | `src/PTDoc.UI/Pages/Intake/IntakeWizardPage.razor:681` captures the saving state and rejects returned ID/version assignments after navigation replaces it. Both callers (`:516`, `:808`) guard dirty/error updates. This protects the exact-ID/version write contract used by `IntakeApiService.SaveDraftAsync`. `tests/PTDoc.Tests/UI/Intake/IntakeWizardPageTests.cs:42` covers delayed success and failure, preserves B's identity/version/dirty state, and checks the subsequent save targets B. |
| [4406603157: PostgreSQL version precision](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#discussion_r4406603157), `PRRT_kwDOQt-faM6xVvxJ` | **Outdated — addressed**. Resolved; GitHub outdated=false. | `src/PTDoc.Infrastructure/Data/Interceptors/SyncMetadataInterceptor.cs:64` truncates only PostgreSQL encounter-intake versions to microseconds before saving and returning tracked values. Production registrations in `src/PTDoc.Api/Program.cs` install the interceptor; both staff and standalone update routes serialize after `SaveChangesAsync`. `IntakeEncounterRules.ValidateWriteAsync` retains exact equality and the interceptor retains its sync-only bypass. `tests/PTDoc.Tests/Integration/DatabaseProviderSmokeTests.cs:33` exercises a real PostgreSQL save → save → submit with a seven-tick remainder, fresh tracking, persisted/returned equality and actual stale-write rejection. `tests/PTDoc.Tests/Intake/EncounterIntakeConcurrencyTests.cs:16` checks sync acknowledgement preserves the version while answer edits advance it. |
| [4406603185: cached invite target](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#discussion_r4406603185), `PRRT_kwDOQt-faM6xVvxc` | **Outdated — addressed**. Resolved; GitHub outdated=true. | `src/PTDoc.UI/Pages/Intake/IntakeWizardPage.razor:202` keys `SendIntakeModal` by patient and encounter, forcing replacement of the component whose `EnsureIntakeIdAsync` caches `_intakeId`. `tests/PTDoc.Tests/UI/Intake/IntakeWizardPageTests.cs:86` opens A's invite, navigates to B for the same patient, verifies replacement/removal of A's link, and generates B's link. This remains a distinct issue from the save continuation race. |

Duplicate mapping: all five bullets in [review 5690344114](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#pullrequestreview-5690344114) map one-to-one, in order, to comments `4406603000`, `4406603059`, `4406603110`, `4406603157` and `4406603185`; no separate fix or resolution is required. Closure comments `4411813003`, `4411813461`, `4411814180`, `4411815048` and `4411815622` map to those same rows and are confirmations, not new defects. No ambiguous product decision or unresolved review finding remains.

### Changes, verification and thread disposition

This refresh changes documentation only and preserves the pre-existing formatting edit in `IntakeWizardPageTests.cs` byte-for-byte. The current commit already contains the five fixes and 12 regression cases described below; no additional code or tests are needed for these findings. The earlier merge conflicts are resolved and no new conflict work is required. `git diff --check` passes.

No build, formatter or test was run during this refresh, following `AGENTS.md`. The existing closure replies report that the user confirmed successful builds/tests; that is historical reported verification, not independently inspected execution output from this pass. The exact build, focused test, CoreCi, PostgreSQL and formatter commands in the historical verification handoff below remain applicable to this checkout. The PostgreSQL regression requires a disposable database and a non-skipped pass.

All five threads have already been replied to and resolved on GitHub. **No unresolved thread is newly eligible for a reply or resolution**, and duplicate summary references need no action. This refresh made no commit, push, GitHub reply/resolution, PR-level response or review request.

## Historical PR 163 review verification — 2026-10-29

The remainder records the original remediation and conflict-resolution work at `7b8dc4e7`. Its status statements describe that earlier pass; use the current re-verification above for present branch, verification and thread status.

### Inventory and branch boundary

Reviewed head: `7b8dc4e7c7db1a7ad4fc66d9b1157e0517400407`, base `ee9a0066d11b135841e7da33643d53ab954a4a76` (`main`). The current review was submitted against that exact head. These source paths are relative to that head plus the uncommitted remediation, not the older local `e3292175` checkout.

Retrieved the complete review-thread list, review submissions and merged discussion timeline using the GitHub connector; independently retrieved the paginated discussion with the review-comment skill script and checked GraphQL totals, nested pagination and minimized metadata. Results: **5 threads, 5 inline comments, 1 review submission, 0 conversation comments; every page complete**. All five threads are open, unresolved and not outdated. No inline comment is minimized/suppressed. The full review body (including collapsed details) contains neither suppressed findings nor **Previously missed** findings. This inventories all feedback GitHub exposes; it does not claim access to unpublished internal reviewer output.

The review summary's five linked bullets repeat the five inline findings below and are classified as **duplicate references**, not additional defects. There are **5 unique valid/actionable findings; 0 outdated, 0 invalid and 0 ambiguous unique findings**. No source finding is discarded as a duplicate of another source finding: the save-state race and cached-modal identity are separate defects with different fixes.

The original `/Users/calvinccarter/Projects/PTDoc` checkout has an unfinished merge (`HEAD=e3292175`, `MERGE_HEAD=7b8dc4e7`) and 82 changed/unmerged paths. Work was isolated at `/Users/calvinccarter/.codex/worktrees/pr-163-review/PTDoc`, detached at the latest PR head. During the initial review pass, the original files and merge index were not edited. No commit, push, reply, thread resolution, PR response or review request was performed.

### Complete classification and evidence

| Finding / thread | Classification | Current-code, contract, call-site and history evidence | Remediation / regression evidence |
| --- | --- | --- | --- |
| [4406603000: patient encounter read ownership](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#discussion_r4406603000), `PRRT_kwDOQt-faM6xVvv5` | **Valid; implemented, verification pending** | `AuthorizationPolicies.IntakeRead` in `src/PTDoc.Application/Services/IRoleService.cs` includes Patient. `IntakeEndpoints.GetForNote` queried supplied patient/note IDs; `ApplicationDbContext`'s IntakeForm filter enforces clinic only. A same-clinic patient's supplied IDs therefore matched without caller ownership. `SubmitIntake` supplies the existing identity + `IPatientContextAccessor` pattern; `HttpPatientContextAccessor` resolves the authenticated patient and rejects an unmapped patient. | Added that ownership check before querying. `EncounterIntakeIntegrationTests.EncounterRead_EnforcesPatientOwnership_AndPreservesStaffAccess` covers own read, another actual same-clinic patient, staff read and unmapped patient. The test auth handler accepts an explicit test-only patient claim; production auth is unchanged. |
| [4406603059: note permission bypass](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#discussion_r4406603059), `PRRT_kwDOQt-faM6xVvwc` | **Valid; implemented, verification pending** | `NoteRead` includes Billing; `IntakeRead` excludes Billing. The encounter `/intake-seed` route required only `NoteRead`. `NoteWorkspaceV2Service.GetEncounterEvaluationSeedAsync` builds a payload directly from matching submitted intake before the clinician's UI acceptance. UI confirmation is not a server authorization boundary. | Require both existing policies on the encounter seed route. `EncounterSeed_RequiresBothNoteAndIntakeRead` covers PT/PTA/Admin/Owner success and Billing/FrontDesk/Patient rejection. Billing's ordinary note GET remains successful in the regression. |
| [4406603110: pending save replaces another encounter's identity](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#discussion_r4406603110), `PRRT_kwDOQt-faM6xVvw1` | **Valid; implemented, verification pending** | `LoadDraftAsync` replaces `State` after `noteId` navigation. `PersistDraftAsync` previously assigned the returned ID/version to whatever `State` existed after awaiting. `IntakeApiService.SaveDraftAsync` mutates its draft with returned ID/version, and `IntakeService.SaveDraftAsync` selects the exact intake ID for the same patient, making subsequent writes to the previous encounter possible. Load-generation checks protect reads, not these save continuations. | Capture the state being saved and discard ID/version assignments when it is no longer active. Both save callers also preserve the new state's dirty flag and suppress stale save errors. `PendingSave_EncounterNavigationPreservesNewIdentityVersionAndDirtyState` covers delayed success and failure, then verifies the next save uses the new encounter's ID and version. |
| [4406603157: PostgreSQL false version conflict](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#discussion_r4406603157), `PRRT_kwDOQt-faM6xVvxJ` | **Valid; implemented, verification pending** | PostgreSQL snapshot maps IntakeForm `LastModifiedUtc` to `timestamp with time zone` and marks it a concurrency token. `SyncMetadataInterceptor` stamped full .NET ticks; staff `UpdateIntake` and standalone `UpdateIntakeDraft` returned the tracked entity. `IntakeEncounterRules.ValidateWriteAsync` and the EF token compare exact versions on the next request. PostgreSQL timestamp resolution is one microsecond, so unpersistable fractional ticks can cause a false conflict. | Normalize only PostgreSQL encounter-intake timestamps before persistence/response; retain exact equality, other providers and sync-only acknowledgement bypass. Optional `TimeProvider` defaults to the system clock and makes the regression deterministic. `DatabaseProviderSmokeTests.PostgresEncounterSaveSaveSubmit_ReturnedVersionsMatchStorage_AndStaleWritesStillFail` uses a real PostgreSQL database, a seven-tick remainder, fresh tracking between requests, two saves and submission, persisted/returned version equality and rejection of an actually stale version. It uses the production service/rules/interceptor, not a mocked database. Existing `EncounterIntakeConcurrencyTests.ProductionSyncAcknowledgement_PreservesAnswerVersion_WhileAnswerEditsAdvanceIt` guards acknowledgement behavior. |
| [4406603185: invite modal caches previous encounter](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#discussion_r4406603185), `PRRT_kwDOQt-faM6xVvxc` | **Valid; implemented, verification pending** | The wizard reused an unkeyed `SendIntakeModal`. Its `OnParametersSetAsync` compares only `InitialPatientId`; `EnsureIntakeIdAsync` returns cached `_intakeId`. Changing only `noteId` for the same patient therefore reused the previous invite target. This is separate from the pending-save state race. | Key the wizard's modal by `(State.PatientId, State.EncounterNoteId)`. `OpenInvite_EncounterNavigationGeneratesLinkForTheDisplayedEncounter` opens/generates an A link, switches to B, checks modal replacement and removal of the old link, then verifies link generation uses B's intake. |

History: all five reported exposure points were introduced by `993cf186` (encounter intake). Its existing load-generation protection and sync-acknowledgement exclusion address different problems and do not invalidate these reports. The only subsequent PR commit, `7b8dc4e7`, corrects migration downgrade indexes; it does not change these paths. The existing OTP isolation, stale-write, note-copy and provider migration regressions remain relevant but do not cover the newly added patient-read, role, UI race or stored timestamp precision cases.

Duplicate inventory: the five bullets in [review 5690344114](https://github.com/BlackHouseDeveloper/PTDoc/pull/163#pullrequestreview-5690344114) link respectively to `4406603000`, `4406603059`, `4406603110`, `4406603157` and `4406603185`. Their classification is **duplicate** and each is addressed by the corresponding implementation above; no separate change or extra thread exists for those summaries. There are no outdated/invalid findings requiring a no-change rationale and no ambiguous findings requiring a product decision.

Supporting external contracts: [PostgreSQL timestamp storage resolution](https://www.postgresql.org/docs/current/datatype-datetime.html#DATATYPE-DATETIME-TABLE) and [bUnit query-parameter testing](https://bunit.dev/docs/providing-input/passing-parameters-to-components.html#passing-query-parameters-supplyparameterfromquery-to-a-component). The wizard regressions navigate through the query parameter rather than assigning `NoteId` directly.

### Verification handoff and resolution eligibility

New coverage: 12 cases (1 ownership workflow, 7 reader roles, 2 delayed-save outcomes, 1 invite switch, 1 PostgreSQL workflow). `git diff --check` passes. **No build, formatter or test execution is claimed for this remediation.** The earlier pass counts above are historical and do not verify these changes. Per `AGENTS.md`, the user runs builds/tests. There is no schema change.

Run from the original checkout after the conflict-resolution follow-up below:

```bash
cd /Users/calvinccarter/Projects/PTDoc
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj --verbosity minimal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --filter 'FullyQualifiedName~Intake|FullyQualifiedName~NoteWorkspaceV2ServiceTests|FullyQualifiedName~NoteWorkspacePageTests|FullyQualifiedName~SendIntakeModalTests|FullyQualifiedName~SyncClientProtocolTests' --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --filter 'Category=CoreCi' --verbosity normal
```

With `Database__ConnectionString` exported for a disposable PostgreSQL test database, run the exact precision regression (it applies migrations unless `CI_DB_MIGRATIONS_ALREADY_APPLIED=true`, and rolls its synthetic records back). A skipped run does not verify the finding:

```bash
DB_PROVIDER=postgres dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --filter 'FullyQualifiedName~DatabaseProviderSmokeTests.PostgresEncounterSaveSaveSubmit_ReturnedVersionsMatchStorage_AndStaleWritesStillFail' --verbosity normal
```

Relevant CI formatter checks:

```bash
dotnet format src/PTDoc.Api/PTDoc.Api.csproj --verify-no-changes --verbosity minimal
dotnet format src/PTDoc.Infrastructure/PTDoc.Infrastructure.csproj --verify-no-changes --verbosity minimal
dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --verify-no-changes --verbosity minimal
```

After the corresponding regressions and build pass, all five listed thread IDs are eligible for a factual reply and resolution. The PostgreSQL thread specifically requires its non-skipped live-provider pass. The duplicate review-summary bullets need no independent resolution. GitHub writes still require explicit authorization; no thread has been replied to or resolved. The follow-up below integrates the remediation into the original checkout without committing the merge.


### Conflict-resolution follow-up — 2026-10-29

At the user's request, resolved all 12 unmerged files in `/Users/calvinccarter/Projects/PTDoc`. The conflict blocks were older Plan 07 prefill implementations versus the rebased branch's provenance snapshots, exact per-policy versions, canonical payer mappings, server-validated reconciliation actions and their regression evidence. The incoming implementations preserve Plan 08's encounter identity normalization, note-specific loading and version contracts. Applying those resolutions while retaining the nonconflicting content produced the incoming PR-head file contents exactly.

Also corrected an automatic merge in `tests/PTDoc.Tests/Intake/IntakeServiceTests.cs`: five prefill test methods appeared twice. Retained the updated copies (including canonical JSON-key expectations) and removed the obsolete duplicate block; no unique scenario was removed. The resolved index now matches `MERGE_HEAD=7b8dc4e7` exactly. `git diff --name-only --diff-filter=U` is empty, and no merge markers remain in tracked files.

Applied the isolated worktree's complete review-fix patch cleanly to this checkout, including all five fixes, 12 regression cases, classification ledger and changelog. The original nonconflicted files outside that reviewed patch and the duplicate-test correction were preserved. A backup of the starting index and changed files was taken before resolution. The isolated worktree remains available unchanged.

The merge resolutions are staged; the review-fix patch and this handoff update remain unstaged for inspection. No commit, push or GitHub write was performed. The merge is still in progress solely because no merge commit is authorized. Builds/tests were not run; use the commands above from `/Users/calvinccarter/Projects/PTDoc`. Existing regression coverage is retained, and no new behavior was added solely for conflict resolution.
