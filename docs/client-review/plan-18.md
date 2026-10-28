# Plan PR 18: Define clinician handoff and shared-treatment sign-off

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R041, R042
- Original plan status: Needs Clarification
- Branch: `codex/plan-18-clinician-handoff`
- Base: `main`
- Expected GitHub number: #172; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-18)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

### Implemented workflow

The shared note workspace exposes **Send to clinician** to the existing NoteWrite policy. A dirty editable draft must save successfully before opening. The dialog displays current responsibility, purpose, eligible opposite-role recipients, the selected recipient review, and the existing edit/signing restrictions. Saved signed and PendingCoSign notes use the same handoff surface without enabling editing.

`GET /api/v1/notes/{noteId}/handoffs` returns the authoritative UTC note version, handoff sequence, responsible clinician and eligible recipients. `POST` requires a request GUID, recipient GUID, purpose, expected UTC note version and expected handoff sequence. Both endpoints use NoteWrite; the service additionally resolves an active sender in the current clinic, verifies PT/PTA role and effective read/write capabilities, checks note and patient clinic ownership, and verifies an active same-clinic opposite-role recipient with effective note-read permission. No handoff creates permission or signature records.

### Atomicity, concurrency and privacy

`NoteHandoffs` is a new append-only metadata table recording clinic, note, sender, recipient, purpose, exact note version, timestamp and sequence. Its request primary key and unique note/sequence index arbitrate duplicate requests and competing sends. Responsibility is the recipient of the latest reassignment event; collaboration does not replace it. The history is separate from original authorship and clinical content.

A serializable transaction validates the note version and commits the event plus existing `UserNotifications` row together. Provider execution-strategy retries reuse the command ID and verify its original payload/actor. A repeated matching request returns the original receipt, even if the note changes afterward. Reusing an ID for another payload is a conflict. A competing new command with an old sequence or stale clinical version is a conflict. Storage failures do not masquerade as validation errors. A failed notification insert rolls back the handoff too.

Notification previews contain no patient name or clinical text. The target is the existing patient/note workspace route; opening it still enforces note access. One deliberately new forward uses a new request ID and creates one notification. Lost-response retries in the dialog freeze the reviewed payload and keep the same ID, including closing/reopening the dialog. A definite server conflict requires a reload and fresh recipient review. Reloading the entire browser page discards the in-memory request; the unknown-result message instructs users to keep the page open and retry the same send.

### Migration and existing evidence

All three provider migrations create only the handoff table, restrictive foreign keys and its indexes. Existing clinical notes/signatures are not backfilled or updated. SQL Server/Postgres snapshots materialize existing enterprise models and remove a pre-existing duplicate integration helper invocation that prevented scaffolding. Existing enterprise migration indexes are retained; generated duplicate index operations and equivalent SQL filter spelling changes were removed from the new DDL. All provider model-drift checks pass.

SQLite migration up/down/up passed on an isolated database. Actual SQL Server/Postgres database upgrades and concurrent workloads were not run in this environment; these remain provider rollout checks. Database downgrade drops handoff history: export/backup it first, or roll back the application without removing the additive table. Integrating independent Plan 14 and Plan 18 migrations requires reconciling snapshots and checking the combined model on all providers; no artificial code stack is needed.

### Acceptance evidence

- **AC1:** All 24 role/type/state combinations (PT/PTA × four stored note types × Draft/PendingCoSign/Signed) passed with exactly one correctly scoped recipient notification and the correct note link. Actual HTTP tests passed in both directions. Browser PT → PTA collaboration delivery and PTA → PT reassignment passed; the recipient opened the correct signed note from the notification, and the reassigned PT persisted as responsible clinician.
- **AC2:** Sequential and concurrent duplicate requests return the same receipt and one event/notification. Changed payload, stale sequence/version and malformed request tests pass. UI lost-response retry preserves the exact command through closing/reopening.
- **AC3:** Active status, opposite role, sender/recipient effective capabilities, clinic, patient and note boundaries are tested. HTTP anonymous requests return 401; Admin/Owner/Billing/FrontDesk/Aide/Patient requests return 403. Recipient discovery and direct posts enforce the same rules.
- **AC4:** Serialized clinical rows, signature hashes, signer/timestamp fields and content remain byte/value-identical across every matrix case. Existing PT/PTA signing regression tests pass. No signature service behavior was broadened.

### Browser findings and fixes

