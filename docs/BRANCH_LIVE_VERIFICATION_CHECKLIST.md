# Client Feedback Branch Live Verification Checklist

This checklist validates every product change currently present on `clientfeedback/uiupdate` relative to `main` at `2d6e500` as of August 9, 2026. The branch changes are currently represented by the working tree, so re-run the branch inventory and update this checklist if files or requirements change before release.

Use this checklist with [PTDoc Beta QA](BETA_QA.md), [Responsive UI QA](RESPONSIVE_QA.md), [Clinical Visit Ordinal](CLINICAL_VISIT_ORDINAL.md), and the [Payer Normalization Runbook](PAYER_NORMALIZATION_RUNBOOK.md). Those documents remain authoritative for general environment, deployment, and migration procedures; this document is the targeted live regression gate for this branch.

## Result and evidence contract

Mark every check with one disposition:

- **Pass** — the expected result was directly observed.
- **Pass with limitation** — the primary outcome worked, with a documented non-blocking limitation.
- **Fail** — observed behavior contradicted the expected result.
- **Blocked** — required role, fixture, configuration, or service was unavailable.
- **Unsafe** — the action could not be performed safely or reversibly.
- **Not applicable** — include a concrete justification.

For every Fail, Blocked, or Pass with limitation, record:

- Timestamp and timezone.
- Environment, source SHA, and release ID.
- Browser/app version, OS, viewport, zoom, theme, and input method.
- Role and username without PIN or credentials.
- Route and checklist ID.
- Preconditions and synthetic fixture identifiers.
- Expected and observed behavior.
- Screenshot/video, console, and network references when PHI-safe.
- Data created, modified, and cleaned up.

Never record real PHI, payer identifiers, contact details, credentials, invite tokens, OTPs, or access tokens. Use seeded patients and `.test` data. Do not send a real intake, sign a clinical note, or archive production data.

## Execution record

| Field | Value |
| --- | --- |
| Environment |  |
| Web URL |  |
| API URL |  |
| Source SHA |  |
| Release ID |  |
| Database provider/version |  |
| Browser/app and version |  |
| OS |  |
| Tester |  |
| Started |  |
| Completed |  |

Recommended seeded roles and fixtures are documented in [PTDoc Beta QA](BETA_QA.md). At minimum, use `january.beta` for Admin, `dani.beta` for PT, `pta.beta` for PTA, and `patient.beta` for Patient coverage, with the current PIN supplied out of band.

## Branch-change coverage map

| Branch change group | Primary checklist coverage |
| --- | --- |
| SOAP intervention modules and libraries | `LV-SOAP-*`, `LV-REG-004` through `LV-REG-006` |
| Provider/template/insurance compile and migration stabilization | `LV-ENV-*`, `LV-PROV-*`, `LV-INS-*`, `LV-TPL-*` |
| Appointment modal draft preservation and handoff test fixes | `LV-HANDOFF-*`, `LV-REG-001` |
| OIDC/session and authentication regression stabilization | `LV-AUTH-*`, `LV-XCUT-006`, `LV-XCUT-007` |
| Legacy payer aliases, carrier catalog, and normalized projection | `LV-INS-010` through `LV-INS-014`, `LV-INTAKE-007` and `LV-INTAKE-008` |
| Manual responsive-QA workflow guidance | `LV-ENV-006`, `LV-NAV-*`, `LV-REG-005` |
| Appointment-type PATCH and immutable clinical visit ordinal | `LV-APPT-*`, `LV-XCUT-001`, `LV-XCUT-003`, `LV-XCUT-005` |
| Smaller-desktop navigation allocation | `LV-NAV-*` |
| Automatic Web and MAUI logout on invalid authentication | `LV-AUTH-*` |
| Tenant provider directory and relationships | `LV-PROV-*`, `LV-XCUT-*` |
| Normalized insurance policies and authorizations | `LV-INS-*`, `LV-XCUT-*` |
| Governed note-template administration | `LV-TPL-*`, `LV-XCUT-*` |
| Structured intake and Evaluation Subjective alignment | `LV-INTAKE-*`, `LV-REG-003`, `LV-REG-004` |
| Add Patient and Send Intake scheduling handoff | `LV-HANDOFF-*` |
| Editable appointment details and prototype metadata | `LV-APPT-001` through `LV-APPT-010`, `LV-APPT-015` |
| PT Dashboard Today's Appointments | `LV-DASH-009` through `LV-DASH-017` |
| Dashboard Notes/Authorization filtering | `LV-DASH-001` through `LV-DASH-008` |

## 1. Build, migration, and deployment gate

