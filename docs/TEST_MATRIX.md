# PTDoc Functional Test Matrix

## Scope and evidence rules

This matrix is an inventory of independent manual-test scenarios for the
browser-facing PTDoc Web application. It is intended for manual QA, UI audits,
regression planning, and future browser automation. It covers browser-facing
routes declared in `src/PTDoc.UI/Pages` and `src/PTDoc.Web/Program.cs`,
role-specific behavior, and workflows called out by the beta QA and UI QA
documentation.

Each scenario's **Action** starts with an evidence label:

- **Confirmed** — the route/control/permission is directly declared in source or
  explicitly covered by an existing QA document. The expected result is the
  observable behavior documented by that evidence; it has not necessarily been
  executed in this session.
- **Observed** — a query-backed state, redirect, or browser-QA flow is directly
  referenced as an existing supported behavior.
- **Expected** — reasonable test coverage is documented or implied by an explicit
  feature surface, but live behavior or deployment configuration still needs
  validation.
- **Unable to Verify** — the repository does not provide enough evidence or a safe
  fixture to define a concrete pass/fail outcome.

Do not use real PHI, credentials, payer details, phone numbers, or clinic secrets.
The shared beta PIN is out-of-band and must never be written into test evidence.

### Known fixtures

| Fixture | Use |
| --- | --- |
| `january.beta` (Admin) | Dashboard, settings/admin, patient management, intake administration. PIN is out-of-band. |
| `dani.beta` (PT) | Appointments, clinical notes, documentation, export, intake review, AI when enabled. PIN is out-of-band. |
| `pta.beta` (PTA) | PTA note permissions, draft editing, PT-only boundary checks. PIN is out-of-band. |
| `patient.beta` (Patient) | Patient-only surfaces and clinician-route restrictions. PIN is out-of-band. |
| `BETA-PT-001` Avery Adams | Seeded right-shoulder patient. |
| `BETA-PT-002` Jordan Lee | Seeded low-back patient. |
| `BETA-PT-003` Morgan Patel | Seeded right-knee patient. |
| `BETA-PT-004` Riley Chen | Seeded neck-pain patient. |
| Safe fake record | Use `Audit Test <timestamp>`, `audit+<timestamp>@example.test`, fictional address/phone, and approved sandbox values. |
| Reversible Evaluation draft | A PT-owned draft supplied through `PTDOC_UI_QA_EVALUATION_DRAFT_PATH`; restore original content after the test. |
| Other writable note/intake | `PTDOC_UI_QA_WRITABLE_NOTE_WORKSPACE_PATH` or `PTDOC_UI_QA_INTAKE_PATH`, only when approved as safe and reversible. |

When a fixture is not named below, the matrix says **Unknown – Requires
Verification** rather than assuming a record, permission, external integration,
or deployment setting exists.

## Authentication and public pages

