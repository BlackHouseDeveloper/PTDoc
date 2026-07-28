# Azure Warning Remediation Tracker

## Purpose

This runbook tracks the three consolidated failures documented in
`Consolidated Error Analysis for the Azure Warnings Document.md`. The source
represents 138 pages, 69 raw error entries, 27 request-level occurrences, and
three unique root causes. The underlying raw Azure warning document is not
stored in this repository, so occurrence pages below refer to the source
analysis.

Do not add unrelated defects to this tracker. Do not place connection strings,
keys, prompts, patient data, or other PHI in commands, screenshots, logs, or
closure evidence.

## Status

| ID | Finding | Source evidence | Repository status | Environment status |
|---|---|---|---|---|
| REM-01 | SQL Server `Appointments` writes conflict with the overlap trigger because EF uses `OUTPUT` DML | Critical; 15 request occurrences, 57 raw entries; pages 1, 10, 18, 31, 40, 48, 58, 67, 76, 86, 95, 104, 113, 121, 129 | Implemented; verification required | Migration/application deployment and production-like smoke required |
| REM-02 | Azure OpenAI returns 404 for plan, prognosis, and assessment generation | High; 10 occurrences; pages 27, 28 (2), 29 (2), 30 (2), 84, 85, 128 | Endpoint guardrail and 404 regression coverage implemented; verification required | Azure resource/configuration reconciliation and canary required |
| REM-03 | Generic Azure SQL connection errors affect sync status and navigation badges | Severity undetermined; 2 occurrences; pages 103 and 128 | PHI-safe connection-attempt telemetry implemented; verification required | Telemetry correlation and evidence-selected infrastructure correction required |

## REM-01 Deployment and Validation

The SQL Server EF model disables `OUTPUT` only for `Appointments`. The
`TR_Appointments_PreventOverlap` trigger remains the database-level scheduling
guard. Migration `20260723010000_DisableAppointmentSqlOutputClause` is
metadata-only and intentionally emits no DDL.

### Pre-deployment checks

1. Review the migration and confirm `Up` and `Down` contain no trigger or table
   DDL.
2. Confirm the SQL Server model snapshot contains
   `UseSqlOutputClause(false)` only for `Appointments`.
3. Generate and review the provider migration script. It should add only the
   migration-history record for this migration.
4. In the target database, confirm the trigger remains present:

   ```sql
   SELECT OBJECT_ID(N'[dbo].[TR_Appointments_PreventOverlap]', N'TR') AS TriggerObjectId;
   ```

### Release order

1. Apply the reviewed SQL Server migration script out-of-band.
2. Confirm `/health/ready` and `/diagnostics/db`.
3. Deploy the API artifact.
4. Update appointment notes, type, start/end time, and duration through
   `PUT /api/v1/appointments/{id}`.
5. Check in the appointment twice through
   `POST /api/v1/appointments/{id}/check-in`; both requests must succeed and the
   persisted status must remain `CheckedIn`.
6. Attempt a genuine overlapping appointment and confirm the existing
   scheduling-conflict response is retained.
7. Query telemetry for SQL error 334 and require no recurrence during the
   validation window.

## REM-02 Azure OpenAI Reconciliation

PTDoc builds the chat-completions route. `AzureOpenAIEndpoint` must therefore be
the base HTTPS resource URL, with no `/openai/...` path, query string, fragment,
or embedded credentials. AI-enabled non-development startup now rejects any
other endpoint shape.

### Safe configuration order

1. Identify the exact API App Service and deployed release through release
   metadata. Do not assume a resource name from historical logs.
2. Set `FeatureFlags__EnableAiGeneration=false` on that API instance.
3. Compare the App Service's effective non-secret settings with the Azure
   resource:
   - `AzureOpenAIEndpoint`: base resource URL only.
   - `AzureOpenAIDeployment`: exact deployed name.
   - `AzureOpenAIApiVersion`: version supported by that deployment.
   - `AzureOpenAIKey`: secret from the same resource, configured through the
     approved secret mechanism.
4. Apply the endpoint, deployment, API version, and key as one reviewed
   configuration change. Do not place the key directly in a checked-in script
   or shell history.
5. Restart only the API and inspect the Admin/Owner diagnostics response:

   ```bash
   curl -H "Authorization: Bearer <admin-token>" \
     https://<api-host>/diagnostics/runtime
   ```

6. Require:
   - the expected release ID/source SHA;
   - `configurationState` equal to `Complete`;
   - the expected base endpoint, deployment, and API version;
   - `runtimeHealthGate` equal to
     `AuthenticatedSavedNoteAiRequestRequired`.
7. Use an approved synthetic saved note to run one Plan, one Prognosis, and one
   Assessment generation. Confirm each remains review-only until accepted.
