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

The suite covers login validation, protected `/dashboard`, dashboard Notes Due routing, appointments week grouping, and notes pagination. Set `PTDOC_UI_QA_INTAKE_PATH=/intake/<patient-id>` to include the editable intake validation/body-map keyboard check. Set `PTDOC_UI_QA_WRITABLE_NOTE_WORKSPACE_PATH=/patient/<patient-id>/notes/<note-id>` only for a safe PT-role draft note when verifying note autosave/CPT/HEP persistence.

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
