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
- Confirm `https://api-ptdoc.bhdevsites.com/health/ready` returns healthy before account validation.
- Sign in with `january.beta`, `dani.beta`, `pta.beta`, and `patient.beta`.
- Confirm no beta browser network calls use `localhost`, `127.0.0.1`, `devtunnels.ms`, or temporary `azurewebsites.net` URLs.
- Confirm Blazor navigation remains connected after login, page changes, and refreshes.

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
- Save a draft, refresh, and confirm entered content persists.
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

### Patient Coverage

- Sign in as `patient.beta`.
- Confirm patient users do not see clinician-only navigation such as Patients, Appointments, Notes, or admin settings.
- Complete any patient-facing intake or patient-only workflow available in beta.
- Confirm patient-facing copy is understandable and does not expose clinician-only implementation details.

### Intake

- Send or open an intake workflow using seeded or fake `.test` patient data.
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
- Seeded Admin, PT, PTA, and Patient users can sign in with the configured beta PIN.
- The Admin tester (`january.beta`) can validate the dashboard, patient directory, seeded patients, and intake entry points without unclear setup instructions.
- The PT tester (`dani.beta`) can validate appointment entry, patient profile navigation, note draft save/reload, note review, and supported PDF export without losing draft work.
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
