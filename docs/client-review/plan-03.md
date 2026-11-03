# Plan PR 03: Correct patient timeline timestamps using the agreed display time zone

**Implemented** — only the changes and checks listed below are claimed.

The latest review pass is recorded in [Current PR 158 review audit](#current-pr-158-review-audit--2026-11-02). Its fixes remain uncommitted. The user subsequently confirmed all 2,578 Debug tests passed after the corrections; the separate post-correction Release Web build also passed with zero warnings/errors. See the latest verification entry below.

- Requirements: R007
- Original plan status: Verification Required
- Branch: `codex/plan-03-timeline-timezones`
- Base: `main`
- Expected GitHub number: #157; actual: [#158](https://github.com/BlackHouseDeveloper/PTDoc/pull/158)
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-03)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Reproduced on main-derived code with a UTC host: a New York browser displayed synthetic intake instant 2026-10-26T01:30:00Z as 10/26 1:30 AM instead of 10/25 9:30 PM. After the fix, the same stored event displays 10/25/2026 9:30 PM UTC-04:00 with both UTC and Asia/Tokyo hosts. Browser comparisons of all five rendered synthetic rows (notes, appointments and intake) matched their UTC instants and remained in descending instant order. Stored timestamps and date-of-service fields are unchanged. No database migration.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

User confirmed 2026-10-26: display timeline dates/times in the viewer’s local time zone; preserve stored UTC timestamps.

## Development specification

### User Problem

The meeting showed timeline dates/times that did not match the participants’ expectation.

### Confirmed Requirement

Investigate and correct patient clinical-info timeline timestamps. The meeting did not specify whether display follows clinic time, browser time or event location.

### Expected Behavior

Timeline ordering and displayed date/time represent the stored event consistently in the agreed zone, including date boundaries.

### Scope

Timestamp source, DateTime kind and presentation conversion for the three timeline event types.

### Out of Scope

Changing dates of service or rewriting historical event instants.

### Implementation Guidance

**Technical Recommendation:** trace UTC values before presentation; distinguish LastModified, submitted and appointment-start events. Use existing clinic TimeZoneId if product selects clinic time; do not assume the developer’s workstation zone. Keep date-only clinical fields date-only.

### Data and Migration Considerations

Normally a presentation fix. Investigate stored data before deciding on any migration; do not apply a blanket time offset.

### Security and Privacy Considerations

**Technical Recommendation:** inspect synthetic events and record only identifiers/reason codes in logs.

### Acceptance Criteria

- [ ] **AC1:** Given the same UTC event in two deployment host time zones, when displayed under the agreed product zone, then the visible date/time is identical.
- [ ] **AC2:** Given events near midnight or a daylight-saving transition, when sorted and rendered, then chronology and local dates remain correct.
- [ ] **AC3:** Given an intake without a submission timestamp, when shown in the timeline, then its documented fallback is used without inventing a completed event.

### Rollout and Regression Risks