The recipient flow reproduced a pre-existing inbox defect: `.notif-list-area` used a zero flex basis inside a container without an explicit height, collapsing the list to 0px and hiding its clickable notification. Changing that list alone to a content-based flex basis produced a 121px list for the synthetic handoff and restored normal pointer navigation. The final build also explicitly rerenders the handoff dialog after a JavaScript Escape callback; a regression test and real keyboard check verify dismissal.

Final Chromium checks passed at 390/768/1440 widths with no horizontal overflow. The labeled dialog traps Tab, Escape closes it, background inert state is restored (zero remaining inert elements), and keyboard Enter confirms the reviewed recipient/purpose. The phone screenshot was visually inspected. Local SQLite contained exactly two events and two notifications after two deliberate forwards, one per direction; the signed note retained its original `{}` content, UTC last-modified timestamp and synthetic signature hash. No external channels or real patient data were used.


## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

The user approved the matrix on 2026-10-26, superseding the original brief’s unresolved policy gate:

| Decision | Approved behavior |
| --- | --- |
| Purpose | Collaboration and reassignment |
| Collaboration | Keep current responsible clinician |
| Reassignment | Change responsibility to the recipient |
| Directions | PT → PTA and PTA → PT only, within the same clinic |
| Eligible notes | All saved note types and all states: Draft, PendingCoSign and Signed |
| Editing | Both clinicians retain only their existing edit permissions; handoff grants none |
| Shared treatment signing | PT provides final signature; existing PTA Daily initial-sign → PendingCoSign → PT co-sign remains enforced |
| Signed notes | Handoff metadata can change; signed content/signatures remain immutable |

Legacy notes have no reliable original responsible-clinician field. They start Unassigned; the implementation does not infer authorship from the last editor. Collaboration preserves this state. A first explicit reassignment establishes responsibility. If an assignee later leaves the clinic, their ID remains in history and the dialog shows a neutral unavailable-clinician label without exposing their new-clinic profile.


## Development specification

### User Problem

Clinicians sharing treatment need to send work to each other and know who should complete or sign it.

### Confirmed Requirement

Add Send to between PT/PTA with a notification for each forwarded note. Shared-treatment completion/sign-off was discussed, but ownership, editable states and signature responsibilities were not settled.

### Expected Behavior

The approved PT↔PTA matrix provides collaboration or reassignment across saved note states with one in-app notification, explicit responsibility and unchanged edit/sign permissions.

### Scope

Approved same-clinic note collaboration/reassignment using existing in-app notification infrastructure.

### Out of Scope

Expanding PTA signing permissions, cross-clinic forwarding, sending an entire chart by default or treating notification as clinical acceptance.

### Implementation Guidance

**Technical Recommendation:** model handoff separately from signatures and maintain immutable signed versions. Use idempotent server commands and notification creation; decide reassignment versus collaboration explicitly.

### Data and Migration Considerations

Additive handoff history stores sender, recipient, purpose, note version and sequence; original authorship and signed content remain unchanged.

### Security and Privacy Considerations

**Technical Recommendation:** recipient eligibility and note access must be server enforced; notification previews should minimize patient information.

### Acceptance Criteria

- [x] **AC1:** Given an approved handoff matrix, when an eligible clinician forwards an eligible note, then the intended recipient receives one notification linked to the correct note.
- [x] **AC2:** Given a duplicate submission, when retried, then it does not create duplicate handoffs or notifications.
- [x] **AC3:** Given a recipient without clinic/note permission, when selected or posted directly, then handoff is denied.
- [x] **AC4:** Given a signed record, when handed off, then its signature and content remain immutable.

### Rollout and Regression Risks