- [ ] **LV-ENV-001 — Source parity:** Confirm Web and API runtime diagnostics report the source SHA and release ID being tested. Expected: both hosts match the intended branch artifact before functional testing begins.
- [ ] **LV-ENV-002 — Health:** Confirm Web/API liveness and one API readiness probe. Expected: all are healthy and no migration/startup failure is reported.
- [ ] **LV-ENV-003 — Migration application:** Confirm both enterprise migrations and the clinical-visit-ordinal migration are recorded for the deployed database provider. Expected: provider, relationship, insurance policy, authorization, note-template, template-version, and visit-ordinal storage is present.
- [ ] **LV-ENV-004 — Existing data:** Open representative pre-branch patients, appointments, and notes. Expected: legacy records still load; unmigrated payer or legacy-note fallback does not surface `null`, `undefined`, raw JSON, or schema errors.
- [ ] **LV-ENV-005 — Clean client session:** Clear only the test browser’s PTDoc cookies/storage and sign in again. Expected: the app establishes a fresh authenticated circuit without console/framework errors.
- [ ] **LV-ENV-006 — Automated gate evidence:** Attach the user-run focused and full build/test output. Expected: no new build errors or warnings and relevant CoreCi, RBAC, Tenancy, OfflineSync, Compliance, DatabaseProvider, and UI tests pass.

## 2. Authentication expiration and protected navigation

Use a short-lived test session, controlled revocation, or another approved non-production method. Do not manipulate production signing keys.

- [ ] **LV-AUTH-001 — Normal login:** Sign in and open several protected routes. Expected: login, authorized navigation, and protected API requests work normally before expiration.
- [ ] **LV-AUTH-002 — Expiry while open:** Leave the authenticated app open until the API session expires, then invoke a protected action. Expected: the existing logout flow clears the authenticated UI and redirects to Login without a manual refresh.
- [ ] **LV-AUTH-003 — Expiry during navigation:** After expiration, navigate using the sidebar. Expected: protected content never renders as an authenticated shell with missing data; the user is signed out.
- [ ] **LV-AUTH-004 — Direct-route protection:** After logout, enter `/dashboard`, `/appointments`, `/patients`, `/settings`, and a note-workspace URL directly or use Back. Expected: every protected route returns to Login or the established access flow.
- [ ] **LV-AUTH-005 — Missing or malformed session:** In an approved test profile, remove or invalidate the required session token and initiate a protected request. Expected: this converges on the same centralized logout path and no invalid request loop continues.
- [ ] **LV-AUTH-006 — Terminal 401:** Cause one protected API request to return `401`. Expected: logout occurs once; concurrent failures do not create repeated redirects, duplicate dialogs, or request storms.
- [ ] **LV-AUTH-007 — Non-terminal 403:** As a valid user lacking a permission, open a forbidden function. Expected: access is denied without logging out the otherwise valid session.
- [ ] **LV-AUTH-008 — Fresh reauthentication:** Sign in after expiration. Expected: navigation and protected data recover without stale user, clinic, or route state.
- [ ] **LV-AUTH-009 — MAUI invalid-token path:** On each supported MAUI target available for the release, expire or invalidate a safe test token. Expected: refresh is attempted at most once where supported, local credentials clear on terminal failure, authentication state becomes anonymous, and Login is shown.

## 3. Smaller-desktop navigation layout

- [ ] **LV-NAV-001 — Beta floor:** At `1280x720`, 100% zoom, expanded desktop navigation, inspect the complete sidebar. Expected: brand uses approximately 30%, scrollable navigation approximately 50%, footer approximately 10%, and remaining space is balanced spacing.
- [ ] **LV-NAV-002 — Navigation scroll:** At `1280x720`, scroll the complete navigation list by mouse, keyboard, and touchpad. Expected: all items are reachable and the brand/footer remain usable without overlapping the list.
- [ ] **LV-NAV-003 — No clipping:** Inspect the first and last navigation items, badges, focus rings, brand content, and footer. Expected: no vertical/horizontal clipping or incoherent overlap.
- [ ] **LV-NAV-004 — Larger desktop:** Repeat at `1440x900` or `1536x864`. Expected: the sidebar remains intentionally balanced and does not create excessive empty or compressed regions.
- [ ] **LV-NAV-005 — Icon rail and drawer regression:** Collapse the desktop sidebar, then test below `1200px`. Expected: the icon rail and existing drawer behavior remain unchanged and every navigation target still works.
- [ ] **LV-NAV-006 — Themes:** Repeat the short-desktop check in light and dark themes. Expected: borders, text, icons, focus states, and scrolling remain legible.

## 4. Dashboard alerts and PT Today's Appointments

### Alert categorization

