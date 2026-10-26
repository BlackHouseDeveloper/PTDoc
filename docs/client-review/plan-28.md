# Plan PR 28: Generate distinct SOAP summaries from their corresponding sections

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R073
- Original plan status: Ready for Development
- Branch: `codex/plan-28-soap-generation`
- Base: `main`
- Expected GitHub number: #182; actual: [#185](https://github.com/BlackHouseDeveloper/PTDoc/pull/185)
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-28)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

The prior PlanTab summary, discharge and follow-up operations used one plan-of-care generator. They now use explicit SOAP-summary, discharge-recommendation and next-visit purposes; SOAP summary requests generate four separately scoped paragraphs. The Review page uses the same saved-source contract. Accepted text stays unchanged until acceptance; discard restores it. Dirty/unsaved/signed contexts cannot generate or accept stale results. Cancellation and source-context changes discard late responses. Provider failures expose a safe recoverable message. Source hash, saved timestamp, template versions and model identifiers are retained in accepted narrative metadata through the JSON/UI mapper. This is implemented technical behavior; live clinical prose quality is not claimed. [Release-quality review](ai-narrative-quality.md) remains a clinical release gate.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

No new provider/model or clinical policy is selected. Missing section facts are reported explicitly without a model call. Clinical owner approval of output examples remains open; it does not block the bounded technical contract.

## Development specification

### User Problem

Generated summaries are repetitive and do not clearly reflect their corresponding S/O/A/P sections.

### Confirmed Requirement

Produce distinct Subjective, Objective, Assessment and Plan paragraphs from the appropriate corresponding content.

### Expected Behavior

Each summary operation has an explicit section purpose and context, and output remains a reviewable suggestion.

### Scope

Section-specific request/context/output contracts and UI acceptance, preserving existing generation infrastructure.

### Out of Scope

New model/vendor selection, automatic signing or invented clinical facts to fill missing sections.

### Implementation Guidance

**Technical Recommendation:** differentiate operation purpose server-side and restrict context to relevant authorized note data. Use deterministic contract tests plus a clinician-reviewed fixed synthetic corpus for quality evaluation; do not test only exact prose strings.

### Data and Migration Considerations

Preserve accepted text, suggestion state and provenance. A regeneration must not overwrite accepted content without explicit user action.

### Security and Privacy Considerations

**Technical Recommendation:** enforce authorization, generation rate limits and explicit clinician acceptance; exclude unrelated patient data from prompts/logs.

### Acceptance Criteria

- [ ] **AC1:** Given distinct S/O/A/P source content, when each summary is generated, then its request and displayed output correspond to that section.
- [ ] **AC2:** Given missing source facts, when generating, then no fabricated measurements, diagnoses or completed interventions are introduced.
- [ ] **AC3:** Given accepted text and a new suggestion, when generation completes, then accepted text remains until the clinician explicitly replaces it.
- [ ] **AC4:** Given disabled AI or provider failure, when generation is requested, then a recoverable state appears and manual editing remains usable.

### Rollout and Regression Risks

Clinical hallucination and accepted-text overwrite; staged feature-flag rollout with review.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~NoteNarrative&#124;FullyQualifiedName~PlanTabTests&#124;FullyQualifiedName~EditableNarrativeBoxTests&#124;FullyQualifiedName~NoteWorkspacePayloadMapperTests&#124;FullyQualifiedName~AiEndpointErrorContractIntegrationTests' --verbosity minimal | 100 passed, 0 failed, 0 skipped; API/Web compile. Synthetic saved-note permissions, clinic isolation, pre/post-provider version/signature checks, prompt isolation, failures, review and provenance. |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --no-restore --filter 'FullyQualifiedName~PlanTabTests&#124;FullyQualifiedName~EditableNarrativeBoxTests' --verbosity minimal | 30 passed, 0 failed, 0 skipped after adding editor-identity/source-change regression; 101 distinct focused tests total. |
| Task-local Playwright CLI: disabled AI, manual typing/focus, 390/768/1440 layouts and mobile screenshots | Passed with synthetic fixture; no external model call. Disabled-generation error recoverable, manual text editable, typing focus retained and no horizontal overflow. |
| python3 workflow.py check 28; git diff --check | Traceability, document links and whitespace checks pass. |

## Remaining acceptance work

Clinical owner must approve the fixed synthetic examples/rubric and evaluate live-provider relevance, distinctness and unsupported facts before enabling release generation. Mocked transport validates routing/prompt boundaries, not model clinical quality. Full screen-reader and MAUI device publishing remain unverified. Plan 32 consumes this contract; unsupported full-discharge content still depends on Plan 31.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
