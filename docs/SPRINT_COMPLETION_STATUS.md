# PTDoc Sprint-by-Sprint Completion Status (UC-Omega)

**Effective:** Sprint UC-Omega  
**Purpose:** Provides an honest, test-backed record of completion status for every
UC sprint. Every ✅ must trace to a passing test, CI gate, or documented manual
verification. Claims unsupported by evidence are marked ⚠️ or ❌.

> **Codex audit note:** The previous release readiness documentation over-claimed
> implementation completeness. This document is the corrected, evidence-bound record.
> Any ⚠️ or ❌ row is a known gap and must not be treated as complete.

---

## Status Key

| Symbol | Meaning |
|---|---|
| ✅ | Verified — passing test or CI gate exists |
| ⚠️ | Partial — implemented but test coverage incomplete or behavior edge-case missing |
| ❌ | Gap — not implemented or not test-backed; ship/no-ship blocker |
| 🔁 | Deferred — accepted deferral with explicit scope note |

---

## Sprint UC-Alpha — Authorization Infrastructure and RBAC

| Item | Status | Evidence |
|---|---|---|
| All API routes have authorization policies applied | ✅ | `AuthorizationCoverageTests.cs` — `[Category=RBAC]` |
| Role matrix enforced at API layer (must-catch list) | ✅ | `RbacRoleMatrixTests.cs` + `RbacHttpSmokeTests.cs` — `[Category=RBAC]` |
| PTA blocked from eval endpoints | ✅ | `RbacHttpSmokeTests.PTA_Cannot_Create_EvalNote_Returns_403` |
| Billing blocked from note-write | ✅ | `RbacRoleMatrixTests` + `RbacHttpSmokeTests.Billing_Cannot_Write_Notes_Returns_403` |
| No unauthenticated access to protected routes | ✅ | `AuthorizationCoverageTests` + `EndToEndWorkflowTests.Unauthenticated_Request_Returns_401` |
| CI gate: rbac-gate in ci-release-gate.yml | ✅ | `ci-release-gate.yml — rbac-gate` |

---

## Sprint UC-Beta — Intake Workflow

| Item | Status | Evidence |
|---|---|---|
| Patient submits intake (sets IsLocked + SubmittedAt) | ✅ | `IntakeEndpointsTests` |
| FrontDesk creates intake invite | ✅ | `EndToEndWorkflowTests.Intake_Workflow_FrontDesk_Creates_PT_Reviews` |
| PT reviews submitted intake | ✅ | `EndToEndWorkflowTests.Intake_Workflow_FrontDesk_Creates_PT_Reviews` |
| HIPAA consent required for submission | ✅ | `IntakeEndpointsTests` — `SubmitIntake_Requires_HipaaAcknowledged` |
| Billing blocked from creating intake | ✅ | `EndToEndWorkflowTests.Intake_Billing_Cannot_Create_Returns_403` |
| Intake requires IsLocked + SubmittedAt for review | ✅ | `IntakeEndpointsTests` |
| Audit events logged (IntakeSubmitted, IntakeLocked, IntakeReviewed) | ✅ | `IntakeEndpointsTests` |

---

## Sprint UC-Gamma — Carry-Forward and AI Guardrails

| Item | Status | Evidence |
|---|---|---|
| Carry-forward reads last signed note (read-only) | ✅ | `NoteComplianceIntegrationTests` — carry-forward tests |
| AI goals endpoint validates note existence server-side | ✅ | `AiEndpointTests` |
| AI output guarded by note-signed state | ✅ | `AiEndpointTests.AI_Goals_Blocked_When_Note_Signed` |
| AcceptAiSuggestion validates section whitelist | ✅ | `NoteEndpointsTests.AcceptAiSuggestion_*` |
| PTA domain guard enforced in UpdateNote | ✅ | `RbacRoleMatrixTests` + `RbacHttpSmokeTests` |
| Evaluation note returns null carry-forward (use intake) | ✅ | `NoteComplianceIntegrationTests` |