| Role | Route | Action | Expected Result | Data Fixture | Screenshot Needed |
| --- | --- | --- | --- | --- | --- |
| Anonymous | `/login` | **Confirmed —** Open the canonical login URL. | PTDoc login page renders with username/email, 4-digit PIN, theme toggle, and available external-login messaging. | None | Yes — baseline login page. |
| Anonymous | `/login` | **Confirmed —** Submit with username and PIN blank. | Inline required-field validation appears and the form remains on Login. | None | Yes — validation evidence. |
| Anonymous | `/login` | **Confirmed —** Submit a malformed PIN (not exactly four digits). | PIN format validation appears; no authenticated shell or protected page is shown. | Fake username; no real credential | Yes — validation evidence. |
| Anonymous | `/login` | **Confirmed —** Submit invalid credentials. | Login returns to `/login` with visible failure feedback and no authenticated navigation. | Fake username and fake PIN | Yes — invalid-login message. |
| Admin/PT/PTA/Patient | `/login` → `/auth/login` | **Confirmed —** Sign in with a seeded account. | Web creates the authenticated session and redirects to the safe return URL; default is `/` except Patient clinician-route handling. | Corresponding seeded account; out-of-band PIN | Yes — post-login landing and role nav. |
| Patient | `/login` → `/intake` | **Confirmed —** Sign in with no custom return URL. | Patient is redirected to `/intake` because `/` is classified as a clinician route. | `patient.beta`; out-of-band PIN | Yes — patient landing page. |
| Anonymous | Protected route such as `/dashboard` | **Observed —** Open a protected route while signed out. | Router redirects to `/login?returnUrl=...`; after successful login, the user can return to the safe requested route when authorized. | None, then a seeded authorized account | Yes — redirect URL and login return state. |
| Authenticated non-clinical role | `/`, `/dashboard` | **Confirmed —** Open Dashboard as FrontDesk, Billing, Aide, or PracticeManager. | Route is not authorized and the router sends the authenticated user to `/denied`; record that the sidebar may still show Dashboard. | A role-specific account; **Unknown – Requires Verification** for seeded credentials | Yes — permission result and visible nav mismatch. |
| Patient | `/patients`, `/appointments`, `/notes`, `/settings` | **Confirmed —** Open clinician/admin routes directly. | Route authorization denies access and no clinician data or admin controls render. | `patient.beta`; out-of-band PIN | Yes — permission result. |
| Authenticated user | `/logout` → `/auth/logout` | **Confirmed —** Click Sidebar Logout. | Session is cleared and browser returns to `/login`; stale authenticated shell is not usable. | Any seeded account | Yes — post-logout login page. |
| Anonymous | `GET /auth/login` | **Confirmed —** Open the legacy GET login endpoint directly. | Host redirects to the canonical `/login` page without starting an authenticated session. | None | No — simple redirect; capture only if it fails. |
| Anonymous | `GET /auth/external/start?returnUrl={path}` | **Expected —** Select external sign-in or open the SSO start endpoint when Entra is enabled. | OpenID Connect challenge starts and returns to a validated local path; when Entra is disabled, the control/route is unavailable. | Entra-enabled deployment or explicit disabled configuration; **Unknown – Requires Verification** | Yes — SSO availability/error state, without identity-provider secrets. |
| Anonymous | `GET /auth/external-login?returnUrl={path}` | **Expected —** Open the legacy external-login alias when Entra is enabled. | Alias starts the same external challenge as `/auth/external/start`; disabled environments do not expose it. | Entra-enabled deployment; **Unknown – Requires Verification** | Yes — only if route is exposed. |
| Anonymous | `/signup` | **Expected —** Open Registration and inspect availability. | If self-service registration is enabled, Sign Up fields and clinic/role choices render; otherwise the route/tab is unavailable or suppressed. | None; deployment configuration required | Yes — enabled/disabled registration state. |
| Anonymous | `/signup` | **Expected —** Submit a valid fake registration. | Registration confirmation states that the account is awaiting administrator approval, if self-service registration is enabled. | Fake `.test` identity and approved clinic/role fixture; **Unknown – Requires Verification** | Yes — confirmation message. |
| Anonymous | `/forgot-password` | **Confirmed —** Switch recovery channel between email and SMS and submit a fake contact. | The selected contact label/channel changes; response remains safe and does not disclose whether an account exists. | Fake email/phone | Yes — recovery form and confirmation/error state. |
| Reset-token holder | `/reset-password?token={token}` | **Confirmed —** Open a valid reset link, enter matching new PIN values, and submit. | Token is accepted, PIN reset completes, and page navigates to `/login`. | Safe generated reset token; **Unknown – Requires Verification** | Yes — completion confirmation. |
| Reset-token holder | `/reset-password?token={token}` | **Expected —** Open an invalid, expired, or missing token. | Reset is rejected with visible safe feedback; no PIN change occurs. | Invalid/expired token; **Unknown – Requires Verification** | Yes — error state. |
| Anonymous | `/sms-consent`, `/privacy`, `/privacy-policy`, `/terms`, `/terms-and-conditions` | **Confirmed —** Open each public document and follow cross-links. | Each document renders in the public-document layout; Privacy/Terms aliases resolve to the same content and links remain usable. | None | Yes — one representative legal page; additional screenshots only for defects. |
| Authenticated unauthorized user | `/denied` | **Confirmed —** Follow Return to Dashboard. | Link targets `/`; authorized clinical roles reach Dashboard, while non-clinical roles remain denied. | Seeded Patient or other non-clinical role | Yes — denial message and target behavior. |
| Anonymous/authenticated | `/not-found`, unknown URL | **Confirmed —** Open an unmatched path. | Anonymous users are sent to Login; authorized ClinicalStaff users see Page Not Found with recovery links; authenticated non-clinical users are denied by the protected catch-all. | None, then seeded role | Yes — representative not-found/denied result. |
| Any visitor | `/Error` | **Confirmed —** Open the framework/application error destination or capture it after a controlled rendering failure. | Error page presents a user-facing error message rather than raw exception details; it is not a normal feature navigation target. | Controlled non-production failure; **Unknown – Requires Verification** | Yes — error evidence without PHI. |
| Any visitor | `/header-test` | **Unable to Verify —** Open the source-declared global-header test page directly. | Determine whether the page is intentionally exposed in the deployed environment; do not treat exposure as production behavior without product confirmation. | Deployment intent and safe environment; **Unknown – Requires Verification** | Yes — only to document exposure or access restriction. |
| Any visitor | `/patient/info` | **Unable to Verify —** Open the no-ID Patient & Payer route directly. | Determine whether the app selects a patient, redirects, shows an empty state, or denies access; no concrete destination is established by source. | Authenticated role and patient-selection fixture; **Unknown – Requires Verification** | Yes — capture actual outcome for route documentation. |

## Dashboards and application shell

| Role | Route | Action | Expected Result | Data Fixture | Screenshot Needed |
| --- | --- | --- | --- | --- | --- |
| Admin | `/` | **Confirmed —** Review dashboard overview, notifications, recent activity, recent notes, POC cards, and authorization widgets. | Dashboard loads or shows a visible loading/empty/error state; cards identify actionable clinic activity without exposing unrelated tenant data. | `january.beta`; seeded activity if present | Yes — dashboard baseline. |
| PT | `/dashboard` | **Confirmed —** Open the dashboard alias and inspect clinical workflow cards. | `/dashboard` resolves to the same dashboard page as `/`; PT-visible clinical/schedule/billing widgets render according to role layout. | `dani.beta`; seeded activity if present | Yes — PT dashboard. |
| PTA | `/` | **Expected —** Inspect dashboard layout and role-specific widget visibility. | Dashboard access is allowed by `ClinicalStaff`; PTA receives clinical/schedule-focused content and no unauthorized system-health controls. | `pta.beta`; out-of-band PIN | Yes — PTA dashboard differences. |
| Admin/PT | Dashboard metric cards | **Observed —** Click Patients Today, Appointments, Notes Due, Drafts, Unsigned, and Intake cards. | Each card navigates to its documented queue: appointments, filtered notes, or intake; route/query and visible filter agree. | Seeded activity with each count; **Unknown – Requires Verification** if empty | Yes — one successful tile-to-page transition and any broken tile. |
| Admin/PT | Dashboard alerts/activity | **Confirmed —** Open a notification, recent activity, recent note, or authorization target. | Target route opens the referenced patient, note, appointment, or queue with context preserved. | Seeded alert/activity; **Unknown – Requires Verification** if absent | Yes — target page with context. |
| Admin/PT | Dashboard | **Confirmed —** Force a recoverable dashboard load failure and select Retry/Refresh. | Error state is visible, Retry is actionable, and a successful retry restores content without a blank shell. | Controlled API failure or unavailable backend; **Unknown – Requires Verification** | Yes — error and recovered states. |
| All authenticated roles | Main shell/sidebar | **Confirmed —** Navigate through each role-visible sidebar item and inspect active state/badges. | Only policy-allowed items are actionable; active route styling and Intake/Notes badge counts correspond to the current page. | Seeded account per role; badge-producing data **Unknown – Requires Verification** | Yes — role nav evidence. |
| All authenticated roles | Main shell | **Observed —** Toggle compact/mobile menu at desktop and mobile widths using click, Enter, and Space. | Menu opens/closes, `aria-expanded` and visible navigation state update, and no page-level overflow appears. | Any seeded account; 1280×720 plus mobile viewport | Yes — open menu at desktop/mobile. |
| All authenticated roles | Main shell | **Expected —** Toggle light/dark theme, reload, and revisit Dashboard or Login. | Theme changes visibly and persists after reload without unreadable text or clipped controls. | Any account; browser local storage available | Yes — before/after theme states. |

