# PTDoc Role → Capability Verification Map (Sprint UC-Omega)

**Effective:** Sprint UC-Omega  
**Status:** Binding enforcement verified — every "must-catch" row maps to an automated test  
**Purpose:** Documents the minimum role → capability matrix required by the PFPT spec,
the enforcement layer for each capability, and the test(s) that prove enforcement is active.

---

## How to Read This Document

Each row records:

| Column | Meaning |
|---|---|
| **Capability** | The action or data-access operation |
| **Role** | The actor attempting the operation |
| **Decision** | Allow / Deny / Conditional |
| **Enforcement Layer** | Where the enforcement lives (API policy, service guard, UI only) |
| **Test Reference** | The test(s) that prove the decision is enforced server-side |

> **Governance rule:** "UI only" enforcement is noncompliant for a HIPAA-conscious system.
> Every row in this table that says `Deny` **must** have a corresponding passing test at the
> API layer. If a test is missing, the row is tagged ❌ and treated as a blocker.

---

## Core Role Definitions

| Role Constant | Description |
|---|---|
| `PT` | Physical Therapist — primary clinician |
| `PTA` | Physical Therapist Assistant — supervised clinician |
| `Billing` | Billing staff — read-only clinical access, billing workflow |
| `Owner` | Practice owner — read-only operational view |
| `Patient` | Patient self-service access only |
| `FrontDesk` | Administrative / scheduling staff |
| `Aide` | Support staff — limited access |
| `Admin` | Internal administrative role (not a PFPT spec role — used internally) |

---

## Patient Demographics

| Capability | Role | Decision | Enforcement Layer | Test Reference |
|---|---|---|---|---|
| View patient list/profile | PT | Allow | API policy (`PatientRead`) | `AuthorizationCoverageTests` |
| View patient list/profile | PTA | Allow (as assigned) | API policy | `AuthorizationCoverageTests` |
| View patient list/profile | Billing | Allow (read-only) | API policy | `AuthorizationCoverageTests` |
| View patient list/profile | Owner | Allow (read-only) | API policy | `AuthorizationCoverageTests` |
| View patient list/profile | Patient | Deny (except self-portal) | API policy | `AuthorizationCoverageTests` |
| View patient list/profile | FrontDesk | Allow (admin scope) | API policy | `AuthorizationCoverageTests` |
| View patient list/profile | Aide | Allow (limited) | API policy | `AuthorizationCoverageTests` |
| Edit patient demographics | PT | Allow | API policy (`PatientWrite`) | `AuthorizationCoverageTests` |
| Edit patient demographics | PTA | Allow | API policy (`PatientWrite`) | `AuthorizationCoverageTests` |
| Edit patient demographics | Billing | Deny | API policy + service | `EndToEndWorkflowTests.Billing_Cannot_Edit_Patient_Demographics_Returns_403` ✅ |
| Edit patient demographics | Owner | Deny | API policy + service | `EndToEndWorkflowTests.Owner_Cannot_Edit_Patient_Demographics_Returns_403` ✅ |
| Edit patient demographics | FrontDesk | Deny | API policy | `RbacRoleMatrixTests.PatientWrite_NonWriterRoles_AreNotAuthorized` |
| Edit patient demographics | Aide | Deny | API policy | `RbacRoleMatrixTests.PatientWrite_NonWriterRoles_AreNotAuthorized` |
| Edit patient demographics | Patient | Deny | API policy | `RbacRoleMatrixTests.PatientWrite_NonWriterRoles_AreNotAuthorized` |

---

## Intake Workflow

| Capability | Role | Decision | Enforcement Layer | Test Reference |
|---|---|---|---|---|
| Start intake invite | PT | Allow | API policy (`IntakeWrite`) | `AuthorizationCoverageTests` |
| Start intake invite | PTA | Allow (if permitted by policy) | API policy | `AuthorizationCoverageTests` |
| Start intake invite | Billing | Deny | API policy | `EndToEndWorkflowTests.Intake_Billing_Cannot_Create_Returns_403` ✅ |
| Start intake invite | Owner | Deny | API policy | `RbacRoleMatrixTests.IntakeWrite_Owner_IsNotAuthorized` |
| Start intake invite | FrontDesk | Allow | API policy | `EndToEndWorkflowTests.Intake_Workflow_FrontDesk_Creates_PT_Reviews` ✅ |
| Start intake invite | Aide | Deny | API policy | `RbacRoleMatrixTests.IntakeWrite_NonWriterRoles_AreNotAuthorized` |
| Fill intake | Patient | Allow (input only) | API policy (`IntakeRead`) + service | `IntakeEndpointsTests` |
| Fill intake | FrontDesk | Deny (except admin resend) | API policy | `RbacRoleMatrixTests / RbacHttpSmokeTests` |
| Review intake | PT | Allow | API policy (`ClinicalStaff`) | `EndToEndWorkflowTests.Intake_Workflow_FrontDesk_Creates_PT_Reviews` ✅ |
| Review intake | Billing | Deny | API policy | `RbacRoleMatrixTests / RbacHttpSmokeTests` |

---

