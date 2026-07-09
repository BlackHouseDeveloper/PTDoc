# PTDoc UX Flow & UI Style Consistency Test Plan

## Executive Summary

This plan defines a standalone UX, design QA, and UI style consistency pass for PTDoc hosted beta. It validates whether the application feels coherent, predictable, accessible, professional, and aligned with the documented PTDoc design system across major workflows and reusable interface patterns.

This is not a functional QA plan. Use it to judge user experience, navigation continuity, interaction consistency, visual hierarchy, layout quality, design token adherence, accessibility, and responsive usability. Do not use it to validate backend/API correctness, data persistence correctness, payment processing correctness, or clinical business rules except where those behaviors appear to the user as visible feedback, state, navigation, or interaction.

Primary source: [Beta E2E Test Plan](BETA_E2E_TEST_PLAN.md). Design and UX anchors: [Global Style System](style-system.md), [Theme Visual Guide](design-system/THEME_VISUAL_GUIDE.md), [Figma Make Prototype v5 Context](context/ptdoc-figma-make-prototype-v5-context.md), [Blazor Component & Page Mapping](PTDocs+_Blazor_Component_Page_Mapping.md), [Responsive UI QA](RESPONSIVE_QA.md), and [Accessibility Utilities Usage Guide](ACCESSIBILITY_USAGE.md).

## Execution Defaults

- Primary target: `https://ptdoc.bhdevsites.com`.
- Reproduction/debug target only after beta issue logging: `http://localhost:5145`.
- Use seeded beta roles from the Beta E2E plan: Admin, PT, PTA, and Patient.
- Use fake data only: `Audit UX <timestamp>` and `audit+ux-<timestamp>@example.test`.
- Do not sign notes, submit irreversible statuses, send external SMS/email, process real payments, or enter real PHI.
- Capture screenshots only when they clarify UX findings and do not expose real PHI.
- Required viewport matrix: `1280x720`, `1440x900`, `1536x864`, `768x1024`, and `430x932`.
- Required theme pass: light and dark mode for Dashboard, Appointments Week View, Patient chart, Intake, Notes workspace, modals, and Settings/Admin where reachable.
- Local responsive diagnostics: use `?ptdocViewportDiagnostics=1`; disable with `?ptdocViewportDiagnostics=0`.

## Discovery Scope

Validate every visible UX surface that can affect flow, style consistency, or usability:

- Authentication and public pages: login, logout, access denied, not found, SMS consent, privacy, and terms.
- Global shell: header, sidebar, mobile drawer, route context, user menu, theme toggle, sync/connectivity, notifications, toast layer, and viewport diagnostics behavior.
- Dashboard: summary cards, metric cards, alerts, authorization and notes groups, recent activity, appointments, incomplete intake, and plan-of-care summaries.
- Appointments: Today view, Week View, admin clinician selector, PT/PTA scoped Week View, grouping controls, filters, schedule cards, detail modal, edit modal, and payment modal.
- Patients: directory, search, cards/list states, Add Patient, Add Patient + Send Intake, Send Intake modal, profile header, timeline, notes, documents, communications, and Insurance & Authorization.
- Intake: standalone access gate, clinician patient selector, wizard progress, demographics, body map, pain details, medical history, payer fields, contacts/providers, review, legal acknowledgements, loading/error/submitted states.
- Notes/SOAP: global notes, filters, pagination, note cards, note workspace, Evaluation, Daily, Progress, Discharge, Dry Needling, Subjective, Objective, Assessment, Plan, Goals, Review, interventions, CPT/HEP, AI generation, autosave/status messaging, validation panel, sticky footer, export/review.
- Secondary modules: Export Center, Reports, Progress Tracking, Settings/Admin, approvals, notification preferences, public document layout, empty/loading/error/success states.
- Reusable patterns: buttons, inputs, selects, checkboxes, switches, tabs, links, accordions, cards, tables, lists, date pickers, modals, dialogs, drawers, popovers, toasts, alerts, badges, progress indicators, skeletons, tooltips, pagination, context actions, search bars, filters, and role-specific nav items.

## Design-System Validation Criteria