- [ ] **LV-DASH-001 — Default alert view:** As Admin or PT with mixed alert fixtures, open Dashboard. Expected: `All` is selected and Notes, Authorization, and other applicable alerts are visible.
- [ ] **LV-DASH-002 — Notes filter:** Select `Notes`. Expected: only alerts explicitly categorized as Notes render; no message-text matching artifacts appear.
- [ ] **LV-DASH-003 — Authorization filter:** Select `Authorization`. Expected: only authorization alerts render.
- [ ] **LV-DASH-004 — Restore All:** Return to `All`. Expected: the original complete alert feed is restored immediately without a new page load.
- [ ] **LV-DASH-005 — Independent expansion:** Expand an alert, change category, return to its category, and exercise its existing action. Expected: category selection does not break alert detail/dropdown behavior or navigation.
- [ ] **LV-DASH-006 — Category empty state:** Select a category with no results while other alerts exist. Expected: a category-specific empty state appears; the global no-alert state is not shown incorrectly.
- [ ] **LV-DASH-007 — Alert loading/error/dismissal regression:** Verify available loading, error, dismissal, urgent count, and navigation states. Expected: behavior remains consistent across categories.
- [ ] **LV-DASH-008 — Keyboard and screen reader:** Operate the category selector and alert expansion without a pointer. Expected: visible focus, meaningful labels, correct selected/expanded state, and logical tab order.

### PT Today's Appointments

- [ ] **LV-DASH-009 — PT-only visibility:** Sign in separately as PT, Admin, PTA, and Patient. Expected: Today's Appointments appears only for PT and only on Dashboard.
- [ ] **LV-DASH-010 — Appointment grid:** As PT with a populated day, inspect the section at desktop width. Expected: two columns when space permits, one column when constrained, no fixed-height clipping, and long content wraps safely.
- [ ] **LV-DASH-011 — Status variants:** Verify Scheduled, Checked In/note-started, intake In Progress, intake Missing, and completed/terminal fixtures where available. Expected: badge and action hierarchy matches state; Missing includes textual `Action Required`, not color alone.
- [ ] **LV-DASH-012 — Operational metadata:** Compare time, duration, appointment type, PN Due, visit count, scheduling note/description, and intake status to authoritative appointment/patient records. Expected: no fabricated dates or counts; absent PN Due is omitted.
- [ ] **LV-DASH-013 — Checked-in actions:** Use `Start Note` and `Details` on a safe checked-in fixture. Expected: each routes through the established note/details workflow with the correct patient and appointment.
- [ ] **LV-DASH-014 — Scheduled actions:** Exercise `Check In`, `Details`, and `Reschedule`. Expected: established workflows open with the correct appointment; visible Cancel remains disabled because no cancellation endpoint is implemented.
- [ ] **LV-DASH-015 — Add action:** Select `Add`. Expected: the existing new-appointment workflow opens; no parallel creation flow appears.
- [ ] **LV-DASH-016 — Loading/error/empty:** Verify these states using approved fixtures or controlled API failure. Expected: clear status, recovery where established, and no stale cards.
- [ ] **LV-DASH-017 — Visual/accessibility:** At `1280x720` and a larger desktop, light and dark themes, keyboard through all card actions. Expected: 44px targets, visible focus, readable status contrast, no overflow, and correct tab order.

## 5. Appointment details, type PATCH, and clinical visit ordinal

- [ ] **LV-APPT-001 — Complete details:** Open a scheduled appointment from `/appointments`. Expected: patient name/ID, date, time, duration, status, editable appointment type, PN Due, Visit, and Appointment Notes appear with graceful unavailable values.
- [ ] **LV-APPT-002 — Authoritative type options:** Open the appointment-type selector. Expected: all and only scheduling-supported types from the shared catalog appear, including Re-evaluation as distinct from Evaluation.
- [ ] **LV-APPT-003 — Narrow PATCH:** Change appointment type and inspect the network request. Expected: `PATCH /api/v1/appointments/{id}/appointment-type` is used rather than a reconstructed full appointment update.
- [ ] **LV-APPT-004 — Successful refresh:** After the PATCH succeeds, inspect the modal, scheduler block, dashboard card where applicable, and reopen after refresh. Expected: every consumer displays the persisted new type.
- [ ] **LV-APPT-005 — No collateral overwrite:** Before changing type, record patient, clinician, start/end, duration, status, scheduling note, PN Due, and visit ordinal. Expected: all remain unchanged afterward.
- [ ] **LV-APPT-006 — Failure rollback:** Force an approved API failure. Expected: the selector returns to the persisted value and visible error feedback appears; schedule state does not falsely update.
- [ ] **LV-APPT-007 — Stale concurrency:** Open the same appointment in two sessions and update from one before changing type in the other. Expected: the stale change is rejected without overwriting the newer appointment.
- [ ] **LV-APPT-008 — Terminal restrictions:** Open completed, cancelled, and other locked appointments. Expected: type editing follows existing business rules and cannot bypass terminal restrictions.
- [ ] **LV-APPT-009 — PN Due source:** Compare displayed PN Due with the latest applicable Evaluation/Progress Note plan-of-care data. Expected: exact agreement or an explicit unavailable state; no independent calculation is visible.
- [ ] **LV-APPT-010 — Visit meaning:** Compare displayed Visit with `ClinicalVisitOrdinal`, and separately inspect attended `VisitCount` where exposed. Expected: immutable ordinal and attended count are not conflated.
- [ ] **LV-APPT-011 — Ordinal assignment:** Create multiple eligible scheduled visits for one synthetic patient. Expected: unique, increasing patient-scoped ordinals are assigned by the server.
- [ ] **LV-APPT-012 — Ordinal immutability:** Change type and reschedule an eligible visit. Expected: its ordinal does not change.
- [ ] **LV-APPT-013 — Cancellation/no-show:** Cancel or mark no-show only if an approved reversible fixture exists. Expected: displayed visit number follows the documented hiding rule and its ordinal is not reused by a later appointment.
- [ ] **LV-APPT-014 — Legacy fallback:** Open an unmigrated/legacy appointment fixture if available. Expected: visit information uses the compatibility fallback without modifying unrelated data.
- [ ] **LV-APPT-015 — Appointment notes distinction:** Confirm scheduling notes render as `Appointment Notes` and are not labeled or treated as clinical Progress Notes.

