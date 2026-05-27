# PTDoc (PFPT)

## Unified Functional Specification Document (Master FSD)

**Client:** January Austin -- Physically Fit Physical Therapy\
**Developer:** Black House Developers LLC\
**Target Platform:** Blazor Web + .NET MAUI (Mobile/Tablet)\
**Target Launch:** January 17, 2026\
**Compliance Target:** HIPAA‑conscious design (MVP → scalable
compliance)

------------------------------------------------------------------------

## 1. Purpose of This Document

This Master Functional Specification Document (FSD) consolidates and
refines all prior PFPT documentation, including:

-   Functional Specification Document drafts (v1--v2)
-   Backend foundation + PTDoc frontend specifications
-   Clinician-facing healthcare application FSD
-   Proposal, kickoff summary, and workflow definitions
-   Blazor migration and offline-first architectural requirements

The goal is to provide one authoritative, execution-ready source of
truth for:

-   Functional requirements
-   Expected UI behavior
-   Role-based permissions
-   Clinical workflows and Medicare enforcement rules
-   AI behavior constraints
-   Technical, offline-first, and security expectations

Duplicate concepts have been merged; no functional intent has been
removed.

------------------------------------------------------------------------

## 2. Product Vision & Core Objectives

### 2.1 Vision

PFPT is a clinician‑first physical therapy documentation application
designed to dramatically reduce documentation time while preserving
clinical reasoning, compliance, and therapist control.

### 2.2 Core Objectives

-   Replace manual dictation with structured, click‑based inputs
-   Convert patient and clinician inputs into editable, natural‑sounding
    SOAP notes
-   Maintain HIPAA‑conscious architecture at every tier
-   Support evaluation → discharge workflows without EMR lock‑in
-   Enable future EMR integration (e.g., Strata) without architectural
    rewrites

------------------------------------------------------------------------

## 3. User Roles & Permissions

### 3.1 Practice Owner / Admin

-   Full system configuration access
-   Role and permission management
-   Branding (logo, colors)
-   Data retention settings
-   Audit log access

### 3.2 Physical Therapist (PT / DPT)

-   Full patient chart access
-   Create/edit Evaluation, Daily, Progress (Re‑Eval), Discharge notes
-   Plan of Care creation and certification tracking
-   Goal creation and modification
-   Outcome measure administration and interpretation
-   ICD‑10 selection and review
-   CPT code selection and unit entry
-   Documentation signature and attestation
-   Clinical messaging

### 3.3 Physical Therapist Assistant (PTA)

-   Limited patient chart access
-   Create/edit daily treatment notes
-   Update goal status (no goal creation)
-   CPT selection within scope
-   Unit/time entry
-   Outcome measure administration (no interpretation edits)
-   Scheduling (assigned visits only)
-   Documentation signature (PT co‑sign required)

### 3.4 Therapy Aide / Rehab Tech

-   View patient demographics only
-   No SOAP, goals, or clinical decision access

### 3.5 Patient (Standalone Access)

-   Complete intake questionnaires via secure link
-   No access to clinician notes or summaries

------------------------------------------------------------------------

## 4. Clinical Note Types (SOAP)

All notes follow standardized SOAP structure with AI‑assisted drafting.

### 4.1 Evaluation (Eval)

-   Full intake review
-   Baseline ROM/MMT
-   Outcome measures
-   Initial goals
-   Plan of Care

### 4.2 Daily Treatment Note

-   Focused subjective update
-   Interventions performed
-   Response to treatment
-   Progress toward goals

### 4.3 Progress Note / Re‑Evaluation

-   Required every 30 days or 10 visits
-   Side‑by‑side comparison with prior Eval/PN
-   Updated goals and POC adjustments

### 4.4 Discharge Note

-   Final functional status
-   Outcome measure comparison
-   Home exercise plan summary

------------------------------------------------------------------------

## 5. Patient Intake Workflow

### 5.1 Delivery Methods

-   Web link
-   QR code
-   Email
-   Text message

### 5.2 Intake Content

-   Demographics
-   Pain descriptors
-   Affected body region(s)
-   Medications (predictive text)
-   Comorbidities
-   Assistive devices
-   Functional limitations
-   Outcome Measures step after Pain Details / Pain & Function
-   One patient-facing primary outcome measure per distinct selected body part
-   Optional patient-entered previous outcome measure or functional score

### 5.3 Branching Logic

-   Dynamic question sets based on body part and condition
-   Only clinically relevant questions appear
-   Structured body-part selections map to outcome-measure recommendations through the runtime clinical reference library
-   Standard patient intake shows no more than one primary outcome measure per selected body part; laterality does not create duplicate patient-facing forms
-   Primary patient-facing outcome measures must come from explicit runtime clinical-reference configuration, not from arbitrary catalog order
-   Clinicians may administer, edit, interpret, or add additional outcome measures later

------------------------------------------------------------------------

## 6. Clinician Dashboard