Use these criteria on every page and component family:

- Typography: Inter family where applicable, documented semantic text scale, consistent heading hierarchy, readable line height, no oversized panel headings, and no cramped paragraph spacing.
- Color: semantic token use for primary emerald, secondary navy/black, surface/background, status colors, disabled states, focus rings, warning/error/success/info, and dark-mode equivalents.
- Spacing: token-based padding/margins, consistent grid gutters, predictable section spacing, no nested-card crowding, and no unexplained layout shifts.
- Borders and radius: consistent divider weight, input borders, card borders, and radius tokens.
- Elevation and layering: shadows and z-index behave predictably for menus, modals, drawers, toasts, tooltips, sticky footers, and overlays.
- Icons: consistent size, stroke weight, placement, alignment, and accessible labeling when icons are interactive.
- Motion: hover/focus/pressed/loading transitions are subtle, consistent, and not required to understand state.
- Accessibility: WCAG 2.1 AA intent, visible focus, keyboard operation, ARIA names where observable, color contrast, readable typography, and touch target size.
- Responsiveness: no page-level horizontal overflow except intended internal schedule/table scrolling; mobile navigation remains reachable.

## Test Case Format

Use this format for every executed UX test:

| Field | Required content |
| --- | --- |
| Test ID | Unique ID such as `UX-04.03`. |
| Feature Area | Module or workflow under review. |
| UX Category | Navigation flow, style consistency, component consistency, form experience, interaction, responsive, accessibility, visual cohesion, or friction. |
| Screen/Page | Route or visible page/surface. |
| Preconditions | Role, viewport, theme, seeded data, and safe fake data needs. |
| User Journey | Step-by-step user path. |
| Expected UX Behavior | User intent remains clear, predictable, and low-friction. |
| Expected UI Behavior | Visual state, hierarchy, layout, and feedback match documented patterns. |
| Visual Consistency Criteria | Typography, color, spacing, border, icon, elevation, and layout checks. |
| Interaction Consistency Criteria | Hover, focus, pressed, disabled, loading, cancellation, success, and error behavior. |
| Accessibility Considerations | Keyboard, focus, ARIA/name, contrast, touch target, and screen-reader notes. |
| Responsive Considerations | Required viewport/theme checks and overflow behavior. |
| Pass/Fail Criteria | Observable pass/fail rules. |
| Notes | Evidence, screenshots, source anchors, and unresolved product questions. |

## Scoring Model

| Result | Meaning |
| --- | --- |
| Pass | UX and UI match documented expectations with no material usability issue. |
| Pass with Minor Issues | Small polish issue that does not disrupt flow, comprehension, or accessibility. |
| Needs UX Work | Significant inconsistency, confusing flow, clutter, fragile interaction, or accessibility concern. |
| Blocked / Unable to Verify | Access, seed data, environment, safety, or missing-product-context prevents UX judgment. |

| Severity | Meaning |
| --- | --- |
| Critical UX | Blocks core role workflow, hides critical action/state, causes serious accessibility failure, or risks unsafe user action. |
| High UX | Major workflow friction, unreadable layout, confusing navigation, or inconsistent modal/form behavior in frequent workflows. |
| Medium UX | Non-blocking but repeated inconsistency, unclear hierarchy, redundant steps, or visual drift. |
| Low UX | Minor polish, copy, alignment, icon, spacing, or low-frequency visual issue. |

| Readiness | Criteria |
| --- | --- |
| Prototype-like | Inconsistent, incomplete, visually rough, or hard to navigate across major flows. |
| Beta-ready UX | Major workflows are coherent and usable, with minor polish or documented limitations. |
| Release-ready UX | Unified, stable, accessible, responsive, role-consistent, and professional across core workflows. |

## UX Test Suites

### UX-00: Environment, Evidence, And Baseline Setup

Objective: Establish a safe, repeatable UX review baseline.

Preconditions:
- Hosted beta URL is available.
- Current beta PIN is obtained out of band.
- Browser zoom is 100%.
- Tester has screenshot capture ready.

Tests:

