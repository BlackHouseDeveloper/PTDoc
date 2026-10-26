# Plan PR 17: Fax signed notes from review and improve clinic patient selection

**Partially implemented** — only the changes and checks listed below are claimed.

- Requirements: R025, R038
- Original plan status: Ready for Development
- Branch: `codex/plan-17-signed-note-fax`
- Base: `main`
- Expected GitHub number: #171; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-17)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Implemented R025/R038 in the shared UI, API and existing fax integration. The signed review link carries an exact UTC version and current patient; draft or unavailable/changed contexts cannot preselect a send. Stable patient pages use a 25-row selector with one lookahead row, tenant filtering, archived exclusion and an ID tie-breaker. The same selector serves inbound assignment.

The composer freezes an explicit patient/document/destination review, sends only on confirmation and retains the same RequestId after a failed response (including edit/review). The server rejects changed payload/user reuse and stale note versions; concurrent identical requests resolve to one transmission/outbox/log and discard the unused PDF. Legacy requests without RequestId retain their existing behavior and are not claimed idempotent. Failed-recipient resend uses one durable child per terminal parent; unknown provider outcomes require reconciliation.

New real-SQLite worker tests reproduced a pre-existing failure: newly keyed FaxStatusEvent entries were inferred Modified and caused DbUpdateConcurrencyException before provider submission. Explicitly marking new status events Added fixes that precise defect; accepted and lost-response provider paths now pass without a duplicate call.

Local Chromium verified the actual review hyperlink (keyboard Enter), exact UTC query binding, 31st synthetic patient selection, signed source preselection, stale-version rejection and explicit destination review. 390/768/1440 page widths equal their viewports. The mobile confirmation uses 44px touch targets and token-based styling. No transmission was queued by browser checks; provider features/workers were disabled. Only synthetic fixtures were used.

## Dependencies, blockers and owners

- Integration owner: supply approved sandbox credentials/destination and separately authorize external synthetic delivery before full AC4 acceptance. The current request prohibits external sends; all provider calls here are mocked.

## Decisions and options

Integration owner supplies sandbox access and test destination before end-to-end acceptance. Development can use mocks.

**Development impact:** The confirmed bounded scope can start; questions gate only the stated optional extension or acceptance detail. Do not add unresolved behavior to that scope.

**Dependent criteria:** Provider delivery/failure criterion at sandbox acceptance.

## Development specification

### User Problem

Users must leave the note review workflow to fax it and Fax Center patient selection is cumbersome.

### Confirmed Requirement

Provide fax composition equivalent to Fax Center from a signed note; offer current-clinic patient choices in Fax Center.

### Expected Behavior

An authorized user can open existing fax composition with the signed note selected, verify destination and explicitly send. Patient selection supports finding any eligible clinic patient.

### Scope

Reuse fax composition from review; replace/improve lookup with a searchable, paginated selector.

### Out of Scope

Multi-note fax bundles (exploratory), automatic sending, schedule faxing or a new fax vendor.

### Implementation Guidance

**Technical Recommendation:** reuse canonical signed-document rendering and existing fax service/status tracking. Use search-backed paging rather than loading every patient into the browser. Keep destination review and explicit Send.

### Data and Migration Considerations

Preserve transmission/document identifiers and audit history. A note edit/version change must not substitute an unsigned draft after selection.

### Security and Privacy Considerations

**Technical Recommendation:** verify patient/note/clinic permissions and recipient before enqueueing; protect fax content and credentials; use synthetic documents in sandbox tests.

### Acceptance Criteria

- [ ] **AC1:** Given an authorized user reviewing a signed note, when choosing Fax, then composition opens with that exact signed version and patient context.
- [ ] **AC2:** Given a draft/unsigned or inaccessible note, when attempting fax through UI or API, then it is not sent.
- [ ] **AC3:** Given a patient beyond the first result page, when searched in Fax Center, then the correct clinic-scoped patient can be selected.
- [ ] **AC4:** Given explicit Send with a verified destination, when the provider accepts or fails the request, then status and retry behavior are clear without duplicate dispatch.

### Rollout and Regression Risks

Wrong-recipient and duplicate-send risk; preserve explicit confirmation and traceable status.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~SignedNoteFaxTests&#124;FullyQualifiedName~FaxCenterTests&#124;FullyQualifiedName~PatientSelectorPagingTests&#124;FullyQualifiedName~SoapReviewPageTests&#124;FullyQualifiedName~ListEndpointQueryBindingIntegrationTests&#124;FullyQualifiedName~IntegrationServicesTests&#124;FullyQualifiedName~TenantIsolationTests' --verbosity minimal -m:1 | PASS: 90 tests, zero failed/skipped. Real SQLite concurrent queue/retry, worker outcomes, tenant boundaries, API paging/authorization and UI regression. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~FaxCenterTests' --verbosity minimal -m:1 | PASS: 6 component tests after the final context-loading guard. |
| dotnet build src/PTDoc.Web/PTDoc.Web.csproj --no-restore -m:1 --verbosity minimal | PASS: 0 warnings, 0 errors. |
| dotnet format whitespace src/PTDoc.Application/PTDoc.Application.csproj --no-restore --include src/PTDoc.Application/Integrations/IntegrationOperationModels.cs src/PTDoc.Application/Services/IPatientService.cs --verify-no-changes | PASS: changed C# files only. |
| dotnet format whitespace src/PTDoc.Infrastructure/PTDoc.Infrastructure.csproj --no-restore --include src/PTDoc.Infrastructure/Integrations/IntegrationOperationsService.cs --verify-no-changes | PASS: changed C# files only. |
| dotnet format whitespace src/PTDoc.Api/PTDoc.Api.csproj --no-restore --include src/PTDoc.Api/Patients/PatientEndpoints.cs --verify-no-changes | PASS: changed C# files only. |
| dotnet format whitespace src/PTDoc.UI/PTDoc.UI.csproj --no-restore --include src/PTDoc.UI/Services/PatientApiService.cs --verify-no-changes | PASS: changed C# files only. |
| dotnet format whitespace tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --include tests/PTDoc.Tests/Integration/PatientSelectorPagingTests.cs tests/PTDoc.Tests/Integrations/SignedNoteFaxTests.cs tests/PTDoc.Tests/UI/Pages/FaxCenterTests.cs tests/PTDoc.Tests/UI/Pages/PatientsPageTests.cs --verify-no-changes | PASS: changed C# files only. |
| Local Playwright CLI Chromium verification with synthetic fixtures | PASS: review link/UTC context, page-two patient, destination confirmation, stale-version rejection, 390/768/1440 widths; mobile screenshot inspected. Zero local browser-queued transmissions. |
| python3 workflow.py check 17; git diff --check | PASS: requirement IDs, document sections, local links, sensitive-path and whitespace checks. |

## Remaining acceptance work

AC1–AC3 and the local/mocked AC4 paths are verified. Full AC4 still needs authorized vendor sandbox delivery/failure/status reconciliation evidence from the integration owner. Keep this PR draft until that acceptance gate is met. Native MAUI publish/install and full assistive-technology/client acceptance are not claimed.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