### 6.1 Dashboard Features

-   Patient queue
-   Alerts (PN due, missing signatures, precautions)
-   Questionnaire status
-   Quick navigation into note builder

### 6.2 Scheduling

-   View schedule
-   Edit own appointments (PT/PTA scope)

------------------------------------------------------------------------

## 7. SOAP Note Builder (Core Feature)

### 7.1 Input Method

-   Dropdowns
-   Checklists
-   Limited structured free‑text

### 7.2 AI‑Generated Content

-   Subjective
-   Assessment
-   Goals
-   Plan of Care

**Rules:**

-   AI output is always editable
-   No hallucination beyond structured inputs
-   Clinician controls prompt submission

### 7.3 Clinical Intelligence

-   ROM/MMT differentials trigger goal suggestions
-   Functional deficits mapped to goal templates
-   ICD‑10 lookup tied to assessment findings

### 7.4 Clinical Data Persistence and Carry-Forward

PTDoc must preserve patient and clinical context across the full episode
workflow: Intake → Evaluation SOAP → Daily SOAP → Progress/Re‑Eval SOAP
→ Discharge SOAP. Clinicians should not need to manually re-enter data
that already exists in the system. Completed Intake remains the locked
patient-reported source, and the signed Evaluation becomes the
clinician-confirmed baseline for downstream documentation.

**Workflow rules:**

-   Intake → Evaluation SOAP: the first Evaluation for an episode must
    prefill from the latest locked or submitted Intake when no later
    Evaluation already exists.
-   Intake-derived Evaluation prefill includes demographics, payer and
    referring-provider context, affected regions, pain location and
    descriptors, pain intensity, medications, comorbidities, assistive
    devices, living situation or caregiver support, functional
    limitations, goals or concerns, precautions or red flags, intake
    questionnaire context, assigned outcome measures, and any
    intake-derived subjective narrative draft.
-   The clinician must be able to review, edit, accept, or exclude
    Intake-derived content before finalizing the Evaluation.
-   Evaluation SOAP becomes the episode baseline once signed, including
    diagnoses, goals, plan of care, precautions, HEP, baseline outcome
    scores, ROM/MMT and other objective deficits, functional
    limitations, and clinician assessment.
-   Daily SOAP notes carry forward active Evaluation data needed for
    treatment, but session-specific subjective, objective, intervention,
    response, and billing content is stored only on the Daily note and
    must not overwrite the Evaluation baseline.
-   Progress/Re‑Eval SOAP notes must load the Evaluation baseline and
    the most recent relevant signed Daily or Progress note for
    comparison, clearly distinguishing baseline, prior status, and
    current clinician-entered findings.
-   Discharge SOAP notes must summarize the full episode using the
    Evaluation baseline, latest Progress/Re‑Eval status, final
    subjective and objective findings, outcome scores, goal outcomes,
    final functional status, discharge reason and prognosis, final HEP
    and instructions, visit totals, treatment overview, and follow-up
    recommendations.

**Persistence rules:**

-   Submitted Intake responses are locked and remain available as the
    original patient-reported source.
-   Evaluation baseline data is persisted as clinician-confirmed note
    content and is never overwritten by Daily, Progress, Re‑Eval, or
    Discharge workflows.
-   Carry-forward metadata must identify the source type, source record
    or note id, source date, and whether the carried value is inherited,
    clinician-edited, excluded, or newly entered.
-   Draft notes may be edited and autosaved until signed. Signed notes
    are immutable.
-   Corrections to signed notes must be handled through linked addendums
    rather than direct edits.
-   Offline note drafts, signatures, addendums, carry-forward metadata,
    and accepted edits must persist locally and sync later without data
    loss.
-   Intake-derived patient-entered outcome scores remain context until a
    clinician confirms or corrects them as official outcome result
    records.

**Interface and UI rules:**

-   Note workspace payloads must support multiple source contexts where
    applicable, including Intake source, Evaluation baseline source,
    prior note source, and episode summary source.
-   Prefilled fields must show visible source indicators such as From
    Intake, From Evaluation, From Daily Note, or From Progress Note.
-   Locked source views must be read-only and reachable from the note
    workspace for traceability.
-   Evaluation must clearly distinguish patient-reported Intake content
    from clinician-entered findings.
-   Progress/Re‑Eval must provide side-by-side comparison for baseline
    versus current ROM/MMT, outcome scores, pain and function status,
    goals, and functional limitations.
-   Goal status tracking must support Met, Ongoing, Modified, Deferred,
    and Discontinued without losing historical goal identity.
-   Discharge must show an episode summary view with baseline, progress,
    final status, goals, visit count, and discharge recommendations.
-   The UI must warn when required carried-forward data is missing,
    unavailable offline, unsigned, or explicitly excluded.
-   AI generation may use only structured, sanitized persisted inputs
    and must create reviewable draft text only. AI output must not
    overwrite persisted clinical data until a clinician accepts it.

