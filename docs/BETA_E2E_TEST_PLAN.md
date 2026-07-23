# PTDoc Comprehensive Beta End-to-End UI/UX and Functional Audit Plan

## Executive Summary

This plan defines the execution-ready verification framework for the hosted PTDoc beta environment. It is intended for QA engineers, developers, browser agents, or hybrid manual/automated testing teams validating release readiness across authentication, scheduling, patient management, intake, clinical documentation, payments, integrations, responsive behavior, accessibility, role boundaries, and UI/UX consistency.

This is an audit-plan and verification-blueprint document. It does not claim that any hosted beta feature currently passes or fails. During execution, all final classifications must be based on directly observable hosted-beta behavior, not prototypes, source code, prior assumptions, or similar components elsewhere in the application.

Primary beta targets:

- Web application: `https://ptdoc.bhdevsites.com`
- API health environment: `https://api-ptdoc.bhdevsites.com`

Hosted beta must be tested first. Localhost may be used only after a hosted-beta issue is already documented and the tester is authorized to reproduce or diagnose it locally.

"100% coverage" in this plan means **100% of all observable surfaces discovered during the audit**, including any additional routes, components, states, workflows, and role boundaries found after testing begins. The audit must not be declared "100% complete" while blocked, unsafe, or unable-to-verify items remain; those categories must be measured and reported separately.

## Source Priority

Use this hierarchy when determining expected behavior and classifying findings:

1. Current QA assignment or conversation context.
2. This beta end-to-end audit plan.
3. [PTDoc Beta QA](BETA_QA.md), [live audit notes](audits/ptdoc-live-audit-2026-06-21.md), [UX flow and UI style consistency plan](UX_FLOW_UI_STYLE_CONSISTENCY_TEST_PLAN.md), [responsive QA](RESPONSIVE_QA.md), and attached product, audit, client-feedback, prototype, and QA materials.
4. Repository documentation referenced by the existing plan.
5. Observable hosted-beta behavior during execution.
6. External sources only when absolutely necessary.

Documentation, prototypes, source code, PR descriptions, and prior audits may define expected intent. They are not proof that the hosted beta works. During execution, distinguish among:

- Documented expectation.
- Observable current behavior.
- Product expectation requiring confirmation.
- Functionality blocked by environment or data.
- Functionality that cannot safely be tested.

## Non-Negotiable Coverage Rules

### No Sampling Of Discovered Components

Do not assume that one working instance proves that all instances work.

Examples:

- One working modal does not validate every modal.
- One working dropdown does not validate every dropdown.
- One working patient card does not validate every card variation.
- One working table does not validate every table.
- One working note type does not validate every note type.
- One working role does not validate other roles.
- One working breakpoint does not validate other breakpoints.
- One working theme does not validate the other theme.

Every discovered instance must be directly tested or explicitly marked as inaccessible, unsafe to test, blocked by missing data/configuration, not applicable with justification, or requiring product confirmation.

### Dynamic Discovery

Discovery remains active throughout the audit. When testing reveals a previously unknown route, page, tab, modal, drawer, field, state, workflow, context menu, action, component variation, permission boundary, or error path, the auditor must immediately:

1. Add it to the master inventory.
2. Assign it a stable unique coverage identifier.
3. Add corresponding verification cases.
4. Test it before closing the audit or document why it could not be tested.

### No Silent Omissions

Every inventory item must end with one final disposition:

- Passed.
- Passed with limitation.
- Failed.
- Partially blocked.
- Unable to verify.
- Not applicable, with documented justification.
- Unsafe or irreversible to test, with documented justification.
- Requires product confirmation.

### Observable UI Coverage

Cover both obvious and non-obvious UI surfaces, including controls revealed by hover, focus, scrolling, expansion, selected data, role, viewport, theme, permission denial, invalid input, successful completion, failed network/integration behavior, and state transitions.

## Safety Rules

Use the current beta PIN supplied out of band by the beta owner. Never include the beta PIN in screenshots, videos, finding reports, issue trackers, source files, test-plan documents, chat logs, committed automation files, environment dumps, or copied terminal output.

Preserve these safeguards throughout execution:

- Do not use real PHI.
- Do not use real payer information.
- Do not use real payment cards.
- Do not use real patient contact information.
- Do not send external SMS or email unless explicitly approved.
- Do not sign or finalize irreversible clinical documentation unless a reversible fixture is explicitly approved.
- Do not process payments unless the beta owner confirms a sandbox environment and authorizes the exact test.
- Stop before any action that cannot be confidently reversed.
- Mark blocked actions as **Partially blocked** or **Unable to verify** rather than assuming behavior.
- Use screenshots and recordings only when they do not expose real PHI, PINs, access tokens, API keys, payment data, or sensitive configuration.

## Test Data And Accounts

Use seeded beta accounts from [PTDoc Beta QA](BETA_QA.md):

| Role | Username | Primary testing purpose |
| --- | --- | --- |
| Admin | `january.beta` | Dashboard, settings, patient management, intake administration, operational workflows |
| PT | `dani.beta` | Clinical workflows, appointments, documentation, exports, AI features |
| PTA | `pta.beta` | PTA documentation permissions and PT-only boundary verification |
| Patient | `patient.beta` | Patient-facing workflows and clinician-route access restrictions |

Seeded beta patient fixtures:

| MRN | Patient | Clinical focus |
| --- | --- | --- |
| `BETA-PT-001` | Avery Adams | Right shoulder pain |
| `BETA-PT-002` | Jordan Lee | Low back pain |
| `BETA-PT-003` | Morgan Patel | Right knee pain |
| `BETA-PT-004` | Riley Chen | Neck pain |

Use seeded patients before creating additional records. Any additional records must use clearly fake data, such as:

- `Audit Test <timestamp>`
- `audit+<timestamp>@example.test`
- Non-routable or clearly fictional phone numbers.
- Fictional addresses.
- Non-production payer information.
- Approved sandbox payment data only.

## Evidence And Status Rules

For each finding and blocked item, capture:

- Timestamp and timezone.
- Browser, OS, viewport, zoom, theme, and input method.
- Account role and username without PIN.
- Exact route, page, section, component ID, and workflow ID where applicable.
- Preconditions and test data used.
- Exact visible control activated.
- Expected behavior with source anchor.
- Observed behavior.
- Severity, category, reproducibility, and evidence status.
- Screenshot/video reference when safe.
- Console or network reference when safely available.
- Data created or modified.
- Cleanup performed.
- Related findings.

Evidence status values:

- Confirmed.
- Partially blocked.
- Unable to verify.
- Requires product confirmation.
- Unsafe to test.
- Environment-dependent.

## Recommended Tooling

Manual browser testing should use Chrome or Edge in a clean profile or incognito context. Disable browser extensions unless the extension behavior is part of the test.

Run Playwright browser QA where useful:

```bash
cd tests/PTDoc.Web.UiQa
PTDOC_WEB_BASE_URL=https://ptdoc.bhdevsites.com \
PTDOC_UI_QA_USERNAME=<beta-user> \
PTDOC_UI_QA_PIN=<current-out-of-band-beta-pin> \
npm run test:responsive
```

Run the focused hosted-beta E2E gate for the repeatable health, seeded-role login, route/refresh, role-boundary, UX, and persistence checks:

```bash
cd tests/PTDoc.Web.UiQa
PTDOC_WEB_BASE_URL=https://ptdoc.bhdevsites.com \
PTDOC_UI_QA_PIN=<current-out-of-band-beta-pin> \
npm run test:beta-e2e
```

The gate has no record-creation or communication side effects. To include its server-persistence check, explicitly provide `PTDOC_UI_QA_EVALUATION_DRAFT_PATH` for an approved reversible PT Evaluation draft; the suite verifies a synthetic marker after refresh and restores the original value in cleanup.

Optional browser QA inputs:

- `PTDOC_UI_QA_PT_USERNAME` and `PTDOC_UI_QA_PT_PIN` for PT-role flows.
- `PTDOC_UI_QA_PATIENT_CHART_PATH=/patient/<patient-id>` for known safe patient chart upload coverage.
- `PTDOC_UI_QA_INTAKE_PATH=/intake/<safe-intake-id>` for patient intake coverage.
- `PTDOC_UI_QA_NOTE_WORKSPACE_PATH=/patient/<patient-id>/notes/<note-id>` for seeded note-workspace coverage.
- `PTDOC_UI_QA_WRITABLE_NOTE_WORKSPACE_PATH=/patient/<patient-id>/notes/<note-id>` only for a safe writable draft.

## Minimum Feature Coverage Matrix

This matrix is not a substitute for dynamic discovery. It is the minimum starting scope that must be expanded whenever new observable surfaces are found.

| Area | Critical paths | Roles | Priority | Readiness decision criteria |
| --- | --- | --- | --- | --- |
| Environment and auth | Web/API health, login, logout, protected routes, session refresh | All | Critical | No seeded account is blocked; patient users cannot access clinician routes. |
| Navigation and shell | Sidebar/header, dashboard links, direct routes, stale session state | Admin, PT, PTA, Patient | High | Routes are predictable and no stale clinician shell appears after logout. |
| Dashboard | Tiles, alerts, authorization grouping, recent activity, POC summaries | Admin, PT | High | Tiles route to actionable workflows; alerts are grouped and meaningful. |
| Appointments | Today/week views, clinician grouping, details, type edit, check-in gate, copay readiness | Admin, PT, PTA | Critical | Schedule remains readable; copay/check-in gates prevent invalid status changes. |
| Patient directory | Search, filters, add patient, add + send intake | Admin | Critical | New fake patient can be created and found; intake handoff is clear. |
| Patient chart | Timeline, notes, documents, communications, insurance/auth | Admin, PT | Critical | Tabs are visible, writable where permitted, and persisted after refresh. |
| Intake | Send/open link, multi-step form, validation, submission, clinician seeding | Admin, Patient, PT | Critical | Patient can submit; clinician can use intake context in evaluation. |
| SOAP workspace | Evaluation, Daily, Progress, Discharge, Dry Needling, discovered note types | PT, PTA | Critical | Draft save/refresh works; note sections preserve data; Review/PDF match content. |
| Interventions/CPT/HEP | Row-level CPT, assistance/cueing, response, HEP linkage | PT | Critical | Selections persist across tabs, save, refresh, Review, and export. |
| Payments | Authorize.net config, copay due UI, tokenized payment gate, no card storage | Admin, PT | Critical | Copay-required appointments cannot check in unpaid; sandbox payment flow is safe when authorized. |
| AI prognosis | Prognosis generation, disabled/error/rate-limit states, body-part correctness | PT | Medium | AI output matches selected body part and failures are localized and safe. |
| Global notes | Pagination, filters/search/sort, open/edit consistency | Admin, PT | High | Large lists are bounded and searchable without UI stalls. |
| Reports/export/progress/settings | Reports, Export Center, Progress Tracking, Settings/Admin | Admin, PT | Medium | Critical visible areas are reachable or documented as limitations with role boundaries intact. |
| Responsive/dark mode/accessibility | Beta floor, mobile/tablet, keyboard/focus/contrast | All | High | No critical workflow is unreadable or unreachable by keyboard. |
| Compliance and audit UX | PHI-safe messages, role boundaries, PDF export, no sensitive logs visible | Admin, PT, Patient | Critical | User-facing failures avoid PHI leakage and destructive actions are gated. |

## Change Group Verification Matrix

Use this matrix when a beta deploy or PR/hotfix claims to affect one of these product areas. The matrix supports targeted regression planning but does not limit the broader inventory-driven audit.

| Change group | Intended functionality | Expected beta behavior | Positive tests | Negative / regression tests |
| --- | --- | --- | --- | --- |
| Dashboard alerts and appointment detail UX | Role-scoped dashboard data, alert grouping, appointment readiness indicators, week labels | Dashboard alerts route to workflows; appointment details show billing/intake/document readiness and clinician context. | Click every dashboard tile; open appointment detail; switch Today/Week. | Empty/no-auth alert states do not show misleading groups; dense week view remains usable. |
| Patient insurance, authorization, and intake field expansion | Secondary insurance, adjuster fields, cost sharing, visit limits, auth/referral history | Patient Info and intake expose payer/auth fields with validation and persistence. | Add/edit fake insurance/auth data; save; refresh. | Invalid date ranges, overlapping auth history, and bad numeric values block save visibly. |
| Patient chart documents and communications | Patient document upload and communication-log storage | Documents and Communications tabs support create/list states. | Upload safe text/PDF fixture; add communication log; refresh. | Oversized/invalid file types are rejected; Patient role cannot access clinician chart storage. |
| SOAP workspace intervention, CPT, HEP, export semantics | Row-level intervention CPT, assistance, cueing, response, HEP linkage, PDF/export summaries | Interventions persist and surface in Review/PDF. | Add intervention row; set CPT/assistance/cueing/response/HEP; save/refresh/export. | Removing CPT clears linked references; blank/invalid rows show clear validation. |
| AI prognosis generation | Prognosis prompt/API/UI integration | PT can generate prognosis when AI is enabled; disabled/rate-limited states are explicit. | Generate using a safe draft and selected body part. | Disabled AI, rate limit, and upstream errors do not break workspace or lose draft. |
| Live audit remediation and route-backed UI | `/dashboard`, protected fallback, login validation, notes pagination, week grouping, Add Patient, note chooser | Route-backed controls work after refresh and direct URL entry. | Direct routes for dashboard/appointments/patients/notes; route-backed modals/tabs. | Malformed query params and logout/direct-route access do not break render. |
| Repo guidance and QA docs | QA/runbook clarity and ignored artifacts | QA knows how to run beta tests and what not to commit. | Verify docs point to beta URLs and safe-data rules. | Browser artifacts, local DBs, secrets, and PINs are not included in reports or PRs. |
| Authorize.net check-in payments | Tokenized AcceptUI copay collection and payment-aware appointment projections | Copay-required appointment shows payment modal and blocks unpaid check-in. | Open payment fixture; verify config, amount, modal, sandbox script when approved. | Missing token is rejected; no real card data is stored; placeholder config disables or blocks payment safely. |

