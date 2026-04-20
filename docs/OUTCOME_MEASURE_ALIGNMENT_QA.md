# Outcome Measure Alignment QA Fixtures

Use these non-production fixtures to complete live signoff for outcome-measure alignment after the registry-driven `QuickDASH` + historical-only `VAS` rollout.

## Fixture 1: Historical `VAS` note

Use this fixture to verify that historical-only `VAS` rows still render in outcome history while remaining unavailable for new entry.

### Identifiers

- `PatientId`: `5f2d7a29-3c5f-4a0c-9c6b-2d45c8f78a31`
- `NoteId`: `8d0c3d47-5e33-46a4-9c6d-6d6c01d7e8f4`
- `OutcomeMeasureResultId`: `2b80b173-53b2-4d92-b017-6cfa52aca5c1`
- `NoteType`: `ProgressNote`
- `MeasureType`: `VAS`
- `Score`: `5`
- `RecordedAtUtc`: `2026-04-05T12:00:00Z`

### Expected UI state

- The outcome history table shows the recorded `VAS` row.
- The conditional legacy-history helper copy appears because a historical-only measure is present.
- The new-entry outcome dropdown does **not** offer `VAS`.

### Provisioning requirements

- Provision this fixture in a non-production environment only.
- Do **not** create it through the normal therapist UI. The current app correctly blocks new `VAS` entry.
- Local development environments that run the default `DatabaseSeeder` now provision this fixture automatically.
- Source the fixture from either:
  - a migrated legacy note, or
  - a direct non-prod DB/admin seed step that creates the note plus one persisted `OutcomeMeasureResult` row with the identifiers above.
- Ensure the patient and note are visible to the therapist QA account used for signoff.

## Fixture 2: Submitted shoulder intake prefill

Use this fixture to verify the positive live “Suggested from Intake” chip path for a shoulder evaluation. The expected suggestion set must come from:

`StructuredData.BodyPartSelections -> intake body-part mapper -> registry recommendations`

Do **not** make this fixture pass by manually stuffing recommendation strings into the stored draft.

### Identifiers

- `PatientId`: `c4d1f4e9-f5a5-4ccb-b92a-4c1a1a6ce7d2`
- `IntakeFormId`: `6bdb789b-a4c5-41c2-9a4b-bf6173f0d4c8`

### Required intake state

- `SubmittedAt` must be non-null.
- `IsLocked` may be `true` or `false`, but the row must satisfy the evaluation-seed query: `(IsLocked || SubmittedAt.HasValue)`.
- This intake must be the **latest qualifying intake** for the patient by `SubmittedAt ?? LastModifiedUtc`.
- No newer evaluation note may exist for the patient after that intake reference time, or evaluation seeding may be suppressed.
- `SelectedBodyRegion` must be `RightShoulderFront`.

### Required structured payload

`StructuredDataJson` must contain the canonical shoulder selection:

```json
{
  "schemaVersion": "2026-03-30",
  "bodyPartSelections": [
    {
      "bodyPartId": "shoulder",
      "lateralities": ["right"],
      "digitIds": []
    }
  ]
}
```

### `ResponseJson` rules

- `recommendedOutcomeMeasures` must be omitted or left empty.
- `structuredData` must either:
  - match `StructuredDataJson`, or
  - be omitted entirely.
- The goal is to prove the live seeded recommendation set is rebuilt from the structured body-part selection, not copied from an old stored recommendation list.

### Expected live seeded set

- `DASH`
- `QuickDASH`
- `PSFS`
- `NPRS`

No `VAS`, `VAS/NPRS`, `NPRS/VAS`, or unsupported measures should appear.

## Direct DB/admin provisioning requirements

Preferred path: use an existing admin/service provisioning flow that creates normal intake rows.

For local development environments, running the default `DatabaseSeeder` now provisions both fixtures automatically. Shared or hosted non-prod environments still need an equivalent admin/service or direct DB provisioning step unless they also run that seed path.

If environment owners provision the shoulder fixture directly, the minimum persisted `IntakeForm` fields must include:

- `Id`
- `PatientId`
- `TemplateVersion = "1.0"`
- `AccessToken` with a non-empty placeholder/hash value
- `ResponseJson` with a valid serialized `IntakeResponseDraft`
- `StructuredDataJson` with the shoulder structured-data payload above
- `PainMapData` populated consistently for the same shoulder selection
- `Consents` populated with valid JSON
- `SubmittedAt` populated
- `IsLocked` populated
- `LastModifiedUtc` populated
- `ModifiedByUserId` populated
- `ClinicId` populated so the intake is visible to the therapist tenant
- `SyncState` populated

The paired patient record must already exist and be visible to the same clinic/tenant.

## Manual signoff checklist

### Historical `VAS` fixture

1. Authenticate as a therapist and open patient `5f2d7a29-3c5f-4a0c-9c6b-2d45c8f78a31`.
2. Open note `8d0c3d47-5e33-46a4-9c6d-6d6c01d7e8f4`.
3. Verify the recorded history table shows `VAS` with the persisted score.
4. Verify the panel shows the legacy-history helper copy explaining that legacy measures may remain visible while new scores use the current selectable set.
5. Verify the measure dropdown does not contain `VAS`.

### Submitted shoulder intake fixture

1. Authenticate as a therapist and open patient `c4d1f4e9-f5a5-4ccb-b92a-4c1a1a6ce7d2`.
2. Start a new evaluation flow that uses the latest submitted/locked intake prefill.
3. Verify positive “Suggested from Intake” chips render.
4. Verify the suggestion set is exactly `DASH`, `QuickDASH`, `PSFS`, and `NPRS`.
5. Verify no `VAS`, `VAS/NPRS`, `NPRS/VAS`, or unsupported measures appear in the suggestion chips or dropdowns.

### Negative validation note

- Treat any attempted new-`VAS` submission as a stale/tampered payload validation check, not as a normal therapist workflow.

## Ownership

- QA or environment owners must provision or restore these fixtures before live signoff.
- Local development environments that run the default `DatabaseSeeder` now get both fixtures automatically.
- Shared or hosted non-prod environments must still provision or restore equivalent fixture rows before live signoff if they do not run that seed path.