## 6. Add Patient and Send Intake from scheduling

Use a unique fake `.test` patient for each submission path and record created IDs for cleanup.

- [ ] **LV-HANDOFF-001 — Entry point:** Open New Appointment and invoke Add Patient. Expected: the existing patient form opens inside the established modal workflow.
- [ ] **LV-HANDOFF-002 — Two clear actions:** Inspect the action area on desktop and narrow width. Expected: Cancel, Add Patient, and Add Patient and Send Intake are distinguishable, keyboard reachable, and do not overflow.
- [ ] **LV-HANDOFF-003 — Shared validation:** Submit both actions with the same invalid patient fields. Expected: the existing patient validation is used in both paths with no duplicate rules or duplicate create attempts.
- [ ] **LV-HANDOFF-004 — Add Patient only:** Create a fake patient with `Add Patient`. Expected: exactly one patient is persisted, no intake is sent, the patient appears and is selected in the appointment form, and the modal resumes its prior state.
- [ ] **LV-HANDOFF-005 — Draft preservation:** Before opening Add Patient, enter appointment type, clinician, date/time, duration, and a unique appointment note. Expected: every value, including the latest note keystroke, is restored after patient creation.
- [ ] **LV-HANDOFF-006 — Combined success:** Create a different fake patient with a valid synthetic intake destination using `Add Patient and Send Intake`. Expected: patient creation completes first, the authoritative patient ID feeds the established Send Intake flow, one invitation is sent, and the appointment resumes with that patient selected.
- [ ] **LV-HANDOFF-007 — Required destination:** Omit the delivery field required by the configured intake channel. Expected: only the combined path is blocked with a clear explanation; Add Patient remains governed by its existing requirements.
- [ ] **LV-HANDOFF-008 — Patient-create failure:** Force a safe create failure. Expected: no intake preparation/send request occurs and entered patient/appointment data remains recoverable.
- [ ] **LV-HANDOFF-009 — Intake-send failure after create:** Force a controlled invitation failure after successful creation. Expected: the patient exists once, remains selected/available, contextual partial-success feedback appears, and retry does not recreate the patient.
- [ ] **LV-HANDOFF-010 — Double submission:** Double-click or press Enter repeatedly during each path. Expected: submission actions disable appropriately and neither patient nor invitation is duplicated.
- [ ] **LV-HANDOFF-011 — Permissions:** Verify PatientWrite and intake-send role boundaries using allowed and denied roles. Expected: the modal does not bypass server policies.

## 7. Tenant provider directory and patient-provider relationships