## Patient management

| Role | Route | Action | Expected Result | Data Fixture | Screenshot Needed |
| --- | --- | --- | --- | --- | --- |
| Admin | `/patients` | **Observed —** Search seeded patient by name and MRN, then clear search. | Matching patient cards appear; clearing restores the directory; no-results state is recoverable. | `january.beta`; Avery Adams / `BETA-PT-001` | Yes — search result and empty/cleared state if available. |
| PT | `/patients` | **Confirmed —** Open a seeded patient card. | Patient chart opens at `/patient/{id}` with patient identity and clinical context. | `dani.beta`; one seeded patient | Yes — chart header. |
| Admin | `/patients?action=add` | **Observed —** Open Add Patient from the directory, submit blank required fields, then cancel. | Add modal opens from route state; validation is visible; Cancel closes the modal and returns to the directory without a record. | `january.beta`; no real data | Yes — modal and validation. |
| Admin | `/patients?action=add` | **Expected —** Submit a fake patient with valid fields and search for it afterward. | Patient creation succeeds visibly, directory can find the fake patient, and chart opens. | Safe fake record; cleanup plan required | Yes — successful create and lookup. |
| Owner/Billing/Aide/FrontDesk | `/patients` | **Confirmed —** Search/open a patient within read capability. | Directory/chart are readable according to `PatientRead`; write-only controls are absent or rejected. | Role account and seeded patient; non-seeded role credentials **Unknown – Requires Verification** | Yes — read-only boundary. |
| Patient | `/patients` | **Confirmed —** Attempt directory navigation from direct URL and shell. | Patient cannot access the directory or clinician chart; no patient-list data renders. | `patient.beta`; out-of-band PIN | Yes — denial/no-data evidence. |
| PT/Admin | `/patient/{id}` | **Observed —** Switch Timeline, Notes, Documents, and Communications tabs and reload/direct-link each query state. | `tab=...` changes the active chart section and survives refresh/direct URL where supported; patient context remains visible. | Seeded patient, e.g. Avery; safe chart data | Yes — each active tab or one composite evidence capture. |
| PT | `/patient/{id}?action=new-note` | **Observed —** Open the chart route with `action=new-note`, choose a note type, then navigate away. | Note-type chooser opens from the route query; leaving the query closes the chooser and starts/opens the intended workspace. | `dani.beta`; seeded patient | Yes — chooser open and workspace route. |
| PT/Admin | `/patient/{id}/info` | **Confirmed —** Open Patient & Payer Information panels and inspect validation/save states where editable. | Payer, authorization, referral, visit-utilization, and supporting-document panels render; invalid values show visible validation; successful changes remain after reload when a writable fixture exists. | Safe patient info fixture; **Unknown – Requires Verification** for writable values | Yes — complex panel and validation/success state. |
| Admin/PT | `/patient/{id}` Documents tab | **Observed —** Upload a synthetic non-PHI text/PDF fixture, verify the row, reload, and verify it remains. | Accepted upload appears in Documents and persists after chart reload; invalid/oversized types are rejected visibly if exercised. | Safe seeded patient; synthetic non-PHI file | Yes — uploaded row and reload persistence. |
| Admin/PT | `/patient/{id}` Communications tab | **Expected —** Inspect or add a safe communication-log entry when the control is available. | Communication list/form displays clear patient context and successful entry remains after reload; if unavailable, record as documented limitation. | Safe seeded patient; fake non-PHI communication; **Unknown – Requires Verification** | Yes — form/list or unavailable-state evidence. |

## Scheduling and appointments

| Role | Route | Action | Expected Result | Data Fixture | Screenshot Needed |
| --- | --- | --- | --- | --- | --- |
| Admin/PT/PTA/FrontDesk/PracticeManager | `/appointments` | **Confirmed —** Open Appointments from sidebar and inspect Today view. | Today heading, schedule state, patients, statuses, and available actions render without overlap; empty/loading/error states are explicit. | Role account; seeded appointments **Unknown – Requires Verification** | Yes — Today view. |
| Admin/PT | `/appointments?dateRange=week` | **Observed —** Switch Today/Week via visible control and direct URL. | Heading, selected view, empty state, and schedule grouping remain synchronized with `dateRange=week`. | Seeded appointments or safe empty state | Yes — Week view and route. |
| Admin | `/appointments?dateRange=week&groupBy=clinician` | **Observed —** Switch clinician/day grouping and, when available, select `clinicianId`. | Week view uses the requested grouping and clinician scope; selected state matches URL. | Admin account; PT/PTA appointments and clinician fixture **Unknown – Requires Verification** | Yes — grouping selector/state. |
| Admin/PT | `/appointments?needsNote=true&dateRange=today` | **Observed —** Open dashboard Notes Due/Appointments needing notes route. | Appointment list filters to today’s visits needing notes; no unrelated view is shown. | Appointment requiring note; **Unknown – Requires Verification** | Yes — filtered result. |
| Admin/PT/FrontDesk/PracticeManager | `/appointments?action=appointments.new` | **Confirmed —** Open new-appointment action and submit blank fields, then cancel. | Appointment form/modal opens; required-field validation is visible; Cancel returns without creating a visit. | Role account; no real patient | Yes — form and validation. |
| Admin/PT | `/appointments` | **Expected —** Create a safe appointment with valid patient/date/type data. | Appointment appears in the selected schedule view and can be opened afterward. | Safe fake appointment or approved seeded patient; cleanup required | Yes — created appointment. |
| Admin/PT/PTA | `/appointments` | **Confirmed —** Open appointment details and use the visit-note action. | Detail surface shows patient/date/status context; note action navigates to existing or new Note Workspace with appointment/date parameters. | Seeded appointment; **Unknown – Requires Verification** if none | Yes — detail and resulting route. |
| Admin/PT | `/appointments` | **Expected —** Exercise check-in/payment gate with an approved sandbox copay fixture. | Copay-required visit cannot be checked in unpaid; payment modal/configuration state is visible and no real card data is stored. | Approved sandbox payment fixture; **Unknown – Requires Verification** | Yes — payment gate/modal; never capture card data. |
| Billing/Patient/Aide | `/appointments` | **Confirmed —** Attempt direct route. | Role lacks `SchedulingAccess`; route is denied and schedule data does not render. | Role account; credentials **Unknown – Requires Verification** | Yes — denial evidence. |