| Test ID | User Journey | Expected UX/UI Behavior | Pass/Fail Criteria |
| --- | --- | --- | --- |
| UX-00.01 | Open beta Web and record browser, OS, viewport, role, timestamp, theme. | App loads over HTTPS with no mixed-content or certificate warning. | Pass if visual baseline is clean and route is beta-hosted. |
| UX-00.02 | Confirm evidence rules before testing. | Screenshots avoid real PHI and include only fake/seeded-safe data. | Fail if evidence process risks PHI exposure. |
| UX-00.03 | Run each viewport in the required matrix. | Layout mode aligns to responsive contract: drawer below `1200px`, desktop at `>=1200px`. | Fail if nav or page content becomes unreachable. |

Accessibility considerations: verify browser zoom is not masking layout issues; record if assistive tech is used.

### UX-01: Authentication And Public-Page Flow

Objective: Verify login, logout, access-denied, public policy pages, and unauthenticated navigation feel predictable and consistent.

| Test ID | Screen/Page | User Journey | Expected UX/UI Behavior | Pass/Fail Criteria |
| --- | --- | --- | --- | --- |
| UX-01.01 | `/login` | Tab through username/PIN, submit blank, malformed, invalid, and valid credentials. | Field order, inline errors, button states, focus, and invalid-credential feedback are consistent with form patterns. | Fail if validation appears generic, focus is lost, or error styling differs from other forms. |
| UX-01.02 | `/logout` and protected routes | Logout then visit `/dashboard`, `/appointments`, `/notes`. | User returns to login without stale shell, nav, or role labels. | Fail if authenticated shell remains visible. |
| UX-01.03 | `/sms-consent`, `/privacy`, `/terms` | Visit public pages from anonymous session. | Public document layout is visually cohesive, readable, and not mixed with clinician shell. | Fail if public pages require auth or use inconsistent typography/spacing. |
| UX-01.04 | `/access-denied`, unknown routes | Visit unauthorized/missing routes as Patient and anonymous user. | Access and not-found messaging is clear, nontechnical, and visually aligned. | Fail if page feels like raw error output or offers misleading actions. |

Responsive considerations: repeat login and public pages at `430x932`.

### UX-02: Global Shell And Navigation Consistency

Objective: Verify app-wide navigation, route context, theme, drawer, and notification patterns.

| Test ID | Screen/Page | User Journey | Expected UX/UI Behavior | Pass/Fail Criteria |
| --- | --- | --- | --- | --- |
| UX-02.01 | Authenticated shell | Move across Dashboard, Appointments, Patients, Notes, Export/Tools, Reports, Settings. | Active nav state, page titles, primary actions, and role-visible items are predictable. | Fail if current location is ambiguous or nav order changes unexpectedly. |
| UX-02.02 | Sidebar/drawer | Collapse desktop sidebar, open mobile drawer, close with backdrop/Escape. | Icon rail/drawer maintain labels, focus, readable icons, and no clipping. | Fail if drawer traps or hides critical nav. |
| UX-02.03 | Header controls | Use theme toggle, user menu, notifications, sync/connectivity controls. | Header controls share icon size, focus ring, tooltip/label behavior, and feedback tone. | Fail if controls look unrelated or focus order is illogical. |
| UX-02.04 | Toast/notification layer | Trigger safe success/error states where available. | Toasts do not steal focus, overlap modals, or obscure primary actions. | Fail if feedback hides required controls or uses inconsistent tone. |

Accessibility considerations: full keyboard pass through header, sidebar, drawer, and user menu.

### UX-03: Dashboard Information Architecture And Alert Clarity

Objective: Verify dashboard hierarchy, grouping, and actionable card patterns.

| Test ID | Screen/Page | User Journey | Expected UX/UI Behavior | Pass/Fail Criteria |
| --- | --- | --- | --- | --- |
| UX-03.01 | Dashboard cards | Scan summary cards left-to-right/top-to-bottom. | Cards use consistent typography, icon alignment, counts, status colors, and click affordances. | Fail if cards visually compete or unclear which are clickable. |
| UX-03.02 | Tile routing | Click each dashboard tile and return. | Destination matches tile wording and back/return behavior is predictable. | Fail if tile feels decorative but navigates, or actionable but does nothing. |
| UX-03.03 | Alerts | Expand/collapse Notes and Authorization alert groups where data exists. | Grouping, badges, priority colors, focus, and click targets are consistent. | Fail if grouping appears misleading or alert text lacks next step. |
| UX-03.04 | POC/recent activity | Review recent activity and plan-of-care summary cards. | Patient, note type, date, context, and next action are visually clear. | Fail if context is missing or hierarchy is cluttered. |

