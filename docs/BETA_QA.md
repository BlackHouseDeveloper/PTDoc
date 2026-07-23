# PTDoc Beta QA

This guide is the manual beta test matrix for seeded Admin/PT/PTA/Patient accounts and internal QA. Use it with the beta deployment runbook in [docs/deployment/BETA_DEPLOYMENT.md](deployment/BETA_DEPLOYMENT.md) and the responsive browser matrix in [docs/RESPONSIVE_QA.md](RESPONSIVE_QA.md).

Source context (internal, may require access): [Prototype Beta Notes](https://docs.google.com/document/d/1zq2i-Nrlnq4C3yjJ_N-5q70VQhgcYYYU3G04hzz21gI/edit?usp=sharing).

## Beta Start Rules

- Test the hosted beta app at `https://ptdoc.bhdevsites.com`.
- Use seeded `.test` accounts and seeded patient fixtures whenever possible.
- Do not enter real patient PHI, real payer details, real credentials, or real clinic secrets.
- Record one bug per report so each issue can be triaged independently.
- Include screenshots only when they do not contain real PHI.
- Note whether the bug blocks beta testing or is a known limitation.

## Test Accounts

The shared beta PIN is managed outside the repository in Azure as `BetaAccess__SeedPin`. Get the current PIN from the beta environment owner. Do not commit the PIN to the repository, bug reports, screenshots, or chat logs.

| Account focus | Username | Email | Role | Primary coverage |
| --- | --- | --- | --- | --- |
| Admin coverage | `january.beta` | `january.beta@physicallyfitpt.test` | Admin | Login, dashboard, settings/admin visibility, patient directory, intake send/review, non-clinical beta readiness |
| PT coverage | `dani.beta` | `dani.beta@physicallyfitpt.test` | PT | Clinician workflow, notes, appointments, intake review, PDF export, AI when enabled |
| PTA coverage | `pta.beta` | `pta.beta@physicallyfitpt.test` | PTA | PTA note creation/editing, co-sign limitations, appointment note entry |
| Patient coverage | `patient.beta` | `patient.beta@physicallyfitpt.test` | Patient | Patient login boundaries and patient-only surfaces |

Seeded beta patient fixtures:

| MRN | Patient | Diagnosis focus |
| --- | --- | --- |
| `BETA-PT-001` | Avery Adams | Right shoulder pain |
| `BETA-PT-002` | Jordan Lee | Low back pain |
| `BETA-PT-003` | Morgan Patel | Right knee pain |
| `BETA-PT-004` | Riley Chen | Neck pain |

## Beta Test Checklist

### Environment Gate

- Open `https://ptdoc.bhdevsites.com` and confirm the login page loads without unstyled content.
- Confirm `https://ptdoc.bhdevsites.com/health/live` and `https://ptdoc.bhdevsites.com/health/ready` are healthy.
- Confirm `https://api-ptdoc.bhdevsites.com/health/live` returns healthy for basic availability.
- Confirm `https://api-ptdoc.bhdevsites.com/health/ready` returns healthy once before account validation; do not use readiness as a frequent polling probe.
- Sign in with `january.beta`, `dani.beta`, `pta.beta`, and `patient.beta`.
- Confirm no beta browser network calls use `localhost`, `127.0.0.1`, `devtunnels.ms`, or temporary `azurewebsites.net` URLs.
- Confirm Blazor navigation remains connected after login, page changes, and refreshes.
- Navigate continuously for 15 minutes and confirm there is no HTTP `503`, blank application state, or unexpected SignalR failure.
- Confirm the deployment logs contain exactly one acceptable Beta seed outcome and no new `Production Breakpoint Instrumentation Method` errors after two controlled restarts.

### Admin Readiness (`january.beta`)

- Sign in as `january.beta`.
- Review dashboard cards, recent activity, alerts, and navigation badges.
- Click dashboard tiles and alert/navigation targets; record any tile that does not route somewhere useful.
- Open Patients and search by seeded names, MRNs, and `.test` email addresses.
- Open each seeded patient profile and confirm demographics and diagnosis context are understandable.
- Create a new test patient only with fake `.test` data, then verify the pending workflows are clear.
- Send or inspect intake workflow entry points where available.
- Open Settings/Admin areas and record any area that is unavailable, unclear, or prototype-critical.
- Confirm Admin can view clinical data needed for oversight but cannot perform PT-only note-writing actions where the app enforces that boundary.

### PT Clinical Workflow (`dani.beta`)

- Sign in as `dani.beta`.
- Review dashboard workflow cards, notifications, and recently edited plan-of-care content.
- Open Appointments and inspect day/week schedule readability.
- Open an appointment detail, edit appointment type where available, and verify visit-note actions are understandable.
- Start or enter a visit note from an appointment and confirm the route lands on the intended note.
- Open Patients, search seeded fixtures, and create a new draft note from a patient profile.
- Complete an Evaluation note with subjective, objective, assessment, and plan content.
- Enter a unique exact value in Additional functional limitations, save, refresh, and confirm the exact value persists. Restore the original value, save, and confirm cleanup after a second refresh.
- Open the same draft in two sessions, save one session, then confirm the stale session retains its local value and shows `This note changed in another session` with `Stay here` and `Reload latest` actions.
- During a deliberately delayed save, make another edit and confirm the workspace remains dirty until the follow-up save is server-confirmed.
- Confirm autosave/save feedback is visible for successful saves and actionable for failures.
- Sign or prepare a note where permitted; record any unclear compliance or validation message.
- Export or preview PDF output for supported note states.
- If AI generation is enabled, test Assessment and Plan generation with a selected body part and verify generated text matches the selected body region.
- Test dark mode at the beta floor viewport `1280x720` and capture unreadable text or clipped controls.

### PTA Coverage

- Sign in as `pta.beta`.
- Start or edit a PTA-authored note.
- Confirm PTA can save draft note content.
- Confirm PT-only co-sign/finalization boundaries are clear and not bypassable.
- Confirm PDF/export and read/write affordances match the PTA role.
- From Notes, activate a view-only note and confirm the route opens read-only content rather than note-entry controls.
- Confirm `PDF Tools` routes to Review. Use only an approved synthetic draft before starting a PDF export, verify non-final labeling, then discard the artifact.

### Patient Coverage

- Sign in as `patient.beta`.
- Confirm patient users do not see clinician-only navigation such as Patients, Appointments, Notes, or admin settings.
- Complete any patient-facing intake or patient-only workflow available in beta.
- Confirm patient-facing copy is understandable and does not expose clinician-only implementation details.

### Intake

- Send or open an intake workflow using an approved matching-recipient synthetic fixture. Verify send, resend, code verification, expiry, and recovery.
- For failed sends, give the opaque request ID to an Admin. Confirm `GET /diagnostics/intake-otp` identifies the intake, channel, provider, outcome, and safe error code without contact values, invite tokens, or OTPs.
- Complete demographics, insurance, care-team, body-part, pain, functional limitation, outcome measure, and review steps where available.
- Submit intake and confirm the success message is visible without requiring hidden scrolling.
- Reopen the clinician view and confirm submitted intake context appears in the Evaluation workflow where expected.
- Record missing prototype-parity fields as known limitations unless they block completing beta intake.

### Responsive And Browser Coverage

- At minimum, test Chrome or Edge on a laptop-class viewport at `1280x720` and 100% browser zoom.
- Also test one larger desktop viewport, such as `1440x900` or `1536x864`.
- For mobile/tablet spot checks, record the device, OS, browser, and orientation.
- Check light mode and dark mode on dashboard, appointments, patients, intake, notes, and settings/progress if reachable.
- Record any horizontal page overflow, clipped modal footer, unreadable dark-mode text, or sidebar overlap.
- In real Chromium at desktop and mobile widths, focus `Open menu` and activate it with Enter and Space. Confirm `aria-expanded` and the visible navigation state change each time.
- During an induced Blazor interruption, confirm the reconnect dialog reports retry attempts; for a rejected circuit, confirm it explains the server restart and offers Reload.

### Current Route-Backed Retest Focus

- From Appointments, switch between Today and Week using the visible controls and direct URLs such as `/appointments?dateRange=week`; confirm the heading, empty state, and clinician/day grouping stay in sync.
- From a seeded patient chart, switch between Timeline, Notes, Documents, and Communications using the visible chart links and direct query URLs such as `/patient/<id>?tab=documents`; confirm the active section changes without needing a full reload.
- As a PT user, open a seeded patient chart with `/patient/<id>?action=new-note`; confirm the note-type chooser opens, then navigate away from the query and confirm the chooser closes.
- As a PT user, confirm Export Center appears under the Tools navigation section rather than under Admin.

The focused browser suite accepts these optional safe-fixture settings:

```text
PTDOC_UI_QA_EVALUATION_DRAFT_PATH=/patient/<patient-id>/note/<note-id>
PTDOC_UI_QA_PTA_USERNAME=<pta-beta-user>
PTDOC_UI_QA_PTA_PIN=<out-of-band-pin>
```

`PTDOC_UI_QA_EVALUATION_DRAFT_PATH` must identify a reversible PT-role Evaluation draft. The test writes a synthetic marker and restores the original value; do not point it at a signed note or real clinical content.

Run the hosted beta E2E gate for the repeatable preflight, role-boundary, UX, route-refresh, and persistence coverage:

```bash
cd tests/PTDoc.Web.UiQa
PTDOC_WEB_BASE_URL=https://ptdoc.bhdevsites.com \
PTDOC_UI_QA_PIN=<current-out-of-band-beta-pin> \
PTDOC_UI_QA_EVALUATION_DRAFT_PATH=/patient/<patient-id>/note/<note-id> \
npm run test:beta-e2e
```

Only supply `PTDOC_UI_QA_EVALUATION_DRAFT_PATH` after the beta owner approves a reversible PT-role Evaluation draft. Without it, the mutation-and-cleanup persistence check is intentionally skipped while the non-mutating hosted-beta checks still run.

## Known Issues And Limitations

Treat these as documented beta limitations unless they block a core workflow in the pass/fail gate.

- Prototype parity is incomplete. The Prototype Beta Notes doc includes broad design and workflow requests across Dashboard, Appointments, Patients, Intake, Settings, Progress Tracking, Interventions, Evaluation, Daily, Progress, Discharge, and Dry Needling.
- Dashboard alert categorization, redundant cards, and tile routing remain a retest focus.
- Appointment week view with multiple clinicians, schedule density, and therapist labeling remain a retest focus.
- Patient chart prototype areas such as secondary insurance, authorization/referral history, cost sharing, visit limits, document upload by type, inactive/discharged status, and communication logs remain broader product gaps.
- Intake prototype parity remains incomplete for some field wording, predictive text, liability/adjuster fields, prior/current function detail, functional limitation presentation, and QR/link workflow questions.
- Settings and Progress Tracking are exploratory for beta; record clarity issues and missing prototype-critical controls.
- Some legal copy is placeholder beta content until final clinic-approved Terms of Service and Privacy Policy copy is supplied.
- AI generation is config-gated. If it is disabled in beta, report that as environment state rather than an app bug.
- AI generation is rate-limited when enabled. If repeated AI requests return `ai_rate_limited`, wait for the configured window instead of treating it as a beta blocker.
- Do not treat final product-design preferences from the Prototype Beta Notes as beta blockers unless they prevent the seeded Admin/PT testers from completing the current manual test flow.

## Bug Report Format

Use the GitHub beta bug template in `.github/ISSUE_TEMPLATE/beta-bug-report.md` or copy this shape into the beta bug tracker:

```text
Page:
User role:
Device/browser:
Steps to reproduce:
Expected behavior:
Actual behavior:
Screenshot:
Severity:
Beta blocker?:
Notes or Prototype Beta Notes reference:
```

Good bug reports include the account used, exact route/page, one problem, numbered reproduction steps, and whether the problem is new, repeated, or already listed above.

## Beta Pass/Fail Gate

Beta can start when all of these are true:

- Hosted Web and API beta URLs are reachable and healthy.
- Staging and production Web/API health checks pass, and post-swap validation did not require an unresolved rollback.
- Seeded Admin, PT, PTA, and Patient users can sign in with the configured beta PIN.
- The Admin tester (`january.beta`) can validate the dashboard, patient directory, seeded patients, and intake entry points without unclear setup instructions.
- The PT tester (`dani.beta`) can validate appointment entry, patient profile navigation, note draft save/reload, note review, and supported PDF export without losing draft work.
- Exact-value draft reload, rapid-edit follow-up save, and two-session stale-write conflict checks pass.
- Matching-recipient intake OTP send/resend/verify/expiry/recovery and clinician handoff pass with an approved synthetic fixture.
- The 15-minute navigation soak has no `503`, blank application state, or unexpected SignalR failure.
- Seeded-role login smoke checks pass, the seed outcome is acceptable, and the obsolete Production Breakpoint instrumentation error is absent after two restarts.
- Patient users cannot access clinician-only workflows.
- Known limitations are documented and not confused with unexpected regressions.
- Bugs are reported with page, role, device/browser, steps, expected behavior, actual behavior, and screenshot when safe.

Beta should not start, or should pause, if any of these occur:

- The beta app or API is unreachable.
- Seeded beta accounts cannot sign in.
- Real PHI is required to complete testing.
- Role boundaries expose clinician/admin workflows to patient users.
- Core draft save/reload loses note or intake content.
- Critical actions fail without visible feedback.
- The UI is unusable at the documented `1280x720` beta floor.
- The seeded Admin/PT testers cannot tell what to test or how to report bugs.
