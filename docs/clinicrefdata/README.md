# Clinic Reference Data

This folder is the human-facing index for clinic reference material used by the Branches 1-4 runtime reference-data implementation.

- `authoring source`: the markdown file is the canonical human authoring input, mirrored into a runtime asset.
- `reference-only`: the markdown file is retained for human traceability, but the runtime asset or service named in the authority column is canonical.
- `archived`: the markdown file is historical only, is not consumed by the application, and must not remain live runtime provenance.
- `conflicting source of truth`: none. Branch 5 resolves this bucket to zero files.

| File | Status | Role | Runtime Consumed Directly | Runtime Authority |
| --- | --- | --- | --- | --- |
| `docs/clinicrefdata/ICD-10 codes.md` | `authoring source` | Canonical markdown authoring source for ICD-10 lookup coverage. | `Yes (mirrored)` | `src/PTDoc.Application/Data/WorkspaceLookupReferenceData.json` |
| `docs/clinicrefdata/Commonly used CPT codes and modifiers.md` | `authoring source` | Canonical markdown authoring source for CPT lookup coverage and modifier options. | `Yes (mirrored)` | `src/PTDoc.Application/Data/WorkspaceLookupReferenceData.json` |
| `docs/clinicrefdata/app-list-of-body-parts.md` | `reference-only` | Human reference sheet for intake body-part coverage. | `No` | `src/PTDoc.Infrastructure/ReferenceData/IntakeReferenceDataCatalogService.cs` |
| `docs/clinicrefdata/app-list-of-medications.md` | `reference-only` | Human reference sheet for intake medication coverage. | `No` | `src/PTDoc.Infrastructure/ReferenceData/IntakeReferenceDataCatalogService.cs` |
| `docs/clinicrefdata/app-pain-quality-descriptors-patient.md` | `reference-only` | Human reference sheet for intake pain-descriptor coverage. | `No` | `src/PTDoc.Infrastructure/ReferenceData/IntakeReferenceDataCatalogService.cs` |
| `docs/clinicrefdata/Comorbidities.md` | `reference-only` | Human reference sheet for intake comorbidity options. | `No` | `src/PTDoc.Application/Data/IntakeSupplementalReferenceData.json` |
| `docs/clinicrefdata/Living Situation.md` | `reference-only` | Human reference sheet for intake living-situation options. | `No` | `src/PTDoc.Application/Data/IntakeSupplementalReferenceData.json` |
| `docs/clinicrefdata/House Levels & Room Location Options.md` | `reference-only` | Human reference sheet for intake house-layout options. | `No` | `src/PTDoc.Application/Data/IntakeSupplementalReferenceData.json` |
| `docs/clinicrefdata/Assistive Devices-patient.md` | `reference-only` | Human reference sheet for intake assistive-device options. | `No` | `src/PTDoc.Application/Data/IntakeSupplementalReferenceData.json` |
| `docs/clinicrefdata/C-spine limitations_objective_Goals.md` | `reference-only` | Historical clinic worksheet for cervical functional limitations and goals. | `No` | `src/PTDoc.Application/Data/WorkspaceReferenceCatalog.json` |
| `docs/clinicrefdata/LBP limitations_object_smart goals.md` | `reference-only` | Historical clinic worksheet for lumbar functional limitations and goals. | `No` | `src/PTDoc.Application/Data/WorkspaceReferenceCatalog.json` |
| `docs/clinicrefdata/LE limitations_objectives_Goals.md` | `reference-only` | Historical clinic worksheet for lower-extremity functional limitations and goals. | `No` | `src/PTDoc.Application/Data/WorkspaceReferenceCatalog.json` |
| `docs/clinicrefdata/Pelvic Floor limitations_objectives_Goals.md` | `reference-only` | Historical clinic worksheet for pelvic-floor functional limitations and goals. | `No` | `src/PTDoc.Application/Data/WorkspaceReferenceCatalog.json` |
| `docs/clinicrefdata/List of commonly used Special test.md` | `reference-only` | Human reference sheet for workspace special-test options. | `No` | `src/PTDoc.Application/Data/WorkspaceReferenceCatalog.json` |
| `docs/clinicrefdata/List of functional outcome measures.md` | `reference-only` | Human reference sheet for supported outcome measures. | `No` | `src/PTDoc.Application/Data/OutcomeMeasureReferenceData.json` |
| `docs/clinicrefdata/Normal ROM Measurements.md` | `reference-only` | Human reference sheet for workspace ROM defaults. | `No` | `src/PTDoc.Application/Data/WorkspaceReferenceCatalog.json` |
| `docs/clinicrefdata/Muscles TTP.md` | `reference-only` | Human reference sheet for workspace tender-muscle options. | `No` | `src/PTDoc.Application/Data/WorkspaceReferenceCatalog.json` |
| `docs/clinicrefdata/Exercises.md` | `reference-only` | Human reference sheet for workspace suggested exercise options. | `No` | `src/PTDoc.Application/Data/WorkspaceReferenceCatalog.json` |
| `docs/clinicrefdata/Joint mobility and MMT.md` | `reference-only` | Human reference sheet for workspace joint-mobility and MMT grading. | `No` | `src/PTDoc.Application/Data/WorkspaceReferenceCatalog.json` |
| `docs/clinicrefdata/what-generally-was-worked-on.md` | `reference-only` | Human reference sheet for shared treatment intervention options. | `No` | `src/PTDoc.Application/Data/WorkspaceReferenceCatalog.json` |
| `docs/clinicrefdata/what-was-specifically-worked-on.md` | `reference-only` | Human reference sheet for body-part treatment focus options. | `No` | `src/PTDoc.Application/Data/WorkspaceReferenceCatalog.json` |
| `docs/clinicrefdata/archive/Exercise Table.md` | `archived` | Historical worksheet for exercise-table ideas. | `No` | `src/PTDoc.Application/Notes/Workspace/WorkspaceContracts.cs` |
| `docs/clinicrefdata/archive/Policies_and_Consent.md` | `archived` | Historical consent-form export retained for context only. | `No` | `src/PTDoc.Application/Intake/IntakeConsentPacket.cs` |
| `docs/clinicrefdata/archive/Pelvic Floor functional limitations.md` | `archived` | Historical pelvic-floor limitation worksheet replaced by the workspace asset. | `No` | `src/PTDoc.Application/Data/WorkspaceReferenceCatalog.json` |
| `docs/clinicrefdata/archive/limitations by body part.md` | `archived` | Historical upper-extremity worksheet replaced by the workspace asset. | `No` | `src/PTDoc.Application/Data/WorkspaceReferenceCatalog.json` |