- [ ] **LV-PROV-001 — Settings visibility:** As Admin/Owner, open Settings > Provider Directory. Expected: pending and active sections, search, refresh, and candidate submission are available. PT sees only the Documentation settings entry and cannot administer providers.
- [ ] **LV-PROV-002 — Active search:** Search by provider name, organization, and exact NPI. Expected: only active providers in the current clinic appear in general search.
- [ ] **LV-PROV-003 — Patient relationship:** On `/patient/{id}/info`, search an approved provider and add Primary Care, Referring, and Other relationships as supported. Expected: the relationship persists, displays role/status, and can be removed without deleting the directory provider.
- [ ] **LV-PROV-004 — Provider not found:** Submit a unique provider candidate from the patient Care Team Directory. Expected: it is immediately linked only to that patient as Pending and is not reusable in another patient’s active search.
- [ ] **LV-PROV-005 — Intake candidate:** Submit an unknown provider during intake. Expected: the same pending, patient-scoped workflow is used and the same-primary/referring shortcut does not create a duplicate provider.
- [ ] **LV-PROV-006 — Approval:** As Admin/Owner, approve a unique pending candidate. Expected: status becomes Active, it leaves the pending queue, appears in clinic-wide search, and the patient relationship remains intact.
- [ ] **LV-PROV-007 — Rejection:** Reject a unique candidate. Expected: it leaves reusable search and the patient record communicates the resulting status without exposing it clinic-wide.
- [ ] **LV-PROV-008 — Exact NPI duplicate:** Submit a candidate with an active provider’s NPI. Expected: exact tenant-local duplicate detection prevents approval as a separate provider and requires merge/review.
- [ ] **LV-PROV-009 — Fuzzy duplicate:** Submit without NPI using a normalized matching name plus phone/address. Expected: a visible possible-duplicate warning requires explicit merge or `Approve as separate` confirmation.
- [ ] **LV-PROV-010 — Merge:** Merge a pending candidate into an active provider. Expected: relationships point to the target provider and the candidate is not reusable as a separate active entry.
- [ ] **LV-PROV-011 — Archive:** Archive an active provider. Expected: it disappears from general search while historical patient relationships remain understandable.
- [ ] **LV-PROV-012 — Tenant isolation:** In two approved test clinics, repeat provider search with distinct data. Expected: no cross-clinic providers, candidates, relationships, or duplicate hints appear.
- [ ] **LV-PROV-013 — Concurrency:** Review/update the same candidate from two sessions. Expected: the stale action is rejected without overwriting the first decision.
- [ ] **LV-PROV-014 — Audit privacy:** Inspect authorized audit output for submit, approve, reject, merge, and archive. Expected: event type and non-PHI identifiers/counts are present; provider and patient names are absent from metadata.

## 8. Normalized insurance policies and authorizations

Follow the migration sequence and reconciliation rules in [Payer Normalization Runbook](PAYER_NORMALIZATION_RUNBOOK.md).

- [ ] **LV-INS-001 — Patient policies UI:** Open `/patient/{id}/info` and locate Insurance Policies. Expected: existing normalized policies and nested authorizations load independently of the legacy payer editor.
- [ ] **LV-INS-002 — Create policy:** Add a fake Primary policy with carrier suggestion, member/group data, dates, plan year, cost sharing, and optional adjuster information. Expected: typed values persist after refresh and the stable carrier key is retained for a catalog match.
- [ ] **LV-INS-003 — Unmatched carrier:** Save a safe unmatched carrier display name. Expected: the explicit display name persists without fabricating a catalog key.
- [ ] **LV-INS-004 — Self-pay:** Add a Self Pay policy without carrier/member values. Expected: it is accepted if otherwise valid and does not display misleading empty identifiers.
- [ ] **LV-INS-005 — Coverage priorities:** Add active Primary, Secondary, and Tertiary policies and reorder them. Expected: exactly one active policy per priority; attempting a duplicate active priority is rejected visibly.
- [ ] **LV-INS-006 — Policy history:** Archive/expire a policy. Expected: it leaves active use but remains available as history and does not erase authorization history.
- [ ] **LV-INS-007 — Authorization/referral:** Add and edit authorization and referral records with dates, reference, authorized/used units, period, reauthorization date, threshold, and notes. Expected: each record persists independently and is attached to the correct policy.
- [ ] **LV-INS-008 — Typed validation:** Attempt negative amounts/units, coinsurance over 100, invalid date ranges, or other rejected enum/numeric values. Expected: save is rejected without changing the persisted record.
- [ ] **LV-INS-009 — Concurrency:** Edit one policy or authorization in two sessions. Expected: stale writes produce a conflict and do not overwrite the newer record.
- [ ] **LV-INS-010 — Intake upsert:** Submit partial insurance/authorization data through intake for an existing patient. Expected: the matching normalized records update after patient persistence and omitted values do not erase existing data.
- [ ] **LV-INS-011 — Dual-read/write parity:** During compatibility mode, compare normalized API output, Patient Info, authorization dashboard alert/copay behavior, and legacy payer data for migrated and unmigrated patients. Expected: normalized-first results agree and fallback occurs only where intended.
- [ ] **LV-INS-012 — Backfill idempotence:** Run the approved backfill twice in a disposable database. Expected: the second run creates no duplicates; malformed/ambiguous rows are reported without deleting source JSON.
- [ ] **LV-INS-013 — Alias preservation:** Include legacy member-ID aliases in a disposable backfill fixture. Expected: member/policy values survive normalization.
- [ ] **LV-INS-014 — Tenant isolation and sync:** Verify policy/authorization sync in two approved clinics and an offline client. Expected: only authorized tenant records synchronize; push/pull preserves concurrency and priority rules.

## 9. Structured intake and Evaluation Subjective alignment

