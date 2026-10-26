# Plan PR 29: Reproduce and repair goal-generation loss and candidate exhaustion

**Partially implemented** — only the changes and checks listed below are claimed.

- Requirements: R070, R071
- Original plan status: Verification Required
- Branch: `codex/plan-29-goal-generation-evidence`
- Base: `main`
- Expected GitHub number: #183; actual: [#186](https://github.com/BlackHouseDeveloper/PTDoc/pull/186)
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-29)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Baseline main 6276027: the new synthetic component reproducer fails 3 of 4 cases. Duplicate-only success, blank success and transient-failure-then-success retry remove the existing pending candidate. The new-ninth-candidate case already passes, showing no UI eight-goal cap in this path. After the fix all four cases pass within 72 focused regressions. Accepted goal IDs/count and mapped save/reload state remain intact; generated candidates stay unaccepted and excluded from persisted accepted goals until explicit acceptance. No provider quota, prompt template, short/long-term behavior or clinical scoring is changed. CI follow-up corrected initializer whitespace using the owning-project formatter. The C# diff changes only initializer spacing/line breaks, confirmed by whitespace-normalized source comparison; feature behavior and prior passing feature tests are unchanged.

## Dependencies, blockers and owners

- Clinical owner (January): anonymized good/bad outputs and approved quality rubric for R070 are still absent.
- Engineering/client: the exact meeting build/provider response and whether eight goals were saved versus merely selected remain unavailable. The reproduced pending-loss cause is fixed; provider-specific eight-goal exhaustion is not established.

## Decisions and options

Do not raise quotas or change goal clinical templates without evidence. Obtain source build, saved/selected goal count, response shape and approved anonymized quality examples. Do not revisit the withdrawn short-versus-long-term redesign.

## Development specification

### User Problem

Goal suggestions reportedly disappear, degrade in quality and stop producing new options after eight accepted goals.

### Confirmed Requirement

Investigate the observed loss/exhaustion and quality regression. The short-versus-long-term concern was withdrawn and does not authorize redesign.

### Expected Behavior

Accepted goals remain intact and new requests have a clear result, error or documented limit; quality meets an approved clinical rubric.

### Scope

Reproduce state transitions and inspect request/response/filtering; repair demonstrated causes and evaluate quality separately.

### Out of Scope

Assuming an eight-item constant exists, increasing quotas blindly or rewriting all goal templates.

### Implementation Guidance

**Technical Recommendation:** trace candidate IDs, accepted/rejected states and generation pagination/filtering with non-PHI diagnostics. Preserve accepted goals during retries; evaluate clinical specificity/measurability against approved examples.

### Data and Migration Considerations

No schema change assumed. If goal IDs/state persist incorrectly, use compatible repair and never discard accepted historical goals.

### Security and Privacy Considerations

**Technical Recommendation:** maintain clinician acceptance, note ownership and bounded generation; do not log clinical prompt bodies.

### Acceptance Criteria

- [ ] **AC1:** Given eight accepted goals and a valid new request, when generation completes, then accepted goals remain and the UI shows new candidates or an explicit supported constraint/error.
- [ ] **AC2:** Given a transient generation failure, when retrying, then accepted and pending goal state is not silently lost.
- [ ] **AC3:** Given the approved synthetic quality corpus, when evaluated, then suggestions meet the recorded rubric without invented patient facts.

### Rollout and Regression Risks

Blindly raising limits may increase cost without fixing loss; diagnose before coding.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~GoalRegenerationTests&#124;FullyQualifiedName~AssessmentWorkspaceSectionPrognosisTests&#124;FullyQualifiedName~NoteWorkspacePayloadMapperTests&#124;FullyQualifiedName~ClinicalGenerationServiceTests' --verbosity minimal | 72 passed, 0 failed, 0 skipped. The baseline reproducer previously failed 3 of its 4 cases; all four now pass. |
| python3 workflow.py check 29; git diff --check | Traceability, local links and whitespace pass. |
| dotnet format tests/PTDoc.Tests/PTDoc.Tests.csproj --verify-no-changes --verbosity minimal; task-local whitespace-normalized source comparison | PASS: complete owning-project CI formatter; whitespace-only C# correction. Existing feature-test results remain applicable. |

## Remaining acceptance work

Draft remains partial: R071 pending-candidate loss is reproduced and repaired; the specific eight-goal exhaustion report and R070 clinical quality regression still require recorded provider output/build and clinical review. Live-provider/browser generation was not enabled; deterministic real-component tests exercise the observed loss. Pending suggestions remain session review state, not automatically accepted saved goals.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
