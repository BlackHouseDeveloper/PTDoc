# Client review implementation tracker

Last updated: 2026-10-26. Plan identifiers stay stable; GitHub allocates actual numbers. No number is reserved. This tracker is maintained on the Plan 01 branch until that documentation is merged. Individual independent branches contain their own `plan-XX.md` records.

## Authoritative sources and baseline

- [Implementation plan](source/implementation-plan.md)
- [Requirement ledger](source/requirement-ledger.json)
- Main baseline: `6276027251609df64ffbbfa883f66e2d83dd57e4`
- [#153](https://github.com/BlackHouseDeveloper/PTDoc/pull/153) is merged. [#154](https://github.com/BlackHouseDeveloper/PTDoc/pull/154) is open, ready for review, and targets main at inspection. Do not recreate its Settings UI.
- The implementation request explicitly authorizes relevant builds/tests, commits after checks, pushes and PR publication. Documentation-only commits require document/link/diff checks; no unrelated .NET run is required.

## Mapping

| Plan | Title / requirements | Expected | Actual | Branch → base | Status | Dependencies / blockers | Verification |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 01 | Verify and repair active-use and multi-tab sessions; R003, R005, R048, R061 | #155 | Not published | `codex/plan-01-session-verification → main` | Planning only | Engineering/QA: run a controlled browser scenario on the integrated #154 build covering active editing across the idle interval, a second tab, explicit logout and API revocation. Existing unit/integration tests are not a substitute for this circuit/browser evidence.; Product/security owner: confirm the existing anti-piggyback invariant and allowed idle/absolute policy; no timeout weakening is proposed. | Passed: 49; failed: 0; skipped: 0. Main 6276027; synthetic fixtures; no production session used.; Passed: requirement IDs, required sections, local links, sensitive local paths and whitespace checked. The script is a task-local document checker; no .NET behavior is asserted by it. |
| 02 | Recover from note-save errors without trapping navigation; R006, R091 | #156 | Not published | `codex/plan-02-note-save-recovery → main` | Not started | None recorded yet | Not yet run |
| 03 | Correct patient timeline timestamps using the agreed display time zone; R007 | #157 | Not published | `codex/plan-03-timeline-timezones → main` | Not started | January/product owner: choose clinic-time or viewer-time display, including cross-zone users.; Engineering/QA: compare the same synthetic UTC note/intake/appointment events on two host zones, including midnight and daylight-saving boundaries, after the display contract is approved. | Not yet run |
| 04 | Validate role configuration and visit types against the existing Settings stack; R004, R037 | #158 | Not published | `codex/plan-04-settings-verification → main` | Not started | Engineering/user merge checkpoint: integrate or otherwise authorize testing the exact #154 candidate with main, then verify R004/R037 against that integrated source. No merge is authorized here.; January/security owner: use the approved role capability matrix; do not infer new privileges from the old meeting role-picker complaint. | Not yet run |
| 05 | Decide organization and clinic ownership before migrating tenancy; R001, R085 | #159 | Not published | `codex/plan-05-organization-decision → main` | Not started | January with engineering/security: approve organization versus clinic ownership, staff membership, invitation/approval powers and cross-clinic clinical access before any schema/access migration. | Not yet run |
| 06 | Decide individual subscription and organization onboarding rules; R084, R086 | #160 | Not published | `codex/plan-06-onboarding-decision → main` | Not started | January/business owner: approve individual/organization signup, payment provider and server entitlement lifecycle, staff limits, failure recovery and treatment of existing pending registrations.; Plan 05 ownership decisions precede organization-dependent onboarding implementation. | Not yet run |
| 07 | Prefill intake and preserve patient and policy data across entry flows; R020, R031 | #161 | Not published | `codex/plan-07-intake-prefill → main` | Not started | None recorded yet | Not yet run |
| 08 | Make intake content and repeat encounters follow the destination note type; R010, R062, R074 | #162 | Not published | `codex/plan-08-note-type-intake → main` | Not started | None recorded yet | Not yet run |
| 09 | Make intake navigation, review editing and body-map controls clearer; R009, R013, R018, R019, R093 | #163 | Not published | `codex/plan-09-intake-navigation → main` | Not started | None recorded yet | Not yet run |
| 10 | Clarify medical history, medications and functional limitations; R014, R015, R016, R022, R063 | #164 | Not published | `codex/plan-10-intake-language → main` | Not started | None recorded yet | Not yet run |
| 11 | Group PHI release information and remove intake revocation controls; R021, R047 | #165 | Not published | `codex/plan-11-intake-consent → main` | Not started | None recorded yet | Not yet run |
| 12 | Complete patient chart editing and patient-entry actions; R008, R024, R028 | #166 | Not published | `codex/plan-12-patient-chart → main` | Not started | None recorded yet | Not yet run |
| 13 | Edit provider-directory entries and carry fax details into care teams; R023, R026 | #167 | Not published | `codex/plan-13-provider-directory → main` | Not started | None recorded yet | Not yet run |
| 14 | Consolidate insurance policy and authorization editing; R044, R045, R046 | #168 | Not published | `codex/plan-14-insurance-editor → main` | Not started | None recorded yet | Not yet run |
| 15 | Resolve authorization-field placement in patient and policy entry; R029, R030 | #169 | Not published | `codex/plan-15-authorization-placement → main` | Not started | None recorded yet | Not yet run |
| 16 | Create appointments from empty clinician slots and enforce schedulable roles; R032, R033, R034, R035 | #170 | Not published | `codex/plan-16-empty-schedule-slots → main` | Not started | None recorded yet | Not yet run |
| 17 | Fax signed notes from review and improve clinic patient selection; R025, R038 | #171 | Not published | `codex/plan-17-signed-note-fax → main` | Not started | None recorded yet | Not yet run |
| 18 | Define clinician handoff and shared-treatment sign-off; R041, R042 | #172 | Not published | `codex/plan-18-clinician-handoff → main` | Not started | None recorded yet | Not yet run |
| 19 | Add clinician-authored patient alerts to chart and note headers; R043 | #173 | Not published | `codex/plan-19-patient-alerts → main` | Not started | None recorded yet | Not yet run |
| 20 | Implement the agreed daily subjective and objective scope; R049, R050, R051, R058 | #174 | Not published | `codex/plan-20-daily-questionnaire → main` | Not started | None recorded yet | Not yet run |
| 21 | Represent ROM by movement, side and active/passive measurement; R053, R054, R055, R057 | #175 | Not published | `codex/plan-21-rom-dimensions → main` | Not started | None recorded yet | Not yet run |
| 22 | Separate MMT from joint mobility and reuse ROM movement selections; R056, R065 | #176 | Not published | `codex/plan-22-mmt-joint-mobility → main` | Not started | None recorded yet | Not yet run |
| 23 | Establish versioned outcome definitions and validate supplied source assets; R012, R095 | #177 | Not published | `codex/plan-23-outcome-contracts → main` | Not started | None recorded yet | Not yet run |
| 24 | Deliver approved patient questionnaires with automatic scoring; R011 | #178 | Not published | `codex/plan-24-patient-outcomes → main` | Not started | None recorded yet | Not yet run |
| 25 | Carry computed outcomes into clinical notes with provenance; R066 | #179 | Not published | `codex/plan-25-outcome-note-projection → main` | Not started | None recorded yet | Not yet run |
| 26 | Capture patient personal goals and use them in SMART-goal drafting; R064 | #180 | Not published | `codex/plan-26-patient-personal-goals → main` | Not started | None recorded yet | Not yet run |
| 27 | Place evaluation assessment narrative after contributing inputs; R059, R068, R069 | #181 | Not published | `codex/plan-27-assessment-order → main` | Not started | None recorded yet | Not yet run |
| 28 | Generate distinct SOAP summaries from their corresponding sections; R073 | #182 | Not published | `codex/plan-28-soap-generation → main` | Not started | None recorded yet | Not yet run |
| 29 | Reproduce and repair goal-generation loss and candidate exhaustion; R070, R071 | #183 | Not published | `codex/plan-29-goal-generation-evidence → main` | Not started | None recorded yet | Not yet run |
| 30 | Complete progress activity tracking, improvement and plan frequency; R052, R076, R077, R078, R080 | #184 | Not published | `codex/plan-30-progress-activities → main` | Not started | None recorded yet | Not yet run |
| 31 | Implement the approved discharge questionnaire and recommendation choices; R081, R082, R094 | #185 | Not published | `codex/plan-31-discharge-content → main` | Not started | None recorded yet | Not yet run |
| 32 | Make progress and discharge AI actions produce reviewable suggestions; R079, R083 | #186 | Not published | `codex/plan-32-progress-discharge-ai → main` | Not started | None recorded yet | Not yet run |
| 33 | Verify and remove redundant Settings scrolling; R002 | #187 | Not published | `codex/plan-33-settings-scrolling → main` | Not started | None recorded yet | Not yet run |
| 34 | Decide sex and gender terminology, storage and clinical use; R017 | #188 | Not published | `codex/plan-34-identity-fields → main` | Not started | None recorded yet | Not yet run |
| 35 | Decide the zero-authorization-visit documentation safeguard; R089 | #189 | Not published | `codex/plan-35-authorization-safeguard → main` | Not started | None recorded yet | Not yet run |
| 36 | Support approved clinician-administered outcome measures; R067 | #190 | Not published | `codex/plan-36-clinician-outcomes → main` | Not started | None recorded yet | Not yet run |

## Stack and merge checkpoints

Only actual code dependencies form stacks. Planned chains are 07→08 (09 only if consuming its contracts), 21→22 and 28→32. Outcome foundation23 has independent patient24 and clinician36 consumers; projection25 requires both completed contracts. Planning documents alone target main and do not create artificial code stacks. No multi-parent stack or duplicated foundation is allowed. Actual base branches above are the publication source of truth.

- User action: merge/reconcile #154 before claiming integrated Settings acceptance.
- User action: merge accepted foundational PRs before retargeting independent consumers where a shared foundation cannot be represented as one short linear stack.
- Clinical/product decisions and source-material approvals remain separate from any documentation merge.
- No merge or deployment is performed by this task.
