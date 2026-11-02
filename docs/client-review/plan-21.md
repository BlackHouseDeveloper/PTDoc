# Plan PR 21: Represent ROM by movement, side and active/passive measurement

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R053, R054, R055, R057
- Original plan status: Ready for Development
- Branch: `codex/plan-21-rom-dimensions`
- Base: `main`
- Expected GitHub number: #175; actual: [#177](https://github.com/BlackHouseDeveloper/PTDoc/pull/177)
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-21)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Implemented R053/R054/R055/R057. Paired-region selections produce four empty Left/Right × Active/Passive cells. Spine produces two unsided modes. Each observation pins its own region; switching the catalog retains all entered rows. The existing combined limb catalog is filtered by its explicit movement prefixes. Reference values and provenance come from the existing runtime catalog, and appear before current measurements. No missing normal values or legacy modes are inferred.

Workspace JSON and typed metrics retain the same identity and values; legacy Daily reads and sync pulls now carry dimensions. Workspace, generic note, legacy Daily and offline writes share validation. Older writers cannot erase a versioned measurement payload. Removed/reordered rows no longer inherit another row's values through positional fallback. Prior comparisons require the complete region/type/movement/side/mode/unit key; ambiguous same-date history is not guessed. Existing signature, clinic, ownership and stale-write checks remain enforced.

Seven nullable columns are added for each provider; existing Side and Unit now flow through all projections. SQLite was upgraded, downgraded and upgraded with populated legacy data: identity, note relationship, value, side, units and timestamp survived, and newly re-added dimensions remained null. SQLite rollback explicitly rebuilds the table because the preceding handwritten migration has no target model. SQL Server/Postgres generated DDL and model-drift checks passed; live engine execution remains a rollout check. Their snapshot helper materialization is required EF scaffolding; indexes already created by the enterprise migration are not recreated.

The PDF ROM table identifies region/movement, side, mode, reference, previous, current, unit and source; historical missing mode is explicitly labelled. Tests cover dimensions, selection, exact-cell persistence, missing units, unknown legacy data, deletion/reorder, older-client protection, stale writes, offline projection and existing PDF regressions.

Rapid browser edits also verified an editor callback issue: stale cloned row references could drop queued changes. The editor now resolves updates/removals by an editor-only row identity retained through render clones; identical-looking legacy observations remain distinct. This identity is deliberately absent from clinical JSON and typed storage. CI provider validation identified that SQL Server/PostgreSQL dimension removals had been included in Up instead of an implemented Down. The migration now keeps all seven nullable dimensions after upgrade and removes them only on downgrade, without recreating an unrelated existing NoteTemplates index. Provider-operation regressions cover both assemblies; SQLite behavior is unchanged.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

Use existing runtime-canonical catalog values and provenance. Missing region-specific references remain absent until the clinical owner approves additional content. No optional catalog additions are implemented.

## Development specification

### User Problem

ROM entry does not provide the side and active/passive comparisons needed for clinical documentation.

### Confirmed Requirement

Show relevant movements for selected body regions while allowing additional regions; show normal before current; support applicable left/right comparisons and active/passive current, normal and prior measurements. Spine does not acquire artificial left/right sides.

### Expected Behavior

Each ROM observation has unambiguous region/movement/side/mode context and preserves current/prior values without conflation.

### Scope

Extend the existing metric representation and editor; reuse current region filtering where it already satisfies the request.

### Out of Scope

Inventing normal ranges, inferring active/passive values from legacy data, or interpreting spine side exception as removal of active/passive.

### Implementation Guidance

**Technical Recommendation:** use explicit stable dimensions and units; preserve unknown mode for old records. Coordinate typed metrics and workspace JSON mapping, export and sync. Reference values require approved catalog provenance.

### Data and Migration Considerations

Potential additive metric mode/side mapping across provider migrations and JSON snapshots. Never duplicate a legacy value into both active and passive as measured facts.

### Security and Privacy Considerations

**Technical Recommendation:** maintain clinic/note scope and draft/signature immutability; no patient identifiers in diagnostics.

### Acceptance Criteria