## Feature Readiness Labels

Use these labels consistently in final reports:

| Status | Criteria |
| --- | --- |
| Incomplete | Missing core functionality, blocked by access/data, or cannot be tested end to end. |
| Needs Work | Mostly implemented but has significant issues, inconsistent states, fragile validation, or unclear UX. |
| Beta Ready | Feature-complete enough for beta testers, with minor issues or documented limitations. |
| Release Ready | Stable, complete, consistent, accessible, permission-safe, and suitable for production use. |

## Finding Classification

Every issue must receive one primary category:

- Functional defect.
- Visual defect.
- UX inconsistency.
- Accessibility issue.
- Role or permission defect.
- Navigation defect.
- Data persistence defect.
- Validation defect.
- Error-handling defect.
- Integration defect.
- Responsive defect.
- Theme defect.
- Performance or loading defect.
- Security or privacy concern.
- Incomplete implementation.
- Partial implementation.
- Regression.
- Product decision requiring confirmation.

Also classify historical status where evidence permits:

- New.
- Existing.
- Resolved.
- Regressed.
- Partially resolved.
- Unable to compare.

Do not classify an issue as a regression unless a prior verified working state is available.

## Severity Framework

| Severity | Definition | Examples |
| --- | --- | --- |
| Critical | Blocks beta use or creates material authentication, authorization, privacy, data separation, payment safety, clinical persistence, or irreversible corruption risk. | Patient can access clinician data; note content is lost after save; copay-required check-in bypasses payment; cross-patient information appears. |
| High | Blocks a major workflow or creates severe recurring user friction. | Add Patient cannot save; intake cannot submit; appointments cannot be opened; required note type is unusable; navigation traps the user. |
| Medium | Feature works partially but is inconsistent, fragile, confusing, or missing non-blocking behavior. | Filter reset behaves inconsistently; validation message is unclear; dense Week View is hard to interpret. |
| Low | Minor visual, copy, spacing, or polish issue with limited impact. | Icon misalignment; inconsistent capitalization; minor spacing differences; non-critical tooltip wording. |

## Phase 0 - Environment, Safety, And Audit Setup

Before discovery begins, document:

- Test date and timezone.
- Tester or agent identity.
- Browser name and version.
- Operating system.
- Device type.
- Viewport dimensions.
- Browser zoom.
- Theme.
- Connection type.
- Account role.
- Username without PIN.
- Hosted beta build or deployment identifier when observable.
- Whether browser extensions are disabled.
- Whether testing uses a clean profile or incognito context.
- Whether payment, AI, email, SMS, upload, and other integrations are enabled.

Perform the preflight gate:

1. Open `https://ptdoc.bhdevsites.com`.
2. Confirm the login page loads with expected styling and HTTPS validity.
3. Open `https://api-ptdoc.bhdevsites.com/health/live`.
4. Open `https://api-ptdoc.bhdevsites.com/health/ready` once before account validation.
5. Check redirect behavior, mixed-content behavior, asset loading, and session behavior in a clean context.
6. Inspect for user-visible console errors where browser tooling is available.
7. Confirm no beta browser calls use `localhost`, `127.0.0.1`, `devtunnels.ms`, or temporary deployment domains.

Establish an evidence folder or evidence register before testing begins. Do not start functional conclusions until the evidence structure exists.

## Phase 1 - Complete Discovery And Inventory

Functional conclusions must not begin until an initial discovery pass is complete. Discovery then remains active throughout execution.

### 1.1 Route Discovery

Identify all routes linked from:

- Global navigation.
- Responsive navigation.
- Dashboard cards.
- Tables and lists.
- Patient records.
- Notifications.
- Appointment actions.
- Notes.
- Settings.
- Buttons, menus, tabs, breadcrumbs, and secondary links.
- Routes visible only to specific roles.
- Routes accessible after creating or selecting data.

Also identify:

- Direct URL routes.
- Parameterized routes.
- Query-string variants.
- Redirect aliases.
- Authentication callbacks.
- Error routes.
- Unauthorized routes.
- Not-found routes.
- Unknown routes.

For each route, record:

- Route identifier.
- Exact URL or route pattern.
- Page name.
- Required role.
- Authentication requirement.
- Entry points.
- Exit paths.
- Redirect behavior.
- Required data.
- Parameter requirements.
- Query parameters.
- Expected layout.
- Whether the route works after direct refresh.
- Whether browser Back and Forward behave correctly.
- Whether the route can be bookmarked.
- Whether unauthorized access is blocked.
- Whether the route exposes an incorrect shell before redirect.

### 1.2 Page And Layout Discovery

Identify every:

- Authentication layout.
- Clinician layout.
- Admin layout.
- Patient layout.
- Intake-only layout.
- Full-width layout.
- Sidebar layout.
- Modal-only or overlay-driven view.
- Responsive navigation layout.
- Empty page shell.
- Error page.
- Access-denied page.

### 1.3 Overlay And Secondary Surface Discovery

Identify every modal, dialog, confirmation prompt, drawer, side panel, popup, popover, flyout, tooltip, context menu, overflow menu, date picker, time picker, command menu, search suggestion panel, notification panel, toast, banner, alert, wizard, QR-code panel, file preview, PDF preview, payment host frame, AI generation panel, and help panel.