- [ ] **LV-INTAKE-001 — Functional limitations:** On Intake question 6, expand Self Care, Mobility, Household Activities, Community Activities, and Sleep; select multiple activities. Expected: groups expand/collapse accessibly and selections remain visible when revisited.
- [ ] **LV-INTAKE-002 — Structured context:** Complete prior/current function, onset, cause/mechanism, imaging, and additional limitations. Expected: values survive wizard navigation and draft reload.
- [ ] **LV-INTAKE-003 — Evaluation seed:** Submit intake, start a new Evaluation, and inspect Subjective. Expected: structured values and source-backed limitation metadata prefill using IntakePrefill provenance without overwriting clinician-authored content.
- [ ] **LV-INTAKE-004 — Body-region catalog alignment:** Compare intake functional options with the selected body region and clinician Subjective options. Expected: both use the shared canonical catalog; unknown values are not silently accepted.
- [ ] **LV-INTAKE-005 — None exclusivity:** For medications, comorbidities, and assistive devices, select values and then `None`, and reverse the sequence. Expected: `None` is mutually exclusive and contradictory selections cannot persist.
- [ ] **LV-INTAKE-006 — Medication/device search:** Search and select documented medications, comorbidities, and assistive devices. Expected: result chips are keyboard operable and exact selections survive navigation/reload.
- [ ] **LV-INTAKE-007 — Carrier suggestions:** Enter primary and secondary insurer text. Expected: reusable carrier suggestions appear without preventing an unmatched display name.
- [ ] **LV-INTAKE-008 — Workers’ compensation:** Select Workers’ Compensation and inspect conditional adjuster/coverage fields. Expected: applicable fields remain available and unrelated payer fields are not fabricated.
- [ ] **LV-INTAKE-009 — Same provider shortcut:** Use the same-primary/referring-provider option. Expected: both relationships are represented without auto-creating a duplicate provider entry.
- [ ] **LV-INTAKE-010 — Authorized contacts:** Enter up to three contacts, select approved PHI recipients, review, save, and reopen. Expected: selections remain linked by stable contact identity rather than copied strings.
- [ ] **LV-INTAKE-011 — PHI release:** Toggle authorization and inspect Review/clinician view. Expected: Allowed/denied status and selected recipients are understandable without exposing clinical notes or unrelated contacts.
- [ ] **LV-INTAKE-012 — Legacy consent:** Open a safe legacy consent fixture. Expected: aliases hydrate to the canonical packet, invalid recipient references are pruned/rejected, and no `null` identifiers appear.
- [ ] **LV-INTAKE-013 — Accessibility/responsive:** Complete the changed cards at `1280x720` and a narrow viewport using keyboard only. Expected: accordion state, checkboxes, suggestions, errors, and Review content remain reachable with no clipping.

## 10. Versioned note-template administration

Use an Admin/Owner author and a different PT clinical reviewer. Do not publish a template in production without explicit approval.

- [ ] **LV-TPL-001 — Baseline fallback:** Before creating tenant templates, start each supported note type: Evaluation, Daily, Progress Note, and Discharge, plus Re-evaluation/Dry Needling variants where applicable. Expected: packaged baseline layouts resolve and render.
- [ ] **LV-TPL-002 — Settings role boundary:** As Admin/Owner, open Settings > Documentation and Compliance. As PT, open Settings. Expected: Admin/Owner sees draft administration; PT sees only clinical template governance; other settings remain hidden from clinical-approver-only PT mode.
- [ ] **LV-TPL-003 — Create draft:** Clone a packaged baseline into a tenant draft for each supported type/variant needed by the test. Expected: Draft version 1 is created with ordered sections and fields.
- [ ] **LV-TPL-004 — Builder edits:** Change labels, help text, order, visibility, required state, defaults, approved choice source, static choices, and supported visibility rules. Expected: preview reflects edits and the draft saves with optimistic concurrency.
- [ ] **LV-TPL-005 — Validation:** Validate a permitted schema. Expected: a clear valid summary appears. Then use an unknown binding, unsupported renderer, or unsupported condition. Expected: validation rejects it; no arbitrary script/expression is accepted.
- [ ] **LV-TPL-006 — Specialized renderer preservation:** Preview sections backed by body-region or other specialized clinical components. Expected: templates compose existing registered components and authoritative catalog keys rather than replacing them with copied values.
- [ ] **LV-TPL-007 — Submit:** Submit a valid draft for clinical approval. Expected: status becomes Pending Clinical Approval and Admin editing stops.
- [ ] **LV-TPL-008 — Two-person publish:** As a different PT, review comparison/history and publish. Expected: the active version updates; the original submitter cannot self-publish.
- [ ] **LV-TPL-009 — Reject/revise:** On a separate draft, return it for changes with a comment. Expected: Admin can revise/validate/resubmit without changing the published version.
- [ ] **LV-TPL-010 — Published immutability:** Attempt to edit a published version. Expected: direct editing is unavailable/rejected; change requires cloning a new draft.
- [ ] **LV-TPL-011 — Version pinning:** Create a note under version A, publish version B, then reopen the existing draft and create a new note. Expected: existing draft remains pinned to A; new note uses B; signed notes retain their original version.
- [ ] **LV-TPL-012 — Retirement:** Retire a published version with an approved disposable template. Expected: historical notes still render; new notes use another active version or packaged compatibility fallback.
- [ ] **LV-TPL-013 — Compliance hard stops:** Try to remove or weaken signatures, PTA restrictions, signed-note immutability, Review, and other statutory constraints through a template. Expected: server-enforced compliance remains authoritative.
- [ ] **LV-TPL-014 — Concurrency/tenant isolation:** Edit a draft in two sessions and inspect another test clinic. Expected: stale save conflict is visible and templates never cross clinic boundaries.