---

## Sprint UC-Delta — Compliance Rules and PDF Export

| Item | Status | Evidence |
|---|---|---|
| 8-minute rule enforced server-side | ✅ | `ComplianceTests.[Category=Compliance]` |
| Known timed CPT codes override client IsTimed flag | ✅ | `NoteEndpointsTests.EnforceKnownTimedCptStatus_*` |
| PDF export requires signed note (null hash → 422) | ✅ | `PdfEndpointTests` |
| PDF export policy: PT, PTA, Admin only (NoteExport) | ✅ | `AuthorizationCoverageTests` |
| Billing blocked from PDF export | ✅ | `EndToEndWorkflowTests.Billing_Cannot_Export_Note_Returns_403` |
| Owner blocked from PDF export | ✅ | `EndToEndWorkflowTests.Owner_Cannot_Export_Note_Returns_403` |
| CI gate: compliance-gate in ci-release-gate.yml | ✅ | `ci-release-gate.yml — compliance-gate` |

---

## Sprint UC-Epsilon — Sync Engine and Offline Queue

| Item | Status | Evidence |
|---|---|---|
| NoteEndpoints enqueue sync after SaveChanges | ✅ | `SyncQueueIntegrationTests` |
| IntakeEndpoints enqueue sync after SaveChanges | ✅ | `SyncQueueIntegrationTests` |
| SyncEngine role-scoped: Aide/FrontDesk/Patient excluded from clinical data | ✅ | `SyncEngineTests.GetClientDelta_ExcludesClinicalNoteForNonClinicalRoles` |
| LocalDbContext supports IntakeFormDraft and ClinicalNoteDraft | ✅ | `LocalSyncOrchestratorTests` |
| Signed notes trigger conflict on pull (LWW for drafts) | ✅ | `LocalSyncOrchestratorTests` |
| SyncEngine.ApplyEntityFromPayloadAsync never trusts client SignatureHash | ✅ | `SyncEngineTests` |
| CI gate: offline-sync-gate in ci-release-gate.yml | ✅ | `ci-release-gate.yml — offline-sync-gate` |

---

## Sprint UC-Omega — End-to-End Validation and Evidence Pack Accuracy