The user-approved matrix above resolves the original decision gate. Handoffs never grant editing/signing rights; protect signed evidence and validate provider migrations before rollout.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~NoteHandoffTests&#124;FullyQualifiedName~NoteHandoffDialogTests&#124;FullyQualifiedName~NoteHandoffEndpointTests&#124;FullyQualifiedName~SignatureServiceTests&#124;FullyQualifiedName~AuthorizationCoverageTests&#124;FullyQualifiedName~NoteWorkspacePageTests' --verbosity minimal -m:1 | 108 passed; 0 failed; 0 skipped. Includes actual SQLite concurrency/rollback, HTTP authorization, all note states, signature preservation and UI recovery. |
| dotnet build src/PTDoc.Api/PTDoc.Api.csproj --no-restore -m:1; dotnet build src/PTDoc.Web/PTDoc.Web.csproj --no-restore -m:1 | API and Web builds passed with zero warnings/errors; final focused test run rebuilt both successfully. |
| EF_PROVIDER=sqlite PTDoc_DB_PATH=<isolated-synthetic-db> dotnet ef database update [20260823150000_LockAdminClinicSettingsRecovery] --no-build -p src/PTDoc.Infrastructure.Migrations.Sqlite -s src/PTDoc.Api | Upgrade to AddNoteHandoffs, downgrade to prior migration, and re-upgrade all passed. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~NoteHandoffDialogTests' --verbosity minimal -m:1 | 4 passed after final Escape fix; includes explicit JS callback rendering, lost-response retry, stale reload and save-failure guard. |
| dotnet format whitespace src/PTDoc.Core/PTDoc.Core.csproj --no-restore --include src/PTDoc.Core/Models/NoteHandoff.cs --verify-no-changes | PASS: changed C# files only; final Escape test rechecked. |
| dotnet format whitespace src/PTDoc.Application/PTDoc.Application.csproj --no-restore --include src/PTDoc.Application/Services/INoteHandoffService.cs --verify-no-changes | PASS: changed C# files only; final Escape test rechecked. |
| dotnet format whitespace src/PTDoc.Infrastructure/PTDoc.Infrastructure.csproj --no-restore --include src/PTDoc.Infrastructure/Services/NoteHandoffService.cs src/PTDoc.Infrastructure/Data/ApplicationDbContext.cs --verify-no-changes | PASS: changed C# files only; final Escape test rechecked. |
| dotnet format whitespace src/PTDoc.Api/PTDoc.Api.csproj --no-restore --include src/PTDoc.Api/Notes/NoteHandoffEndpoints.cs --verify-no-changes | PASS: changed C# files only; final Escape test rechecked. |
| dotnet format whitespace src/PTDoc.UI/PTDoc.UI.csproj --no-restore --include src/PTDoc.UI/Services/NoteHandoffApiService.cs --verify-no-changes | PASS: changed C# files only; final Escape test rechecked. |
| dotnet format whitespace src/PTDoc.Web/PTDoc.Web.csproj --no-restore --include src/PTDoc.Web/Program.cs --verify-no-changes | PASS: changed C# files only; final Escape test rechecked. |
| dotnet format whitespace tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --include tests/PTDoc.Tests/Notes/NoteHandoffTests.cs tests/PTDoc.Tests/Integration/NoteHandoffEndpointTests.cs tests/PTDoc.Tests/UI/Notes/NoteHandoffDialogTests.cs tests/PTDoc.Tests/Security/AuthorizationCoverageTests.cs --verify-no-changes | PASS: changed C# files only; final Escape test rechecked. |
| EF_PROVIDER=sqlite dotnet ef migrations has-pending-model-changes --no-build -p src/PTDoc.Infrastructure.Migrations.Sqlite -s src/PTDoc.Api | PASS: no pending model changes; SQL/Pg tooling supplied local placeholder connection strings without database connections. |
| EF_PROVIDER=sqlserver dotnet ef migrations has-pending-model-changes --no-build -p src/PTDoc.Infrastructure.Migrations.SqlServer -s src/PTDoc.Api | PASS: no pending model changes; SQL/Pg tooling supplied local placeholder connection strings without database connections. |
| EF_PROVIDER=postgres dotnet ef migrations has-pending-model-changes --no-build -p src/PTDoc.Infrastructure.Migrations.Postgres -s src/PTDoc.Api | PASS: no pending model changes; SQL/Pg tooling supplied local placeholder connection strings without database connections. |
| Local Playwright CLI Chromium synthetic handoff acceptance | PASS: PT collaboration → PTA inbox → exact signed note → PTA reassignment to PT → responsibility reload; two events/two notifications, unchanged signed evidence. Inbox list 0px defect reproduced and fixed to 121px; final 390/768/1440 widths, Tab/Enter/Escape and background restoration passed. |
| python3 workflow.py check 18; git diff --check | PASS: requirements, documentation sections, local links, sensitive-path and whitespace checks. |

## Remaining acceptance work

All four bounded handoff acceptance criteria are verified with the approved matrix. Actual SQL Server/Postgres database upgrades/concurrency, native MAUI device publishing and full assistive-technology/client acceptance remain rollout/platform checks and are not claimed. No external notification channels, production data, deployments or merges were used.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