## 11. SOAP Notes Intervention Tab UI

Use an approved writable Evaluation draft with no real PHI. The new modules intentionally coexist above the existing CPT, assistance, cueing, response, general-intervention, and HEP-note controls.

- [ ] **LV-SOAP-001 — Empty modules:** Open the Evaluation Interventions tab with no exercise/manual fixtures. Expected: `Exercises`, `0 exercises`, exact exercise empty copy, `Manual Work Techniques`, `0 techniques`, and exact manual empty copy render in stacked full-width cards.
- [ ] **LV-SOAP-002 — Existing controls preserved:** Scroll below the new modules. Expected: existing CPT Procedure Codes, intervention/activity rows, general interventions, and HEP notes remain present and functional.
- [ ] **LV-SOAP-003 — Exercise dialog default:** Select Add Exercise. Expected: centered `Add Therapeutic Exercise` dialog, exact description, Exercise Library selected, search, all ten region filters, six exercise fixtures, focus trap, Escape/close behavior, and no backdrop-click dismissal.
- [ ] **LV-SOAP-004 — Exercise filter:** Enter `Scap` and select Shoulder. Expected: Scapular Retraction is the only visible result; focus outline and selected filter are clear.
- [ ] **LV-SOAP-005 — Add exercise:** Add Pendulum Exercise and close the dialog. Expected: count updates and an expanded card displays Range of Motion, Shoulder, Sets `3`, Reps `10`, Frequency `3x/week`, and the exact HEP label.
- [ ] **LV-SOAP-006 — Card interactions:** Edit prescription values, toggle HEP, collapse/expand, duplicate, and remove. Expected: each visible state updates predictably, icon-only controls have descriptive accessible names, and removal does not affect unrelated rows.
- [ ] **LV-SOAP-007 — Custom Exercise:** Reopen, select Custom Exercise, enter name and optional notes, then Add Custom Exercise. Expected: exact fields/action appear and a new visible exercise card is inserted without undocumented validation banners or toasts.
- [ ] **LV-SOAP-008 — Manual library default:** Select Add Technique. Expected: `Add Manual Technique`, exact description, Technique Library selected, ten region filters, no search field, and seven documented technique rows.
- [ ] **LV-SOAP-009 — Manual Shoulder filter:** Select Shoulder. Expected: the five Shoulder techniques remain visible and Elbow techniques are excluded.
- [ ] **LV-SOAP-010 — Add technique:** Add a technique and close. Expected: technique count updates through the existing Plan collection; no invented populated manual-technique card appears.
- [ ] **LV-SOAP-011 — Custom Technique boundary:** Select Custom Technique. Expected: the tab is selectable, but no undocumented fields, actions, or validation appear.
- [ ] **LV-SOAP-012 — Read-only note:** Open a read-only Evaluation. Expected: add/edit/duplicate/remove/HEP mutation is unavailable, collapse remains usable, and clinical content remains readable.
- [ ] **LV-SOAP-013 — Layout/accessibility:** Verify light mode at the reference desktop width and a narrow viewport with keyboard and increased text size. Expected: no clipping, horizontal overflow, focus loss, or unexpected layout shift. Dark-specific visual parity is not a requirement of the supplied intervention specification.

## 12. Cross-cutting API, sync, tenancy, and audit checks