| Item | Status | Evidence |
|---|---|---|
| HTTP API integration test harness (WebApplicationFactory) | ✅ | `tests/PTDoc.Tests/Integration/EndToEndWorkflowTests.cs` |
| Unauthenticated request returns 401 | ✅ | `EndToEndWorkflowTests.Unauthenticated_Request_Returns_401` |
| FrontDesk creates intake → PT submits → PT reviews (full workflow) | ✅ | `EndToEndWorkflowTests.Intake_Workflow_FrontDesk_Creates_PT_Reviews` |
| PT creates Daily note → returns 201 with note ID | ✅ | `EndToEndWorkflowTests.PT_Creates_DailyNote_Returns_201_With_NoteId` |
| PTA creates Daily note → returns 201 | ✅ | `EndToEndWorkflowTests.PTA_Can_Create_DailyNote_Returns_201` |
| PTA blocked from Eval note → 403 | ✅ | `EndToEndWorkflowTests.PTA_Cannot_Create_EvalNote_Returns_403` |
| PT creates Eval note then signs → 200 or 422 (reachability confirmed) | ✅ | `EndToEndWorkflowTests.PT_Creates_Note_Then_Signs_Successfully` |
| Billing blocked from note write | ✅ | `EndToEndWorkflowTests.Billing_Cannot_Write_Notes_Returns_403` |
| Billing blocked from sign | ✅ | `EndToEndWorkflowTests.Billing_Cannot_Sign_Note_Returns_403` |
| Billing blocked from PDF export | ✅ | `EndToEndWorkflowTests.Billing_Cannot_Export_Note_Returns_403` |
| Billing blocked from patient demographics edit | ✅ | `EndToEndWorkflowTests.Billing_Cannot_Edit_Patient_Demographics_Returns_403` |
| Billing blocked from sync | ✅ | `EndToEndWorkflowTests.Billing_Cannot_Access_Sync_Returns_403` |
| Owner blocked from note write | ✅ | `EndToEndWorkflowTests.Owner_Cannot_Write_Notes_Returns_403` |
| Owner blocked from patient demographics edit | ✅ | `EndToEndWorkflowTests.Owner_Cannot_Edit_Patient_Demographics_Returns_403` |
| Owner blocked from PDF export | ✅ | `EndToEndWorkflowTests.Owner_Cannot_Export_Note_Returns_403` |
| FrontDesk blocked from note write | ✅ | `EndToEndWorkflowTests.FrontDesk_Cannot_Write_Notes_Returns_403` |
| FrontDesk blocked from sync | ✅ | `EndToEndWorkflowTests.FrontDesk_Cannot_Access_Sync_Returns_403` |
| Aide blocked from note write | ✅ | `EndToEndWorkflowTests.Aide_Cannot_Write_Notes_Returns_403` |
| Patient blocked from clinical sync | ✅ | `EndToEndWorkflowTests.Patient_Cannot_Access_Sync_Status_Returns_403` |
| PTA blocked from co-sign (PT-only) | ✅ | `EndToEndWorkflowTests.PTA_Cannot_CoSign_Note_Returns_403` |
| PT can access sync status | ✅ | `EndToEndWorkflowTests.PT_Can_Access_Sync_Status_Returns_200` |
| Health liveness endpoint returns 200 without auth | ✅ | `EndToEndWorkflowTests.HealthLive_Returns_200_Without_Auth` |
| Intake billing-create blocked → 403 | ✅ | `EndToEndWorkflowTests.Intake_Billing_Cannot_Create_Returns_403` |
| CI gate: e2e-workflow-gate in ci-release-gate.yml | ✅ | `ci-release-gate.yml — e2e-workflow-gate` |
| Role capability verification map published | ✅ | `docs/ROLE_CAPABILITY_VERIFICATION_MAP.md` |
| Sprint completion status doc corrects overclaims | ✅ | This document |
| Evidence pack updated (ACCEPTANCE_EVIDENCE_MAP.md) | ✅ | `docs/ACCEPTANCE_EVIDENCE_MAP.md — Sprint UC-Omega section` |

---

## Known Remaining Gaps (Not UC-Omega Scope)

The following items were identified by the Codex audit as risks. They are **not** resolved
by UC-Omega but are documented here so that the evidence pack does not over-claim.

| Gap | Severity | Notes |
|---|---|---|
| Co-sign lifecycle completeness (PT-only attestation path) | ⚠️ | Co-sign mechanics implemented (Sprint UC4); PT-only override attestation edge case not fully covered by automated tests. Manual verification documented. |
| Draft/Pending/Final note lifecycle state machine | ⚠️ | Note states implemented; full state machine transition tests are backend-centric. UI state binding tests not present (no UI automation harness). |
| Offline-first end-to-end clinical documentation | ⚠️ | Sync engine and local DB scaffolding complete (Sprint UC-Epsilon). Full offline scenario (airplane-mode → sync) is validated by OfflineSync unit tests but no device integration test exists. |
| UI automation harness | ❌ | No Playwright/Selenium test harness exists. UI enforcement relies on API-layer tests (compliant for HIPAA per architecture). UI-specific regression tests remain out of scope until a UI automation sprint is planned. |
| AI pathway bypass via web-host surface | ⚠️ | AI endpoints validated by `AiEndpointTests`. Web-host UI pathway reviewed in code; no known bypass. Manual verification completed. |

---

*Last updated: Sprint UC-Omega. To be updated when gaps are resolved or new sprints complete.*
