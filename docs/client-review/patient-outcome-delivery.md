# Patient outcome delivery contract — Plan 24

Planning only. All 13 patient instruments M01–M13 are selected for first release; none has been activated by this document. The four clinician measures are excluded from patient collection. [Plan 23 #180](https://github.com/BlackHouseDeveloper/PTDoc/pull/180) supplies the shared technical foundation and [instrument approval matrix](https://github.com/BlackHouseDeveloper/PTDoc/blob/codex/plan-23-outcome-contracts/docs/client-review/outcome-approval-matrix.md).

## Proposed request and persistence flow

1. A trusted assignment binds clinic, patient, intake/encounter, note type, instrument ID and exact definition version. The client cannot choose a different patient/version by posting an ID. The clinical assignment table remains unapproved; runtime recommendations are compatibility evidence, not approval for all new forms.
2. A patient invite resolves its own authorized assignment. Return only active patient-reported definitions and existing responses for that assignment. Do not return clinician-only tasks or another patient's history. Keep the existing invite expiry/revocation/submission protections.
3. Save partial item responses with optimistic version checks and draft completion state. Distinguish blank, explicit zero, N/A and unable; expose only states supported by the approved item. Resume the same definition version and retain previous answers. A release must not replace a draft definition silently.
4. Submit answers and expected draft version, never a trusted client total. Revalidate ownership, assignment, active definition and intake state server-side; use the exact approved scorer. Reject altered instrument/version, unknown items, invalid options/ranges, duplicate items and client-supplied computed results. Return field-level safe error codes without logging responses.
5. Persist immutable completed item evidence and calculation/provenance together. Incomplete responses retain their state and have no fabricated numeric result. Use idempotent submission so retries do not duplicate results. Submitted intake evidence stays immutable; corrections require the approved attributable workflow.
6. Preserve historical Previous Outcome Measure or Functional Score reports as manual patient-reported history. Do not relabel them as computed or delete them when replacing the new-collection experience.

## User interface specification

Render one approved definition using associated radio/choice labels, fieldsets and legends; bounded numeric fields only for approved numeric item responses, never a total-score input. Provide visible answered/remaining progress, explicit optionality/N/A instructions, saved/resume feedback and accessible error summary links. Use existing UI tokens and shared intake navigation. Do not ship disabled mock instruments that imply clinical availability.

## Acceptance matrix

| Case | Required result |
| --- | --- |
| Complete approved fixture | Exact server-derived total/domains and retained definition/scorer versions |
| Missing/N/A/unable | Exact approved rule; no zero coercion or unapproved denominator |
| Save, close, resume | Same assignment/version and answers; stale write does not overwrite newer answers |
| Tampered total/item/version | Reject or disregard untrusted total; reject invalid answer or assignment |
| Foreign patient/clinic/invite | No read/write; no response content in errors/logs |
| Clinician definition requested via patient link | Omitted from catalog and denied on submission |
| Signed/submitted evidence | No silent rewrite; new version does not rescore history |
| Keyboard and 390/768/1440 layouts | Logical order, labelled controls, readable errors and no horizontal overflow |

## Merge and activation checkpoint

This documentation branch targets main because it consumes no unmerged code. When implementation begins before #180 merges, use a short Plan 23 → Plan 24 code stack. Plan 36 independently consumes Plan 23; do not stack it above patient forms. Plan 25 waits for reconciled consumer contracts and intake routing. A documentation merge does not satisfy any clinical activation gate.