------------------------------------------------------------------------

## 8. Goals Engine

-   SMART‑formatted goal templates
-   Auto‑suggested based on ROM, Strength, Functional limitations
-   Fully editable by PT

------------------------------------------------------------------------

## 9. Outcome Measures

-   Auto‑assigned by body region
-   Patient intake presents only the configured primary measure for each selected body part
-   Patients may optionally enter a known questionnaire name, score/percentage, completion date, and notes
-   Patient-entered intake values remain review context until a clinician confirms or corrects them
-   Stored per visit
-   Displayed longitudinally
-   Editable interpretation (PT only)

------------------------------------------------------------------------

## 10. Exercise & Intervention Library

-   Custom exercise entry
-   Auto‑suggestions from local reference library
-   Saved per patient

------------------------------------------------------------------------

## 11. Progress Note Comparison

-   Side‑by‑side Eval vs PN view
-   Highlighted changes (ROM, Strength, Outcomes, Goals)

------------------------------------------------------------------------

## 12. AI Integration Constraints

-   Stateless GPT processing only
-   Patient data is never used for training
-   Structured, sanitized inputs only
-   AI cannot fabricate measurements, diagnoses, or findings
-   AI outputs are never auto-finalized
-   Clinician must explicitly review, edit, and accept content
-   Prompts are editable pre-generation
-   Output is editable post-generation

------------------------------------------------------------------------

## 13. Technical Architecture

### 13.1 Platform & Deployment Targets

  -----------------------------------------------------------------------
  Platform           Technology            Primary Use Case
  ------------------ --------------------- ------------------------------
  Tablet             .NET MAUI Blazor      Point-of-care documentation
  (iOS/Android)      Hybrid                

  Desktop            .NET MAUI Blazor      Office documentation
  (Mac/Windows)      Hybrid                

  Web                Blazor WebAssembly    Patient portal, admin access
  -----------------------------------------------------------------------

Shared UI logic is implemented via a Razor Class Library.

### 13.2 Offline-First Data Strategy

-   All clinical data written immediately to local encrypted SQLite (EF
    Core)
-   Fully functional offline
-   Visual indicators for Offline vs Online state
-   Background sync every \~30 seconds when online
-   Automatic sync on reconnection
-   Draft: last-write-wins
-   Signed: immutable

### 13.3 Data Models

-   Relational SQLite schema
-   JSON blobs for flexible SOAP content
-   SHA‑256 content hashes generated upon signature

### 13.4 PDF Export

-   Secure PDF generation
-   EMR-ready formatting
-   Signed footer with timestamp and credentials

------------------------------------------------------------------------

## 14. Security & Compliance

-   HTTPS enforced
-   PIN‑based access
-   Role‑based permissions
-   Audit logs (login, edit, export)
-   7‑year data retention (configurable)

------------------------------------------------------------------------

## 15. Branding & UX

-   Colors: Lime Green, Black, Gray, White (optional Navy)
-   Fonts: Inter / Roboto
-   Mobile‑first, high‑contrast design
-   Custom logo upload

------------------------------------------------------------------------

## 16. Non‑Goals

-   Direct billing submission (initial releases)
-   Insurance claim management
-   Autonomous AI clinical decision‑making

------------------------------------------------------------------------

## 17. Future Expansion Readiness

-   EMR integration APIs
-   Multi‑clinic scaling
-   Advanced analytics
-   Expanded patient portal

------------------------------------------------------------------------

## 18. Acceptance Criteria

-   All SOAP note types functional end-to-end
-   Medicare Progress Note enforcement
-   8-minute CPT validation
-   Offline documentation functional
-   Editable AI output with clinician acceptance gate
-   Role permissions enforced
-   PDF export validated
-   Clinical beta sign-off
-   Completed Intake automatically prepopulates the first Evaluation SOAP
    with relevant patient-reported data.
-   Evaluation shows Intake-derived fields as traceable, editable
    prefill and allows clinician accept/edit/exclude before signature.
-   Signed Evaluation becomes the immutable episode baseline.
-   Daily SOAP carries active Evaluation data forward without
    overwriting Evaluation baseline values.
-   Progress/Re‑Eval SOAP displays Evaluation baseline, prior signed-note
    status, and current findings as distinct data.
-   Discharge SOAP summarizes the full episode from persisted
    Evaluation, Daily, Progress/Re‑Eval, and final findings.
-   Signed note corrections create linked addendums instead of direct
    clinical-content edits.
-   Offline note drafts, signatures, addendums, and carry-forward
    metadata persist locally and sync later without data loss.
-   AI-generated text is based only on persisted structured inputs and
    requires clinician acceptance before saving.

------------------------------------------------------------------------

## 19. Change Management

Any scope changes must be documented and approved before implementation.

------------------------------------------------------------------------

This document supersedes all prior PFPT functional drafts and serves as
the authoritative specification for design, development, and QA.