Responsive considerations: repeat at `1280x720`, `430x932`, and dark mode.

### UX-04: Appointments Schedule Readability And Modal Behavior

Objective: Validate schedule navigation, density, clinician scoping, and appointment modal consistency.

| Test ID | Screen/Page | User Journey | Expected UX/UI Behavior | Pass/Fail Criteria |
| --- | --- | --- | --- | --- |
| UX-04.01 | `/appointments` Today | Scan date, counts, filters, cards, status chips, and clinician columns. | Time, patient, status, intake/note indicators, and quick actions remain readable. | Fail if cards overlap, status chips clip, or filters obscure schedule. |
| UX-04.02 | Week View Admin | Switch to Week View as Admin and use clinician selector. | Admin sees searchable PT/PTA selector, selected clinician week is less dense, grouping controls remain clear. | Fail if all clinicians render by default in an unreadable grid. |
| UX-04.03 | Week View PT/PTA | Open Week View as PT/PTA. | No admin clinician selector; schedule visually scopes to signed-in clinician or clear empty state. | Fail if other clinicians clutter PT/PTA view. |
| UX-04.04 | Grouping controls | Switch Clinician/Day grouping. | Controls use consistent selected/pressed states and route remains predictable. | Fail if selected state is not visible or route and UI disagree. |
| UX-04.05 | Appointment detail modal | Open details, tab through, close via Close/Escape/backdrop where supported. | Modal header/body/footer, focus trap, scroll behavior, and action hierarchy match modal patterns. | Fail if focus escapes, footer clips, or actions are visually ambiguous. |
| UX-04.06 | Payment modal UX | Open copay/payment surface only with safe sandbox fixture. | Payment-required copy, amount, disabled/processing states, and cancellation are clear. | Fail if unavailable payment appears active or real payment data seems requested. |

Responsive considerations: schedule may scroll internally; page-level horizontal overflow fails.

### UX-05: Patient Directory, Add Patient, And Intake Handoff

Objective: Verify patient search and creation flows feel efficient and consistent.

| Test ID | Screen/Page | User Journey | Expected UX/UI Behavior | Pass/Fail Criteria |
| --- | --- | --- | --- | --- |
| UX-05.01 | `/patients` | Search by seeded name/MRN, clear search, inspect empty state. | Search bar, cards/list, loading, empty, and clear states are visually consistent. | Fail if no-results state lacks recovery action. |
| UX-05.02 | Add Patient modal | Open, tab through, submit blank, cancel. | Field order, required indicators, validation placement, modal spacing, and footer actions are consistent. | Fail if validation is scattered or modal feels unlike other forms. |
| UX-05.03 | Add Patient + Send Intake | Use safe fake patient and trigger handoff without external send. | Handoff from creation to Send Intake is obvious and avoids duplicate search. | Fail if user cannot tell whether patient was created or selected. |
| UX-05.04 | Send Intake modal | Inspect link/QR/email/SMS options where available. | External side effects are clearly separated from copy/link actions. | Fail if send actions look identical to safe copy actions. |

Accessibility considerations: modal focus returns to trigger and upload/copy controls are keyboard reachable.

### UX-06: Patient Chart Navigation And Section Cohesion

Objective: Verify chart-level navigation and patient context are cohesive across tabs and panels.