## Clinical Note Authoring

| Capability | Role | Decision | Enforcement Layer | Test Reference |
|---|---|---|---|---|
| Create Eval note | PT | Allow | API policy (`NoteWrite`) | `EndToEndWorkflowTests.PT_Creates_Note_Then_Signs_Successfully` ✅ |
| Create Eval note | PTA | Deny | API policy + domain guard | `EndToEndWorkflowTests.PTA_Cannot_Create_EvalNote_Returns_403` ✅ |
| Create Eval note | Billing | Deny | API policy | `EndToEndWorkflowTests.Billing_Cannot_Write_Notes_Returns_403` ✅ |
| Create Eval note | Owner | Deny | API policy | `EndToEndWorkflowTests.Owner_Cannot_Write_Notes_Returns_403` ✅ |
| Create Eval note | FrontDesk | Deny | API policy | `EndToEndWorkflowTests.FrontDesk_Cannot_Write_Notes_Returns_403` ✅ |
| Create Eval note | Aide | Deny | API policy | `EndToEndWorkflowTests.Aide_Cannot_Write_Notes_Returns_403` ✅ |
| Create Daily note | PT | Allow | API policy (`NoteWrite`) | `EndToEndWorkflowTests.PT_Creates_DailyNote_Returns_201_With_NoteId` ✅ |
| Create Daily note | PTA | Allow | API policy (`NoteWrite`) | `EndToEndWorkflowTests.PTA_Can_Create_DailyNote_Returns_201` ✅ |
| Create Daily note | Billing | Deny | API policy | `EndToEndWorkflowTests.Billing_Cannot_Write_Notes_Returns_403` ✅ |
| Create Daily note | Owner | Deny | API policy | `EndToEndWorkflowTests.Owner_Cannot_Write_Notes_Returns_403` ✅ |
| Create Daily note | FrontDesk | Deny | API policy | `EndToEndWorkflowTests.FrontDesk_Cannot_Write_Notes_Returns_403` ✅ |
| Create Daily note | Aide | Deny | API policy | `EndToEndWorkflowTests.Aide_Cannot_Write_Notes_Returns_403` ✅ |

---

## Signature and Co-Sign

| Capability | Role | Decision | Enforcement Layer | Test Reference |
|---|---|---|---|---|
| Sign Eval/PN/DC note | PT | Allow | API policy (`NoteWrite`) | `EndToEndWorkflowTests.PT_Creates_Note_Then_Signs_Successfully` ✅ |
| Sign Eval/PN/DC note | PTA | Deny | API policy | `RbacHttpSmokeTests.PTA_Cannot_Create_EvalNote_Returns_403` |
| Sign Eval/PN/DC note | Billing | Deny | API policy | `EndToEndWorkflowTests.Billing_Cannot_Sign_Note_Returns_403` ✅ |
| Sign Daily note | PT | Allow | API policy (`NoteWrite`) | `EndToEndWorkflowTests.PT_Creates_Note_Then_Signs_Successfully` ✅ |
| Sign Daily note | PTA | Allow → Pending Co-sign | API policy (`NoteWrite`) + service guard | `SignatureServiceTests.SignNote_PtaDailyNote_CreatesLegalSignatureWithoutFinalizingNote` |
| PT co-sign PTA Daily | PT | Allow | API policy (`NoteCoSign`) | `AuthorizationCoverageTests` |
| PT co-sign PTA Daily | PTA | Deny | API policy | `EndToEndWorkflowTests.PTA_Cannot_CoSign_Note_Returns_403` ✅ |
| PT co-sign PTA Daily | Billing | Deny | API policy | `RbacRoleMatrixTests.NoteCoSign_Roles_MatchPolicy` |
| Add addendum | PT | Allow | API policy (`NoteWrite`) | `AuthorizationCoverageTests` |
| Add addendum | Billing | Deny | API policy | `RbacRoleMatrixTests.NoteWrite_NonClinicalRoles_AreNotAuthorized` |

---

## Export

| Capability | Role | Decision | Enforcement Layer | Test Reference |
|---|---|---|---|---|
| Export PDF | PT | Allow | API policy (`NoteExport`) | `AuthorizationCoverageTests` |
| Export PDF | PTA | Allow (if permitted) | API policy | `AuthorizationCoverageTests` |
| Export PDF | Billing | Deny | API policy (`NoteExport`) | `EndToEndWorkflowTests.Billing_Cannot_Export_Note_Returns_403` ✅ |
| Export PDF | Owner | Deny | API policy | `EndToEndWorkflowTests.Owner_Cannot_Export_Note_Returns_403` ✅ |
| Export PDF | Patient | Deny | API policy | `RbacRoleMatrixTests.NoteExport_Roles_MatchPolicy` |
| Export PDF | FrontDesk | Deny | API policy | `RbacRoleMatrixTests.NoteExport_Roles_MatchPolicy` |
| Export PDF | Aide | Deny | API policy | `RbacRoleMatrixTests.NoteExport_Roles_MatchPolicy` |

---

## Sync