- [ ] **LV-XCUT-001 — Endpoint authorization:** Exercise new provider, insurance, template, appointment-type PATCH, and dashboard routes with allowed and denied roles. Expected: server policies—not UI visibility alone—enforce access.
- [ ] **LV-XCUT-002 — Cross-tenant denial:** Attempt direct IDs from a second approved test clinic for every new aggregate. Expected: no provider, relationship, policy, authorization, or template data leaks or mutates.
- [ ] **LV-XCUT-003 — Offline sync:** Pull and push provider relationships, policies, authorizations, and appointment ordinals with an approved offline client. Expected: tenant/role filtering, server-owned ordinal assignment, timestamps, and conflicts remain correct.
- [ ] **LV-XCUT-004 — Audit:** Inspect audit entries for sensitive mutations. Expected: action, tenant-safe identifiers, and outcome are present without provider/patient names, payer values, contact data, notes, tokens, or other PHI in metadata.
- [ ] **LV-XCUT-005 — Concurrency response:** For appointment type, provider, insurance, and templates, verify stale writes return a conflict rather than a silent last-write-wins update.
- [ ] **LV-XCUT-006 — Loading and retry:** Introduce a safe transient API failure on each new settings/patient panel. Expected: bounded loading, an accessible error, and established retry/refresh behavior with no duplicate mutation.
- [ ] **LV-XCUT-007 — Console/network hygiene:** Exercise all changed routes while observing console and network panels. Expected: no unhandled exception, Blazor error overlay, repeated unauthorized loop, `localhost` request in hosted Beta, or sensitive value in URLs/logs.

## 13. Final regression and release decision

- [ ] **LV-REG-001 — Existing scheduling:** Create, open, reschedule, check in, and start a note using safe fixtures. Expected: existing workflows still work outside the new type and patient-handoff paths.
- [ ] **LV-REG-002 — Existing patient workflow:** Search patients, open chart tabs, edit an approved reversible Patient Info value, refresh, and restore it. Expected: no regression from provider/insurance additions.
- [ ] **LV-REG-003 — Existing intake:** Complete and reopen an intake without using the new provider or normalized payer options. Expected: legacy-compatible behavior remains intact.
- [ ] **LV-REG-004 — Existing note types:** Open/save approved drafts for Evaluation, Daily, Progress Note, and Discharge. Expected: template resolution and Intervention mounting do not regress unrelated sections or note types.
- [ ] **LV-REG-005 — Theme and viewport matrix:** Run the established responsive matrix and manually inspect every changed component at `1280x720`, one larger desktop, and one narrow viewport in applicable themes. Expected: no document overflow, clipped actions, unreadable states, or lost focus.
- [ ] **LV-REG-006 — Keyboard pass:** Navigate changed pages without a pointer. Expected: logical order, visible focus, operable dialogs/tabs/selects/accordions, meaningful names, and focus restoration.
- [ ] **LV-REG-007 — Data cleanup:** Archive/delete only created synthetic fixtures using approved reversible workflows and restore edited records. Expected: no stray patients, appointments, invites, candidates, policies, authorizations, drafts, or notes remain.
- [ ] **LV-REG-008 — Evidence reconciliation:** Every checklist item has a disposition and every failure/blocker links to evidence. Expected: no silent omissions.
- [ ] **LV-REG-009 — Release decision:** Record Go, Conditional Go, or No-Go with owner, unresolved critical/high findings, accepted limitations, and follow-up dates.

## Automated commands to accompany live verification

Repository policy requires the user to run build and test commands and provide their output before commit. Start with focused coverage, then run the broader gates:

```bash
dotnet build PTDoc.sln --no-restore

dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj \
  --filter "FullyQualifiedName~InterventionLibrarySectionTests|FullyQualifiedName~EvaluationInterventionsSectionTests|FullyQualifiedName~Appointment|FullyQualifiedName~Dashboard|FullyQualifiedName~ProviderDirectory|FullyQualifiedName~InsurancePolicy|FullyQualifiedName~NoteTemplate|FullyQualifiedName~Intake|FullyQualifiedName~Authentication" \
  --verbosity normal

dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter "Category=CoreCi" --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter "Category=RBAC" --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter "Category=Tenancy" --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter "Category=OfflineSync" --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter "Category=Compliance" --verbosity normal
```

For local browser verification, start API and Web with the repository setup documented in [Responsive UI QA](RESPONSIVE_QA.md), then run:

```bash
cd tests/PTDoc.Web.UiQa

PTDOC_WEB_BASE_URL=http://localhost:5145 \
PTDOC_UI_QA_USERNAME=<safe-test-user> \
PTDOC_UI_QA_PIN=<out-of-band-pin> \
npm run test:responsive
```

For hosted Beta, use the manual `UI Responsive QA` workflow with `upload_artifacts=true` and the required safe fixture paths. Never store credentials or browser storage-state artifacts in the repository.

## Completion summary

| Measure | Count |
| --- | ---: |
| Total checks | 148 |
| Pass |  |
| Pass with limitation |  |
| Fail |  |
| Blocked |  |
| Unsafe |  |
| Not applicable |  |
| Critical/high open findings |  |
| Synthetic records requiring cleanup |  |

**Release decision:**<br>
**Decision owner:**<br>
**Decision timestamp:**<br>
**Accepted limitations:**<br>
**Required follow-ups and owners:**