| Test ID | Screen/Page | User Journey | Expected UX/UI Behavior | Pass/Fail Criteria |
| --- | --- | --- | --- | --- |
| UX-06.01 | Patient profile | Open seeded patient, scan header and primary actions. | Patient identity, status, clinical context, and primary actions are visually distinct. | Fail if actions compete or patient context is lost. |
| UX-06.02 | Timeline/Notes/Documents/Communications | Navigate each section by click and direct URL where supported. | Section navigation uses one consistent link/tab treatment and active state. | Fail if some sections look like tabs and others like unrelated links without reason. |
| UX-06.03 | Insurance & Authorization | Review payer/auth panels, history rows, save/error states. | First-class chart area with consistent panel headings, fields, separators, and validation. | Fail if it feels visually detached from patient chart. |
| UX-06.04 | Documents/Communications | Inspect upload/log forms, lists, loading, success, and error states. | Storage workflows share field layout, status message tone, and empty states. | Fail if status messages contradict errors or controls are hard to locate. |

Responsive considerations: patient context remains visible or recoverable at mobile width.

### UX-07: Intake Wizard Flow, Body-Map Accessibility, And Review Clarity

Objective: Validate patient-facing intake UX and clinician-started intake handoff.

| Test ID | Screen/Page | User Journey | Expected UX/UI Behavior | Pass/Fail Criteria |
| --- | --- | --- | --- | --- |
| UX-07.01 | Intake access gate | Open safe intake route or clinician intake selector. | Access state explains what to do next and avoids technical language. | Fail if user cannot tell why intake is unavailable. |
| UX-07.02 | Wizard navigation | Move next/back through steps with partial fake data. | Progress indicator, headings, next/back placement, and saved/validation feedback are consistent. | Fail if back loses context or progress is unclear. |
| UX-07.03 | Body map | Select body region by mouse and keyboard. | Region focus styling, selected state, Enter/Space behavior, and no page scroll on Space are clear. | Fail if keyboard selection is invisible or unreliable. |
| UX-07.04 | Pain/details/forms | Trigger missing-field validation and recover. | Required indicators, inline errors, and helper text match other forms. | Fail if validation appears only at page top without field context. |
| UX-07.05 | Review/legal/submitted | Review, consent/terms, submit-safe if fixture allows. | Review hierarchy is scannable; confirmation appears in current viewport. | Fail if confirmation is hidden above/below or legal states are visually buried. |

Responsive considerations: mobile intake must avoid horizontal scroll and maintain touch targets.

### UX-08: SOAP Workspace And Note-Type Consistency

Objective: Verify documentation workspaces share predictable navigation, status, and editor patterns.

| Test ID | Screen/Page | User Journey | Expected UX/UI Behavior | Pass/Fail Criteria |
| --- | --- | --- | --- | --- |
| UX-08.01 | Start New Note | Open note chooser from patient or appointment. | Note types, descriptions, and route transitions are clear. | Fail if chooser looks like a dead menu or ambiguous action list. |
| UX-08.02 | Evaluation workspace | Move through Subjective, Objective, Assessment, Plan, Goals, Review. | Section nav, patient context, required fields, autosave/status, and sticky footer are consistent. | Fail if section changes lose context or controls jump. |
| UX-08.03 | Daily/Progress/Discharge/Dry Needling | Compare section headings, cards, text areas, tables, and review surface. | Note variants share a design language while preserving note-specific structure. | Fail if one note type feels like a different app. |
| UX-08.04 | Goals/outcomes/interventions/CPT/HEP | Inspect editors and associations. | Row controls, autocomplete, selects, badges, and Review summaries remain aligned and readable. | Fail if row-level actions are visually hidden or associations are unclear. |
| UX-08.05 | Save/submit/sign surfaces | Attempt safe draft save and inspect blocked submit/sign UX without irreversible action. | Completion blockers, consent dialog, disabled states, and error messages are clear and nontechnical. | Fail if generic failure hides actionable next step. |
| UX-08.06 | PDF/export preview | Open review/export where safe. | Export header, preview hierarchy, and document styling are professional and not implementation-labeled. | Fail if internal labels or unreadable tables appear. |

Accessibility considerations: note workspace section nav and sticky footer must be keyboard usable.

### UX-09: AI Generation Interaction UX

Objective: Verify AI-assisted surfaces are understandable, optional, and safely integrated.