## Intake

| Role | Route | Action | Expected Result | Data Fixture | Screenshot Needed |
| --- | --- | --- | --- | --- | --- |
| Admin/PT/PTA/FrontDesk/Owner/Patient | `/intake` | **Confirmed —** Open Intake from a role-visible entry point. | Intake route renders the clinician selector, patient mode, or access state appropriate to the identity; badge/queue state is visible where data exists. | Role account; intake fixture **Unknown – Requires Verification** | Yes — role-specific intake entry. |
| Anonymous patient | `/intake?invite={token}` | **Confirmed —** Open a valid secure invite. | Standalone gate asks for matching phone/email and OTP rather than exposing the form immediately. | Valid synthetic intake invite; **Unknown – Requires Verification** | Yes — identity gate. |
| Anonymous patient | `/intake?invite={token}` | **Confirmed —** Submit matching contact and request code. | “Send Code” enters code-verification state and does not display raw invite/OTP data. | Valid invite plus approved synthetic contact | Yes — code entry state. |
| Anonymous patient | `/intake?invite={token}` | **Confirmed —** Submit an invalid contact or OTP. | Generic safe error appears; user can resend or use a different contact without gaining intake access. | Valid invite with deliberately wrong synthetic value | Yes — validation/error evidence. |
| Anonymous patient | `/intake?invite={token}` | **Expected —** Allow a valid code/session to expire, then revisit. | Session-expired state asks the user to verify again; previous intake content is not exposed without renewed access. | Short-lived synthetic invite/session; **Unknown – Requires Verification** | Yes — expired-session state. |
| Patient | `/intake?mode=patient` or `/intake/{patientId}` | **Confirmed —** Complete demographics, pain/body map, functional limitations, outcome measures, and review steps. | Wizard advances between steps, validates required inputs, and keeps patient mode controls separate from clinician review controls. | `patient.beta` plus approved editable intake path; **Unknown – Requires Verification** | Yes — complex body-map/step state. |
| PT/Admin/FrontDesk | `/intake` | **Confirmed —** Select a patient and open/create an intake draft. | Authorized selector and draft controls appear; selected patient context loads the wizard. | Seeded patient and safe intake draft | Yes — selector and loaded draft. |
| Owner/Patient | `/intake/{patientId}` | **Confirmed —** Open a submitted/locked intake. | Read-only or patient-mode notice explains that editing/review actions are unavailable; submitted content remains visible as permitted. | Submitted intake fixture; **Unknown – Requires Verification** | Yes — locked/read-only state. |
| PT/PTA/Admin/Owner | `/intake/{patientId}` | **Confirmed —** Review submitted intake and mark reviewed where permitted. | Review status is visible; mark-reviewed action appears only for the listed roles and updates visible status. | Submitted, locked intake; **Unknown – Requires Verification** | Yes — review status/action. |
| Patient | `/intake` | **Confirmed —** Attempt clinician-only patient selector, review, or manage-draft actions. | Selector/manage/review controls are not available in patient mode; no other patient’s intake can be selected. | `patient.beta`; valid patient-mode session | Yes — restricted controls. |
| PT | Evaluation workspace after intake | **Expected —** Start an Evaluation using submitted intake context. | Intake demographics, pain, limitations, and outcomes appear as available prefilled clinical context; missing prototype fields are recorded as limitations. | Submitted intake linked to a safe patient and PT Evaluation draft; **Unknown – Requires Verification** | Yes — prefilled context. |
| Admin | `GET /diagnostics/intake-otp` | **Expected —** Provide opaque request ID to Admin diagnostics after a controlled send failure. | Diagnostics show channel/outcome/safe error code without contact values, invite tokens, or OTPs. | Controlled synthetic intake send and request ID; **Unknown – Requires Verification** | Yes — sanitized diagnostics only. |

## Documentation, HEP, fax, and clinical integrations

| Role | Route | Action | Expected Result | Data Fixture | Screenshot Needed |
| --- | --- | --- | --- | --- | --- |
| PT/PTA | `/patient/{id}/hep` | **Confirmed —** Open clinician HEP for a seeded patient and search exercises. | HEP page shows patient context, program history, authoring controls, and exercise results after a valid two-character search. | `dani.beta` or `pta.beta`; seeded patient; HEP integration/config **Unknown – Requires Verification** | Yes — HEP editor/search. |
| PT/PTA | `/patient/{id}/hep` | **Expected —** Create/revise a prescription, save/publish, then reload history. | Program revision appears in history with visible status; authoring controls are limited to PT/PTA. | Approved safe HEP/Wibbi sandbox fixture; cleanup required | Yes — published/revision status. |
| Admin/Owner | `/patient/{id}/hep` | **Confirmed —** Open HEP and inspect read-only boundary. | HEP history/FlowSheet access is available under `HepRead`; create/revise/publish controls are absent because `HepAuthor` excludes these roles. | Seeded patient and HEP program; **Unknown – Requires Verification** | Yes — read-only boundary. |
| Patient | `/my-hep` | **Confirmed —** Open My Exercise Program and select a launch action. | Patient-only HEP launch surface renders; selected launch navigates to the delegated external provider URL without exposing clinician authoring controls. | `patient.beta`; available patient HEP revision **Unknown – Requires Verification** | Yes — patient HEP page; do not capture external PHI. |
| PT/PTA/Admin/Owner/FrontDesk | `/fax-center` | **Confirmed —** Open Fax Center, switch Outbox/Inbox, refresh, and filter history. | Fax history tabs/status/search controls render; FrontDesk/Admin triage controls follow their policies. | Role account; fax records **Unknown – Requires Verification** | Yes — Fax Center baseline/tabs. |
| PT/PTA/Admin | `/fax-center` | **Confirmed —** Search a seeded patient, select a signed PDF/note, enter synthetic recipient details, and queue a fax. | Queue action gives visible success/error feedback; no real fax is sent during non-production testing unless explicitly approved. | Signed safe note/PDF and sandbox/fake recipient; external fax config **Unknown – Requires Verification** | Yes — form and result; never capture real recipient data. |
| Admin/FrontDesk | `/fax-center` | **Expected —** Assign an inbound fax to a safe patient/document type and reason. | Assignment form validates required patient/type/reason and shows the updated triage state. | Synthetic inbound fax and seeded patient; **Unknown – Requires Verification** | Yes — triage form/result. |
| Owner/Billing/Aide/Patient | `/fax-center` | **Confirmed —** Attempt direct access. | Roles outside `FaxRead` receive access denial; no fax records or send controls render. | Role account; non-seeded credentials **Unknown – Requires Verification** | Yes — permission evidence. |