| Capability | Role | Decision | Enforcement Layer | Test Reference |
|---|---|---|---|---|
| Run/trigger sync | PT | Allow | API policy (`ClinicalStaff`) | `EndToEndWorkflowTests.PT_Can_Access_Sync_Status_Returns_200` ✅ |
| Run/trigger sync | PTA | Allow | API policy (`ClinicalStaff`) | `AuthorizationCoverageTests` |
| Run/trigger sync | Billing | Deny | API policy (`ClinicalStaff`) | `EndToEndWorkflowTests.Billing_Cannot_Access_Sync_Returns_403` ✅ |
| Run/trigger sync | Owner | Allow | API policy (`ClinicalStaff`) | `AuthorizationCoverageTests` |
| Run/trigger sync | Patient | Deny | API policy (`ClinicalStaff`) | `EndToEndWorkflowTests.Patient_Cannot_Access_Sync_Status_Returns_403` ✅ |
| Run/trigger sync | FrontDesk | Deny | API policy (`ClinicalStaff`) | `EndToEndWorkflowTests.FrontDesk_Cannot_Access_Sync_Returns_403` ✅ |
| Run/trigger sync | Aide | Deny | API policy (`ClinicalStaff`) | `RbacRoleMatrixTests / RbacHttpSmokeTests` |
| Sync excludes clinical data | Aide/FrontDesk/Patient | Enforced by scoping | SyncEngine service | `SyncClientProtocolTests` |

---

## Audit Trail

| Capability | Role | Decision | Enforcement Layer | Test Reference |
|---|---|---|---|---|
| View audit trail | PT | Allow (as permitted) | API policy (`AuditRead`) | `AuthorizationCoverageTests` |
| View audit trail | Billing | Deny | API policy | `RbacRoleMatrixTests / RbacHttpSmokeTests` |
| View audit trail | Owner | Allow (read-only) | API policy | `AuthorizationCoverageTests` |
| View audit trail | Patient | Deny | API policy | `RbacRoleMatrixTests / RbacHttpSmokeTests` |

---

## Must-Catch List (Codex Audit Blockers)

The following scenarios are explicitly called out as "must-catch" in the problem statement.
Each row must have a passing test — any gap here is a ship/no-ship blocker.

| Scenario | Test | Status |
|---|---|---|
| PTA accessing eval endpoints | `EndToEndWorkflowTests.PTA_Cannot_Create_EvalNote_Returns_403` | ✅ Passing |
| Billing editing notes | `EndToEndWorkflowTests.Billing_Cannot_Write_Notes_Returns_403` | ✅ Passing |
| Billing editing patient demographics | `EndToEndWorkflowTests.Billing_Cannot_Edit_Patient_Demographics_Returns_403` | ✅ Passing |
| Owner modifying data | `EndToEndWorkflowTests.Owner_Cannot_Write_Notes_Returns_403` | ✅ Passing |
| Owner modifying demographics | `EndToEndWorkflowTests.Owner_Cannot_Edit_Patient_Demographics_Returns_403` | ✅ Passing |
| Unsecured endpoints accessible without auth | `EndToEndWorkflowTests.Unauthenticated_Request_Returns_401` | ✅ Passing |
| FrontDesk writing clinical notes | `EndToEndWorkflowTests.FrontDesk_Cannot_Write_Notes_Returns_403` | ✅ Passing |
| Aide writing clinical notes | `EndToEndWorkflowTests.Aide_Cannot_Write_Notes_Returns_403` | ✅ Passing |
| PTA co-signing (PT-only operation) | `EndToEndWorkflowTests.PTA_Cannot_CoSign_Note_Returns_403` | ✅ Passing |
| Billing signing notes | `EndToEndWorkflowTests.Billing_Cannot_Sign_Note_Returns_403` | ✅ Passing |
| Billing exporting PDFs | `EndToEndWorkflowTests.Billing_Cannot_Export_Note_Returns_403` | ✅ Passing |
| Owner exporting PDFs | `EndToEndWorkflowTests.Owner_Cannot_Export_Note_Returns_403` | ✅ Passing |
| Billing accessing sync | `EndToEndWorkflowTests.Billing_Cannot_Access_Sync_Returns_403` | ✅ Passing |
| Patient accessing clinical sync | `EndToEndWorkflowTests.Patient_Cannot_Access_Sync_Status_Returns_403` | ✅ Passing |
| FrontDesk accessing sync | `EndToEndWorkflowTests.FrontDesk_Cannot_Access_Sync_Returns_403` | ✅ Passing |

---

## CI Gate Binding

The `e2e-workflow-gate` job in `ci-release-gate.yml` runs all `[Category=EndToEnd]` tests on
every PR. A PR cannot merge if this gate fails. This ensures the "Must-Catch" rows above are
continuously verified against the live API, not just at snapshot time.

**Workflow file:** `.github/workflows/ci-release-gate.yml` — job `e2e-workflow-gate`  
**Test file:** `tests/PTDoc.Tests/Integration/EndToEndWorkflowTests.cs`  
**Test filter:** `dotnet test --filter "Category=EndToEnd"`

---

*Generated by Sprint UC-Omega. Updated when new capabilities are added or role policies change.*