| Test ID | Screen/Page | User Journey | Expected UX/UI Behavior | Pass/Fail Criteria |
| --- | --- | --- | --- | --- |
| UX-09.01 | Prognosis/Assessment AI | Inspect Generate button, disabled state, loading, output, accept/edit controls. | AI feels like an assistant inside the editor, not a separate workflow. | Fail if output location or ownership is unclear. |
| UX-09.02 | AI error/rate-limit/disabled | Trigger or observe disabled/error state where available. | Failure is localized to the AI control and does not visually break the workspace. | Fail if page-wide generic error appears for AI-only failure. |
| UX-09.03 | AI content review | Review generated content before accepting. | Accept/edit/cancel affordances are clear and clinician remains in control. | Fail if generated text appears final without review. |

Notes: If beta AI is disabled, mark as Blocked/Unable to Verify and record visible disabled-state UX.

### UX-10: Notes List, Filters, Pagination, And Empty States

Objective: Verify global notes list and patient-specific notes use consistent discovery and list patterns.

| Test ID | Screen/Page | User Journey | Expected UX/UI Behavior | Pass/Fail Criteria |
| --- | --- | --- | --- | --- |
| UX-10.01 | `/notes` | Scan list, cards/table, sort/filter/search controls. | Notes are bounded, scannable, and filter controls use consistent form styling. | Fail if first render feels overloaded or filters are visually detached. |
| UX-10.02 | Pagination/load more | Use Load More if available. | Load state, appended records, and end-of-list messaging are clear. | Fail if content jumps or duplicate controls appear. |
| UX-10.03 | Global vs patient notes | Compare open/edit/view behavior from global list and patient chart. | Layout, buttons, statuses, and validation feedback are consistent. | Fail if same note action changes label/visual meaning across contexts. |

Responsive considerations: notes list should not require page-level horizontal scroll.

### UX-11: Export, Reports, Progress, And Settings/Admin

Objective: Verify secondary modules do not drift from the primary design system.

| Test ID | Screen/Page | User Journey | Expected UX/UI Behavior | Pass/Fail Criteria |
| --- | --- | --- | --- | --- |
| UX-11.01 | Export Center | Navigate filters, type selectors, format selectors, preview, recent activity. | Panels and selectors match card/form/table patterns. | Fail if preview looks unrelated to rest of app. |
| UX-11.02 | Reports | Open reports and scan available cards/tables/empty states. | If exploratory, status is explicit and visually polished. | Fail if dead controls or placeholder clutter appear. |
| UX-11.03 | Progress Tracking | Open patient/progress surfaces and inspect charts/cards. | Charts, metric cards, filters, and empty states are clear and consistent. | Fail if chart labels are unreadable or source context missing. |
| UX-11.04 | Settings/Admin | Open settings categories and approval/admin surfaces. | Sidebar, forms, badges, status chips, and deferred sections are cohesive. | Fail if settings uses a different navigation or form style without reason. |

Accessibility considerations: admin tables and charts need readable labels and keyboard-reachable controls.

### UX-12: Reusable Component Consistency Audit

Objective: Validate that repeated UI elements behave consistently across modules.

| Test ID | Component family | Surfaces to sample | Expected UX/UI Behavior | Pass/Fail Criteria |
| --- | --- | --- | --- | --- |
| UX-12.01 | Buttons/links | Login, Dashboard, Patients, Appointments, Notes, Settings. | Primary/secondary/destructive/ghost/link semantics are visually distinct and consistent. | Fail if links have button semantics without link behavior or vice versa. |
| UX-12.02 | Form controls | Login, Add Patient, Patient Info, Intake, Notes. | Labels, required markers, helper text, disabled/error/focus states align. | Fail if same control type has conflicting visual language. |
| UX-12.03 | Cards/panels | Dashboard, Patient chart, Notes, Progress, Export. | Card radius, border, padding, header/footer treatment, and hierarchy are coherent. | Fail if nested cards create clutter or inconsistent shadows. |
| UX-12.04 | Tables/lists | Notes, Documents, Communications, Settings, Export. | Header, row hover/focus, empty/loading, and pagination match. | Fail if table/list actions are hidden or inconsistent. |
| UX-12.05 | Modals/drawers/popovers | Add Patient, Send Intake, Appointment Detail, Payment, Notifications, mobile nav. | Focus trap, close behavior, backdrop, footer, and mobile layout are consistent. | Fail if modal behavior differs without clear reason. |
| UX-12.06 | Status feedback | Toasts, alerts, validation, badges, sync state, save state. | Success/warning/error/info colors and language are consistent. | Fail if feedback tone or color semantics conflict. |