## Notes and documentation workspace

| Role | Route | Action | Expected Result | Data Fixture | Screenshot Needed |
| --- | --- | --- | --- | --- | --- |
| Admin/PT/PTA/Owner/Billing | `/notes` | **Confirmed —** Open global Notes list and inspect loading, populated, empty, and error states. | Notes page renders bounded list/cards and a visible recovery state when data is unavailable. | Role account; seeded notes **Unknown – Requires Verification** | Yes — representative list/empty state. |
| Admin/PT | `/notes?status=Draft&dateRange=today&noteType=...` | **Observed —** Apply status, date range, note type, patient, and search query filters. | Query-backed filters populate the UI and server query; visible results match selected criteria. | Seeded notes across statuses/types; **Unknown – Requires Verification** | Yes — filtered result and URL. |
| Admin/PT | `/notes` | **Observed —** Navigate notes pagination or large-list controls. | Page changes are stable, list remains bounded, and opening a note preserves selected patient/note context. | Large seeded note list; **Unknown – Requires Verification** | Yes — pagination state. |
| PT | `/patient/{id}/new-note` | **Observed —** Start a new Evaluation, Daily Treatment, Progress, Discharge, or Dry Needling note from a patient/appointment. | Note chooser/workspace opens with requested patient and note type; route contains patient/new-note context. | `dani.beta`; seeded patient; safe writable draft | Yes — chooser/workspace start. |
| PTA | `/patient/{id}/new-note` | **Confirmed —** Start/edit a PTA-authored draft note and save. | PTA can edit permitted draft content and sees save feedback; PT-only co-sign/finalization remains unavailable. | `pta.beta`; safe writable PTA draft | Yes — saved draft and permission boundary. |
| Admin/Owner/Billing | `/patient/{id}/note/{noteId}` | **Confirmed —** Open an existing note from Notes or Chart. | Note content is viewable according to `NoteRead`; draft-authoring controls are not offered to roles excluded from `NoteWrite`. | Seeded existing note; **Unknown – Requires Verification** | Yes — read-only workspace. |
| Patient/FrontDesk/Aide/PracticeManager | `/notes` or note workspace | **Confirmed —** Attempt direct Notes/note URL. | `NoteRead` denies the route; no clinical note content renders. | Role account; credentials **Unknown – Requires Verification** | Yes — permission evidence. |
| PT | `/patient/{id}/notes/{noteId}` | **Observed —** Enter an exact value in a reversible Evaluation draft, save, reload, and restore original content. | Exact value persists after reload; cleanup restores original content and confirms it again. | Approved `PTDOC_UI_QA_EVALUATION_DRAFT_PATH` | Yes — persistence result and cleanup confirmation. |
| PT | Writable note workspace | **Observed —** Edit during a delayed save and then make a follow-up edit. | Workspace remains dirty until the latest save is confirmed; newer local edits are not overwritten by stale save responses. | Approved writable PT draft; controlled delayed save | Yes — dirty/save state. |
| PT | Same Evaluation draft in two sessions | **Observed —** Save in one session, then edit/save or reload the stale session. | Stale session shows “This note changed in another session” with Stay here and Reload latest choices; local content is not silently lost. | Approved reversible Evaluation draft in two browser contexts | Yes — stale-write conflict alert/dialog. |
| PT/PTA | Note workspace section navigation | **Confirmed —** Move through Subjective, Objective, Assessment, Plan, Goals, and Review; attempt leaving incomplete required fields. | SOAP section navigation updates the visible section; incomplete-note safeguard identifies missing required information and offers a clear continue/cancel choice. | Safe editable draft with intentionally missing required fields | Yes — missing-required panel/modal. |
| PT | Note workspace | **Expected —** Save/sign a complete note and inspect signature/compliance confirmation. | Completion/signature controls validate required content and show visible confirmation or actionable failure; legal-signature state is clear. | Approved complete synthetic note; **Unknown – Requires Verification** | Yes — confirmation, never real PHI. |
| PT/PTA/Admin | Note workspace / Review | **Observed —** Open PDF Tools/Review and export a safe non-final draft where permitted. | PDF/review route or surface opens, labels non-final content correctly, and export controls follow `NoteExport`. | Approved synthetic draft; no real PHI | Yes — review/export preview. |
| Owner/Billing | `/export-center` or PDF Tools | **Confirmed —** Attempt note export. | `NoteExport` excludes Owner and Billing; access is denied or export action is unavailable while read-only notes remain accessible. | Owner/Billing account; credentials **Unknown – Requires Verification** | Yes — export permission boundary. |
| PT | Note workspace with AI enabled | **Expected —** Generate Assessment/Plan/prognosis text for a selected body part. | Generated text appears in the intended field and reflects the selected body region; failure/rate-limit/disabled states are visible and do not lose draft content. | PT draft; `FeatureFlags__EnableAiGeneration=true` and safe Azure config; **Unknown – Requires Verification** | Yes — generated/disabled/error state without sensitive text. |

## Progress, reports, and export

