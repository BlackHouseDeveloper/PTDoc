# Section narrative release review — Plan 28 / R073

Technical routing and review are implemented. Live clinical output quality is **not approved**. The feature flag stays under the existing deployment configuration; this PR does not enable it.

## Fixed synthetic review cases

| Case | Documented facts | Required review | Reject examples |
| --- | --- | --- | --- |
| Distinct sections | S: patient reports knee discomfort on stairs. O: clinician records a measured knee movement. A: clinician records assessment and an accepted goal. P: clinician records a proposed exercise. | Four corresponding paragraphs preserve attribution, recorded dimensions and uncertainty. | Reporting the proposed exercise as already performed; moving the patient's report into objective findings. |
| Missing objective data | Patient complaint only; no measured values or performed exercises. | Objective paragraph states source facts are absent; no provider call for the empty section. | Fabricated range, strength, normal examination or performed treatment. |
| Pending suggestions | One accepted goal and one unaccepted suggested goal. | Only the accepted goal enters assessment context. | Unaccepted goal presented as a clinician decision. |
| Regeneration | An accepted summary exists; a new result arrives. | Accepted saved value remains until explicit acceptance; discard restores it; changed/signed/other-clinic source rejects result. | Automatic overwrite, cross-note response, or acceptance after source version changes. |

The clinical owner must supply/approve exact synthetic values and expected factual assertions, then review outputs for section relevance, distinctness, unsupported claims, missing qualifiers and readability. Record provider deployment/model, template version, source hash and date. A failed factual assertion blocks clinical activation even when transport tests pass. Do not add real patient data to this corpus.

## Contract and rollout

`POST /api/v1/ai/narrative` accepts note ID, expected saved timestamp and explicit purpose. The server loads the current clinic's editable draft, validates supported workspace JSON, builds allow-listed contexts, applies the existing AI rate limiter and feature flag, and rechecks saved content, signature and clinic after provider latency. Source text never comes from the client request. No note write occurs during generation.

Acceptance retains the source SHA-256/timestamp and provider/template identifiers in additive plan JSON metadata. It records the last accepted generation, including clinician-edited text; it is not a signature or access-control credential. Older client writers can omit this field, so coordinated client rollout or explicit version negotiation is required before mixed-version editing. Preserve existing accepted narratives regardless of AI availability.

Plan 32 consumes these contracts in a short stack. Plan 31 still owns the unavailable approved discharge questionnaire/full-discharge content. Reconcile new source fields from Plans 20–22/26 after those foundations merge; do not invent absent dimensions or convert planned care into completed treatment.
