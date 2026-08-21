# Settings Architecture Decisions

**Status:** Binding
**Scope:** Clinic Settings administration, authorization, authentication, scheduling, reminders, Auto Check-In, and Kiosk Check-In

## Binding product and architecture decisions

1. Dynamic permissions are clinic-scoped and enforced by the server. UI visibility is never an authorization boundary.
2. Visit types are persisted clinic records referenced by stable IDs. Appointment enum/string fields remain compatibility data only during the migration window.
3. Staff authentication uses numeric PIN credentials and TOTP MFA with hashed single-use recovery codes. These choices are product decisions and are not represented as phishing-resistant or as a formal assurance-level certification.
4. Every Settings mutation is tenant-scoped, authorized, validated, versioned with `long Version`, and audited without PHI.
5. Clients submit `ExpectedVersion`. A stale mutation returns `409 Conflict`; field validation returns `422`; unauthenticated requests return `401`; insufficient capability returns `403`; tenant-safe missing resources return `404`.
6. Mandatory compliance/security auditing is permanently enabled and is not configurable from Settings.

## Authorization rollout

Dynamic authorization supports three modes:

- `Static`: the canonical role policy is authoritative.
- `Shadow`: the canonical role policy remains authoritative and differences from clinic configuration are emitted as identifier-only telemetry/audit events.
- `Enforced`: the dynamic capability decision is authoritative, while tenant, patient-self, signed-note, PTA-supervision, last-recovery-admin, and other domain guards remain mandatory.

Missing or invalid permission rows fail closed to the canonical restrictive baseline. Locked minima cannot be reduced. Unsupported capabilities remain `None` and cannot create access to endpoints that do not exist.

## Security defaults and migration

- Clinic time zone: `America/Los_Angeles` using an IANA identifier.
- Session inactivity: 15 minutes, valid range 5–60.
- New or changed PIN: 8–12 numeric digits.
- Existing four-digit PIN hashes: accepted only during the 14-day migration grace period; a compliant PIN is required at reset, forced change, or grace-period expiration.
- PIN changes are event-driven (first login, reset, suspected compromise, or administrator action); periodic expiration is disabled.
- MFA: TOTP with recovery codes. Enrollment is verified before activation, accepted time steps cannot be replayed, and reset forces re-enrollment.
- Entra External ID satisfies MFA only when the validated token explicitly contains `mfa` in its `amr` claim. Policy-specific ACR values are not inferred. An external token without that assurance is denied when clinic enforcement is active; the user can instead complete PTDoc's local PIN/TOTP workflow.

The Web session and API/MAUI JWT paths use the same staged authentication state machine. Primary
PIN verification can return a short-lived purpose-bound PIN-change, MFA-enrollment, or
MFA-verification challenge. Neither a Web session nor an access/refresh-token pair is issued until
all required stages complete. Existing four-digit credentials are confined to the migration grace
path; registration, reset, and required-change paths accept only 8–12 numeric digits.

## Scheduling and compatibility

Clinic-local availability is evaluated centrally from IANA time-zone business hours, optional lunch intervals, recurring blocks, configured buffer, existing appointment overlap, and own-schedule restrictions. Daylight-saving gaps and ambiguous local times must be handled explicitly.

Appointment visit-type migration is release-oriented:

1. Add nullable `VisitTypeId`, seed the 12 canonical clinic visit types, and backfill known legacy values.
2. Dual-read/write stable IDs and legacy appointment type fields while Web and MAUI clients migrate.
3. Require the foreign key, retain legacy response fields for one compatibility release, and then remove enum dependency.

Visit types referenced historically are soft-deactivated. Authorized double booking is persisted explicitly so provider-specific database overlap guards can distinguish approved overlap from accidental or concurrent conflicts.

Provider migrations `20260821041512_AddClinicSettingsAdministration` (SQLite),
`20260821041521_AddClinicSettingsAdministration` (SQL Server), and
`20260821041528_AddClinicSettingsAdministration` (PostgreSQL) add the settings/authentication
schema, seed existing clinics, backfill known legacy appointment types, and revise each provider's
overlap trigger to honor only the explicit authorized-overlap flag. Production operators generate
and review scripts and apply migrations before enabling new read/write paths; the API does not
auto-apply them in Production by default.

## Administration boundaries

Roles, Security, Scheduling, Auto Check-In, and Kiosk administration are Web-only surfaces. Authentication decisions and server-side authorization apply equally to Web and MAUI clients. Kiosk credentials are clinic/station scoped and cannot access patient charts, notes, billing, Settings, or unrestricted appointment search.

The approved Documentation & Compliance specification is implemented as a frontend review surface only. Its controls maintain a local draft, Reset applies documented defaults locally, Cancel restores the loaded snapshot, and Save is disabled with an explicit contract-boundary explanation. No documentation enforcement or persistence behavior is inferred until an Application-layer contract is approved. Billing & Coding and AI & Outcome Measures remain on `DeferredSettingsSection` for the same reason and require their own approved UI architecture specifications before implementation.
