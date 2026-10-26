# Plan PR 22: Separate MMT from joint mobility and reuse ROM movement selections

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R056, R065
- Original plan status: Ready for Development
- Branch: `codex/plan-22-mmt-joint-mobility`
- Base: `codex/plan-21-rom-dimensions`
- Expected GitHub number: #176; actual: [#178](https://github.com/BlackHouseDeveloper/PTDoc/pull/178)
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-22)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

R056/R065 are implemented as the incremental consumer of [Plan 21 #177](https://github.com/BlackHouseDeveloper/PTDoc/pull/177). The combined editor and union of grade options are removed. MMT uses stable ROM movement/region/side identities with empty grade values; active/passive ROM selections deduplicate into one MMT observation per applicable side. Degrees, normal ROM ranges and prior ROM values are never copied into strength grades. Additional MMT movements can be selected from the existing region catalog. Joint mobility has its own assessment rows and grading scale, with paired sides for limbs and unsided spine assessments.

The new JointMobility enum value is additive (5); existing numeric values are unchanged. Measurement JSON schema v2 preserves the distinction and refuses older writers that would lose it. No additional database columns are required beyond Plan 21. Typed metrics, legacy Daily projections and offline sync reuse the lower layer's dimension mappings. Shared server validation rejects cross-kind grades, degree units or ROM modes on graded observations, inappropriate spine sides and unidentified new joint observations. Historical combined/ambiguous MMT and Other entries remain visibly unclassified and read-only in the new editor; no data migration guesses their category.

MMT and joint mobility export in separate tables with movement/assessment, side, current/prior values, unit and grading provenance. Removing ROM does not remove independent MMT data. Parameter models are cloned, and callbacks resolve the current row by stable identity so queued edits cannot mutate detached render clones. The actual browser rapid-edit regression retained all grades in the JSON and typed rows after reload.

The lower ROM layer was corrected and its original editor regressions updated on #177 before this branch was rebased. The complete branch and this incremental diff were rechecked; no shared foundation commits are duplicated.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

Existing runtime MMT and joint-mobility grade definitions are reused without additions or numeric conversion. Optional movement exceptions/new grades still require clinical approval.

## Development specification

### User Problem

Combined strength and joint-mobility controls obscure different measurement meanings and force duplicate movement selection.

### Confirmed Requirement

Give MMT and joint mobility separate sections/grades; seed MMT movement selections from ROM-selected movements, preserving applicable side comparisons. Joint mobility also preserves the requested applicable left/right distinction; the spine exception applies to artificial sides.

### Expected Behavior

MMT and joint mobility retain distinct values and grading choices. Selecting a ROM movement makes its corresponding MMT choice available without copying degrees into strength grades.

### Scope

Separate editor/serialization semantics, selection synchronization and legacy compatibility.

### Out of Scope

Copying ROM numbers, inventing conversion formulas or automatically deleting clinician-entered MMT when ROM selection changes.

### Implementation Guidance

**Technical Recommendation:** share movement IDs and map selection only. Existing catalog already separates MmtGradeOptions and JointMobilityGradeOptions; stop combining them in the editor. Preserve independent observations and typed/JSON mapping; no new grade definitions are required for this slice.

### Data and Migration Considerations

Legacy combined entries need explicit classification or retained legacy display; ambiguous values must not be guessed into one category.

### Security and Privacy Considerations

**Technical Recommendation:** enforce metric-kind validation and note ownership server-side, including offline/sync replay.

### Acceptance Criteria

- [ ] **AC1:** Given ROM movement selection, when MMT opens, then corresponding movement choices are seeded with empty strength values.
- [ ] **AC2:** Given an entered MMT grade and ROM degrees, when saving/reloading, then each retains its own type and value.
- [ ] **AC3:** Given a joint-mobility grade, when editing MMT, then joint-specific grades are unavailable in MMT and vice versa.
- [ ] **AC4:** Given a removed ROM selection with existing MMT data, when selections refresh, then that clinical value is not silently erased.
- [ ] **AC5:** Given a paired joint-mobility assessment, when left/right values are entered, then both remain distinct after reload; spine is not forced into artificial paired sides.

### Rollout and Regression Risks

Combined historical records may be ambiguous. Preserve them with explicit legacy rendering until classified.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~GradedObservation&#124;FullyQualifiedName~ObjectiveMetricDimensionsTests&#124;FullyQualifiedName~RomObservationEditorTests&#124;FullyQualifiedName~NoteWorkspacePayloadMapperTests&#124;FullyQualifiedName~NoteWorkspaceV2ServiceTests&#124;FullyQualifiedName~SyncClientProtocolTests&#124;FullyQualifiedName~DailyNoteServiceTests&#124;FullyQualifiedName~NoteWriteServiceTests&#124;FullyQualifiedName~PdfIntegrationTests&#124;FullyQualifiedName~StructuredWorkspaceEditorsTests' --verbosity minimal -m:1 | 203 passed, 0 failed, 0 skipped on the final stack; includes SQLite exact-value persistence and cross-kind rejection, existing security/signature regressions and export compatibility. |
| dotnet build src/PTDoc.Web/PTDoc.Web.csproj --no-restore -m:1 | Succeeded with 0 warnings and 0 errors; final code also compiled in the passing test dependency graph. |
| Task-local Playwright CLI synthetic note: inspect empty seeded MMT, enter distinct grades, remove matching ROM, save/reload, verify database, keyboard and 390/768/1440 viewports | Passed: separate grade choices; 3 MMT and 3 joint grades retained; two Knee MMT grades survive complete Knee ROM removal; unsided cervical assessments; JSON/typed persistence; keyboard Tab and no horizontal overflow; mobile screenshot visually inspected. |
| python3 workflow.py check 22; git diff --check; git diff codex/plan-21-rom-dimensions --stat | Requirement/section/local-link/path and whitespace checks pass; incremental diff contains only Plan 22 changes. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~GradedObservation&#124;FullyQualifiedName~ObjectiveMetric&#124;FullyQualifiedName~RomObservationEditorTests&#124;FullyQualifiedName~NoteWorkspacePayloadMapperTests&#124;FullyQualifiedName~NoteWorkspaceV2ServiceTests&#124;FullyQualifiedName~SyncClientProtocolTests&#124;FullyQualifiedName~DailyNoteServiceTests&#124;FullyQualifiedName~NoteWriteServiceTests&#124;FullyQualifiedName~PdfIntegrationTests&#124;FullyQualifiedName~StructuredWorkspaceEditorsTests' --verbosity minimal -m:1 | PASS 217 after rebasing onto Plan 21 provider migration corrections; own incremental feature diff remains separate. |

## Remaining acceptance work

AC1–AC5 pass for the existing catalogs. Reviewer/client acceptance of optional new grade definitions or movement exceptions remains outside this implementation. MAUI publish/device installation and full assistive-technology testing were not run. Apply Plan 21 migrations first and integrate its lower branch before retargeting this PR to main. The unrelated incomplete-note navigation modal cleanup race remains tracked in Plan 02.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
