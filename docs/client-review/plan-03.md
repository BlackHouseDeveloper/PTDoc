# Plan PR 03: Correct patient timeline timestamps using the agreed display time zone

**Implemented** — only the changes and checks listed below are claimed.

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