8. Enable `FeatureFlags__EnableAiGeneration=true`, restart the API, and repeat
   one canary.
9. If any provider request returns 404, disable the feature flag again and
   retain the Azure response code plus sanitized Azure error code as evidence.

### Closure evidence

- Diagnostics response with the key omitted.
- Successful status/correlation IDs for all three AI operations.
- No `DeploymentNotFound` or generic Azure OpenAI 404 in the validation window.
- Confirmation that logs contain neither keys, prompt text, nor clinical
  content.

## REM-03 Azure SQL Investigation

The API already uses EF Core's SQL Server execution strategy with five retries
and a maximum retry delay of 30 seconds. Do not increase retry counts, change
connection-pool settings, scale Azure SQL, or change networking until the
failure is classified.

### Application Insights correlation

Use the occurrence window and request IDs from retained Azure telemetry. The
source analysis does not include timestamps, so first recover them from the
original warning export.

```kusto
requests
| where url has "/api/v1/sync/status"
    or url has "/api/v1/navigation/badges"
| project timestamp, operation_Id, name, resultCode, success, duration,
          cloud_RoleInstance
| order by timestamp desc
```

```kusto
dependencies
| where type =~ "SQL"
| where target has "ptdoc-sql-prod"
| project timestamp, operation_Id, target, name, resultCode, success, duration,
          cloud_RoleInstance
| order by timestamp desc
```

```kusto
traces
| where message startswith "Database connection attempt failed."
| project timestamp, operation_Id, severityLevel, cloud_RoleInstance,
          SqlErrorNumber=tostring(customDimensions.SqlErrorNumber),
          SqlErrorClass=tostring(customDimensions.SqlErrorClass),
          SqlErrorState=tostring(customDimensions.SqlErrorState),
          ClientConnectionId=tostring(customDimensions.ClientConnectionId),
          TraceId=tostring(customDimensions.TraceId),
          Route=tostring(customDimensions.Route),
          DbConnectionId=tostring(customDimensions.DbConnectionId),
          DbContextId=tostring(customDimensions.DbContextId),
          IsAsync=tostring(customDimensions.IsAsync)
| order by timestamp desc
```

If the logging provider stores structured values only inside the rendered
message, use `parse` on the same stable field names. Never add the exception
message or connection string to make the query easier.

### Classification gate

| Evidence | Required response |
|---|---|
| A failed connection attempt shares an operation ID with an eventual 2xx request | Treat as a handled retry; retain current retry behavior and monitor recurrence. |
| Known transient SQL errors exhaust retries and produce 5xx | Correlate with Azure SQL failover/resource metrics, verify a fresh connection per retry, and tune only the measured bottleneck. |
| Login/authentication error | Correct or rotate the approved App Service secret/identity, then restart the API. |
| Firewall, DNS, private endpoint, or routing failure | Correct the verified App Service/Azure SQL network path. |
| Pool or outbound exhaustion | Prove it through pool/SNAT metrics, then correct leaked/long-held connections or capacity. |
| SQL resource throttling | Tune the identified queries or scale only after Azure SQL metrics confirm saturation. |
| No exception number or correlated request outcome | Keep REM-03 open and collect the next recurrence with the new structured telemetry. |

### Validation

1. Run `/health/ready` and authenticated `/diagnostics/db`.
2. Exercise both affected endpoints at representative concurrent load.
3. In a non-production SQL Server environment, interrupt connectivity and
   confirm the trace shows individual attempts under one operation ID and either
   a recovered request or one safe final failure.
4. Confirm no retry or configuration change causes duplicate writes.
5. Close only after both original occurrences are classified or a richer
   recurrence is captured and the confirmed cause is corrected.

## Required Repository Verification

Per repository policy, request the project owner to run the heavy commands and
return their output:

```bash
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj \
  --filter "Category=CoreCi" \
  --verbosity normal

dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj \
  --filter "Category=DatabaseProvider" \
  --verbosity normal

dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj \
  --filter "Category=Observability" \
  --verbosity normal
```

The SQL Server `DatabaseProvider` run must use the CI-style migration command
and environment described in `docs/CI.md`. Do not commit until the relevant
build and test results are confirmed.

## Final Closure Checklist

- [ ] REM-01 provider and endpoint tests pass against SQL Server.
- [ ] The overlap trigger remains enabled and effective.
- [ ] No SQL error 334 recurs in the validation window.
- [ ] REM-02 runtime settings match the intended Azure resource.
- [ ] Plan, Prognosis, and Assessment canaries succeed without 404.
- [ ] REM-03 occurrences are classified and any confirmed cause is corrected.
- [ ] Connection diagnostics contain correlation fields but no PHI or secrets.
- [ ] `docs/CHANGELOG.md` and release evidence are current.
- [ ] All 69 source entries remain accounted for by REM-01 through REM-03.
