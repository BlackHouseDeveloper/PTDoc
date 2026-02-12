# PTDoc (PFPT)

## Backend Technical Design Document (TDD)

**Derived From:** PFPT Master Functional Specification Document (v1.0)\
**Target Audience:** Backend Engineers, Architects, QA Automation\
**Technology Stack:** .NET 8, ASP.NET Core, EF Core, SQLite, Blazor/MAUI
Clients

------------------------------------------------------------------------

## 1. Purpose

This document translates the PFPT Master FSD into a backend-executable
technical design. It defines services, APIs, data models,
synchronization rules, security controls, and compliance logic required
to implement the PFPT platform.

This document is intentionally implementation-focused and should be
treated as the authoritative backend blueprint.

------------------------------------------------------------------------

## 2. High-Level Architecture

### 2.1 System Components

    [ MAUI / Blazor Clients ]
              |
              v
    [ ASP.NET Core API ]
              |
              v
    [ EF Core + SQLite ]

    Optional future expansion:
    [ Central Sync API ] <--> [ Cloud Database ]

------------------------------------------------------------------------

## 3. Core Backend Principles

-   Offline-first, local persistence\
-   Signed clinical data is immutable\
-   AI never writes directly to storage\
-   All business rules enforced server-side\
-   Client-side validation is advisory only

------------------------------------------------------------------------

## 4. Authentication & Authorization

### 4.1 Authentication

-   PIN-based authentication (MVP)\
-   Future-ready for OAuth / SSO\
-   Session timeout: 15 minutes inactivity

### 4.2 Authorization

-   Role-based access control (RBAC)\
-   Enforced via:

``` csharp
[Authorize(Roles = "...")]
```

-   Domain-level guards in services

------------------------------------------------------------------------

## 5. Domain Entities (EF Core)

### 5.1 Patient

  Field          Type     Notes
  -------------- -------- --------------------
  Id             Guid     PK
  Mrn            string   Optional
  Demographics   JSON     Name, DOB, contact
  PayerInfo      JSON     Insurance/auth
  IsArchived     bool     Soft delete

### 5.2 IntakeResponse

  Field         Type   Notes
  ------------- ------ -----------------
  Id            Guid   PK
  PatientId     Guid   FK
  PainMapData   JSON   Body regions
  Consents      JSON   HIPAA/Treatment
  Locked        bool   Eval reference

### 5.3 ClinicalNote

  Field         Type        Notes
  ------------- ----------- ---------------------
  Id            Guid        PK
  PatientId     Guid        FK
  NoteType      enum        Eval/Daily/PN/DC
  Status        enum        Draft/Pending/Final
  Content       JSON        SOAP payload
  ContentHash   string      SHA-256
  SignedBy      Guid?       UserId
  SignedAt      DateTime?   Timestamp

### 5.4 ObjectiveMetric

  Field        Type     Notes
  ------------ -------- ----------------
  Id           Guid     PK
  NoteId       Guid     FK
  BodyPart     enum     Knee, Shoulder
  MetricType   enum     ROM/MMT
  Value        string   Degrees/Score
  IsWNL        bool     Auto-fill

### 5.5 AuditLog

  Field       Type       Notes
  ----------- ---------- ----------------
  Id          Guid       PK
  UserId      Guid       Actor
  Action      string     View/Edit/Sign
  EntityId    Guid       Target
  Timestamp   DateTime   UTC

------------------------------------------------------------------------

## 6. API Surface (Initial)

### 6.1 Patient APIs

    POST   /api/patients
    GET    /api/patients/{id}
    PUT    /api/patients/{id}
    GET    /api/patients/{id}/notes

### 6.2 Intake APIs

    POST /api/intake
    GET  /api/intake/{id}

### 6.3 Clinical Notes APIs

    POST /api/notes
    PUT  /api/notes/{id}        (Draft only)
    POST /api/notes/{id}/sign
    POST /api/notes/{id}/addendum

### 6.4 AI APIs (Internal)

    POST /api/ai/assessment
    POST /api/ai/plan

AI endpoints return text only. Persistence is client-mediated.

------------------------------------------------------------------------

## 7. Business Rules Engine

### 7.1 Progress Note Enforcement

-   PN required every 10 visits OR 30 days\
-   Hard stop blocks Daily Note creation

### 7.2 CPT 8-Minute Rule

-   Time-based CPT validation\
-   Warning on mismatch\
-   Override requires PT confirmation

------------------------------------------------------------------------

## 8. Offline Sync Strategy

### 8.1 Local Writes

-   All writes go to local SQLite first\
-   EF Core change tracking enabled

### 8.2 Sync Loop

-   Runs every \~30 seconds when online\
-   Uploads unsigned drafts first\
-   Signed notes are immutable

### 8.3 Conflict Resolution

-   Drafts: last-write-wins\
-   Signed notes: rejected

------------------------------------------------------------------------

## 9. AI Integration (Backend Guardrails)

-   AI receives sanitized DTOs only\
-   No PHI persistence\
-   No model fine-tuning\
-   Prompt templates versioned

------------------------------------------------------------------------

## 10. PDF Generation

-   Server-side rendering\
-   Locked notes only\
-   Watermark + signature footer

------------------------------------------------------------------------

## 11. Logging & Monitoring

-   Structured logs (no PHI)\
-   Audit trail mandatory\
-   Error correlation IDs

------------------------------------------------------------------------

## 12. Non-Functional Requirements

-   API response \< 500ms\
-   Local DB read \< 50ms\
-   AI response \< 5s\
-   PDF generation \< 5s

------------------------------------------------------------------------

## 13. Future Extensions

-   EMR API adapters\
-   Multi-clinic tenancy\
-   OAuth / SSO\
-   Central cloud sync

------------------------------------------------------------------------

This TDD is locked to the PFPT Master FSD and must be updated if scope
changes occur.