- [ ] **AC1:** Given a paired region, when entering left/right active/passive measurements, then each value survives save/reload in its exact cell.
- [ ] **AC2:** Given spine, when entering ROM, then no artificial bilateral requirement is imposed and applicable active/passive data remains representable.
- [ ] **AC3:** Given a selected region plus another added region, when switching between them, then movements and saved values remain correct.
- [ ] **AC4:** Given a legacy metric without mode, when displayed, then it is not falsely labeled as both active and passive.
- [ ] **AC5:** Given a ROM row, when viewed, then approved normal/reference information precedes current measurement.

### Rollout and Regression Risks

Clinical mislabeling and legacy data duplication are primary risks. Use additive migration and verify representative old notes.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~ObjectiveMetricDimensionsTests&#124;FullyQualifiedName~RomObservationEditorTests&#124;FullyQualifiedName~NoteWorkspacePayloadMapperTests&#124;FullyQualifiedName~NoteWorkspaceV2ServiceTests&#124;FullyQualifiedName~SyncClientProtocolTests&#124;FullyQualifiedName~DailyNoteServiceTests&#124;FullyQualifiedName~NoteWriteServiceTests&#124;FullyQualifiedName~PdfIntegrationTests' --verbosity minimal -m:1 | 184 passed, 0 failed, 0 skipped. |
| dotnet build src/PTDoc.Web/PTDoc.Web.csproj --no-restore -m:1; dotnet build src/PTDoc.Api/PTDoc.Api.csproj --no-restore -m:1 | Both succeeded with 0 warnings and 0 errors. |
| EF_PROVIDER=<sqlite&#124;sqlserver&#124;postgres> dotnet ef migrations has-pending-model-changes -p src/PTDoc.Infrastructure.Migrations.<Sqlite&#124;SqlServer&#124;Postgres> -s src/PTDoc.Api --no-build | All three provider model-drift checks passed; generated provider DDL inspected. |
| EF_PROVIDER=sqlite dotnet ef database update <AddObjectiveMetricDimensions&#124;preceding migration&#124;AddObjectiveMetricDimensions> -p src/PTDoc.Infrastructure.Migrations.Sqlite -s src/PTDoc.Api --no-build (isolated synthetic databases) | Upgrade/rollback/re-upgrade passed; populated legacy observation, foreign key and index preserved; newly re-added dimensions null. |
| Task-local Playwright CLI: synthetic Knee/Cervical ROM save/reload; typed/JSON comparison; 390/768/1440 widths; mobile screenshot inspection | All six current/prior cells retained exact region/side/mode; typed rows match JSON; no horizontal overflow; normal/reference precedes current and mobile inputs remain visible. |
| python3 workflow.py check 21; git diff --check | Requirement/section/local-link/sensitive-path and whitespace checks passed. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~StructuredWorkspaceEditorsTests' --verbosity minimal -m:1 | 10 passed after updating inherited ROM assertions for four cells, fixed region identity and read-only reference values; existing MMT behavior unchanged in this lower layer. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~RomObservationEditorTests&#124;FullyQualifiedName~StructuredWorkspaceEditorsTests&#124;FullyQualifiedName~NoteWorkspacePayloadMapperTests&#124;FullyQualifiedName~ObjectiveMetricDimensionsTests' --verbosity minimal -m:1 | 57 passed after the rapid-edit correction; includes duplicate legacy row independence across cloned renders. Counts overlap prior suites. |
| Task-local Playwright CLI: twelve rapid ROM input changes, save, database verification and reload | All six previous/current pairs retained; keyboard Tab works without the pre-existing incomplete-navigation modal; editor-only row identity is absent from clinical JSON. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~ObjectiveMetric&#124;FullyQualifiedName~NoteWorkspaceV2ServiceTests&#124;FullyQualifiedName~NoteWorkspacePageTests&#124;FullyQualifiedName~NoteWriteServiceNormalizationTests&#124;FullyQualifiedName~PdfIntegrationTests' --verbosity minimal | PASS 116, including SQL Server/PostgreSQL migration-operation regressions. Live provider CI rerun follows publication. |

## Remaining acceptance work

AC1–AC5 are verified for the bounded existing-catalog implementation. Reviewer/client confirmation of any additional normal-range sources remains separate. SQL Server/Postgres live migration execution and device publish/install were not performed. Combine model snapshots carefully when integrating the independent Plan 14 and Plan 18 migrations; retain their tables/columns. No merge or deployment performed. Browser keyboard testing reproduced an existing incomplete-note modal focus-trap cleanup race after quick Continue; tracked in Plan 02 navigation follow-up, not claimed fixed here.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