| Role | Route | Action | Expected Result | Data Fixture | Screenshot Needed |
| --- | --- | --- | --- | --- | --- |
| PT/Admin | `/progress-tracking` | **Confirmed —** Open Overview, Patients, Goals, and Trends tabs. | Each in-page tab updates selected panel, metrics, and empty/loading state without leaving the route. | Seeded outcomes/notes; **Unknown – Requires Verification** | Yes — tabbed progress view. |
| PT/Admin | `/progress-tracking` | **Confirmed —** Select a patient from alerts or patient panel. | Selected patient context appears and chart navigation target opens `/patient/{id}`. | Seeded patient with progress data; **Unknown – Requires Verification** | Yes — selected patient context. |
| PT/Admin | `/progress-tracking` | **Observed —** Choose progress-summary export. | Navigation reaches `/export-center?template=progress-summary` and Export Center reflects the requested template. | Seeded progress data; **Unknown – Requires Verification** | Yes — handoff URL/template. |
| PT/PTA/Admin | `/export-center` | **Confirmed —** Open filters, type/format selectors, options, preview, and recent activity. | Export Center panels render; changing filters updates preview state without exposing unauthorized records. | Role account; safe seeded notes/patients | Yes — complex export surface. |
| PT/PTA/Admin | `/export-center` | **Expected —** Generate a safe PDF/export preview and inspect resulting document hierarchy. | Preview/export completes visibly with clinical headings and selected filters; no implementation-only labels or real PHI appear in evidence. | Approved synthetic notes; **Unknown – Requires Verification** | Yes — preview/document evidence. |
| Owner | `/reports` | **Observed —** Open Reports compatibility route. | Page replaces itself with Progress Summary Export Center; record current Owner authorization mismatch if Export Center denies access. | Owner account; credentials **Unknown – Requires Verification** | Yes — redirect/denial evidence. |
| Billing/Owner | `/progress-tracking` | **Confirmed —** Attempt direct access. | ClinicalStaff policy denies access; no progress metrics render. | Role account; credentials **Unknown – Requires Verification** | Yes — permission evidence. |

## Settings and administrative features

| Role | Route | Action | Expected Result | Data Fixture | Screenshot Needed |
| --- | --- | --- | --- | --- | --- |
| Admin | `/settings` | **Confirmed —** Open Settings and switch Approvals, Notifications, Integrations, Scheduling, Documentation, Billing, and AI/Outcome sections. | Settings sidebar changes the in-page section; implemented panels render and deferred sections explicitly identify their deferred/backend-contract state. | `january.beta`; safe settings fixture | Yes — Settings baseline and one deferred section. |
| Admin/Owner | `/settings` | **Confirmed —** Review approval dashboard and notification preferences. | Approvals/status badges and notification controls render; changes, if enabled, show visible success/error feedback. | Safe approval/notification fixture; **Unknown – Requires Verification** | Yes — approval/notification state. |
| Admin/Owner | `/settings` | **Confirmed —** Open Clinical Integrations settings. | Integration connection/capability UI renders without exposing provider secrets; unavailable providers show safe state. | Safe integration configuration; **Unknown – Requires Verification** | Yes — integration settings, no secrets. |
| PT/PTA/FrontDesk/Billing/Aide/PracticeManager/Patient | `/settings` | **Confirmed —** Attempt direct Settings route. | Role attribute denies access; no admin settings content renders. | Role account; non-seeded credentials **Unknown – Requires Verification** | Yes — permission evidence. |
| Admin/Owner | `/diagnostics/runtime` | **Confirmed —** Open runtime diagnostics. | JSON/diagnostic response is available only to Admin/Owner; response contains no unapproved secrets or PHI. | Admin/Owner account; safe environment | Yes — sanitized response only. |
| Non-admin role | `/diagnostics/runtime` | **Confirmed —** Attempt diagnostics route. | `AdminOnly` authorization denies access. | PT or Patient account | Yes — denial evidence. |
| Admin/Owner | `/diagnostics/development/communications` | **Expected —** Open development communication diagnostics with safe filters. | Diagnostics proxy returns sanitized development communication data or a clear unavailable state; no raw contact/PHI values are exposed. | Development environment and synthetic communications; **Unknown – Requires Verification** | Yes — sanitized output only. |
| Any caller | `/health/live`, `/health/ready` | **Confirmed —** Open Web liveness/readiness probes. | Liveness/readiness return their documented health response; these are operational endpoints, not application pages. | Running Web/API hosts | No — capture only for deployment-readiness evidence. |

## Responsive, accessibility, and cross-route behavior

| Role | Route | Action | Expected Result | Data Fixture | Screenshot Needed |
| --- | --- | --- | --- | --- | --- |
| Admin/PT/PTA/Patient | All reachable core routes | **Confirmed —** Repeat Dashboard, Appointments, Patients, Intake, Notes, and Settings/Progress at 1280×720 and a mobile/tablet viewport. | No horizontal overflow, clipping, overlap, hidden primary actions, or unusable modal footer; role-visible nav remains reachable. | Seeded role accounts; documented viewport set | Yes — each critical defect; one baseline per viewport. |
| All roles | Login, Dashboard, Patients, Intake, Notes | **Confirmed —** Perform keyboard-only navigation through shell, forms, tabs, modals, filters, and primary actions. | Focus order is logical, focus indicator visible, Enter/Space activate controls, dialogs trap/release focus correctly, and errors are announced. | Seeded account plus safe validation states | Yes — only for accessibility defects or audit evidence. |
| All roles | Main shell and page transitions | **Expected —** Induce a recoverable Blazor interruption and observe reconnect UI. | Reconnect dialog reports retry state; rejected circuit explains restart and offers Reload; application does not remain blank. | Controlled test environment; **Unknown – Requires Verification** | Yes — reconnect/rejected state. |
| All roles | Direct URLs and browser history | **Observed —** Reload route-backed tabs/query states and use Back/Forward. | Chart tabs, appointment views, note chooser, and redirect states remain synchronized with URL and browser history. | Seeded patient, appointment, note | Yes — only for route/history defects. |

## Coverage gaps and future expansion

The following areas are intentionally present as Expected or Unable to Verify
rather than assigned invented pass criteria:

- Separate FrontDesk, Billing, Aide, PracticeManager, or Patient dashboard routes
  are not declared; only one ClinicalStaff dashboard exists.
- Registration and external SSO depend on deployment configuration.
- Billing/charge-review UI is not represented by a routed page even though the
  `BillingAccess` policy exists.
- Writable patient-info, appointment, intake, HEP, fax, payment, communications,
  AI, and diagnostic fixtures require an environment owner to provide safe,
  reversible test data or a sandbox.
