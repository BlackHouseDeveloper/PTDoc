# PTDoc (PFPT) – Acceptance Checklist v1.0

Client: January Austin – Physically Fit Physical Therapy
Developer: Black House Developers LLC
Target Launch: January 17, 2026
Document Status: Release Validation & Client Alignment Gate

---

# PURPOSE

This document verifies that the PTDoc application adheres to:

* Client-defined workflows
* Master Functional Specification (FSD)
* Backend Technical Design Document (TDD)
* Medicare Rules Engine
* AI Prompt Specification
* Offline Sync Specification
* Figma UI Mapping
* Sample Completed Note Formatting

All items must be validated prior to Beta or Production release.

---

# SECTION 1 – CORE CLINICAL WORKFLOW

## 1.1 User Roles & Permissions

* [ ] Admin role configured
* [ ] PT role configured
* [ ] PTA role configured
* [ ] Therapy Aide restricted to demographics only
* [ ] Role-based UI hiding enforced
* [ ] Server-side authorization enforced
* [ ] PTA cannot sign Eval/PN/DC
* [ ] PTA Daily Notes require PT co-sign

---

## 1.2 Patient Intake

* [ ] Intake delivered via web link

* [ ] Intake delivered via QR

* [ ] Intake delivered via email

* [ ] Intake delivered via text

* [ ] Demographics captured

* [ ] Pain descriptors captured

* [ ] Body region selection triggers branching

* [ ] Medications predictive input

* [ ] Comorbidities checklist

* [ ] Assistive devices checklist

* [ ] Functional limitations captured

* [ ] Outcome measures auto-selected by region

* [ ] Intake locks after submission

* [ ] Intake cannot be edited after Eval creation

---

## 1.3 SOAP Note Types

* [ ] Evaluation note complete end-to-end
* [ ] Daily note complete end-to-end
* [ ] Progress Note complete end-to-end
* [ ] Discharge note complete end-to-end

Each note must include:

* [ ] Subjective section
* [ ] Objective section
* [ ] Assessment section
* [ ] Plan section
* [ ] Billing section

---

# SECTION 2 – CLINICAL INTELLIGENCE

## 2.1 Objective Logic

* [ ] ROM capture by body region
* [ ] MMT capture by body region
* [ ] Outcome measure storage per visit
* [ ] Outcome measures display longitudinal trends

---

## 2.2 Goals Engine

* [ ] SMART goal templates implemented
* [ ] ROM deficits trigger goal suggestions
* [ ] Strength deficits trigger goal suggestions
* [ ] Functional limitations map to goals
* [ ] Goals editable by PT
* [ ] PTA cannot create new goals

---

## 2.3 ICD-10 & CPT

* [ ] ICD-10 lookup integrated
* [ ] CPT code picker implemented
* [ ] Timed CPT logic active
* [ ] Units validated against time entered

---

# SECTION 3 – MEDICARE RULES ENGINE

## 3.1 Progress Note Rule

* [ ] Hard stop at 10 visits without PN
* [ ] Hard stop at 30 days without PN
* [ ] Warning at ≥8 visits
* [ ] Warning at ≥25 days
* [ ] PN resets counters
* [ ] No override bypass allowed

---

## 3.2 8-Minute Rule

* [ ] Units auto-calculated
* [ ] Units validated against allowed range
* [ ] Warning displayed when mismatch
* [ ] PT-only override with attestation
* [ ] Override logged in audit trail

---

## 3.3 Signatures & Locking

* [ ] PT signature required for Eval/PN/DC
* [ ] PTA signature sets Pending Co-Sign
* [ ] PT co-sign finalizes PTA note
* [ ] Signed notes immutable
* [ ] SHA-256 content hash generated
* [ ] Addendum creation preserves original note
* [ ] Addendum linked and time-stamped

---

# SECTION 4 – AI GUARDRAILS

## 4.1 Input Integrity

* [ ] AI receives sanitized structured DTO only
* [ ] AI has no persistent memory
* [ ] AI does not store PHI
* [ ] AI cannot write directly to database

---

## 4.2 Assessment Output

* [ ] Includes clinical synthesis
* [ ] Includes impairments
* [ ] Includes functional impact
* [ ] Includes medical necessity
* [ ] No fabricated diagnoses
* [ ] No invented measurements
* [ ] No invented timelines

---

## 4.3 Plan Output

* [ ] Describes treatment focus areas
* [ ] References education provided
* [ ] Justifies skilled PT
* [ ] No invented frequencies
* [ ] No specific exercises unless selected
* [ ] Clinician must click “Accept” before persistence

---

# SECTION 5 – OFFLINE-FIRST ARCHITECTURE

## 5.1 Local Storage

* [ ] All writes persist to encrypted SQLite
* [ ] App fully usable offline
* [ ] Offline badge visible

---

## 5.2 Sync Behavior

* [ ] Auto-sync every ~30 seconds when online
* [ ] Manual “Sync Now” works
* [ ] Unsigned drafts sync first
* [ ] Signed notes sync last
* [ ] Sync queue persists across crash

---

## 5.3 Conflict Resolution

* [ ] Draft conflicts resolved last-write-wins
* [ ] Signed notes reject overwrite
* [ ] Conflict banner shown to user
* [ ] All conflicts logged

---

# SECTION 6 – PDF EXPORT & EMR READINESS

* [ ] Export available only after signature
* [ ] PDF format matches provided sample
* [ ] Patient header formatted correctly
* [ ] SOAP sections properly structured
* [ ] Outcome tables formatted correctly
* [ ] Signature footer includes credentials & timestamp
* [ ] PDF non-editable
* [ ] EMR copy/paste friendly

---

# SECTION 7 – UI & UX PARITY

* [ ] Figma layout matches implemented layout
* [ ] Dashboard shows patient queue
* [ ] PN due alerts visible
* [ ] Unsigned note alerts visible
* [ ] Minimal typing achieved
* [ ] Continue/Next buttons reduce scrolling
* [ ] Sticky Save / AI / Export controls present
* [ ] Mobile responsiveness verified
* [ ] Tablet layout verified
* [ ] Desktop layout verified

---

# SECTION 8 – SECURITY & AUDIT

* [ ] HTTPS enforced
* [ ] 15-minute session timeout
* [ ] Audit logs capture login
* [ ] Audit logs capture edit
* [ ] Audit logs capture signature
* [ ] Audit logs capture export
* [ ] No PHI stored in system logs
* [ ] 7-year retention configurable

---

# SECTION 9 – PERFORMANCE BENCHMARKS

* [ ] Dashboard load < 2 seconds
* [ ] Local DB read < 50ms
* [ ] API response < 500ms
* [ ] AI generation < 5 seconds
* [ ] PDF generation < 5 seconds

---

# SECTION 10 – FINAL RELEASE GATE

Release Version: ___________________

Environment:

* [ ] Dev
* [ ] Beta
* [ ] Production

Validated By (Developer): ___________________

Validated By (QA): ___________________

Clinical Reviewer Sign-Off: ___________________

All Sections Passed:

* [ ] YES – Approved for Release
* [ ] NO – Release Blocked

Date: ___________________

---

This Acceptance Checklist is binding for all PFPT releases and must be updated if scope or regulatory requirements change.