### 1.4 Content And Component Discovery

Identify every instance of:

- Headers, footers, sidebars, navigation groups, breadcrumbs, and toolbars.
- Cards, tables, grids, lists, timelines, charts, graphs, badges, status chips, avatars, and icon buttons.
- Floating action buttons, progress indicators, skeletons, spinners, empty-state illustrations, upload controls, export controls, search controls, filters, sort controls, pagination, load-more controls, tabs, accordions, expandable sections, forms, fields, buttons, links, toggles, checkboxes, radio buttons, dropdowns, multi-selects, text editors, date fields, time fields, numeric fields, body-map interactions, signature controls, AI controls, and payment controls.

### 1.5 Role Discovery

Repeat discovery using every seeded role. Build a role-to-route and role-to-component matrix showing each item as:

- Visible.
- Hidden.
- Disabled.
- Read-only.
- Editable.
- Redirected.
- Access denied.
- Unexpectedly accessible.

### 1.6 Initial Discovery Deliverable

Before verification starts, create a **Master Application Inventory**:

| ID | Route | Page | Section | Component or state | Role | Entry point | Required data | Test status |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |

Use stable unique identifiers such as:

- `ROUTE-001`
- `PAGE-004`
- `LAYOUT-002`
- `SECTION-011`
- `MODAL-012`
- `FORM-008`
- `FIELD-045`
- `COMPONENT-083`
- `WORKFLOW-006`
- `STATE-019`
- `ROLEBOUNDARY-003`

## Phase 2 - Per-Page Component Mapping

Create a dedicated page manifest for every discovered page.

Page manifest fields:

- Page ID.
- Page name.
- Route.
- Route pattern.
- Supported roles.
- Restricted roles.
- Entry points.
- Exit paths.
- Required data.
- Dependencies.
- API or integration dependencies where observable.
- Page layout.
- Responsive variants.
- Theme variants.
- Direct-refresh behavior.
- Back/Forward behavior.
- Loading state.
- Empty state.
- Error state.
- Permission state.
- Success state.

Every page manifest must list all instances of global header, local header, breadcrumbs, sidebar, mobile navigation, toolbars, cards, buttons, links, icon controls, search bars, filters, sort controls, inputs, text areas, editors, checkboxes, radio buttons, toggles, select controls, multi-select controls, date pickers, time pickers, tables, lists, grids, pagination, load-more controls, tabs, accordions, expandable sections, timelines, progress indicators, status badges, alerts, banners, modals, drawers, dialogs, toasts, context menus, tooltips, floating buttons, charts, graphs, file uploaders, file preview controls, export controls, print controls, AI controls, help elements, empty-state actions, error recovery controls, and footer components.

No visible or interactable component may be omitted because it appears decorative, duplicated, low priority, or similar to another component.

## Phase 3 - Individual Component Verification

Every inventory item must be tested independently.

### 3.1 Identity And Context

For each component, record:

- Component ID.
- Component type.
- Page and route.
- Page section.
- Role.
- Data state.
- Theme.
- Viewport.
- Whether it is reusable.
- Other locations where it appears.

### 3.2 Visual Verification

Verify alignment, spacing, typography, text wrapping, truncation, colors, contrast, icons, icon alignment, borders, dividers, shadows, corner radius, layering, overlay stacking, backdrop behavior, scrolling, sticky behavior, selected state, hover state, focus state, active state, disabled state, loading state, error state, success state, dark-mode rendering, light-mode rendering, and responsive behavior.

### 3.3 Interaction Verification

Verify all applicable interaction methods:

- Mouse click.
- Double click where relevant.
- Hover.
- Keyboard focus.
- Enter.
- Space.
- Escape.
- Arrow keys.
- Tab and Shift+Tab.
- Touch or mobile tap.
- Long press where relevant.
- Drag and drop.
- Scroll.
- Horizontal scroll.
- Pinch or zoom only where applicable.
- Browser Back.
- Browser Forward.
- Refresh.
- Direct URL entry.

### 3.4 Functional Verification

Verify:

- Intended action occurs.
- Action occurs only once.
- Duplicate submission is prevented.
- Disabled controls cannot be activated.
- Loading feedback appears.
- Success feedback appears.
- Failure feedback appears.
- State updates correctly.
- Data persists after navigation.
- Data persists after refresh.
- Cancel restores the expected state.
- Close behavior is predictable.
- Unsaved changes are handled.
- Destructive actions require confirmation.
- User can recover from failure.
- Repeated use does not create stale or duplicated state.
- Component behavior remains correct when reopened.

### 3.5 Data Verification

Verify:

- Data loads correctly.
- Correct patient, appointment, note, or record context is shown.
- Data refreshes correctly.
- Data is not stale after mutation.
- Empty datasets are handled.
- Missing values are handled.
- Null values are handled.
- Long values are handled.
- Special characters are handled.
- Numeric boundaries are handled.
- Date boundaries are handled.
- Placeholder content is accurate.
- Default values are appropriate.
- Validation messages identify the correct field.
- Cross-record data leakage does not occur.

### 3.6 Accessibility Verification

Verify where observable:

- Logical keyboard order.
- Visible focus.
- Focus containment in modals.
- Focus restoration after overlays close.
- Accessible names for controls.
- Form labels.
- Instructions associated with fields.
- Error messages associated with fields.
- Heading hierarchy.
- Landmark structure.
- Keyboard-operable custom controls.
- Accessible table structure.
- Color contrast.
- Non-color state indicators.
- Minimum touch-target sizing.
- Text zoom behavior.
- Screen-reader-friendly labels when inspectable.
- Reduced-motion behavior where supported.

## Phase 4 - State Coverage

Every component and page must be tested across all applicable states:

- Initial.
- Default.
- Hover.
- Focus.
- Active.
- Selected.
- Expanded.
- Collapsed.
- Enabled.
- Disabled.
- Read-only.
- Required.
- Optional.
- Loading.
- Slow-loading.
- Skeleton.
- Empty.
- Partial-data.
- Populated.
- Maximum-data.
- Validation-error.
- Server-error.
- Integration-error.
- Offline or connectivity-loss where applicable.
- Success.
- Warning.
- Permission-denied.
- Unauthorized.
- Expired-session.
- Stale-data.
- Unsaved-change.
- Completed.
- Cancelled.
- Archived or inactive where applicable.

Do not manufacture unsafe production failures. Use browser throttling, approved test fixtures, invalid fake data, or safe interrupted flows where appropriate.

## Phase 5 - End-To-End Workflow Verification