- Final legal content, prototype-parity fields, and some Settings/Progress
  controls are documented as incomplete or exploratory in beta QA materials.
- Live browser execution, API behavior, and hosted role credentials were not
  performed while authoring this matrix; execution status belongs in the QA run
  record alongside timestamp, browser, viewport, route, fixture, observed result,
  screenshot reference, cleanup, and severity.

## Confirmed Product Defects

These are kept separate from blockers because source evidence points to an
application navigation/authorization mismatch rather than an unavailable test
condition:

| Finding | Affected Route or Workflow | Evidence | Disposition |
| --- | --- | --- | --- |
| Dashboard link is visible to roles that cannot satisfy `ClinicalStaff`. | `/`, `/dashboard`; FrontDesk, Billing, Aide, PracticeManager, and Patient shell navigation. | `NavMenu.razor` renders Dashboard outside an authorization view; `Dashboard.razor` requires `ClinicalStaff`. | Track as a navigation/role-boundary defect; do not classify as an environment blocker. |
| Owner Reports entry redirects to an Owner-restricted destination. | `/reports` → `/export-center?template=progress-summary`; Owner. | `Reports.razor` performs the redirect; `NoteExport` excludes Owner. | Track as a route authorization defect; live reproduction is still useful but lack of an Owner account is a separate access blocker. |

### Known Blockers and Environment Limitations

The rows below describe conditions that prevent or limit execution. They are not
product defects unless a later controlled test isolates the application as the
cause. “Partial” impact means the route can be inspected or tested read-only, but
the complete workflow cannot be completed.

