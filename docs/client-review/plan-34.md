# Plan PR 34: Decide sex and gender terminology, storage and clinical use

**Planning only — this PR does not implement or fix the feature.**

- Requirements: R017
- Original plan status: Needs Clarification
- Branch: `codex/plan-34-identity-fields`
- Base: `main`
- Expected GitHub number: #188; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-34)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Current BasicInfoCard offers optional Sex at Birth values Female, Male, Intersex, Prefer not to say and Other, with a blank placeholder. IntakeWizardState and IntakeResponseDraft store nullable SexAtBirth text; IntakeWizardPage hydrates/submits it, IntakeService copies it into the retained intake draft, and ReviewStep displays the same label/value. Patient and PatientResponse have no corresponding typed sex/gender property. The inspected intake-domain projection does not copy this field into Patient. No explicit SexAtBirth consumer was found in the inspected PDF or sync code; raw intake JSON retention remains relevant. This is a source inventory, not an audit of production values or external exports.

## Dependencies, blockers and owners

- January/clinical owner: approve each collected field’s purpose, label, options, requiredness, visibility and any clinical decision use.
- Product/data owner: approve legacy value treatment and export/integration mapping; no production-data inventory was authorized or performed.

## Decisions and options

| Decision | Options to review | Required evidence |
| --- | --- | --- |
| Field meaning | Keep an explicitly defined sex-at-birth field; use a distinct identity field; collect both only if each has an approved purpose | Clinical/product statement of intended use for each field |
| Optionality | Optional with explicit declined/unknown states; required only with a documented need | Exact allowed values and meaning of blank versus declined |
| Display | Intake only; chart display; explicitly scoped exports | Named authorized audiences and patient correction workflow |
| Historical treatment | Preserve old text and its original label/version; map only approved equivalents | Mapping table including unmatched legacy values |

No option is selected by this planning PR. Do not relabel stored SexAtBirth as gender identity. Keep display wording separate from clinical inference; no inferred value from names or appearance.

## Development specification

### User Problem

The meeting did not settle how identity labels and clinically relevant sex information should be represented.

### Confirmed Requirement

The discussion is unresolved; no approved label, allowed values, requiredness or clinical decision rule can be inferred.

### Expected Behavior

A documented field-purpose and migration decision precedes UI or schema changes.

### Scope

Inventory existing values/uses, agree terminology and clinically necessary distinctions, then scope implementation.

### Out of Scope

Inferring identity from appearance/name, enforcing unapproved binary options or automatically converting existing values.

### Implementation Guidance

**Technical Recommendation:** distinguish display language, data meaning and clinical use; avoid repurposing an existing field without migration/provenance. Review intake, chart, exports and integrations together.

### Data and Migration Considerations

Any change may affect existing demographics and signed records; preserve original historical values and define explicit mappings only after approval.

### Security and Privacy Considerations

**Technical Recommendation:** collect only approved necessary information, restrict access and avoid sensitive values in analytics.

### Acceptance Criteria

- [ ] **AC1:** Given existing field values and consuming workflows, when the decision is reviewed, then each field has an approved purpose, label, options and requiredness.
- [ ] **AC2:** Given a legacy value without an approved mapping, when migration is rehearsed, then it is preserved or flagged for review rather than guessed.
- [ ] **AC3:** Given historical signed documentation, when labels change, then its recorded content is not rewritten.

### Rollout and Regression Risks

Incorrect semantic migration can misrepresent patient data. No label-only change before purpose is agreed.

## Verification performed

| Command/check | Result |
| --- | --- |
| Source inventory: BasicInfoCard, DemographicsStep, ReviewStep, IntakeWizardPage, IntakeResponseDraft, IntakeService, Patient/PatientResponse, PDF/sync direct references | PASS current source paths and five displayed values documented; no production values or external-system behavior claimed. |
| python3 workflow.py check 34; git diff --check | PASS traceability, links, sensitive-path and whitespace checks. |

## Remaining acceptance work

All semantic implementation and AC1–AC3 await the named field-purpose decision. After approval, add versioned definitions/DTO validation, preserve unknown historical strings, rehearse synthetic mappings and verify submitted/signed historical content is unchanged. Recheck patient, intake, PDF and integration consumers together.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
