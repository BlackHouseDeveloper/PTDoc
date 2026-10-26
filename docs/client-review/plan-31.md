# Plan PR 31: Implement the approved discharge questionnaire and recommendation choices

**Planning only — this PR does not implement or fix the feature.**

- Requirements: R081, R082, R094
- Original plan status: Blocked by Dependency
- Branch: `codex/plan-31-discharge-content`
- Base: `main`
- Expected GitHub number: #185; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-31)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Inspected current DischargeSubjectiveSection, DischargePlanSection, WorkspaceDischargeSubjectiveV2 and WorkspacePlanV2. GoalsMetStatus, RemainingDifficulty and nullable PercentImproved already persist; pain uses the shared Subjective model. PrimaryDischargeReason is shared clinician input exposed in both Subjective and Plan; it must remain one stored field. Recommendations and post-discharge instructions remain narrative strings; no approved structured recommendation vocabulary is present. The two directly supplied outcome folders hash-match all 16 PDFs from the original archives and contain no discharge workflow document. Existing GenerateFullNote/GenerateClinicalSummary handlers only invoke change notification; Plan 32 owns that independent wiring fix.

## Dependencies, blockers and owners

- January/clinical owner: supply the exact referenced discharge questionnaire revision and approve patient-versus-clinician ownership for every question.
- January/clinical owner: approve recommendation choice IDs, labels, requiredness, allowed additional text and retirement behavior.
- Engineering: Plan 08 must establish typed encounter-linked Discharge intake and projection before patient-to-note acceptance.

## Decisions and options

Proposed mapping, pending source review:

| Existing evidence | Owner | Proposed compatibility treatment |
| --- | --- | --- |
| GoalsMetStatus, RemainingDifficulty, PercentImproved | Patient report, clinician reviewed | Preserve original response and intake ID/version; never translate patient report into clinician goal attainment. |
| Shared numeric pain values | Patient report, clinician reviewed | Preserve documented zero versus missing and source timestamps. |
| PrimaryDischargeReason and Other explanation | Clinician | Keep the single shared Plan field; deny patient-link mutation server-side. |
| Recommendations and PostDischargeInstructions | Clinician | Retain original narrative. Add approved versioned IDs plus snapshots only after vocabulary approval. Do not infer IDs from old text. |

Choose whether new choices supplement or replace the editable narrative for future notes; historical narrative always remains visible. Obtain the exact document, mark every field owner, then review a synthetic end-to-end example before activation.

## Development specification

### User Problem

Discharge needs structured patient feedback and recommendations aligned with the client’s source document.

### Confirmed Requirement

Collect goals, pain, remaining difficulty and improvement; keep discharge reason as clinician input. Recommendations should use multi-select/dropdown choices.

### Expected Behavior

Approved discharge questions and choice lists appear in the appropriate patient/clinician surfaces without duplicate fields.

### Scope

Compare the missing source against current components, then implement only gaps and aligned intake projection.

### Out of Scope

Inventing recommendation choices or a complete questionnaire from a brief transcript paraphrase.

### Implementation Guidance

**Technical Recommendation:** use stable IDs/versioned definitions shared with intake, reuse existing percentage validation and separate patient responses from clinician decisions.

### Data and Migration Considerations

Preserve free-text historical recommendations and old questionnaire answers. New choice IDs must not be retroactively inferred from narrative text.

### Security and Privacy Considerations

**Technical Recommendation:** only authorized clinicians set discharge reason and accept clinical recommendations; patient links cannot mutate clinician-only fields.

### Acceptance Criteria

- [ ] **AC1:** Given the approved discharge document, when its mapping is reviewed, then every question is assigned to patient or clinician input without unapproved additions.
- [ ] **AC2:** Given completed patient responses, when the linked discharge note opens, then goals, pain, remaining difficulty and improvement are retained.
- [ ] **AC3:** Given clinician recommendation choices, when saving/reloading, then all selected options and allowed additional text persist.
- [ ] **AC4:** Given an old discharge note, when rendered after rollout, then its original narrative and reason remain intact.

### Rollout and Regression Risks

Missing source is a real blocker for content. Existing component inspection and contract planning can proceed.

## Verification performed

| Command/check | Result |
| --- | --- |
| Read DischargeSubjectiveSection, DischargePlanSection, WorkspaceContracts and supplied-folder SHA-256 inventory | PASS inventory: current fields mapped, duplicate surfaces share one reason field; no discharge source document among supplied outcome PDFs. |
| python3 workflow.py check 31; git diff --check | PASS requirement traceability, links, sensitive-path and whitespace checks. |

## Remaining acceptance work

AC1–AC3 are blocked on source/vocabulary approval and Plan 08. AC4 requires old-payload render/reload fixtures after the new contract exists. Plan 32 can wire existing narrative fields without claiming the missing questionnaire is implemented.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