### UX-13: Responsive, Dark Mode, Keyboard, And Accessibility Pass

Objective: Validate full-app usability across viewport, theme, and keyboard conditions.

| Test ID | User Journey | Expected UX/UI Behavior | Pass/Fail Criteria |
| --- | --- | --- | --- |
| UX-13.01 | Run Dashboard, Appointments Week View, Patient chart, Intake, Notes workspace, Settings/Admin at all required viewports. | Content reflows predictably; nav remains reachable. | Fail for page-level overflow, clipping, overlapping text, or hidden actions. |
| UX-13.02 | Repeat key flows in dark mode. | Text, inputs, cards, status chips, and modals meet readable contrast. | Fail if labels, placeholders, disabled states, or borders disappear. |
| UX-13.03 | Keyboard-only pass through shell, first form, modal, note workspace, body map, filters. | Tab order is logical, focus rings visible, Enter/Space activate expected controls. | Fail if keyboard user gets trapped or cannot reach primary action. |
| UX-13.04 | Touch target pass at `430x932` and `768x1024`. | Buttons, links, selects, cards, and row actions meet practical touch sizing. | Fail if targets are too small or adjacent actions are easy to mis-tap. |
| UX-13.05 | Accessibility semantics spot check. | Modal/dialog roles, labels, error announcements, and current/selected states are observable where documented. | Fail if unnamed controls or ambiguous selected states appear. |

### UX-14: UX Friction And Release-Readiness Review

Objective: Identify friction that may not appear as a functional failure.

| Test ID | Review area | Expected UX/UI Behavior | Pass/Fail Criteria |
| --- | --- | --- | --- |
| UX-14.01 | Click count and flow continuity | Core tasks avoid redundant navigation, duplicate search, or unnecessary modal reopen. | Fail if common tasks require avoidable repeated steps. |
| UX-14.02 | Terminology consistency | Same concepts use same labels: notes due, authorization/referral, intake, POC, HEP, CPT, clinician/PT/PTA. | Fail if labels change meaning across modules. |
| UX-14.03 | Cognitive load | Dense clinical screens provide sectioning, hierarchy, and progressive disclosure. | Fail if clinician cannot identify next action within 5 seconds. |
| UX-14.04 | Error recovery | Errors explain what happened and how to recover without blaming the user. | Fail if generic errors appear for recoverable UX states. |
| UX-14.05 | Visual cohesion | Module-to-module review feels like one product. | Fail if pages visibly use unrelated spacing, color, cards, controls, or icon styles. |

## Final UX Report Template

```text
PTDoc UX Flow & UI Style Consistency Report

Environment:
- Date/time:
- Tester:
- Browser/OS:
- Viewports:
- Themes:
- Beta URL:
- Roles/accounts used:

Overall UX readiness:
- Prototype-like / Beta-ready UX / Release-ready UX

Summary:
- Critical UX blockers:
- High UX issues:
- Medium UX issues:
- Low UX polish:
- Unable-to-verify areas:

Source alignment:
- Matches Beta E2E expectations:
- Matches style/design-system expectations:
- Diverges from Figma/prototype intent:
- Needs product/design confirmation:

Findings:
1. Title
   Test ID:
   Feature Area:
   UX Category:
   Screen/Page:
   Preconditions:
   User Journey:
   Expected UX Behavior:
   Expected UI Behavior:
   Visual Consistency Criteria:
   Interaction Consistency Criteria:
   Accessibility Considerations:
   Responsive Considerations:
   Observed Behavior:
   Pass/Fail:
   Severity:
   Evidence:
   Notes:

Release recommendation:
- Proceed / Proceed with UX limitations / Hold for UX remediation
```

