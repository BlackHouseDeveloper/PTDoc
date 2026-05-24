# Responsive UI QA

Responsive acceptance for PTDoc Web is based on CSS viewport pixels, not physical screen size. A 14-inch laptop, a 15.6-inch laptop, Chrome display scaling, Windows display scaling, and browser zoom can all produce different CSS viewport sizes. The beta floor is therefore `1280x720` at 100% browser zoom.

## Layout Contract

The Web UI uses one shared breakpoint contract:

| CSS viewport width | Expected layout |
| --- | --- |
| `<1200px` | Drawer/tight layout. The sidebar is hidden until the hamburger opens it. |
| `>=1200px` | Desktop layout. The sidebar can be full width or collapsed to the icon rail. |

This contract is mirrored by CSS, `layout.js`, the viewport diagnostics overlay, and Playwright assertions.

## Local Browser QA

Start the API and Web app first:

```bash
dotnet run --project src/PTDoc.Api --urls http://localhost:5170
dotnet run --project src/PTDoc.Web --urls http://localhost:5145
```

Then install and run the Playwright UI QA project:

```bash
cd tests/PTDoc.Web.UiQa
npm install
npm run install:browsers

PTDOC_WEB_BASE_URL=http://localhost:5145 \
PTDOC_UI_QA_USERNAME=<dev-or-beta-user> \
PTDOC_UI_QA_PIN=<pin> \
npm run test:responsive
```

Never commit credentials or generated Playwright storage-state files. If a route requires login and neither credentials nor a valid storage-state file establishes a session, the suite fails fast instead of reporting skipped tests.

For `http://localhost:5145`, the Playwright harness signs in through the Web `/auth/login` endpoint and normalizes the returned session cookie for local HTTP browser automation. Hosted HTTPS environments use the app's normal secure cookie behavior.

If a seeded note workspace route is available, include it with:

```bash
PTDOC_UI_QA_NOTE_WORKSPACE_PATH=/patients/<patient-id>/notes/<note-id>
```

## Responsive Matrix

The first-pass Chrome matrix covers:

- Viewports: `1280x720`, `1366x768`, `1440x900`, `1536x864`.
- Routes: dashboard, appointments, intake, notes, and an optional seeded note workspace.
- Themes: light across the matrix, plus dark mode at `1280x720`.
- Sidebar states: desktop full/icon rail and drawer open/closed below `1200px`.

The assertions check document overflow, sidebar clipping, dark-mode menu visibility, layout mode, Blazor error overlay visibility, and relevant console errors.

## Viewport Diagnostics Overlay

The diagnostics overlay shows only viewport/browser data:

- CSS viewport width and height.
- `devicePixelRatio`.
- Zoom estimate.
- Theme.
- Active layout mode.

It does not expose PHI, auth state, patient identifiers, tokens, or secrets.

Enable it for local QA in either of these ways:

```bash
PTDOC_DEVELOPER_MODE=true dotnet run --project src/PTDoc.Web --urls http://localhost:5145
```

or visit a route with:

```text
?ptdocViewportDiagnostics=1
```

Disable the local override with:

```text
?ptdocViewportDiagnostics=0
```

The query-string override persists in localStorage as `ptdoc.viewportDiagnostics`, so testers can reload or navigate while preserving the overlay state.

## Manual GitHub Workflow

Run **UI Responsive QA** from GitHub Actions when you need browser evidence outside the normal CoreCi gate.

- Use `http://localhost:5145` to let the workflow start local API/Web processes.
- Use a deployed beta URL to run against an already-hosted environment.
- Set repository secrets `PTDOC_UI_QA_USERNAME` and `PTDOC_UI_QA_PIN` for authenticated checks.
- Set repository variable `PTDOC_UI_QA_NOTE_WORKSPACE_PATH` to include a known note workspace route.
- Artifacts upload on failure, or on demand with `upload_artifacts=true`.

This workflow is manual-only until runtime stability is proven enough to make it a required PR gate.
