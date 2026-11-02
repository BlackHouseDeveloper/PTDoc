# Computed outcome note association — Plan 25

Planning only. [Patient Plan 24 #181](https://github.com/BlackHouseDeveloper/PTDoc/pull/181), clinician Plan 36 and encounter routing Plan 08 must provide completed contracts before this feature is implemented. [Plan 23 #180](https://github.com/BlackHouseDeveloper/PTDoc/pull/180) is the common versioned definition foundation. No computed result UI, association or migration ships here.

## Association and display specification

A computed result carries a stable result ID, clinic/patient identity, originating intake or clinician administration, encounter association, completion timestamp, administration mode, definition/scorer versions and source hashes. A note explicitly links result IDs from its own encounter. Querying the latest patient score is insufficient: it could belong to another encounter, a different definition, an incomplete assessment or a later correction.

The server must verify every linked result and note share clinic and patient, verify the intended encounter and caller permission, and reject foreign or unavailable IDs. The patient does not select a note/clinic through a free-form result request. Link writes require optimistic concurrency and draft-note permission; signing freezes association and display provenance.

For evaluation, progress and discharge, display completed computed results read-only with instrument name/version, domains/total, units, completion time and source (patient questionnaire or clinician administration). Show incomplete/unable status without a complete score. Keep manual historical aggregates visibly separate. Do not offer new daily outcome collection or turn a current result into an editable numeric field.

## Current and prior decision table

| Situation | Invariant / pending decision |
| --- | --- |
| Multiple patient results | Explicit encounter link identifies current; array order never does |
| Later questionnaire after note signing | Signed snapshot stays unchanged |
| Different instrument version | Preserve both; no automatic rescore or implied comparable trend |
| Equal completion timestamps | Stable result identity breaks ordering; clinical choice of which prior to show remains pending |
| Corrected result | Retain original and attribution; proposed superseding result needs approved correction workflow |
| No linked computed result | Show absence; do not borrow a score from another encounter |
| Legacy/manual score | Preserve value/source with unknown definition/scorer, no fabricated item answers |
| Clinician versus patient result | Distinguish administration and retain original context |

January/clinical owner must choose the prior-result display and correction workflow before those behaviors are implemented. No decision here authorizes overwriting submitted intake or signed records.

## Signing/export transaction

Validate links while saving/signing using existing note concurrency and signature rules. Snapshot the necessary display values, units, completion/source/version/hash metadata and stable result IDs alongside the signed note evidence. PDF/export reads the signed snapshot, not a mutable live registry. Later source-definition changes must not change an existing signed export. Keep item responses in their authorized result store; do not expose more response detail than the permitted note view requires.

## Verification and merge checkpoint

Test two encounters with distinct values, reversed storage order, different versions, equal timestamps, manual aggregates, incomplete responses, a later correction, signed snapshot/hash stability, cross-clinic IDs and stale link updates. Export tests must use the same frozen evidence as the signed UI. Patient/clinician fixtures remain synthetic until exact instruments are approved.

Plan 24 and Plan 36 are independent consumers of Plan 23. Merge/reconcile their approved result contracts and Plan 08 routing onto a common base before implementing this convergence PR. Do not build a multi-parent stack or copy either foundation's commits. This documentation targets main and remains draft; a documentation merge does not implement result projection.
