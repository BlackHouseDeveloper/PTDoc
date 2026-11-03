# Plan PR 23: Establish versioned outcome definitions and validate supplied source assets

**Implemented** — only the changes and checks listed below are claimed.

- Requirements: R012, R095
- Original plan status: Ready for Development
- Branch: `codex/plan-23-outcome-contracts`
- Base: `main`
- Expected GitHub number: #177; actual: assigned by GitHub at publication
- [Authoritative plan brief](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-01-session-verification/docs/client-review/source/implementation-plan.md#pr-23)
- Inspected main: `6276027251609df64ffbbfa883f66e2d83dd57e4` (2026-10-26); this supersedes the source plan’s earlier repository snapshot.

## Current evidence and changes

Implemented stable instrument/version/item identifiers; patient/clinician administration modes; bounded numeric or option responses; explicit unanswered/N/A/unable/not-performed states; server validation; exact scorer/missing-policy provenance; and trusted manifest/fixture approval gates. Empty API composition activates no instruments. Existing aggregate registry/service and stored results remain unchanged; a legacy/manual aggregate projection has no invented item answers. Added [approval matrix](outcome-approval-matrix.md) and [source hashes](outcome-source-inventory.json). User selected all 17 named instruments for first release. All 16 files in the subsequently supplied Drive folders exactly match the ZIP hashes; no additional scoring instructions were supplied.

## Dependencies, blockers and owners

No unresolved implementation prerequisite for the bounded changes; remaining acceptance work is listed below.

## Decisions and options

First-release cohort resolved: all 17 instruments. Individual clinical owners must still approve exact versions/corrections, assignment, missing/N/A/unable rules, expected fixtures and reproduction permission. The matrix lists the exact outstanding issue per instrument; those gates do not block this technical contract.

## Development specification

### User Problem

The system has aggregate score metadata, while supplied forms vary in wording, scoring completeness and administration method.

### Confirmed Requirement

The client supplied outcome forms for digital questionnaires/scoring. The implementation must use the intended versions; anomalies and omitted scoring rules remain decisions, not permission to infer clinical algorithms.

### Expected Behavior

A versioned, reviewable definition contract supports items/options, administration mode, scoring, missing-answer validation and provenance. Only clinically approved definitions can be activated.

### Scope

Technical contract and asset-validation fixture framework; produce the approval matrix in section 8 before enabling each instrument.

### Out of Scope

Activating every supplied PDF, silently correcting HOOS/FAAM/PFDI content, or assuming a filename proves licensing/clinical approval.

### Implementation Guidance

**Technical Recommendation:** reuse OutcomeMeasureRegistry/Service boundaries; add stable instrument/version/item IDs, explicit nullable answers, scorer version and completion status. Keep source hashes and clinical sign-off separate from code review. Use approved example answer sets as fixtures.

### Data and Migration Considerations

Additive response/result/version provenance may be needed; preserve legacy aggregate results as imported/manual historical values rather than fabricated item responses.

### Security and Privacy Considerations

**Technical Recommendation:** score on the server and restrict patient links to assigned instruments; validate item IDs/ranges and avoid logging responses.

### Acceptance Criteria

- [ ] **AC1:** Given a definition without approved scoring/missing-answer rules, when activation is attempted, then it remains unavailable for clinical collection.
- [ ] **AC2:** Given an approved definition and answer fixture, when scored, then the result exactly matches the approved calculation and retains definition/scorer versions.
- [ ] **AC3:** Given an invalid or incomplete answer set, when submitted, then its status follows the approved rule rather than defaulting blanks to zero.
- [ ] **AC4:** Given an old aggregate score, when definitions are introduced, then no artificial item-level responses are created.

### Rollout and Regression Risks

Incorrect clinical scores are the central risk. Activation gates apply per instrument; no all-or-nothing release of unapproved content.

## Verification performed

| Command/check | Result |
| --- | --- |
| dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter 'FullyQualifiedName~VersionedOutcomeRegistryTests&#124;FullyQualifiedName~OutcomeMeasureRegistryTests&#124;FullyQualifiedName~OutcomeMeasureServiceTests' --verbosity minimal | 100 passed, 0 failed, 0 skipped; includes new manifest/scorer/answer gates and existing aggregate compatibility. |
| SHA-256 compare 16 supplied Drive files with 16 ZIP members; validate source inventory and 17-instrument matrix | All 16 byte hashes match; inventory has unique filenames and 64-character hashes; all M01–M17 recorded with individual activation gates. |
| python3 workflow.py check 23; git diff --check | Requirement IDs, required sections, local links, sensitive-path and whitespace checks pass. |

## Remaining acceptance work

The bounded technical contract AC1–AC4 passes synthetic and legacy regression checks. Real instrument activation, patient assignment/persistence and clinician administration remain separate Plans 24/36; projection requires Plan 25. Synthetic framework fixtures are not clinical approval. No UI or schema migration is introduced.

## Publication and merge boundary

No merges, deployments, production-data changes, external messages or review-thread replies/resolutions are authorized. A planning-only PR may be reviewed as documentation; merging it does not complete the feature. Shared subject matter alone does not create a Git stack dependency.