Every workflow must be tested from entry through completion, cancellation, failure, permission denial, refresh/resume, and recovery.

Each workflow specification must include:

- Workflow ID.
- Workflow name.
- Roles.
- Preconditions.
- Required data.
- Entry points.
- Main happy path.
- Alternate paths.
- Cancellation path.
- Validation path.
- Failure path.
- Recovery path.
- Permission path.
- Refresh/resume path.
- Expected persisted state.
- Related pages.
- Related reusable components.
- Evidence requirements.
- Reversal or cleanup procedure.

Use stable workflow IDs in the coverage matrix. Start with these IDs and add new IDs as discovery expands:

| Workflow ID prefix | Area |
| --- | --- |
| `WF-AUTH-*` | Authentication and sessions |
| `WF-SHELL-*` | Global navigation and application shell |
| `WF-DASH-*` | Dashboard |
| `WF-APPT-*` | Appointments |
| `WF-PATIENTS-*` | Patient directory |
| `WF-CHART-*` | Patient chart |
| `WF-INTAKE-*` | Intake |
| `WF-NOTE-EVAL-*` | Evaluation notes |
| `WF-NOTE-DAILY-*` | Daily Treatment notes |
| `WF-NOTE-PROGRESS-*` | Progress notes |
| `WF-NOTE-DISCHARGE-*` | Discharge notes |
| `WF-NOTE-DRYNEEDLE-*` | Dry Needling notes |
| `WF-CLINICAL-ROWS-*` | Goals, outcomes, exercises, CPT, and HEP |
| `WF-NOTES-LIST-*` | Global and patient notes lists |
| `WF-REPORT-*` | Reports and compliance |
| `WF-EXPORT-*` | Export Center |
| `WF-ADMIN-*` | Settings and administration |
| `WF-AI-*` | AI features |
| `WF-PAYMENT-*` | Payments and copay gates |

### Workflow Area: Authentication And Sessions

Required coverage:

- Blank login.
- Malformed PIN.
- Invalid credentials.
- Valid login for every role.
- Session persistence.
- Refresh.
- Logout.
- Protected-route redirect.
- Unknown-route behavior.
- Expired-session behavior.
- Cross-role route denial.
- Stale-shell prevention after logout.

Expected results:

- Blank and malformed input produce inline validation before authentication attempt.
- Invalid credentials show safe, non-secret invalid-credential feedback.
- Seeded Admin, PT, PTA, and Patient users can authenticate when beta environment is healthy.
- Logout returns to the login page and protected routes redirect without exposing authenticated shell state.
- Patient users cannot access clinician or admin routes.

### Workflow Area: Global Navigation And Application Shell

Required coverage:

- Sidebar expansion and collapse.
- Mobile navigation.
- Header controls.
- Theme toggle.
- Sync indicators where present.
- Online/offline indicators where present.
- Active navigation state.
- Deep links.
- Browser Back and Forward.
- Direct route refresh.
- Unknown routes.
- Access-denied routes.

Expected results:

- Routes are predictable after click, direct entry, refresh, Back, and Forward.
- The shell does not show stale role labels or inaccessible navigation after logout or role switch.
- Mobile and desktop navigation expose the expected role-specific destinations.

### Workflow Area: Dashboard

Required coverage:

- Every tile.
- Every count.
- Every card.
- Every alert group.
- Every alert item.
- Collapsed and expanded groups.
- Empty alert states.
- Recent activity.
- Recently edited plans of care.
- Authorization and referral summaries.
- Role-specific dashboard differences.
- Destination consistency between counts and lists.

Expected results:

- Tiles route to actionable workflows that match tile copy.
- Counts and destination lists are materially consistent.
- Alert groups are meaningful, collapsible, and lead to the expected patient/note/workflow.

### Workflow Area: Appointments

Required coverage:

- Today view.
- Week view.
- Day grouping.
- Clinician grouping.
- Date navigation.
- Appointment creation where available.
- Appointment details.
- Appointment type changes.
- Status changes.
- Check-in.
- Copay gates.
- Intake indicators.
- Note indicators.
- Care-team data.
- Billing readiness.
- Documentation readiness.
- Overlapping appointments.
- Multiple clinicians in one time slot.
- Dense schedule layout.
- Empty schedule.
- Appointment cancellation or reversible status paths where safe.

Expected results:

- Today and Week views remain readable and route-backed.
- Appointment detail modals show patient, MRN, date/time, duration, type, status, intake, note, care-team, billing, and document readiness context.
- Type edits and reversible status changes persist and can be restored.
- Copay-required appointments cannot check in unpaid.

### Workflow Area: Patient Directory

Required coverage:

- Initial loading.
- Empty state.
- Name search.
- MRN search.
- Email search where supported.
- Filters.
- Sorting.
- Pagination or load-more.
- Add Patient.
- Add Patient and Send Intake.
- Duplicate data.
- Required-field validation.
- Long names.
- Special characters.
- Successful creation.
- Search after creation.
- Cancellation.
- Reopening the modal.
- Data persistence.

Expected results:

- Search supports seeded names and MRNs and any supported email behavior.
- Add Patient validation is inline and specific.
- Fake patient creation persists, closes the modal, and the record can be found.
- Add Patient + Send Intake preselects the created patient without duplicate search.

### Workflow Area: Patient Chart

Required coverage:

- Every patient-chart tab.
- Timeline.
- Notes.
- Goals.
- Outcomes.
- Documents.
- Communications.
- Insurance and Authorization.
- Summary information.
- Direct tab links.
- Refresh.
- Cross-patient navigation.
- Read-only and editable states.
- Missing-data states.
- File upload.
- File validation.
- Communication logging.
- Authorization history.
- Invalid and overlapping date ranges.
- Patient-role denial.

Expected results:

- Patient identity and context remain correct across tabs, direct links, refresh, and cross-patient navigation.
- Documents and communication logs persist and do not leak across patients.
- Insurance/auth validation blocks invalid ranges and bad numeric values.
- Patient role cannot access clinician chart storage.

### Workflow Area: Intake

Required coverage:

- Intake creation.
- Link generation.
- QR generation.
- Safe copy-link behavior.
- Patient opening the intake link.
- Expired or invalid links.
- Every wizard step.
- Next and Back.
- Step gating.
- Required fields.
- Optional fields.
- Pain scale.
- Body map.
- Keyboard body-map use.
- Insurance.
- Secondary insurance.
- Liability and adjuster fields.
- Authorized contacts.
- PCP.
- Referring provider.
- Surgeries.
- Outcome measures.
- Legal acknowledgement.
- Save or draft behavior where available.
- Submission.
- Visible success confirmation.
- Duplicate submission behavior.
- Intake-to-evaluation data mapping.