A display fix must not shift signed dates or persisted events. No data rewrite unless separately evidenced and reviewed.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~TimelineTimestampFormatterTests&#124;FullyQualifiedName~PageScopedAppointmentUsageTests&#124;FullyQualifiedName~PatientClinicalInfoCardEditableTests' --verbosity minimal -m:1 -p:BuildInParallel=false | PASS: 34 tests, 0 failed, 0 skipped. |
| TZ=UTC and TZ=Asia/Tokyo: dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~TimelineTimestampFormatterTests' --verbosity minimal -m:1 -p:BuildInParallel=false | PASS: 9 tests in each host time zone. |
| Local Playwright browser comparison, America/New_York viewer; hosts TZ=UTC and TZ=Asia/Tokyo | PASS: original intake instant renders identically at 10/25/2026 9:30 PM UTC-04:00; all rendered event types match raw instants and UTC ordering. |
| dotnet format whitespace (PTDoc.UI and PTDoc.Tests, --no-restore --verify-no-changes --include changed C# files) | PASS. |
| python3 workflow.py check 3; git diff --check | PASS: document sections, requirements, local links and whitespace. |

## Remaining acceptance work

AC1–AC3 passed for the bounded timeline display change. Native MAUI platform publish/install and client acceptance remain unverified.

## PR 158 review verification — 2026-11-02

Audited PR head `9e3c9218db81ba927d26fe9e3df3e5fccae991d9` using both the GitHub connector and paginated GraphQL feedback. The inventory contains one review, one unresolved/current inline thread, and no conversation comments. All connections, including thread comments, report no next page. There are zero resolved/outdated threads, zero minimized comments, and no suppressed or “Previously missed” findings in the complete review body. This inventory covers feedback exposed by GitHub, not unpublished reviewer drafts.

The review was submitted against `6d7e6f3ae8e4ed1636eb9b20ccf9028df8be5cc4`. The latest head rebases that implementation; `PatientProfile.razor`, its page tests, and this plan were unchanged between those commits before this audit. The thread is therefore not outdated merely because its reviewed SHA differs.

After PR 157 merged, rebased the local review branch `codex/pr-158-review-follow-up` onto `origin/main` at `673cd58cbec22c43c89383e85fa8762304d6b01e`. Its two existing commits are now `13d7097` and `ac15081`. Preserved all uncommitted review changes and both sides of the changelog-only restoration conflict. Range comparison shows only changed changelog context; the timeline implementation and committed page tests are unchanged. The remote PR head remains `9e3c921` until an authorized push. Subsequent user-run build and focused-test results on this rebased tree are recorded below.

| Finding/source | Classification | Current evidence and disposition |
| --- | --- | --- |
| [Time-zone interop cancellation terminates the circuit](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4376077751) | Invalid as a defect claim | `PatientProfile.OnAfterRenderAsync` is an async Task lifecycle override. Cancellation propagates as a canceled task, which ASP.NET Core's `Renderer.NotifyRenderCompleted` and `GetErrorHandledTask` explicitly ignore. `ReloadTimelineAsync` already formats entries before the lookup completes, and `TimelineTimestampFormatter.Apply` uses `TimeZoneInfo.Utc` when the viewer zone is absent. There is no caller requiring this lifecycle task to complete successfully. No production catch-filter change is required. |
| [Review summary's time-zone timeout issue](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#pullrequestreview-5650386476) | Duplicate | The summary links to the same inline comment; it supplies no separate failure path. Deduplicated into the preceding item. |
| Review summary: native-platform validation is incomplete | Valid, unresolved verification gate | The remaining acceptance section and PR checklist explicitly leave MAUI publish/install unverified. Formatter and bUnit tests do not prove native installation or WebView execution. Requires Android, iOS, and Mac Catalyst publish/install evidence; no speculative code change is appropriate. |
| Review summary: final acceptance is incomplete | Valid, unresolved acceptance gate | Client acceptance remains unrecorded. The previously recorded bounded AC1–AC3 results do not substitute for client sign-off. Requires acceptance from the client; no code change can supply it. |

Framework evidence: [ASP.NET Core 8.0.11 Renderer](https://github.com/dotnet/aspnetcore/blob/v8.0.11/src/Components/Components/src/RenderTree/Renderer.cs) (`NotifyRenderCompleted` and `GetErrorHandledTask`) explicitly ignores canceled tasks. The local test dependency graph resolves Components to 8.0.11; the [8.0.8 source](https://github.com/dotnet/aspnetcore/blob/v8.0.8/src/Components/Components/src/RenderTree/Renderer.cs) corresponding to the UI package declaration has the same guards. This is a source-backed classification; test execution is recorded separately below.

Expanded `PatientProfile_RendersLockedIntakeCalloutAndTimelineEntry` to cover successful New York resolution, canceled module import, and canceled zone lookup. The assertions cover UTC fallback text, zone metadata, unchanged ISO instant, and working Review intake navigation after cancellation. Existing formatter tests cover missing-zone fallback, midnight/DST boundaries, and event timestamp sources. No production code changed. No ambiguous findings remain.

The user reported **2,342 passed, 1 failed, 0 skipped (2,343 total)**. The sole failure was the import-cancellation fixture: bUnit 1.40 rejects `Setup<IJSObjectReference>` before rendering the component. A subsequent attempted custom-handler correction failed compilation with **CS0122** because the `JSRuntimeInvocationHandler<IJSObjectReference>` constructor is inaccessible. Neither failure establishes a production defect.

The current fixture uses a strict Moq `IJSRuntime` registered only for the import-cancellation case. It matches the public two-argument `InvokeAsync<IJSObjectReference>` overload and exact module path, returns a canceled `ValueTask` backed by `Task.FromCanceled`, and verifies the configured invocation. Successful resolution and lookup cancellation retain bUnit's module setup. The installed JSInterop XML documentation confirms the interface signature. UTC text, timestamp metadata, and navigation assertions remain intact. Production code is unchanged.

The user subsequently ran the following command on the corrected fixture. The test project and its dependency graph, including API, UI, and Web, compiled successfully; **all 3 cases passed, 0 failed, 0 skipped (16 ms test duration)**:

```bash
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter 'FullyQualifiedName~PatientProfile_RendersLockedIntakeCalloutAndTimelineEntry' --verbosity minimal -m:1 -p:BuildInParallel=false
```

This verifies successful viewer-zone resolution and both cancellation paths with the production code unchanged, supporting the invalid-defect classification. The user subsequently confirmed that all required builds and tests passed and authorized one commit, a normal push, and evidence-backed replies/resolutions for conclusively addressed threads. This confirmation is user-provided; no additional full-suite log or count was supplied. Thread `PRRT_kwDOQt-faM6wNyy5` is eligible for reply and resolution on that basis. Native runtime/install checks and client acceptance are not established by build/test confirmation.

Run from the repository root for this review coverage:

```bash
dotnet build tests/PTDoc.Tests/PTDoc.Tests.csproj -m:1 -p:BuildInParallel=false
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-build --no-restore --filter 'FullyQualifiedName~TimelineTimestampFormatterTests|FullyQualifiedName~PageScopedAppointmentUsageTests|FullyQualifiedName~PatientClinicalInfoCardEditableTests' --verbosity minimal -m:1 -p:BuildInParallel=false
dotnet format whitespace tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --verify-no-changes --include tests/PTDoc.Tests/UI/Pages/PageScopedAppointmentUsageTests.cs
```

The user subsequently reported that the iOS build/publish stopped because no valid code-signing keys were available in the keychain. This is an environment blocker for signed device artifacts; it supplies no successful iOS validation evidence and does not establish a timeline defect. No signing configuration was changed. On this Apple Silicon Mac, the following simulator-only build avoids requiring an Apple signing certificate:

```bash
dotnet build src/PTDoc.Maui/PTDoc.csproj -c Debug -f net8.0-ios -r iossimulator-arm64 -p:EnableCodeSigning=false
```

The user ran this exact simulator command and reported **Build succeeded: 0 warnings, 0 errors, elapsed 00:00:17.40**. The output includes Core, Application, Infrastructure, UI, and the MAUI `net8.0-ios/iossimulator-arm64` assembly. This establishes simulator build success only: launch and timeline checks remain unverified, as does signed-device publishing. The installed iOS SDK's `Xamarin.Shared.props` honors `EnableCodeSigning` and defaults simulator signing requirements to false; [Microsoft's CLI guide](https://learn.microsoft.com/en-us/dotnet/maui/ios/cli?view=net-maui-8.0) documents simulator execution. Do not use the signing override for a device-distribution artifact.

The user also reported `NETSDK1047` during the solution `Clean` target from `dotnet clean && dotnet build && dotnet test --no-build`. Clean does not restore packages, so this failure stopped the chain before build or tests. At inspection time the assets file already contained `net8.0-ios18.0/iossimulator-arm64` with the `net8.0-ios` alias, so the earlier missing-target state was not established as a persistent project defect. MSBuild property-only evaluation confirmed the simulator RID and iOS 18.0 platform; no clean, restore, build, or test was run by the agent. Refresh the default Debug solution restore before retrying the same scope, without applying the iOS-only RID to the entire solution:

```bash
dotnet restore PTDoc.sln --force -p:Configuration=Debug
dotnet clean PTDoc.sln -c Debug && dotnet build PTDoc.sln -c Debug --no-restore && dotnet test PTDoc.sln -c Debug --no-build
```

The user subsequently confirmed all required builds and tests passed; the exact full-solution recovery output was not supplied. No runtime identifiers or target frameworks were added to the project for this restore-state error.

For the separate outstanding native publish/install gate, use a machine with the required workloads, SDKs, and signing configuration. iOS device publishing remains blocked until a valid certificate/private key and matching provisioning profile are configured:

```bash
dotnet publish src/PTDoc.Maui/PTDoc.csproj -c Release -f net8.0-android
dotnet publish src/PTDoc.Maui/PTDoc.csproj -c Release -f net8.0-ios
dotnet publish src/PTDoc.Maui/PTDoc.csproj -c Release -f net8.0-maccatalyst
```

Publish success alone does not close that gate: install and launch each artifact, verify the same synthetic timeline instant in the device's viewer zone, and record midnight/DST and UTC-fallback behavior. Record client acceptance separately.

## Publication and merge boundary

The user authorized committing the verified changes, pushing normally, and replying to/resolving only conclusively addressed review threads after checking the pushed state. To avoid force-pushing the earlier rebase, the review commit preserves the existing remote PR head as a merge parent while retaining the verified file contents. PR merge, deployment, production-data changes, and another review request remain unauthorized. Shared subject matter alone does not create a Git stack dependency.

## Current PR 158 review audit — 2026-11-02

This section supersedes the earlier inventory and publication status. The previous authorized pass produced `863f8d609b384f5cb0c4d525f191b5f0528b9450` (`test: verify timeline time-zone cancellation fallback`), pushed normally to `codex/plan-03-timeline-timezones`; the cancellation thread was replied to once and resolved. The present request authorizes local fixes only. No commit, push, comment, resolution, PR description/base change, or review request was made in this pass.

### Inventory and history

Refreshed both the GitHub thread-aware connector and live GraphQL, including all review bodies and minimized flags. PR head and local HEAD are `863f8d6`. There are **3 reviews, 11 threads (10 open, 1 resolved), 0 outdated thread flags, 0 issue comments, and 0 minimized/suppressed comments**. Every parent and nested connection reports `hasNextPage=false`. Neither review summary contains suppressed or **Previously missed** sections. This covers all feedback exposed by GitHub; it cannot establish the contents of unpublished reviewer drafts.

The latest review is [5736547970](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#pullrequestreview-5736547970). Its ten issue bullets and resolved cancellation bullet repeat the corresponding inline threads; the original review's cancellation bullet also repeats that thread. These **12 repeated summary references are duplicates**, not separate defects. No independent implementation findings were ambiguous.

Current `main` is `673cd58cbec22c43c89383e85fa8762304d6b01e`, including Plan 07 merge `ee9a006` (#162), Plan 01 merge `6cd3b64` (#156), and Plan 02 merge `673cd58` (#157). GitHub PR metadata still reports historical `baseRefOid=7f3a1e5`, which explains its broader cached diff. Both `git diff --name-only origin/main...HEAD` and GitHub's live `compare/main...codex/plan-03-timeline-timezones` report merge base `673cd58` and only nine timeline/test/documentation files. Retargeting onto a preceding plan branch is unnecessary. The present review fixes intentionally expand the working-tree scope to the nine verified findings in inherited code; a future authorized PR description update must cover intake data safety, Web authentication, note recovery, provider model metadata, and the verification commands below using `.github/pull_request_template.md`.

### Complete classification

“Valid, fixed locally” means source changes and regression coverage are present, **not** that new tests have passed.

| Finding | Classification | Evidence, change, and regression coverage |
| --- | --- | --- |
| [4442378747: changed legacy payer source](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442378747) | Valid, fixed locally | `IntakeAccessEndpoints.Submit` updates `Patient.PayerInfoJson` without normalized projection; the old null-ID check accepted any legacy changes. `IntakeProfilePrefillReader` now fingerprints canonical payer fields independently by priority. `InitializeSource`, explicit acceptance, DTO/UI mapping, and `IntakeProfileProvenance.ProtectedFields` retain these server-owned values. `HasCurrentPolicySourcesAsync` compares them; old nonempty sources without metadata require acceptance or omission. Tests: `ProfilePrefill_LegacyPayerChangesRequirePerPriorityAcceptance` covers both priorities and client forgery; `Submit_RejectsChangedLegacyPayerWithoutNormalizedPolicy` covers both priorities on staff and standalone API routes. Existing old-draft fixtures now explicitly establish accepted sources. |
| [4442378800: policy inserted after validation](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442378800) | Valid, fixed locally | `UpsertPolicyAsync` previously treated null accepted ID as a wildcard and used the newly fetched row's version. `requireAcceptedSource` now rejects an unexpectedly present row for intake projection; `UpsertPatientRecordAsync` explicitly retains chart-edit semantics. Existing active-priority uniqueness protects an insert after the lookup from overwriting an existing row. SQLite `Projection_RejectsPolicyInsertedAfterSourceValidation` inserts a real row just before the final lookup, proves no modification for both priorities, and proves explicit chart upsert still works. The API translates this stale-source rejection to 409. |
| [4442378860: concurrent demographic correction](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442378860) | Valid, fixed locally | `ForPatientWrite` compares field snapshots but cannot protect the read-to-write interval; `PatientEndpoints` stamps chart writes with `LastModifiedUtc`. `ApplicationDbContext` now uses that existing property as a concurrency token. Staff and standalone intake submits return 409 on concurrency failure; direct `IntakeService` returns the existing refresh error. SQLite `Submit_ConcurrentChartEditRollsBackIntakeAndPolicyWrites` performs a separate-context edit immediately before saving, checks the real EF concurrency failure, preserved chart correction, unlocked intake, and rolled-back new policy. All provider snapshots carry the metadata; no DDL/data migration is needed. |
| [4442378917: acceptance succeeds but reload fails](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442378917) | Valid, fixed locally | `ApplyProfileUpdateAsync` previously kept old UI answers after server provenance changed. A reload-required state now blocks all draft write entry points and disables editing until a full saved snapshot loads; an explicit recovery button retries the read. It also handles uncertain save responses conservatively. `ProfileAcceptance_FailedReloadBlocksWritesUntilSavedAnswersAreRecovered` proves failed GET disables editing, sends no extra save/submit, and recovers current answers. |
| [4442378973: remote save may commit after client cancellation](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442378973) | Valid, fixed locally | Canceling `_workspacePersistenceCts` and draining local gates cannot roll back a dispatched API transaction. The recovery panel now says the save may have reached the server and asks the user to reopen the note to check; its action is “Leave without retrying.” Local cancellation/draining remains. `FailedSaveNavigation_OffersStayAndExplicitDiscard_WithoutDisposalRetry` checks that disclosure; the delayed direct-save theory covers both canceled completion and success despite cancellation. No remote rollback guarantee is claimed. |
| [4442379019: Web idle expiry races browser callback](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442379019) | Valid, fixed locally | The forwarding handler checked token/absolute expiry only. `WebSessionActivityStore` now shares validated activity timestamps between HTTP requests and circuits; `HasRecentActivity` observes protected cookie evidence, and renewal rechecks idle expiry after API validation. `ApiAccessTokenForwardingHandler` enforces idle expiry before transport without advancing it. `CircuitForwarding_EnforcesWebIdleWithoutWaitingForBrowserNotification` covers a protected operation at minute 16, valid activity at minute 14, and subsequent expiry despite API traffic. Existing identity/revocation/scoped-client tests remain required. |
| [4442379068: Stay during leave/drain](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442379068) | Valid, fixed locally | Stay is disabled while `_discardingFailedSave` and its handler guards the same state. A retry completing during leave also cannot navigate or open a competing confirmation. The delayed direct-save theory holds draining pending and asserts Stay is disabled and navigation has not completed, then releases either success or cancellation. |
| [4442379114: retry bypasses incomplete-note confirmation](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442379114) | Valid, fixed locally | `HandleFailedSaveRetryAsync` now applies the same `CurrentCompletionState` check and pending-navigation/modal state as `OnLocationChangingAsync`. `FailedSaveNavigation_RetryRequiresIncompleteConfirmationBeforeLeaving` proves a successful save stays on the page until Continue. Existing retry-then-Stay coverage remains. |
| [4442379157: generic JS module/start failure](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442379157) | Valid, fixed locally | `WebSessionActivityMonitor.StartAsync` and disposal catch `JSException` and `JSDisconnectedException` separately, retaining canceled-task handling and connection-up retry. The first attempt incorrectly assumed an inheritance relationship; the correction and user-run results are recorded below. `FailedStartup_RetriesOnConnectionUp_WithoutRestartingSuccessfulMonitor` now covers generic JS errors during both import and start alongside disconnect/cancellation. |
| [4442379211: wrong base/full plan scope](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442379211) | Outdated against current main | The historical PR base snapshot explains the review, but current main already includes the three plan merges. Live GitHub comparison and local merge-base evidence both show only nine timeline files at the pushed head. No retarget is needed. Local documentation records the additional fixes from this pass; remote metadata/description reconciliation is deferred because PR writes are forbidden. |
| [4376077751: cancellation kills timeline circuit](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4376077751) | Invalid defect claim; already resolved | ASP.NET Core `Renderer.NotifyRenderCompleted`/`GetErrorHandledTask` ignore canceled lifecycle tasks; the formatter already provides UTC fallback. The three `PatientProfile_RendersLockedIntakeCalloutAndTimelineEntry` cases verify successful lookup, import cancellation, and lookup cancellation with unchanged instants and working navigation. User-confirmed passing evidence and the `863f8d6` reply already exist. No additional production change or duplicate reply. |
| Original summary: native runtime/install validation incomplete | Valid, unresolved verification gate | Successful simulator compilation and the earlier general build/test confirmation do not establish Android/iOS/Mac Catalyst installation or WebView timeline behavior. Device/runtime evidence remains required. |
| Original summary: final client acceptance incomplete | Valid, unresolved acceptance gate | AC1–AC3 technical evidence exists, but client sign-off has not been supplied. Only the client can close this gate. |

### Verification and limits

The working tree started clean. All changes here are for the verified findings, their tests, model metadata, architecture documentation, and changelog. No build, test, EF command, native install, or browser suite was run automatically. `git diff --check` and `git diff --cached --check` pass; the index remains unchanged. Previous test success must not be treated as verification of this new working tree.

The activity store contains timestamps only, is process-local, expires entries at the absolute session deadline, and falls back to signed initial sign-in time when no protected activity evidence is available. Cross-process requests/restarts can require fresh cookie validation; they cannot extend idle time. Patient concurrency metadata affects all EF patient writes, so include patient/integration regression coverage as well as intake tests.

Run from the repository root, starting with the focused test command (it builds the test project and its dependencies):

```bash
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj -c Debug --filter 'FullyQualifiedName~IntakeServiceTests|FullyQualifiedName~IntakeSubmissionConcurrencyTests|FullyQualifiedName~IntakeProfilePrefillIntegrationTests|FullyQualifiedName~IntakeWizardPageTests|FullyQualifiedName~NoteWorkspacePageTests|FullyQualifiedName~WebSessionActivityIntegrationTests|FullyQualifiedName~WebSessionActivityMonitorTests|FullyQualifiedName~ApiAccessTokenForwardingHandlerTests|FullyQualifiedName~WebScopedHttpClientFactoryTests|FullyQualifiedName~TimelineTimestampFormatterTests|FullyQualifiedName~PageScopedAppointmentUsageTests' --verbosity minimal -m:1 -p:BuildInParallel=false
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj -c Debug --no-build --no-restore --verbosity minimal -m:1 -p:BuildInParallel=false
dotnet build src/PTDoc.Api/PTDoc.Api.csproj -c Release -m:1 -p:BuildInParallel=false
dotnet build src/PTDoc.Web/PTDoc.Web.csproj -c Release -m:1 -p:BuildInParallel=false
dotnet build src/PTDoc.Maui/PTDoc.csproj -c Debug -f net8.0-ios -r iossimulator-arm64 -p:EnableCodeSigning=false -m:1 -p:BuildInParallel=false
dotnet build src/PTDoc.Maui/PTDoc.csproj -c Debug -f net8.0-android -m:1 -p:BuildInParallel=false
dotnet build src/PTDoc.Maui/PTDoc.csproj -c Debug -f net8.0-maccatalyst -m:1 -p:BuildInParallel=false
dotnet tool restore
EF_PROVIDER=sqlite dotnet tool run dotnet-ef migrations has-pending-model-changes -p src/PTDoc.Infrastructure.Migrations.Sqlite -s src/PTDoc.Api
```

The SDK/workloads and development secrets must already be configured as documented in `AGENTS.md`. Use project-targeted commands, not solution-wide clean/build that can select a signed iOS device target. The simulator signing override is not a distribution-signing configuration. Run the existing provider smoke categories in configured SQL Server/Postgres environments as described in `docs/EF_MIGRATIONS.md` before release; this pass does not claim those environments were available or tested. Native runtime/install and client acceptance remain separate from compilation.

### Future thread eligibility

After the above verification passes and the user authorizes publication, the nine valid code threads (4442378747, 4442378800, 4442378860, 4442378917, 4442378973, 4442379019, 4442379068, 4442379114, 4442379157) are candidates for one evidence-backed reply per thread and resolution after checking the pushed tree. The scope thread 4442379211 needs an explanatory reply and reconciliation of the displayed base/description before resolution; do not silently treat it as addressed by code. The cancellation thread is already resolved and needs no duplicate response. The two acceptance gates have no separate resolvable inline threads. There are no individually suppressed findings to reply to or resolve. This pass posted and resolved **zero** threads.


### User-run verification and seven-failure correction

The user reported 2,578 Debug tests executed: **2,571 passed, 7 failed, 0 skipped** (2 minutes 40 seconds). Release API and Web builds, Debug iOS simulator, Android, and Mac Catalyst builds all succeeded with **0 warnings and 0 errors**. Tool restore succeeded and the SQLite model-drift check reported no changes since the last migration. These results validate the pre-correction tree, not an all-green test suite.

Six failures were caused by replacing the monitor's disconnect catch with `JSException`: `JSDisconnectedException` inherits directly from `Exception`, as shown in [Microsoft's type declaration](https://learn.microsoft.com/sv-se/dotnet/api/microsoft.jsinterop.jsdisconnectedexception?view=aspnetcore-7.0). Restored the separate disconnect catch for startup and disposal, keeping generic JS-error and cancellation handling. Existing regressions cover import/start disconnect, pending-startup reconnect, termination, and disposal; none were removed or relaxed.

The seventh failure was in the intake recovery fixture. Its successful reload returned a mostly empty draft with the default Demographics step, unexpectedly rendering `CareTeamCard` without its provider-directory test service. The fixture now returns a complete Review-step draft using the same helper as the original state and asserts that recovery preserves the Review step. The assertions for blocked editing, no extra save/submit, and current insurance answers remain intact. No production intake change was needed.

No build or test was rerun by the agent. Both Git whitespace checks pass. Rerun the two affected test classes first, then the full Debug suite and the affected Release Web build:

```bash
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj -c Debug --filter 'FullyQualifiedName~IntakeWizardPageTests|FullyQualifiedName~WebSessionActivityMonitorTests' --verbosity minimal -m:1 -p:BuildInParallel=false
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj -c Debug --no-build --no-restore --verbosity minimal -m:1 -p:BuildInParallel=false
dotnet build src/PTDoc.Web/PTDoc.Web.csproj -c Release -m:1 -p:BuildInParallel=false
```

API/native source and model metadata did not change in this correction, so their successful checks do not need repeating for these edits. Native runtime/install evidence and client sign-off remain open. All changes remain uncommitted; no GitHub writes were made.


### User-confirmed full-suite success after correction

The user subsequently supplied a successful build (0 warnings, 0 errors, 48.80 seconds) followed by the Debug `net8.0` test result: **2,578 passed, 0 failed, 0 skipped**, duration **2 minutes 42 seconds**. This supersedes the seven-failure result for the corrected test suite. That excerpt did not identify the preceding build's project/configuration; the user subsequently supplied the explicit Release Web command and successful result below. The previously successful API/native builds and SQLite model-drift check remain applicable because those sources did not change in the correction.

Only this evidence record and the changelog changed after the passing run; no production or test code was changed and no verification was rerun by the agent. Both Git whitespace checks pass. The user subsequently ran the remaining Release Web command:

```bash
dotnet build src/PTDoc.Web/PTDoc.Web.csproj -c Release -m:1 -p:BuildInParallel=false
```

**Result: Build succeeded, 0 warnings, 0 errors, elapsed 00:00:01.42.** Together with the 2,578 passing Debug tests, previously successful Release API and Debug iOS simulator/Android/Mac Catalyst builds, and clean SQLite model-drift check, this completes the requested build/test verification. No production or test code changed after these results; only documentation was updated. The nine fixed code threads are candidates for publication-time verification and evidence-backed replies/resolution once explicitly authorized. Native runtime/install evidence and client acceptance remain separate open gates. No commit, push, reply, resolution, or review request was made.

## PR 158 follow-up audit — 2026-11-02 (review submitted 23:59 UTC)

This section supersedes the preceding inventory and publication status. Local HEAD and the live PR head are `a54dd60638fd88dd03a247e8cd2ddb6bbc89472f`. The previous fixes were committed/pushed with authorization, and their nine code threads were replied to/resolved. The PR description was subsequently expanded and scope thread 4442379211 resolved with separate authorization. **This pass authorizes local changes only**; no commit, push, GitHub reply, resolution, PR update, or review request was made.

### Complete inventory and classification

Retrieved the current thread-aware connector results, all review submissions, issue comments, and live GraphQL review/thread connections with minimized flags. There are **14 threads (3 open, 11 resolved), 3 outdated thread flags, 14 review submissions, 25 inline comments including replies, and 0 issue comments**. Every parent and nested connection reports `hasNextPage=false`. There are **0 minimized/suppressed comments and 0 Previously missed sections** in the exposed feedback. This inventory cannot reveal unpublished private reviewer drafts.

The three substantive review bodies contain **25 duplicate inline-finding references**: 1 in the original review, 11 in the preceding review, and 13 in the latest review (3 new findings, the scope item, and 9 resolved items). These references are classified **duplicate**, not new implementation requests. The latest summary's “4 open issues” predates resolution of the scope thread; the live state has 3 open threads. Eleven author replies and the empty review bodies introduce no additional findings.

“Valid, fixed locally” below means implementation and regression coverage are present. The user subsequently confirmed the build/test checks passed, then clarified that native installation/runtime checks were not run and are deferred to issue #197. No test counts or platform logs are inferred. “Outdated” means the previously valid issue is already addressed in the inspected branch or PR metadata, regardless of GitHub's line-outdated flag.

| Finding | Classification | Current evidence and disposition |
| --- | --- | --- |
| [4449237457: stale tab borrows accepted provenance](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4449237457) | **Valid, fixed locally** | `IntakeProfileProvenance.Preserve` previously replaced old source metadata without establishing that incoming answers had seen the accepted sources. Both API PUT routes and direct `IntakeService.SaveDraftAsync`/`SubmitAsync` call `PrepareAsync`; sync calls `Preserve` directly, and `IntakeApiService.SubmitAsync` saves before posting submit. Added a server-owned `ProfileSourceRevision` rotated by explicit profile reconciliation, validated before merging, and carried through DTO, wizard, persistence, and direct-service copying. A missing/older revision after reconciliation returns HTTP 409 or a service reload error. Sync rejects it before timestamp-based last-write-wins. `IntakeForm.LastModifiedUtc` now protects the read/save interval, with provider snapshot metadata and update-route conflict handling. Tests: `ProfileAcceptance_RejectsOlderTabBeforeMergingAnswers` (staff/standalone × primary/secondary), `ProfileAcceptance_RejectsStaleTabSaveAndSubmit` (both priorities), SQLite `Save_ConcurrentProfileAcceptanceCannotBeOverwritten`, sync `ReceiveClientPushAsync_ProfileRevisionOverridesNewerClientClock` (missing/stale/current), and wizard revision round-trip assertions. |
| [4449237491: legacy replacement retains authorization](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4449237491) | **Valid, fixed locally** | `UsePrimary` and `FillBlankFields` compared only nullable policy IDs. `IntakeProfilePrefillReader` supplies separate legacy fingerprints when normalized rows are absent; `UpsertAuthorizationAsync` can attach remaining answer fields to the newly projected policy. Both acceptance paths now compare primary legacy fingerprints as well as IDs before retaining authorization. `ProfileAcceptance_LegacyAuthorizationRequiresSameSource` covers both actions with changed, unknown, and unchanged sources, checking saved fields and actual authorization creation after submit. Existing authorization history is not deleted. |
| [4449237523: null/blank viewer zone](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4449237523) | **Valid, fixed locally** | `PatientProfile.OnAfterRenderAsync` passed JS output directly to `TimeZoneInfo.TryFindSystemTimeZoneById`. The [.NET 8 implementation](https://github.com/dotnet/runtime/blob/v8.0.11/src/libraries/System.Private.CoreLib/src/System/TimeZoneInfo.cs) calls `ArgumentNullException.ThrowIfNull(id)`; the component's catch filter does not cover it. Added a null/whitespace guard before resolution. Extended `PatientProfile_RendersLockedIntakeCalloutAndTimelineEntry` with null, empty, whitespace, and unknown IDs, asserting explicit UTC, unchanged ISO instant, and usable navigation. Empty/unknown IDs already returned false; those cases protect the fallback contract. |
| [4442378747: changed legacy payer source](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442378747) | **Outdated; fixed in a54dd60** | Reader fingerprints each priority; `HasCurrentPolicySourcesAsync` checks accepted legacy fingerprints; provenance strips forged source values. Existing `ProfilePrefill_LegacyPayerChangesRequirePerPriorityAcceptance` and `Submit_RejectsChangedLegacyPayerWithoutNormalizedPolicy` cover independent priorities and both API routes. The new stale-tab issue above is a distinct missing editor-version check. No duplicate source-validation fix needed. |
| [4442378800: policy inserted after validation](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442378800) | **Outdated; fixed in a54dd60** | `UpsertPolicyAsync` rejects an unexpectedly present policy when intake has no accepted ID; explicit `UpsertPatientRecordAsync` keeps its chart-edit contract. SQLite `Projection_RejectsPolicyInsertedAfterSourceValidation` covers both priorities and chart-edit control. No additional change. |
| [4442378860: concurrent demographic correction](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442378860) | **Outdated; fixed in a54dd60** | Patient timestamp is already an EF concurrency token in the model and all snapshots; submit paths handle concurrency. `Submit_ConcurrentChartEditRollsBackIntakeAndPolicyWrites` proves the chart correction survives and intake/policy writes roll back. This pass's intake token protects a different entity/race. |
| [4442378917: failed reload after acceptance](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442378917) | **Outdated; fixed in a54dd60** | `_profileReloadRequired` gates `CanEditDraft`, interactions, and write entry points until the saved snapshot reloads. `ProfileAcceptance_FailedReloadBlocksWritesUntilSavedAnswersAreRecovered` checks no further writes, current answers, and Review-step preservation. The new server revision additionally covers another tab; the UI fix remains required and intact. |
| [4442378973: uncertain remote save outcome](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442378973) | **Outdated; fixed in a54dd60** | Note recovery says an already-sent save may have reached the server; “Leave without retrying” avoids a rollback promise. `FailedSaveNavigation_OffersStayAndExplicitDiscard_WithoutDisposalRetry` and the delayed direct-save theory cover disclosure and success despite cancellation. No additional change. |
| [4442379019: Web idle callback race](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442379019) | **Outdated; fixed in a54dd60** | `ApiAccessTokenForwardingHandler` calls the server activity store's `IsCurrent` before transport; only validated activity advances the timestamp. `CircuitForwarding_EnforcesWebIdleWithoutWaitingForBrowserNotification` covers expired, renewed, and background-traffic cases. No additional change. |
| [4442379068: Stay during leave processing](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442379068) | **Outdated; fixed in a54dd60** | Stay is disabled and its handler guarded while `_discardingFailedSave`; retry completion guards the same state. `FailedSaveNavigation_DiscardCancelsAndDrainsDelayedDirectSaveBeforeLeaving` tests both delayed success and cancellation. No additional change. |
| [4442379114: retry bypasses incomplete confirmation](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442379114) | **Outdated; fixed in a54dd60** | `HandleFailedSaveRetryAsync` checks `CurrentCompletionState` and opens the existing confirmation before navigating. `FailedSaveNavigation_RetryRequiresIncompleteConfirmationBeforeLeaving` covers Continue. No additional change. |
| [4442379157: generic JS monitor startup failure](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442379157) | **Outdated; fixed in a54dd60** | Startup/disposal have separate `JSException`, `JSDisconnectedException`, and canceled-task handling. `FailedStartup_RetriesOnConnectionUp_WithoutRestartingSuccessfulMonitor` and pending-startup/termination/disposal cases retain retry/lifecycle coverage. No additional change. |
| [4442379211: incomplete security/data scope description](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442379211) | **Outdated; metadata corrected and thread resolved** | Live PR Description, Type of Change, Impact Areas, and Verification Commands now cover Web auth/session boundaries, intake provenance/concurrency/sync/TRICARE, note recovery, and builds/tests. They identify merged foundations #156/#157/#162 and pending manual gates. Reply 4449245711 records the correction; no retarget or duplicate reply is required. |
| [4376077751: canceled lifecycle kills circuit](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4376077751) | **Invalid defect claim; already resolved** | The [ASP.NET Core renderer](https://github.com/dotnet/aspnetcore/blob/v8.0.11/src/Components/Components/src/RenderTree/Renderer.cs) ignores canceled lifecycle tasks in `GetErrorHandledTask`; the existing UTC fallback remains. Import/lookup cancellation regressions from 863f8d6 remain intact. Null lookup is a separate non-cancellation exception, fixed above. |
| Original review: native runtime/install evidence incomplete | **Valid; deferred by user** | The user explicitly clarified that native installation/runtime checks were not run. Track them in [issue #197](https://github.com/BlackHouseDeveloper/PTDoc/issues/197) for a separate PR after Web setup. Compilation results do not close this gate. |
| Original review: final client acceptance incomplete | **Outdated; acceptance confirmed** | On 2026-11-02 (America/New_York), the user confirmed: “client has accepted it.” The client-acceptance gate is closed. This confirmation does not establish build/test results for the new local fixes or native installation/runtime results. |

No implementation finding remains ambiguous. Build/test verification and client acceptance are user-confirmed. Native installation/runtime validation remains unperformed and is explicitly deferred to issue #197; it is not part of the current work. There are no separate suppressed findings or Previously missed items to act on or resolve.

### Changes, compatibility, and verification

The working tree started clean; unrelated local changes were absent. Changes are limited to the three confirmed findings, their cross-layer call sites, focused tests, model metadata, architecture notes, this audit, and the changelog. Twenty new test cases were added and two existing wizard cases strengthened. No builds, tests, model-drift commands, browser runs, or native runs were executed by the agent. Both Git whitespace checks pass; nothing is staged. The prior 2,578-test success applies to `a54dd60`, **not these new edits**.

The revision is an optimistic-concurrency value, not an authorization credential. It is stored in existing response JSON and changes only on explicit reconciliation, so ordinary saves do not invalidate their own editor. Older drafts without a revision remain editable until reconciliation; thereafter an older client that drops the field receives a reload/conflict response rather than silently overwriting accepted answers. Creation strips supplied revisions. The existing intake timestamp and all three model snapshots gain concurrency metadata only; no DDL/data migration is added. SQLite model-drift verification is included in the subsequent user confirmation of the requested checks. Full-suite coverage matters because the token also protects other writes to the intake entity.

The requested verification commands, subsequently confirmed passing by the user, were:

```bash
# Focused tests build the test project and dependencies.
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj -c Debug --filter 'FullyQualifiedName~IntakeServiceTests|FullyQualifiedName~IntakeSubmissionConcurrencyTests|FullyQualifiedName~IntakeProfilePrefillIntegrationTests|FullyQualifiedName~SyncClientProtocolTests|FullyQualifiedName~IntakeWizardPageTests|FullyQualifiedName~PageScopedAppointmentUsageTests' --verbosity minimal -m:1 -p:BuildInParallel=false
# Full suite uses those freshly built assemblies.
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj -c Debug --no-build --no-restore --verbosity minimal -m:1 -p:BuildInParallel=false
dotnet build src/PTDoc.Api/PTDoc.Api.csproj -c Release -m:1 -p:BuildInParallel=false
dotnet build src/PTDoc.Web/PTDoc.Web.csproj -c Release -m:1 -p:BuildInParallel=false
dotnet build src/PTDoc.Maui/PTDoc.csproj -c Debug -f net8.0-ios -r iossimulator-arm64 -p:EnableCodeSigning=false -m:1 -p:BuildInParallel=false
dotnet build src/PTDoc.Maui/PTDoc.csproj -c Debug -f net8.0-android -m:1 -p:BuildInParallel=false
dotnet build src/PTDoc.Maui/PTDoc.csproj -c Debug -f net8.0-maccatalyst -m:1 -p:BuildInParallel=false
dotnet tool restore
EF_PROVIDER=sqlite dotnet tool run dotnet-ef migrations has-pending-model-changes -p src/PTDoc.Infrastructure.Migrations.Sqlite -s src/PTDoc.Api
git diff --check
git diff --cached --check
```

Client acceptance and the requested build/test checks were confirmed by the user on 2026-11-02. The user then clarified that native installation/runtime checks were not run and requested deferral until Web setup is complete, with a separate follow-up PR tracked in [issue #197](https://github.com/BlackHouseDeveloper/PTDoc/issues/197). No exact new test totals, timings, per-device logs, or SQL Server/PostgreSQL execution results were supplied; none are inferred. Provider-specific release checks remain governed by `docs/EF_MIGRATIONS.md`.

### Future thread eligibility

With verification user-confirmed, once publication is explicitly authorized and the pushed state is checked, threads **4449237457**, **4449237491**, and **4449237523** will be eligible for one evidence-based reply each and resolution. The other eleven threads are already resolved and should receive no duplicate replies. The native-validation and client-acceptance gates have no separate inline threads; client acceptance is closed, while native validation is deferred to issue #197. Suppressed/Previously missed counts are zero. This pass replied to and resolved **zero** threads.


### User-confirmed build/test completion and native-validation clarification

On 2026-11-02 (America/New_York), the user confirmed: “everything passed.” The user subsequently clarified that native runtime/install checks were not run and asked to defer them until after Web setup. The earlier interpretation that all native gates had passed was incorrect and is superseded here. Record the build/test checks as user-confirmed passing and client acceptance as already confirmed; native validation remains deferred to [issue #197](https://github.com/BlackHouseDeveloper/PTDoc/issues/197), created with explicit authorization for a later separate PR. No builds/tests were rerun by the agent, and no production or test code changed while recording this confirmation. Both Git whitespace checks pass. All changes remain uncommitted; this confirmation does not authorize a commit, push, GitHub reply, resolution, PR update, or another review request.

## PR 158 audit — 2026-11-02 (review submitted 2026-11-03 01:05 UTC)

This section supersedes earlier inventories and publication eligibility. Inspected local HEAD and live PR head: `c153cef42ba50815c452f39c3b60f92f5f75efe3`; live base: `7f3a1e5e2058c279d89dc0fc46ceed3aa1234bdd`. The prior fixes are committed and their threads resolved. This pass makes local changes only. Earlier passing results predated these edits; the user has now separately confirmed that all requested builds and tests pass, as recorded below.

### Retrieval and deduplication

Retrieved the current inline threads, all review submissions, the combined discussion timeline, and a paginated GraphQL inventory with `isMinimized`/`minimizedReason` and nested comment pagination. All connections were exhausted: **18 threads, 4 open, 14 resolved, 5 with GitHub's outdated-line flag, 32 inline comments, 18 reviews (4 nonempty), and no issue comments**. There are **zero exposed minimized/suppressed comments and zero Previously missed sections/items**, including within collapsed review-body details. Unpublished reviewer drafts are not exposed by these APIs.

The four review bodies contain **33 duplicate links to inline findings**, not 33 additional defects. Fourteen author replies and empty reviews add no separate findings. Threads 4449659215 and 4449659273 describe the same proposed self-induced legacy-payer conflict in two call sites and are deduplicated together below. Thus the inline inventory has **17 unique findings: 1 valid/actionable, 13 outdated, 3 invalid, 0 ambiguous**. Counting the repeated thread separately gives one duplicate thread. The original review also contains two non-inline acceptance findings: native validation remains valid but explicitly deferred, and client acceptance is outdated following the user's confirmation.

### Complete classification

“Outdated” means already addressed at the inspected head or in live PR metadata, irrespective of GitHub's line flag. “Fixed locally” identifies uncommitted changes. Verification was initially pending and has now been confirmed by the user, as recorded below.

| Finding / thread | Classification | Current code, contract, test, and history evidence |
| --- | --- | --- |
| [4376077751 — canceled zone lookup kills circuit](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4376077751) | **Invalid; already resolved** | `PatientProfile.razor:218` performs async lifecycle lookup and retains UTC until resolution. ASP.NET Core's `Renderer.NotifyRenderCompleted` and `GetErrorHandledTask` ignore canceled tasks. `PageScopedAppointmentUsageTests.PatientProfile_RendersLockedIntakeCalloutAndTimelineEntry` includes canceled import and lookup, semantic UTC instants, and usable navigation. Coverage was corrected in `863f8d60`; no additional catch is needed for the claimed cancellation failure. |
| [4442378747 — changed legacy payer source](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442378747) | **Outdated; fixed in a54dd606** | `IntakeProfilePrefillReader` computes per-priority legacy fingerprints. `IntakeDomainProjection.HasCurrentPolicySourcesAsync` compares them, and `IntakeProfileProvenance.Preserve` strips client overrides. Existing `ProfilePrefill_LegacyPayerChangesRequirePerPriorityAcceptance` and `Submit_RejectsChangedLegacyPayerWithoutNormalizedPolicy` cover both priorities and both HTTP routes. No extra source-check change needed. |
| [4442378800 — policy appears after source validation](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442378800) | **Outdated; fixed in a54dd606** | `IntakeDomainProjection.UpsertPolicyAsync` rejects an unexpectedly present policy when intake has no accepted ID. Explicit `UpsertPatientRecordAsync` deliberately retains chart-edit semantics. SQLite `Projection_RejectsPolicyInsertedAfterSourceValidation` tests both priorities, preserves the inserted row, and verifies the explicit chart-edit control. |
| [4442378860 — concurrent chart correction overwritten](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442378860) | **Outdated; fixed in a54dd606** | `ApplicationDbContext` marks `Patient.LastModifiedUtc` as a concurrency token; SQLite, SQL Server, and PostgreSQL snapshots agree. Both submit endpoints translate conflicts; the direct service surfaces a refresh error. SQLite `Submit_ConcurrentChartEditRollsBackIntakeAndPolicyWrites` preserves the competing correction and rolls back intake locking and policy insertion. |
| [4442378917 — failed reload re-enables stale answers](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442378917) | **Outdated; fixed in a54dd606** | Wizard `_profileReloadRequired` controls `CanEditDraft`, interaction locking, save and submit gates until the full snapshot loads. `ProfileAcceptance_FailedReloadBlocksWritesUntilSavedAnswersAreRecovered` verifies no further writes, restored insurance, and preserved Review step. |
| [4442378973 — misleading promise to discard dispatched save](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442378973) | **Outdated; fixed in a54dd606** | Note recovery explicitly says a dispatched save may have reached the server and offers “Leave without retrying.” `FailedSaveNavigation_OffersStayAndExplicitDiscard_WithoutDisposalRetry` checks disclosure; `FailedSaveNavigation_DiscardCancelsAndDrainsDelayedDirectSaveBeforeLeaving` covers canceled and successfully committed completion. No remote rollback is promised. |
| [4442379019 — Web idle callback race](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442379019) | **Outdated; fixed in a54dd606** | `ApiAccessTokenForwardingHandler` checks `activityStore.IsCurrent` before transport. `WebSessionActivityStore` enforces 15 minutes, and renewal rechecks expiry; API traffic does not renew it. `CircuitForwarding_EnforcesWebIdleWithoutWaitingForBrowserNotification` covers rejection, valid activity renewal, and later expiry. |
| [4442379068 — Stay can race leave draining](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442379068) | **Outdated; fixed in a54dd606** | Note Stay is disabled while `_discardingFailedSave`; `HandleFailedSaveStay` and retry completion guard that state. The delayed direct-save theory holds persistence pending and verifies disabled Stay and delayed navigation for both success and cancellation. |
| [4442379114 — retry bypasses incomplete-note confirmation](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442379114) | **Outdated; fixed in a54dd606** | `HandleFailedSaveRetryAsync` checks `CurrentCompletionState.IsComplete` and opens the existing confirmation before navigation. `FailedSaveNavigation_RetryRequiresIncompleteConfirmationBeforeLeaving` protects that flow. |
| [4442379157 — JS module startup cannot retry](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442379157) | **Outdated; fixed in a54dd606** | `WebSessionActivityMonitor.StartAsync` separately catches JS disconnection, JS exceptions, and canceled tasks, retaining reconnect eligibility without concurrent starts. Startup, pending-startup reconnect, termination, and disposal theories in `WebSessionActivityMonitorTests` cover the behavior. |
| [4442379211 — description omits security/data scope](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4442379211) | **Outdated; metadata corrected** | Live Description, Type of Change, Impact Areas, and Verification Commands cover auth/session forwarding, intake provenance/concurrency/sync, notes, and builds. The requested scope correction was previously authorized and the thread resolved. Some historical verification text still references a54dd606 and pending client acceptance; this audit records the newer state but does not authorize another GitHub metadata edit. |
| [4449237457 — stale tab borrows accepted provenance](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4449237457) | **Outdated; fixed in c153cef4** | `IntakeProfileProvenance.Preserve` validates `ProfileSourceRevision` before merging protected fields. Both HTTP updates and service save/submit use `PrepareAsync`; sync uses `Preserve`. Intake `LastModifiedUtc` is a concurrency token in all snapshots. Tests: `ProfileAcceptance_RejectsOlderTabBeforeMergingAnswers`, `ProfileAcceptance_RejectsStaleTabSaveAndSubmit`, SQLite `Save_ConcurrentProfileAcceptanceCannotBeOverwritten`, and sync `ReceiveClientPushAsync_ProfileRevisionOverridesNewerClientClock`. |
| [4449237491 — authorization follows replacement legacy payer](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4449237491) | **Outdated; fixed in c153cef4** | Both `UsePrimary` and `FillBlankFields` compare primary legacy fingerprints as well as policy IDs before retaining authorization. `ProfileAcceptance_LegacyAuthorizationRequiresSameSource` covers changed, unknown, and unchanged sources for both actions, including normalized authorization projection. |
| [4449237523 — null or blank viewer zone](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4449237523) | **Outdated; fixed in c153cef4** | `PatientProfile.razor:226` checks `!string.IsNullOrWhiteSpace(zoneId)` before zone resolution. Component tests cover null, empty, whitespace, and unknown values with explicit UTC fallback and intact navigation. |
| [4449659215 — API payer edits invalidate their own source](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4449659215) | **Invalid** | `ApplySubmittedIntakePatientFields` changes only the tracked patient before projection. `UpsertAsync` rechecks through `IntakeProfilePrefillReader.ReadAsync(db, patientId, clinicId)`, which explicitly queries `Patients.AsNoTracking()`; it reads the persisted pre-submit values, not tracked edits. There is no intervening save. The final `SaveChangesAsync` persists both together. Existing real concurrent-chart rollback coverage depends on reaching that save. New `Submit_LegacyPayerAnswerEditsDoNotConflictWithTheirOwnPatientWrite` explicitly checks successful staff/standalone × primary/secondary edits; staff creates normalized policies, standalone keeps its existing legacy-only contract. No production submission reordering required. |
| [4449659273 — direct service payer edits invalidate their own source](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4449659273) | **Duplicate of 4449659215; same invalid premise** | Direct `IntakeService.SubmitAsync` likewise writes only tracked fields before `UpsertAsync`, then calls `SaveChangesAsync`. `ForPatientWrite` returns a filtered draft copy and does not save. The shared no-tracking reader observes persisted data. New SQLite `Submit_LegacyPayerEditsUsePersistedSourceUntilSave` covers both priorities and verifies locked intake, updated legacy JSON, and normalized policy values. |
| [4449659248 — temporary insured draft lacks accepted source](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4449659248) | **Valid; fixed locally, verification user-confirmed** | Creation previously deserialized draft values into `IntakeProfilePrefill` without computing the legacy fingerprints. Subsequent source reads derive non-null fingerprints from saved patient JSON, causing a false conflict. Creation now calls an internal overload of the same profile reader with the newly constructed patient before the single patient/intake save. This reuses legacy alias normalization, source hashing, and demographics instead of trusting incoming provenance or splitting the transaction. `TemporaryPatient_InitializesLegacySourcesAndSubmitsInsurance` covers primary-only, secondary-only, both, and neither; verifies forged metadata replacement, saved-source equality, save/reload/submit, normalized policy values, and payer-type normalization using SQLite. |
| [4449659301 — discovery must compare repository-relative paths](https://github.com/BlackHouseDeveloper/PTDoc/pull/158#discussion_r4449659301) | **Invalid for current config/locked version** | The lockfile pins Playwright 1.60.0. Its JSON reporter uses `path.relative(config.rootDir, location.file)`, and config derives rootDir from top-level testDir. `playwright.config.ts` sets `testDir: './tests'`; the explicit boundary config inherits it. Both files are directly inside that directory, yielding `beta-e2e.spec.ts` and `session-circuit-boundary.spec.ts` as already asserted. Existing `session-boundary-discovery.test.mjs` exercises both configurations and the explicit seven-case count. Keep the exact comparisons; no path-normalization fix is necessary. |
| Original review — native runtime/install evidence incomplete | **Valid, explicitly deferred** | User clarified neither native install/runtime validation was completed and requested a future PR after Web setup. [Issue #197](https://github.com/BlackHouseDeveloper/PTDoc/issues/197) tracks this and the shared MAUI route-assembly blocker. Do not claim builds or launcher PR #198 complete runtime validation. No separately resolvable review thread. |
| Original review — client acceptance incomplete | **Outdated** | User explicitly confirmed “client has accepted it.” The acceptance gate is closed; that does not establish results for this pass's edits. No separately resolvable thread. |
| Review-summary references to inline items | **Duplicate** | All 33 links resolve to the inline findings above, including repeated “resolved since last review” sections. No separate implementation or reply is needed. |
| Suppressed / Previously missed findings | **None exposed** | All minimized flags are false and no corresponding sections or standalone findings occur in the fully retrieved bodies. No hidden-thread resolution is claimed. |

Framework sources checked for the invalid findings: [EF Core no-tracking query semantics](https://learn.microsoft.com/en-us/ef/core/querying/tracking#no-tracking-queries), [Playwright 1.60.0 JSON reporter](https://github.com/microsoft/playwright/blob/v1.60.0/packages/playwright/src/reporters/json.ts#L120-L127), [Playwright rootDir selection](https://github.com/microsoft/playwright/blob/v1.60.0/packages/playwright/src/common/config.ts#L75-L77), and [ASP.NET Core canceled lifecycle handling](https://github.com/dotnet/aspnetcore/blob/v8.0.11/src/Components/Components/src/RenderTree/Renderer.cs#L796-L824).

### Changes and verification handoff

Production change: nine added lines in `IntakeProfilePrefillReader.cs` provide the shared patient overload; temporary creation in `IntakeService.cs` uses it instead of draft deserialization. Existing public source reads remain no-tracking, clinic-scoped, and archive-filtered. Patient and intake creation still use one `SaveChangesAsync`. No schema, model, API contract, note, auth, browser-discovery, or submission-order changes are needed.

Added **10 cases** in the existing `IntakeSubmissionConcurrencyTests.cs` and `IntakeProfilePrefillIntegrationTests.cs` files: 4 temporary-patient, 2 direct-service edit, 4 API edit cases. Existing stale-source rejection and real concurrency rollback tests remain in place. The user subsequently confirmed all requested builds and tests pass for this patch. No new .NET test count or individual build logs were supplied; none are inferred. No builds or tests were run by the agent, following AGENTS.md. Whitespace checks and static source review pass.

Run from the repository root, first the focused classes, then the full suite and consuming builds:

```bash
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj -c Debug --filter 'FullyQualifiedName~IntakeSubmissionConcurrencyTests|FullyQualifiedName~IntakeProfilePrefillIntegrationTests|FullyQualifiedName~IntakeServiceTests' --verbosity minimal -m:1 -p:BuildInParallel=false
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj -c Debug --verbosity minimal -m:1 -p:BuildInParallel=false
dotnet build src/PTDoc.Api/PTDoc.Api.csproj -c Release -m:1 -p:BuildInParallel=false
dotnet build src/PTDoc.Web/PTDoc.Web.csproj -c Release -m:1 -p:BuildInParallel=false
dotnet build src/PTDoc.Maui/PTDoc.csproj -c Debug -f net8.0-ios -r iossimulator-arm64 -p:EnableCodeSigning=false -m:1 -p:BuildInParallel=false
dotnet build src/PTDoc.Maui/PTDoc.csproj -c Debug -f net8.0-android -m:1 -p:BuildInParallel=false
dotnet build src/PTDoc.Maui/PTDoc.csproj -c Debug -f net8.0-maccatalyst -m:1 -p:BuildInParallel=false
git diff --check
git diff --cached --check
```

To independently confirm the unchanged discovery contract (no app host or browser launch needed):

```bash
cd tests/PTDoc.Web.UiQa
npm ci --ignore-scripts
node --test tools/session-boundary-discovery.test.mjs
```

No migration/model change is introduced by this pass. Native installation/runtime remains deferred; the native commands above are compilation checks only.

### Unresolved items and future thread eligibility

No implementation finding is ambiguous. The user confirmed the requested build/test verification passes. Native validation remains deliberately unresolved under #197. The live PR description's historical checkout/result text can be refreshed with later publication authorization and new evidence; it was not edited here.

With verification user-confirmed, and only after explicit permission for publication/replies and a check of the published state, **4449659248** is eligible for an evidence-backed fix reply and resolution. **4449659215** and **4449659273** can receive separate call-site-specific invalid/duplicate dispositions using the now user-confirmed successful-submission coverage; neither should be described as a production ordering fix. **4449659301** can receive an invalid-finding disposition supported by the locked-version source/configuration and passing discovery test. Authorization to post/resolve remains required. The 14 already-resolved threads need no duplicate responses. There are no suppressed findings with missing resolvable threads to claim as resolved.

This pass created no commit, push, reply, resolution, PR-level response, or review request. All changes remain local. The initially modified database became clean independently during the pass; the agent never wrote, restored, staged, or deleted it. No launcher files from separate PR #198 were modified.


### User-confirmed verification completion

The user reported “all builds and tests pass” for the requested commands and supplied the discovery run output:

- `npm ci --ignore-scripts`: installed four packages, audited five, zero vulnerabilities reported.
- `node --test tools/session-boundary-discovery.test.mjs`: **2 passed, 0 failed, 0 skipped, 0 cancelled**, duration **1320.531583 ms**.
- Both ordinary hosted-QA exclusion and explicit seven-case session-boundary discovery passed, supporting the invalid classification of thread 4449659301.

No production or regression-test code changed while recording this confirmation, and no builds/tests were rerun. Native installation/runtime remains deferred under #197. This confirmation does not authorize committing, pushing, replying, resolving, updating the PR, or requesting review; all six changed repository files remain uncommitted.
