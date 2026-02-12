# PTDoc (PFPT)

## Medicare Rules Engine Specification

**Derived From:** Master FSD, Backend TDD, AI Prompt Specification\
**Audience:** Backend Engineers, QA, Compliance Reviewers\
**Scope:** Medicare documentation and billing enforcement logic

------------------------------------------------------------------------

## 1. Purpose

This document defines the Medicare Rules Engine for PFPT. The rules
engine enforces documentation timing, billing constraints, signatures,
and addendum behavior in a deterministic, auditable manner.

The engine is implemented server-side and exposed to the UI as
validation states (warnings, hard stops, locks).

------------------------------------------------------------------------

## 2. Design Principles

-   Rules are deterministic and testable\
-   No reliance on clinician memory\
-   UI reflects rule state but does not enforce it\
-   Rules are versioned for regulatory updates\
-   Overrides require explicit clinician acknowledgment

------------------------------------------------------------------------

## 3. Rule Categories

-   Documentation Frequency Rules\
-   Billing Time Rules (8-Minute Rule)\
-   Signature & Attestation Rules\
-   Edit Locking & Addendum Rules\
-   Alert & Hard Stop Rules

------------------------------------------------------------------------

## 4. Documentation Frequency Rules

### 4.1 Progress Note (PN) Requirement

**Rule:**\
A Progress Note is required every 10 visits OR 30 calendar days,
whichever occurs first.

**Trigger Conditions:**

-   Visits since last Eval or PN ≥ 10\
-   Days since last Eval or PN ≥ 30

**System Behavior:**

-   Blocks creation of Daily Notes\
-   Displays hard-stop message:\
    \> "Progress Note required per Medicare guidelines."

**Resolution:**

-   Completion and signature of PN resets counters

------------------------------------------------------------------------

## 5. Billing Rules -- CMS 8-Minute Rule

### 5.1 Time-Based CPT Codes

Applies to timed CPT codes (e.g., 97110, 97140, 97530).

  Total Minutes   Units Allowed
  --------------- ---------------
  8--22           1
  23--37          2
  38--52          3
  53--67          4

### 5.2 Validation Logic

-   Total treatment minutes entered\
-   Units auto-calculated\
-   User-entered units validated against allowed range

**Behavior:**

-   Warning if units exceed allowed\
-   PT-only override with attestation checkbox

------------------------------------------------------------------------

## 6. Signature & Attestation Rules

### 6.1 PT Signature

-   Required to finalize Eval, PN, Discharge\
-   Generates SHA-256 content hash\
-   Locks all note fields

### 6.2 PTA Signature

-   Allowed on Daily Notes only\
-   Status set to Pending Co-Sign\
-   Supervising PT required to co-sign

------------------------------------------------------------------------

## 7. Edit Locking & Addendums

### 7.1 Post-Signature Locking

-   Signed notes are immutable\
-   No edits permitted

### 7.2 Addendum Creation

-   Addendum references original note ID\
-   Time-stamped\
-   Author identified\
-   Original content preserved

------------------------------------------------------------------------

## 8. Alerts & Notifications

  Alert                                 Severity    Recipient
  ------------------------------------- ----------- -----------
  PN Due Soon (≥8 visits or ≥25 days)   Warning     PT
  PN Required                           Hard Stop   PT
  Missing PT Co-Sign                    Warning     PT
  Unsigned Eval \> 24 hrs               Warning     PT/Admin

------------------------------------------------------------------------

## 9. Override Logging

Overrides require:

-   Reason selection\
-   Clinician acknowledgment\
-   Audit log entry

No override bypasses PN requirement.

------------------------------------------------------------------------

## 10. QA Test Scenarios (Examples)

**Given** patient has 9 visits since last PN\
**When** clinician attempts Daily Note\
**Then** system allows with warning

------------------------------------------------------------------------

## 11. Versioning & Updates

-   Rules versioned independently\
-   Effective date stored\
-   Historical notes evaluated under rule version at time of signing

------------------------------------------------------------------------

This rules engine is binding for compliance and billing behavior across
PFPT.
