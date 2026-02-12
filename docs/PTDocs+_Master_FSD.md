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
-   Auto‑selected outcome measures

### 5.3 Branching Logic

-   Dynamic question sets based on body part and condition
-   Only clinically relevant questions appear

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

------------------------------------------------------------------------

## 8. Goals Engine

-   SMART‑formatted goal templates
-   Auto‑suggested based on ROM, Strength, Functional limitations
-   Fully editable by PT

------------------------------------------------------------------------

## 9. Outcome Measures

-   Auto‑assigned by body region
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

------------------------------------------------------------------------

## 19. Change Management

Any scope changes must be documented and approved before implementation.

------------------------------------------------------------------------

This document supersedes all prior PFPT functional drafts and serves as
the authoritative specification for design, development, and QA.