Expected results:

- Required validation is inline, field-specific, and focusable.
- Pain severity requires explicit interaction; `0` is valid only when selected intentionally.
- Body map works with mouse and keyboard.
- Submitted confirmation is visible without hidden scrolling.
- Clinician workflows clearly indicate intake-sourced data where intended.

### Workflow Area: Clinical Documentation

Test each note type independently:

- Evaluation.
- Daily Treatment.
- Progress.
- Discharge.
- Dry Needling.
- Pelvic Floor Evaluation where present.
- Any additional note type discovered.

For every note type verify:

- Start from appointment.
- Start from patient chart.
- Subjective.
- Objective.
- Assessment.
- Plan.
- Interventions.
- Goals.
- Outcomes.
- CPT.
- Modifiers.
- HEP linkage.
- Assistance.
- Cueing.
- Patient response.
- Carry-forward content.
- Autosave.
- Manual save.
- Refresh.
- Resume.
- Validation.
- Review.
- PDF preview.
- Export.
- Role restrictions.
- Read-only states.
- Sign, submit, amend, and cosign controls only where safely testable.

Expected results:

- Data persists through tab navigation, autosave, manual save, refresh, review, and export.
- Each note type exposes appropriate fields and labels.
- Review/PDF content matches entered structured data.
- Unsafe finalization/signing paths are stopped before irreversible action unless approved.

### Workflow Area: Goals, Outcomes, Exercises, And HEP

Required coverage:

- Create.
- Edit.
- Delete where reversible.
- Reorder where supported.
- Status changes.
- Percentage progress.
- Suggested versus actual exercises.
- Sets.
- Repetitions.
- Duration.
- Resistance.
- Weight.
- CPT linkage.
- Cueing.
- Assistance.
- HEP linkage.
- Carry-forward.
- Review and export appearance.
- Empty and populated states.

Expected results:

- Structured clinical rows persist across save, refresh, review, and export.
- Row-level associations are visible and do not silently detach.
- Historical/read-only values remain readable without allowing unauthorized edits.

### Workflow Area: Global Notes

Required coverage:

- Initial bounded loading.
- Pagination.
- Load More.
- Search.
- Status filters.
- Type filters.
- Date filters.
- Sorting.
- Filter reset behavior.
- Open draft.
- Read-only note.
- Save.
- Patient context.
- Consistency with patient-specific Notes.

Expected results:

- Large lists are bounded and do not stall the UI.
- Filters reset pagination and produce truthful empty/no-results states.
- Global and patient-specific note actions are consistent.

### Workflow Area: Reports And Compliance

Required coverage:

- Every visible report.
- Every filter.
- Date range.
- Empty results.
- Populated results.
- Export.
- Progress-note due indicators.
- Plan-of-care expiration.
- Plan-of-care update due.
- Missing signature.
- Authorization limits.
- Visit limits.
- Deductible and out-of-pocket displays.
- Role restrictions.
- Consistency between dashboard alerts and reports.

Expected results:

- Reports either work with clear results or explicitly state beta limitations.
- Compliance indicators reconcile with dashboard alerts and patient/appointment context.
- Role access matches documented expectations.

### Workflow Area: Export Center

Required coverage:

- Every export type.
- Required selections.
- Date ranges.
- Patient selection.
- Loading.
- Empty result.
- Success.
- File naming.
- Download behavior.
- Duplicate clicks.
- Error handling.
- Content consistency.
- Role access.

Expected results:

- Unsupported export paths are visibly unavailable, not silently actionable.
- Invalid date ranges and missing selections block preview/download with clear feedback.
- Downloaded or previewed content matches selected data and does not expose unrelated patient content.

### Workflow Area: Settings And Administration

Required coverage:

- Every settings category.
- Every field.
- Save.
- Cancel.
- Validation.
- Persistence.
- Role management where present.
- Read-only restrictions.
- Feature flags where visible.
- Integration settings.
- Missing configuration states.
- Unauthorized access.

Expected results:

- Admin can reach beta-supported settings.
- Non-admin users see appropriate boundaries.
- Missing or disabled integration settings are explicit and not confused with broken controls.

### Workflow Area: AI Features

Required coverage:

- Visible AI controls.
- Enabled state.
- Disabled state.
- Loading.
- Successful response.
- Regeneration.
- Wrong or missing context prevention.
- Body-part correctness.
- Rate limiting.
- Upstream failure.
- Partial failure.
- Draft preservation.
- Safe error messages.
- Ability to edit generated text.
- Prevention of silent overwrites.

Expected results:

- If AI is disabled, that is recorded as environment state and the visible disabled state is still tested.
- AI output remains reviewable/editable and does not silently overwrite draft work.
- AI errors remain localized and do not break the workspace.

### Workflow Area: Payments

Required coverage:

- No-copay appointment.
- Copay-required appointment.
- Payment configuration missing.
- Placeholder configuration.
- Sandbox configuration.
- Payment modal.
- Amount accuracy.
- Patient accuracy.
- Appointment accuracy.
- Unpaid check-in restriction.
- Approved sandbox payment when authorized.
- Declined sandbox payment when authorized.
- Duplicate-payment prevention.
- Cancellation.
- Failure recovery.
- Confirmation.
- Transaction status.
- No raw card storage.
- No secret exposure.

Expected results:

- Missing/placeholder configuration disables or clearly blocks payment.
- Copay-required check-in is blocked until approved sandbox payment success or explicit disabled-state handling.
- Client-safe configuration never exposes transaction keys or secrets.
- No card number or raw token is stored or displayed by PTDoc.

## Phase 6 - Reusable Component Consistency

Create a reusable-component registry. For every reusable component, record:

- Component name.
- Component type.
- All routes where it appears.
- All roles that can see it.
- All variants.
- All responsive versions.
- All theme versions.
- All states.
- Whether behavior is intentionally different.

Required component families:

- Primary, secondary, tertiary, destructive, and icon buttons.
- Inputs, search controls, filters, dropdowns, date pickers, validation messages.
- Tables, cards, status indicators, empty states, loading indicators.
- Modals, drawers, tabs, accordions, toasts, alerts, tooltips, navigation.
- File uploads, export controls, AI controls, and payment controls.

