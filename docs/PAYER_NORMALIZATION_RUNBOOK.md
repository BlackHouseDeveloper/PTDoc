# Payer Normalization Runbook

## Purpose

This runbook governs the staged migration from `Patient.PayerInfoJson` to tenant-scoped `PatientInsurancePolicy`, `PatientInsuranceAuthorization`, and `PatientProviderRelationship` records. The source JSON remains intact during the compatibility period and must not be deleted as part of backfill or reconciliation.

## Deployment order

1. Apply `AddEnterpriseDirectoryInsuranceTemplates` for the configured database provider before deploying API or clients that use the normalized endpoints.
2. Deploy the API with dual-read and dual-write support. Intake and patient payer save paths continue writing `PayerInfoJson` and also upsert normalized records without erasing omitted values.
3. Run the tenant-scoped backfill as an Admin or Owner with `POST /api/v1/admin/insurance-policies/backfill` once for each clinic.
4. Capture the returned counts and the PHI-safe lists of malformed or ambiguous patient IDs. Correct source records or resolve provider candidates through Settings, then rerun. The operation is idempotent and skips normalized records already present.
5. Reconcile normalized records against legacy JSON before changing read precedence. Dashboard authorization alerts and appointment copay reads already prefer normalized records and fall back to JSON only when the relevant normalized record is absent.
6. After every supported Web/MAUI/API version reads normalized collections, stop legacy JSON writes in a separate release and mark the legacy contract obsolete.
7. Remove `PayerInfoJson` only in a later contract migration after sync-client telemetry confirms no supported client requests it.

## Preflight

- Take a database backup and confirm restore procedures.
- Confirm the migration assembly matches `Database:Provider`.
- Verify every patient and new normalized row has the expected `ClinicId`.
- Confirm Admin/Owner access for the backfill and provider review queue.
- Do not place patient, provider, carrier, member, or authorization names/numbers in operational logs.

## Reconciliation

For each clinic compare:

- patients with non-empty payer JSON versus patients with at least one normalized policy;
- primary/secondary carrier, member, group, payer type, dates, and cost-sharing values;
- authorization/reference status, dates, authorized/used units, reauthorization date, and alert threshold;
- legacy referring-provider fields versus patient-provider relationships;
- malformed/ambiguous IDs returned by the backfill.

A count difference is acceptable only when the source record is empty, malformed, a duplicate, or intentionally represented as history. Record the reason without copying PHI into deployment notes.

## Rollback

During dual-read/dual-write releases, application rollback is safe because source JSON is retained. Do not run the migration `Down` operation after normalized writes have begun unless those rows have been exported and the loss is explicitly approved. If backfill produces unexpected results, stop the job, retain both representations, correct the parser or source ambiguity, and rerun idempotently.

## Verification commands

Follow `docs/EF_MIGRATIONS.md` for provider-specific commands. At minimum, run the provider migration smoke tests and `dotnet ef migrations has-pending-model-changes` for SQLite, SQL Server, and PostgreSQL before release.