| Blocker | Affected Route or Workflow | Observed Behavior | Testing Impact | Evidence | Recommended Next Step | Classification |
| --- | --- | --- | --- | --- | --- | --- |
| Hosted Web/API health has not been established for this test run. | All authenticated routes; `/health/live`, `/health/ready`; login and session flows. | No live browser or host health probe was executed while authoring this matrix, so availability, readiness, SignalR stability, and deployment state are unknown. | Complete blocker for runtime validation; source-only coverage is possible. | `docs/BETA_QA.md` Environment Gate; this matrix’s source-only authoring record. | Run the documented Web/API liveness/readiness preflight and record deployment/build identifiers before classifying runtime failures. | Environment Blocker |
| Shared beta PIN is controlled out-of-band. | `/login`; every seeded Admin/PT/PTA/Patient workflow. | The repository intentionally contains usernames but not the shared PIN; authentication cannot be completed from repository contents alone. | Complete blocker for authenticated manual/browser tests until the beta owner supplies the current PIN. | `docs/BETA_QA.md` Test Accounts and shared-PIN rule; `tests/PTDoc.Web.UiQa/README.md` Authentication. | Obtain the PIN through the approved out-of-band channel; never place it in screenshots, reports, source, or chat. | Access or Permission Blocker |
| Only four seeded role accounts are documented. | Owner, FrontDesk, Billing, Aide, PracticeManager routes and role boundaries. | Admin, PT, PTA, and Patient accounts are documented; no credentials/fixtures are documented for the other supported role constants. | Complete blocker for those role-specific tests; partial coverage via policy/source inspection only. | `docs/BETA_QA.md` Test Accounts; role and policy source in `src/PTDoc.Application/Auth` and `src/PTDoc.UI`. | Provision safe test accounts or provide approved storage state for each role, then rerun direct-route and sidebar-boundary scenarios. | Access or Permission Blocker |
| Route-specific appointment data is not guaranteed. | `/appointments`, appointment detail, Notes Due query, appointment-to-note launch, payment/check-in. | QA docs require seeded appointments for schedule/readiness scenarios but do not identify a concrete appointment fixture. | Partial: page/empty state can be tested; populated schedule, detail, note launch, and payment paths may be blocked. | `docs/BETA_QA.md` Appointments checklist; `docs/TEST_MATRIX.md` scheduling fixtures marked Unknown. | Supply a fake, reversible appointment set with patient, clinician, date, note status, and safe status values. | Test Data Blocker |
| Safe writable patient/chart fixtures are optional. | `/patient/{id}`, `/patient/{id}/info`, Documents, Communications, `/patients?action=add`. | QA supports a seeded chart path and synthetic upload, but writable note/intake/chart paths and communication data require explicit overrides or safe setup. | Partial: read-only chart navigation can run; edit, upload, communication, and persistence coverage may be blocked. | `tests/PTDoc.Web.UiQa/README.md` Patient Document Upload and optional paths; `docs/BETA_E2E_TEST_PLAN.md` safe-data rules. | Provide an approved seeded chart plus reversible writable patient-info/document/communication fixture; clean up after testing. | Test Data Blocker |
| Reversible Evaluation/writable-note path is not supplied by default. | `/patient/{id}/note/{noteId}`, `/patient/{id}/notes/{noteId}`; autosave, exact-value reload, stale-write conflict, PDF review. | Browser suite skips mutation/persistence coverage without `PTDOC_UI_QA_EVALUATION_DRAFT_PATH` or a safe writable-note override. | Partial: route/read-only and non-mutating UI checks can run; persistence/concurrency coverage is blocked. | `tests/PTDoc.Web.UiQa/README.md` Optional Note Workspace and audit-remediation settings. | Obtain owner approval for a PT-owned reversible draft; set the path only for that fixture and verify cleanup. | Test Data Blocker |
| Intake invite/OTP recipient fixture is not available by default. | `/intake?invite=...`; send, resend, contact match, OTP verify, expiry, recovery, clinician handoff. | QA requires an approved matching-recipient synthetic fixture; no concrete invite token/contact is stored in the repository. | Complete blocker for end-to-end anonymous intake; clinician-side static/read-only intake checks may continue. | `docs/BETA_QA.md` Intake; `docs/BETA_E2E_TEST_PLAN.md` intake-to-evaluation fixture requirement. | Generate a short-lived invite tied to a fictional recipient and obtain the approved delivery/test channel. | Test Data Blocker |
| Email/SMS delivery provider or intake OTP configuration may be unavailable. | Intake OTP send/resend/verify and `/diagnostics/intake-otp`. | Source documents provider outcomes and diagnostics, but no live provider/configuration state is established here. | Partial or complete blocker depending on whether code delivery is required; do not label provider rejection as a UI defect without response evidence. | `docs/BETA_QA.md` Intake diagnostics steps; `docs/BETA_E2E_TEST_PLAN.md` communication/integration failure guidance. | Check provider configuration, delivery logs, opaque request ID, safe error code, and rate limit before filing a product defect. | Dependency or Integration Blocker |
| Self-service registration is configuration-dependent. | `/signup`; registration approval flow. | `SupportsSelfServiceRegistration` controls whether the Sign Up tab/data are available; deployment value is unknown. | Complete blocker for registration validation when disabled or unconfirmed; Login remains testable. | `Login.razor` and `LoginBase.razor.cs` registration behavior. | Confirm deployment flag and approved clinic/role lookup data, then run fake registration and pending-approval checks. | Environment Blocker |
| External identity provider is configuration-dependent. | `/auth/external/start`, `/auth/external-login`; external sign-in return flow. | Web maps these routes only when Entra External ID is enabled; enabled state and test tenant are unknown. | Complete blocker for SSO tests; native PIN login remains independent. | `src/PTDoc.Web/Program.cs` conditional route map. | Confirm Entra configuration and provide a non-production test identity/return URL; otherwise mark SSO Not Applicable for the environment. | Dependency or Integration Blocker |
| Azure OpenAI draft generation is feature-flagged and rate-limited. | Note workspace AI Assessment/Plan/prognosis actions. | Beta docs state AI may be disabled; repeated requests may return `ai_rate_limited`; endpoint/configuration values are not available in this matrix. | Complete blocker for positive AI generation; disabled/error-state UI can still be inspected if visible. | `docs/BETA_QA.md` AI limitation; `docs/BETA_E2E_TEST_PLAN.md` AI coverage and rate-limit guidance. | Record feature-flag/config state, wait for the configured window after rate limiting, and use a safe PT draft only when enabled. | Dependency or Integration Blocker |
| Authorize.Net payment sandbox is not confirmed. | Appointments payment/check-in gate and payment modal. | QA explicitly forbids payment testing without sandbox credentials and owner approval; no approved sandbox fixture is documented. | Complete blocker for payment submission; payment-gate/readiness UI may be inspected without processing. | `docs/BETA_E2E_TEST_PLAN.md` payment safety and configuration requirements. | Confirm sandbox origin, tokenized test configuration, fixture, and exact approval; never use real card data. | Dependency or Integration Blocker |
| Wibbi HEP and Humble Fax provider configuration is not established. | `/patient/{id}/hep`, `/my-hep`, `/fax-center`; publish/launch/send/triage actions. | Routes and policies are implemented, but provider connections, credentials, sandbox records, and safe external destinations are not provided. | Partial: page structure, authorization, and validation can be inspected; provider round trips are blocked. | `HepProgram.razor` and `FaxCenter.razor`; `docs/BETA_E2E_TEST_PLAN.md` integration/configuration requirements. | Obtain clinic-scoped sandbox connections and reversible records; verify provider response before attributing failures to PTDoc. | Dependency or Integration Blocker |
| Final signing/irreversible communication actions require explicit approval. | Note signature/finalization, fax send, external HEP publish, real intake delivery, payment. | QA rules prohibit irreversible clinical, communication, or payment actions without an approved reversible fixture/sandbox. | Complete blocker for final/real side effects; draft/read-only and validation paths remain available. | `docs/BETA_E2E_TEST_PLAN.md` safety rules; `docs/BETA_QA.md` no-real-data rules. | Obtain explicit owner approval and document fixture, rollback/cleanup, and evidence restrictions before execution. | Access or Permission Blocker |
| Browser dependencies/tooling may be absent. | Playwright responsive, beta E2E, patient-document, and audit-remediation suites. | Browser QA requires a separate npm install and browser installation; missing browsers, Node dependencies, or a running local Web/API host prevent execution. | Complete blocker for automated browser scenarios; source/manual planning remains available. | `AGENTS.md` Browser QA commands; `tests/PTDoc.Web.UiQa/README.md` Install/Run Locally. | Install pinned QA dependencies/browsers, start documented hosts, and retain artifacts only under ignored output folders. | Tooling Limitation |
| Missing product contract for some surfaces. | `/patient/info` without ID; separate role dashboards; Billing/charge-review UI; deferred Settings/Progress sections. | Source declares some routes/policies but does not define a concrete destination, separate page, or complete backend contract. | Unable to define a reliable pass/fail expectation; do not report absence as a defect without product confirmation. | Route/page source and Settings descriptions. | Obtain product/API contract or owner decision, then add a concrete scenario and expected result. | Unable to Determine |
| Live environment/browser behavior was not observed during authoring. | All routes and workflows in this matrix. | This artifact was built from source and QA documentation; no claim is made about current hosted rendering, latency, certificates, DNS, VPN, network, or browser-specific behavior. | Runtime status remains unverified; prevents converting source expectations into execution results. | Matrix scope/evidence rules; `docs/BETA_E2E_TEST_PLAN.md` evidence requirements. | Execute the matrix in a clean supported browser and record exact route, role, viewport, timestamp, console/network evidence, and cleanup. | Unable to Determine |

## Source references

- `src/PTDoc.UI/Pages/` and `src/PTDoc.Web/Program.cs` — route declarations,
  navigation hierarchy, redirects, and host endpoints.
- [`docs/BETA_QA.md`](BETA_QA.md) — seeded accounts, patient fixtures, manual beta
  workflows, safe-data rules, and pass/fail gates.
- [`docs/BETA_E2E_TEST_PLAN.md`](BETA_E2E_TEST_PLAN.md) — minimum feature coverage,
  evidence rules, and role/workflow priorities.
- [`tests/PTDoc.Web.UiQa/README.md`](../tests/PTDoc.Web.UiQa/README.md) — runnable
  browser coverage, optional fixture paths, and artifact rules.
- [`docs/UX_FLOW_UI_STYLE_CONSISTENCY_TEST_PLAN.md`](UX_FLOW_UI_STYLE_CONSISTENCY_TEST_PLAN.md)
  — responsive, keyboard, visual, and interaction audit scenarios.
