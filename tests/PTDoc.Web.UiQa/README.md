# PTDoc Web UI QA

This project contains browser-based responsive checks for PTDoc Web. It is intentionally separate from the .NET test project so Playwright browser dependencies do not affect normal CoreCi runs.

## Install

```bash
cd tests/PTDoc.Web.UiQa
npm install
npm run install:browsers
```

## Run Locally

Start PTDoc API/Web first, then run:

```bash
PTDOC_WEB_BASE_URL=http://localhost:5145 \
PTDOC_UI_QA_USERNAME=<dev-or-beta-user> \
PTDOC_UI_QA_PIN=<pin> \
npm run test:responsive
```

`PTDOC_WEB_BASE_URL` defaults to `http://localhost:5145`.

## Hosted Beta E2E Gate

Run the deployed-beta gate only against the hosted beta site:

```bash
PTDOC_WEB_BASE_URL=https://ptdoc.bhdevsites.com \
PTDOC_UI_QA_PIN=<current-out-of-band-beta-pin> \
npm run test:beta-e2e
```

The gate verifies the Web/API health endpoints, seeded Admin/PT/PTA/Patient login UX, patient search and chart refresh behavior, route-backed chart tabs and browser history, patient role boundaries, keyboard-operated theme persistence, and—when explicitly supplied—a reversible Evaluation-draft save/reload/cleanup flow.

Use `PTDOC_UI_QA_ADMIN_USERNAME`, `PTDOC_UI_QA_PT_USERNAME`, `PTDOC_UI_QA_PTA_USERNAME`, and `PTDOC_UI_QA_PATIENT_USERNAME` only when the seeded beta usernames differ from the documented defaults. Role-specific PIN overrides are also supported. Set `PTDOC_UI_QA_EVALUATION_DRAFT_PATH=/patient/<patient-id>/note/<note-id>` only for an approved reversible PT Evaluation draft; without it, the one intentionally mutating persistence check is skipped. `PTDOC_UI_QA_PATIENT_CHART_PATH` can target a different safe seeded chart, and `PTDOC_UI_QA_API_BASE_URL` can override the API health origin.

The suite never creates records or sends communications. Its only server mutation is the approved Evaluation-draft marker, which it verifies and restores before completing. Do not place PINs, storage state, or patient identifiers in tracked files or reports.

## Patient Document Upload QA

Run the focused patient document upload check with:

```bash
PTDOC_WEB_BASE_URL=http://localhost:5145 \
PTDOC_UI_QA_USERNAME=<dev-or-beta-user> \
PTDOC_UI_QA_PIN=<pin> \
npm run test:patient-documents
```

The upload test creates a synthetic non-PHI text file in Playwright's per-test output directory, uploads it through the patient chart Documents tab, verifies the uploaded row, reloads the patient chart, and verifies the row still renders from storage.

By default, the test opens `/patient/f9c2cb68-4ab4-4f57-a1db-73ed8e2da789`. Override this with `PTDOC_UI_QA_PATIENT_CHART_PATH=/patient/<patient-id>` when a different seeded patient should be used.

## Audit Remediation QA

Run the focused audit-remediation checks with:

```bash
PTDOC_WEB_BASE_URL=http://localhost:5145 \
PTDOC_UI_QA_USERNAME=<dev-or-beta-user> \
PTDOC_UI_QA_PIN=<pin> \
npm run test:audit-remediation
```

The suite covers login validation, protected `/dashboard`, dashboard Notes Due routing, keyboard menu activation at desktop/mobile widths, live-click Appointments Week View activation, Patients Add Patient modal activation, patient chart tab routing, PT Start New Note entry, PT/PTA note-action routing, notes pagination, exact Evaluation draft persistence, and two-session conflict handling. Set `PTDOC_UI_QA_PATIENT_CHART_PATH=/patient/<patient-id>` when the seeded patient chart differs from the default. Set `PTDOC_UI_QA_PT_USERNAME` and `PTDOC_UI_QA_PT_PIN` to override the PT-role credentials. Set `PTDOC_UI_QA_PTA_USERNAME` and optional `PTDOC_UI_QA_PTA_PIN` for PTA View/PDF Tools coverage. Set `PTDOC_UI_QA_INTAKE_PATH=/intake/<patient-id>` to include the editable intake validation/body-map keyboard check. Set `PTDOC_UI_QA_WRITABLE_NOTE_WORKSPACE_PATH=/patient/<patient-id>/notes/<note-id>` only for a safe PT-role draft note when verifying note autosave/CPT/HEP persistence. Set `PTDOC_UI_QA_EVALUATION_DRAFT_PATH=/patient/<patient-id>/note/<note-id>` for a reversible Evaluation draft used by exact-value reload and stale-write conflict checks.

## Authentication

The tests support either:

- `PTDOC_UI_QA_USERNAME` and `PTDOC_UI_QA_PIN` for local or beta seeded credentials.
- `PTDOC_UI_QA_STORAGE_STATE` pointing to a Playwright storage-state JSON file.

Do not commit credentials or generated storage-state files. If a route requires login and neither credentials nor a valid storage-state file establishes a session, the suite fails fast instead of reporting skipped tests.

For local `http://localhost:5145` runs, the harness signs in through the Web `/auth/login` endpoint and normalizes the returned auth cookie for HTTP browser automation. Deployed HTTPS runs keep the cookie behavior provided by the hosted app.

## Optional Note Workspace

Set `PTDOC_UI_QA_NOTE_WORKSPACE_PATH` to include a seeded note workspace route in the responsive matrix. If it is unset, the workspace scenario is skipped without failing the rest of the suite.

## Artifacts

Screenshots, video, traces, and HTML reports are written under Playwright output folders in this project:

- `test-results/`
- `playwright-report/`

These are runtime artifacts and should not be committed.