Compare every instance for styling, size, spacing, alignment, terminology, capitalization, icons, tooltip behavior, disabled behavior, loading behavior, validation, keyboard operation, focus behavior, close behavior, confirmation behavior, success feedback, error feedback, persistence, responsive behavior, and theme behavior.

Classify differences as:

- Intentional variant.
- Product decision requiring confirmation.
- UX inconsistency.
- Visual inconsistency.
- Functional inconsistency.
- Accessibility inconsistency.

## Phase 7 - Responsive, Theme, And Input-Method Validation

### Required Viewports

At minimum, verify:

- `1440 x 900`
- `1280 x 720`
- Tablet portrait.
- Tablet landscape.
- `430 x 932` mobile-equivalent viewport.
- One additional narrow mobile viewport where practical.

Use at least one real tablet or mobile device when available. Clearly distinguish device testing from browser emulation.

### Required Theme Coverage

Where supported, verify:

- Light mode.
- Dark mode.
- Theme persistence after refresh.
- Theme persistence after login/logout where expected.
- System-theme behavior where supported.

### Required Responsive Checks

Verify navigation transformation, sidebar behavior, header wrapping, card stacking, table overflow, schedule overflow, modal sizing, drawer sizing, form reflow, button reachability, footer reachability, sticky controls, horizontal scrolling, keyboard overlap on mobile, touch targets, zoom to 200% where practical, no hidden critical controls, no clipped validation messages, no overlapping text, and no inaccessible modal footer.

### Input Methods

Verify applicable workflows with:

- Mouse.
- Keyboard only.
- Touch or emulated touch.
- Browser zoom.
- Screen-reader inspection where tooling permits.

## Phase 8 - Error, Edge Case, And Recovery Validation

Safely test:

- Missing required fields.
- Invalid formats.
- Invalid dates.
- Reversed date ranges.
- Duplicate entries.
- Long text.
- Special characters.
- Zero values.
- Negative values.
- Extremely large numeric values.
- Empty datasets.
- Partial records.
- Deleted or stale records.
- Invalid route parameters.
- Malformed query strings.
- Unauthorized direct routes.
- Expired sessions.
- Rapid repeated clicks.
- Double submission.
- Refresh during loading.
- Refresh after success.
- Back navigation during a wizard.
- Closing a modal with unsaved content.
- Network slowdown.
- Safe network interruption.
- Integration unavailability.
- Missing assets.
- Broken images.
- Failed uploads.
- Invalid upload type.
- Oversized upload.
- AI timeout.
- Payment decline.
- Empty reports.
- No-search-results state.
- Permission denial.
- Unexpected role access.
- Browser Back after logout.
- Reopening completed workflows.
- Attempting actions out of sequence.

Every failure state must be evaluated for:

- Clear explanation.
- Correct location.
- Preservation of entered data.
- Recovery action.
- Retry behavior.
- Prevention of duplicate data.
- Prevention of unauthorized changes.
- Absence of sensitive information in errors.
- Absence of unhandled-error banners or blank screens.

## Phase 9 - Navigation And Route Reconciliation

Perform a dedicated navigation pass that traverses:

- Every global navigation item.
- Every local navigation item.
- Every breadcrumb.
- Every dashboard shortcut.
- Every card link.
- Every table row link.
- Every patient link.
- Every appointment link.
- Every notification link.
- Every report link.
- Every modal-launched route.
- Every browser Back path.
- Every browser Forward path.
- Every direct URL.
- Every route after refresh.
- Every route after session expiration.
- Every unauthorized route.
- Every unknown route.

Create a route reconciliation table:

| Route ID | Route | Linked from | Direct load | Refresh | Back/Forward | Role access | Final status |
| --- | --- | --- | --- | --- | --- | --- | --- |

Any route discovered in the application but missing from the inventory is a coverage defect in the audit process and must be added before completion.

## Phase 10 - Evidence Collection

Require evidence for every defect and every blocked coverage item. The evidence register must be filterable by finding ID, route, page, component ID, workflow ID, role, severity, category, evidence status, and final disposition.

Screenshots and recordings must not expose:

- PINs.
- Real PHI.
- Real payment data.
- Access tokens.
- API keys.
- Sensitive configuration.
- Private patient information.

## Phase 11 - Required Audit Deliverables

The audit output must include all of the following.

### 11.1 Master Application Inventory

Complete inventory of all routes, pages, layouts, sections, components, states, workflows, roles, overlays, integrations, responsive variants, and theme variants.

### 11.2 Route And Role Matrix

Show which roles can view, enter, edit, submit, delete, export, configure, access directly, and access through navigation.

### 11.3 Page Coverage Ledger

For every page, show components discovered, components tested, states tested, roles tested, viewports tested, themes tested, open findings, blocked areas, and final disposition.

### 11.4 Reusable Component Matrix

List every location where each reusable component appears and whether each instance was verified.

### 11.5 Workflow Coverage Matrix

For each workflow, show happy path, alternate path, cancellation, validation, failure, recovery, permission behavior, refresh/resume, and final status.

### 11.6 Findings Register

Organize findings so they can be filtered by page, route, component, workflow, severity, user role, functional area, issue category, historical status, and evidence status.

### 11.7 Inaccessible And Untestable Register

Document every item that could not be tested, including reason, missing role, missing data, missing configuration, unsafe or irreversible action, integration disabled, environment failure, required owner approval, and remaining risk.

### 11.8 Cross-Page Consistency Report

Summarize inconsistencies in styling, behavior, validation, terminology, icons, navigation, feedback, error handling, accessibility, responsive behavior, and theme behavior.

### 11.9 Release Readiness Summary

Provide:

- Overall readiness.
- Critical blockers.
- High-priority issues.
- Modules that passed.
- Modules with limitations.
- Untested areas.
- Environment limitations.
- Remaining product-confirmation questions.
- Recommended disposition: Proceed, Proceed with documented limitations, Hold beta, or Hold release.

## Phase 12 - Coverage Metrics And Completion Criteria

Report quantitative reconciliation:

- Total routes discovered.
- Routes tested.
- Routes blocked.
- Total pages discovered.
- Pages tested.
- Total component instances discovered.
- Component instances tested.
- Total reusable component families.
- Reusable component locations tested.
- Total workflows.
- Workflows completed.
- Total role-route combinations.
- Role-route combinations tested.
- Total state combinations identified.
- State combinations tested.
- Total responsive combinations required.
- Responsive combinations tested.
- Total theme combinations required.
- Theme combinations tested.
- Total open findings by severity.
- Total unable-to-verify items.

Calculate observable coverage as:

```text
Verified inventory items / Total discovered inventory items x 100
```

