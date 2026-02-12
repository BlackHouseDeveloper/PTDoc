# PTDoc (PFPT)

## Offline Sync & Conflict Resolution Specification

**Derived From:** Master FSD, Backend TDD, Medicare Rules Engine\
**Audience:** Backend Engineers, Mobile Engineers, QA, Product\
**Scope:** Offline-first synchronization, conflict handling, data
integrity

------------------------------------------------------------------------

## 1. Purpose

This document defines how PFPT handles offline documentation, data
synchronization, and conflict resolution across devices while preserving
clinical integrity and Medicare compliance.

This specification ensures clinicians can document care anywhere,
without risking data loss, duplication, or compliance violations.

------------------------------------------------------------------------

## 2. Core Principles

-   Offline-first is mandatory -- the app must function fully without
    connectivity\
-   Local truth first -- all writes go to local storage immediately\
-   Signed data is immutable -- conflicts cannot overwrite finalized
    records\
-   Draft data is recoverable -- conflicts resolved deterministically\
-   User trust over automation -- conflicts are surfaced, not hidden

------------------------------------------------------------------------

## 3. Data Storage Model

### 3.1 Local Database

-   Encrypted SQLite database per device\
-   Entity Framework Core tracking enabled

All entities include:

  Field              Description
  ------------------ -----------------------------
  Id                 GUID
  LastModifiedUtc    UTC timestamp
  ModifiedByUserId   User reference
  SyncState          Pending / Synced / Conflict

------------------------------------------------------------------------

## 4. Sync Architecture

### 4.1 Sync Flow Overview

    [ Local SQLite ]
            ↓
       [ Sync Queue ]
            ↓
       [ Sync Service ]
            ↓
       [ Central API ]

### 4.2 Sync Trigger Conditions

-   Automatic every \~30 seconds when online\
-   Manual **Sync Now** action\
-   App resume from background

------------------------------------------------------------------------

## 5. Sync Order of Operations

1.  Unsigned drafts\
2.  Intake responses\
3.  Objective metrics\
4.  Signed notes\
5.  Audit logs

Signed notes are synced last to reduce partial state risks.

------------------------------------------------------------------------

## 6. Conflict Detection

A conflict is detected when:

-   Same entity ID modified on two devices\
-   Incoming `LastModifiedUtc` \< server version\
-   Attempt to overwrite signed content

------------------------------------------------------------------------

## 7. Conflict Resolution Rules

### 7.1 Draft Notes

-   Resolution: Last-write-wins\
-   Earlier version preserved in audit history\
-   User notified if overwrite occurs

### 7.2 Signed Notes

-   Resolution: Reject incoming change\
-   Sync fails with immutable error\
-   User prompted to create Addendum instead

### 7.3 Intake Data

-   Locked after Eval creation\
-   Any conflicting updates rejected

------------------------------------------------------------------------

## 8. Multi-Device Scenarios

### 8.1 Same User, Two Devices

-   Draft notes may diverge\
-   Newer timestamp wins\
-   Warning banner displayed on open

### 8.2 PTA → PT Workflow

-   PTA drafts Daily Note offline\
-   Sync uploads as Pending Co-Sign\
-   PT can review and sign on different device

------------------------------------------------------------------------

## 9. Failure & Recovery Handling

### 9.1 Partial Sync Failure

-   Failed entities remain in Sync Queue\
-   UI badge shows **Sync Pending**

### 9.2 App Crash or Kill

-   Local DB persists\
-   No data loss

------------------------------------------------------------------------

## 10. User-Facing Indicators

  Indicator         Meaning
  ----------------- ----------------------
  Offline Badge     No connectivity
  Sync Spinner      Active sync
  Conflict Banner   Manual review needed

------------------------------------------------------------------------

## 11. Audit & Compliance

-   All sync actions logged\
-   Conflicts recorded with reason\
-   No PHI stored in logs

------------------------------------------------------------------------

## 12. QA Acceptance Scenarios (Examples)

**Given** clinician documents offline\
**When** connectivity restores\
**Then** notes sync automatically without data loss

------------------------------------------------------------------------

## 13. Non-Goals

-   Real-time collaborative editing\
-   Automatic merge of signed clinical narratives

------------------------------------------------------------------------

This specification governs all offline and synchronization behavior in
PFPT and is binding for implementation.
