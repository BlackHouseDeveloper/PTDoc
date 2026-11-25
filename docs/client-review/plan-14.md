# Plan PR 14: Consolidate insurance policy and authorization editing

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R044, R045, R046
- Original plan status: Ready for Development
- Branch: `codex/plan-14-insurance-editor`
- Base branch: `main` at `9914f0d4f9c8014fc97d6dd44608c1ac3af70a8e`
- Pull request: [#169](https://github.com/BlackHouseDeveloper/PTDoc/pull/169)
- [Authoritative plan brief](source/implementation-plan.md#pr-14)

## Current evidence and changes

The chart has one insurance entry. Each policy editor groups the plan year, visit limit, cost sharing and adjuster fields with the policy’s authorizations and referrals. One save creates, corrects or archives the selected entries. The API checks the write role, clinic, patient, policy and expected versions, then commits the policy, edited children, patient version and audit event in one EF save. Untouched children keep their IDs, versions and usage.

The patient-level compatibility form preserves unknown JSON at the top level and inside history rows. It retains legacy-only labels such as “active” and “lifetime” without assigning a new approval or benefit rule. It sends the loaded patient version and projects individual edits onto the corresponding normalized records. A correction that cannot identify its original referral returns a conflict and directs the user to the policy editor. Existing unversioned chart clients can still refresh the saved payer type for intake prefill. Intake retains its accepted-source version checks and the canonical payer type takes precedence over an alias.

The three provider migrations append nullable `TotalVisitLimit` and `VisitLimitType` columns after the individual-subscription migrations on main. The target models and snapshots include merged intake, organization, subscription and patient concurrency metadata. SQL Server and PostgreSQL snapshots also capture existing foreign-key indexes from the enterprise-directory migration. The policy migration issues two column additions on upgrade and removes only those columns on downgrade. Export newly entered limits before downgrading. The rebased migration ID is `20261124010000_PolicyVisitLimits`. Development databases with the October feature-branch IDs require reconciliation before reuse. Verification started from disposable databases on the main migration chain.

## Review findings

| Finding | Classification on the incoming head | Disposition |
| --- | --- | --- |
| [CodeQL alert 18](https://github.com/BlackHouseDeveloper/PTDoc/pull/169#discussion_r4376917921) | Invalid as an access bypass | The flagged helper assigns coverage data. The workflow routes require `InsurancePolicyWrite`. Clinic and child ownership checks precede the write. The historical PR alert instance is marked fixed. |
| [PostgreSQL removals in Up](https://github.com/BlackHouseDeveloper/PTDoc/pull/169#discussion_r4376941893) | Outdated | The incoming migration already adds the columns in Up and drops them in Down. Provider-operation tests and generated SQL verify both directions. |
| [SQL Server removals and duplicate index](https://github.com/BlackHouseDeveloper/PTDoc/pull/169#discussion_r4376941958) | Outdated | The incoming migration already omitted the index operation and separated Up and Down. Regenerated model metadata produces no index DDL in either direction. |
| [Missing legacy history match](https://github.com/BlackHouseDeveloper/PTDoc/pull/169#discussion_r4376941997) | Valid | Reject a correction after the normalized referral has been renamed or archived. A regression checks the original ID, reference and usage. |
| [Adding a child to a saved policy](https://github.com/BlackHouseDeveloper/PTDoc/pull/169#discussion_r4376942043) | Valid | Explicitly track new children as Added. A relational SQLite test reloads a new referral and checks archived evidence. |
| [Isolated fields skipped by the legacy projection](https://github.com/BlackHouseDeveloper/PTDoc/pull/169#discussion_r4376942075) | Valid | Project changed policy and authorization fields, including clears. Bind authorization edits to their previous reference and reject ambiguity. Preserve unknown compatible history fields and unrelated usage. |
| [Per-entry archive missing](https://github.com/BlackHouseDeveloper/PTDoc/pull/169#discussion_r4376942119) | Valid | The editor stages archival in the coordinated, version-checked save. The archived row retains the reference and usage it had before archival. |
| Six review-summary links | Duplicate | Each links to the corresponding inline finding. On November 24, two reviews, seven inline threads and zero conversation comments were visible. Neither review included a separate previously missed section. |

### November 25 review of `4bd8f872`

GitHub exposed three review submissions, 12 inline threads, zero minimized inline comments and zero conversation comments. The six links in the October review and the eleven links in the November review all repeat inline findings. Neither Copilot review exposes a suppressed-comments or Previously missed section. The three generated migration designers listed as unreviewed carry no findings. [November review](https://github.com/BlackHouseDeveloper/PTDoc/pull/169#pullrequestreview-6050838713)

The access-bypass report remains invalid and both migration reports remain outdated for the reasons above. The four formerly valid findings are outdated on this published head. `LegacyCorrection_DoesNotReinsertReferralRenamedInPolicyEditor` covers the missing match. `ExistingPolicy_InsertsNewChildAndArchivesSelectedEntryWithoutRewritingEvidence` covers insertion and archival. `LegacyPatch_AppliesIsolatedEditsAndClears_WithoutReplayingLimitsOrUsage` covers the original field-level examples.

| New finding | Classification | Implementation and regression |
| --- | --- | --- |
| [Secondary member number](https://github.com/BlackHouseDeveloper/PTDoc/pull/169#discussion_r4689525317) | Valid | The form emits both aliases. The server now compares the resolved values in the two snapshots. Coverage includes each original spelling, an unrelated save after a correction, an intentional edit and a clear. |
| [Payer type and Self Pay creation](https://github.com/BlackHouseDeveloper/PTDoc/pull/169#discussion_r4689525385) | Valid | The patch and creation guard use the canonical payer type with an alias fallback. The compatibility page reads the same precedence and updates both keys on intentional selection. Service and component tests cover conflicting keys and carrier-free Self Pay. |
| [Effective date range](https://github.com/BlackHouseDeveloper/PTDoc/pull/169#discussion_r4689525440) | Valid | The patient write validates a changed endpoint against the stored policy endpoint. API tests cover both directions, rejection without partial persistence, and a valid same-day boundary. |
| [Coinsurance percentage](https://github.com/BlackHouseDeveloper/PTDoc/pull/169#discussion_r4689525501) | Valid | The legacy patient write enforces the workflow's 0–100 range. API tests reject 100.01, preserve the prior JSON and policy, and accept 100. |
| [Stale intake limit](https://github.com/BlackHouseDeveloper/PTDoc/pull/169#discussion_r4689525553) | Valid | The accepted primary snapshot now carries its visit limit through prefill, draft persistence and reconciliation. API regression cases submit after accepting a corrected or cleared limit on the same policy. |

The detailed results below apply to the November 24 commit. The repository owner confirmed that the local build and tests passed for the five November 25 fixes before publication.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

User confirmed that Authorization Required should be removed from intake/chart entry (Plan 15). This editor does not introduce that toggle. Existing historical edit/archive permissions remain the default; any broader clinical correction policy requires a separate decision.

## Development specification

### User Problem

Duplicate entry points and scattered forms make policy and authorization information difficult to maintain consistently.

### Confirmed Requirement

Remove the duplicate Insurance & Authorization entry; place visit limit and calendar/plan-year fields with policy entry; remove duplicate adjuster fields; combine related authorization/PCP/referral details and history with one save.

### Expected Behavior

Users edit each policy and its related authorization/referral information in a coherent workflow and receive one clear save result.

### Scope

UI consolidation and an atomic or explicitly coordinated save of already-supported fields; editable history subject to existing record rules.

### Out of Scope

Changing benefit rules, conflating historical and active authorizations, or resolving the disputed Add Patient field by assumption.

### Implementation Guidance

**Technical Recommendation:** retain distinct domain records behind the unified UI. Reuse existing service contracts; if one-save requires orchestration, implement it server-side in the appropriate Application/Infrastructure boundary with concurrency validation.

### Data and Migration Considerations

Preserve primary/secondary policy links, historical validity dates, utilization and referral provenance. No destructive table merge is implied. Migrate only if contract gaps are demonstrated.

### Security and Privacy Considerations

**Technical Recommendation:** API permission and clinic ownership apply to every child record; reject forged policy/authorization IDs and audit substantive edits.

### Acceptance Criteria

- [x] **AC1:** Given a patient chart, when accessing insurance, then a single intended chart entry opens the consolidated workflow.
- [x] **AC2:** Given a policy with visit limit and plan-year information, when saved once and reloaded, then all intended changes persist without duplicate adjuster fields.
- [x] **AC3:** Given multiple authorizations and referrals, when editing one historical entry, then unrelated records and utilization remain unchanged.
- [x] **AC4:** Given a validation/concurrency failure in one section, when saving, then the UI does not report a fully successful save or silently lose edits.

### Rollout and Regression Risks

Consolidation may accidentally combine separate clinical/business meanings. Compare before/after synthetic records.

## Verification performed

The November 24 review replayed four Plan 14 commits. The original published parent was `513f5a93`. Plan 12’s final squash commit `9914f0d4` supplies the patient-chart foundation. The October checks on the former stacked branch are historical and are excluded from the results below.

| Check on the rebased implementation | Result |
| --- | --- |
| Release build of `tests/PTDoc.Tests/PTDoc.Tests.csproj`, including API, Web and shared UI | Passed with zero warnings and zero errors. |
| Insurance, patient chart, intake prefill, UI, RBAC, tenancy and end-to-end tests | 491 passed locally. One PostgreSQL-specific version test is selected by name and runs in the PostgreSQL database job. |
| CoreCi, serialized as in `ci-core.yml` | 2,856 passed, zero failed or skipped. |
| Full-project format verification | All nine passed: Core, Application, Infrastructure, API, Tests, UI and the three provider migration projects. |
| SQLite database provider and observability categories | Four passed. Three PostgreSQL-specific tests skipped as designed for this SQLite run. The focused suite also exercised SQLite up/down/up, successful child insertion and a competing save that rolled back the policy, new child and audit event. |
| EF pending-model checks | SQLite, SQL Server and PostgreSQL all passed on Release binaries. |
| SQL Server and PostgreSQL migration scripts from `AddIndividualSubscriptions` to `PolicyVisitLimits` and back | Both directions generated for both providers. Upgrade adds two nullable columns. Downgrade drops them. Neither direction changes indexes. Live provider executions are recorded by the [PR checks](https://github.com/BlackHouseDeveloper/PTDoc/pull/169/checks). |
| Playwright on disposable local SQLite, Release Web and API | One chart link opened by keyboard. Save/reload persisted the limit, plan year, copay, fax and two children. A historical edit kept its ID and usage and preserved the other child’s version. Archival and insertion in one save passed. Real policy, child and patient conflicts returned 409. Invalid carrier input returned 422. The editor retained the drafts until explicit discard. |
| Keyboard and responsive layout | Enter opened the editor and saved. Tab moved through the inputs and Space selected per-entry archival. The document and tested controls fit at 390, 768 and 1440 CSS pixels. Chromium logged the Blazor unload permissions-policy diagnostic on page load. |
| Incremental Git diff against `origin/main` | Inspected the Plan 14 changes and provider operations. Whitespace checks passed. |

## Remaining acceptance work

Client acceptance, screen-reader testing and native MAUI publish/install remain open. The Web browser run used synthetic development records.

## Publication and merge boundary

The [pull request](https://github.com/BlackHouseDeveloper/PTDoc/pull/169) targets `main`. Plan 13’s provider-directory decisions and Plan 35’s benefit rules are separate work.