Do not count blocked, unsafe, or unable-to-verify items as verified. Report them separately so a high coverage percentage does not conceal untested risk.

The audit may be considered complete only when:

- Every discovered route is inventoried.
- Every discovered page is inventoried.
- Every page has a completed component manifest.
- Every discovered component instance has a final disposition.
- Every reusable component is verified in every discovered location.
- Every supported role is evaluated.
- Every role-access boundary is tested.
- Every navigation path is traversed.
- Every interactive element is exercised.
- Every applicable component state is tested.
- Every core workflow is executed end to end.
- Every workflow includes validation, cancellation, failure, and recovery coverage.
- Every critical workflow is tested at the beta-floor desktop viewport.
- Every critical workflow receives mobile or tablet coverage where applicable.
- Every critical workflow is checked in light and dark mode where supported.
- Every finding has supporting evidence.
- Every inaccessible or unsafe item is explicitly documented.
- Every additional item discovered during execution is added and resolved.
- Route, page, component, role, workflow, and evidence ledgers reconcile.
- No inventory item remains blank or silently omitted.
- All Critical and High findings are reflected in the readiness recommendation.

## Required Execution Order

1. Environment and safety gate.
2. Authentication and session smoke tests.
3. Initial route discovery for every role.
4. Master inventory creation.
5. Page and component mapping.
6. Global navigation and application shell.
7. Dashboard.
8. Appointments.
9. Patient directory.
10. Patient chart.
11. Intake.
12. Evaluation documentation.
13. Daily, Progress, Discharge, Dry Needling, and other note types.
14. Goals, outcomes, exercises, CPT, and HEP.
15. Global Notes.
16. Reports and compliance.
17. Export Center.
18. Settings and administrative areas.
19. AI features.
20. Payment and check-in safeguards.
21. Cross-page reusable-component comparison.
22. Responsive validation.
23. Theme validation.
24. Keyboard and accessibility validation.
25. Error, edge-case, and recovery testing.
26. Route and role reconciliation.
27. Regression sweep.
28. Coverage reconciliation.
29. Release-readiness assessment.

## Regression Sweep

Run these after any beta deploy or hotfix:

1. Login/logout/protected-route smoke for all roles.
2. Dashboard tile routing and alert expansion.
3. Appointments Today/Week and one appointment detail modal.
4. Add Patient modal open, validation, and cancel.
5. Patient chart tabs direct-link refresh.
6. Documents and Communications tab load states.
7. Intake link open and first-step validation.
8. Evaluation draft save/refresh.
9. Daily note Interventions/Plan/Review navigation.
10. Global Notes bounded pagination.
11. Copay-required appointment unpaid check-in block.
12. Dark-mode smoke on dashboard, appointments, and note workspace.
13. Patient role direct-route denial.

## Release Readiness Checklist

The beta is ready for broader release only when all Critical items pass:

- [ ] Web and API beta health pass.
- [ ] All seeded roles can login with the current out-of-band PIN.
- [ ] Patient role cannot access clinician/admin surfaces.
- [ ] Admin can complete dashboard, patient search, patient chart, and intake entry workflows.
- [ ] PT can open appointment, create/edit note draft, save, refresh, review, and export.
- [ ] PTA boundaries are clear and not bypassable.
- [ ] Intake can be completed with fake patient data and appears in clinician workflow.
- [ ] Documents and Communications persist on patient chart.
- [ ] Insurance/auth invalid data is blocked and valid fake data persists.
- [ ] Copay-required check-in is blocked until sandbox payment success or clearly disabled.
- [ ] No visible unhandled-error banner appears in core flows.
- [ ] No critical layout issue at `1280 x 720`.
- [ ] Dark mode is readable on dashboard, appointments, and notes.
- [ ] All discovered routes, pages, components, workflows, states, role boundaries, responsive variants, and theme variants have final dispositions.
- [ ] All known limitations are documented and separated from regressions.

## Risks And Areas Requiring Additional Validation

- Beta PIN and seeded data can change outside the repo; always confirm with the beta owner before testing.
- Authorize.net should not be fully exercised unless sandbox credentials and safe fixtures are explicitly confirmed.
- AI generation may be disabled or rate-limited in beta; record environment state separately from product bugs.
- Prototype screenshot-dependent comments cannot be fully validated from text-only docs.
- Non-billable discharge and dry-needling billing defaults may need product confirmation.
- Week View may pass functionally while still failing client readability expectations at high clinician density.
- Intake-to-evaluation mapping requires a safe submitted-intake fixture to verify completely.
- Review/PDF export must be checked with realistic structured note content, not empty drafts.
- Mobile/tablet behavior may differ from desktop browser emulation; at least one real Apple/iPad pass remains valuable because prior feedback mentioned iPad sync and dark-mode readability.

## Final Report Template

Use this shape after executing the plan:

```text
PTDoc Comprehensive Beta Audit Report

Environment:
- Date/time:
- Tester:
- Browser/device:
- OS:
- Viewports:
- Themes:
- Input methods:
- Beta Web/API build or deployment identifier:
- Accounts used:

Coverage summary:
- Total discovered inventory items:
- Directly verified inventory items:
- Observable coverage percentage:
- Blocked percentage:
- Unsafe-to-test percentage:
- Requires-product-confirmation percentage:
- Unable-to-verify percentage:

Overall readiness:
- Incomplete / Needs Work / Beta Ready / Release Ready
- Recommended disposition: Proceed / Proceed with documented limitations / Hold beta / Hold release

Critical blockers:
- <none recorded>

High-priority issues:
- <none recorded>

Modules passed:
- <none recorded>

Modules with limitations:
- <none recorded>

Untested or blocked areas:
- <none recorded>

Product-confirmation questions:
- <none recorded>

Findings:
1. Title
   Finding ID:
   Category:
   Historical status:
   Severity:
   Evidence status:
   Route:
   Page:
   Section:
   Component ID:
   Workflow ID:
   Role:
   Username without PIN:
   Viewport/theme:
   Preconditions:
   Test data:
   Steps:
   Expected:
   Actual:
   Source expectation:
   Reproducibility:
   Screenshot/video:
   Console/network reference:
   Data created or modified:
   Cleanup performed:
   Related findings:

Deliverables attached:
- Master Application Inventory:
- Route and Role Matrix:
- Page Coverage Ledger:
- Reusable Component Matrix:
- Workflow Coverage Matrix:
- Findings Register:
- Inaccessible and Untestable Register:
- Cross-Page Consistency Report:
- Release Readiness Summary:
```
