# Humble Fax and Wibbi Operations Guide

## Scope and production gate

PTDoc is the system of record for patients, clinicians, episodes, clinical notes, and HEP prescriptions. Humble Fax is the fax transport. Wibbi owns its exercise catalog/media, delegated provider sessions, FlowSheet rendering, and patient-entered tracking.

Do not enable either clinic connection for production PHI until all of these are recorded:

- an executed BAA;
- vendor-risk/security approval, including subprocessors, hosting region, breach terms, and retention/deletion;
- production API entitlement, limits, sandbox evidence, and escalation contacts;
- tenant-isolation, RBAC, security, recovery, and clinic UAT evidence.

The connection API enforces this gate: an integration cannot be enabled without `ComplianceApproved=true` and a secret reference. Disabling a connection stops new provider work without disabling PTDoc chart or note workflows.

## Architecture

All external calls are server-side:

```text
Web or MAUI → PTDoc.Api → Application contract → provider adapter
                              ↓
                 relational outbox + mappings
                              ↓
                  restart-safe background worker
```

Provider payload types remain in `PTDoc.Integrations`. Provider-neutral entities and contracts are in Core/Application. EF persistence, private document storage, job leasing, notifications, and reconciliation are in Infrastructure. Browser and MAUI code never receive API credentials.

Integration records are scoped by clinic and `IntegrationConnectionId`. Global query filters and endpoint checks provide defense in depth. Outbox handlers use stable idempotency keys, leases, exponential retry, and dead-letter states.

## Configuration

### Shared infrastructure

Set private blob storage for hosted environments:

```text
AzureStorageConnectionString=<private-storage-connection>
Integrations__DocumentStore__ContainerName=integration-documents
```

Local Development and Testing use a private application data directory when Azure storage is absent. A hosted environment deliberately fails document writes when private storage is not configured.

Set Redis for multi-instance deployments and delegated Wibbi launch tickets:

```text
ConnectionStrings__Redis=<redis-connection-string>
```

Without Redis, PTDoc uses process-local distributed-memory cache. That fallback is suitable only for local development or a controlled single-instance environment; a scale-out deployment must configure Redis before enabling Wibbi.

Worker settings:

```text
Integrations__Worker__Enabled=true
Integrations__Worker__BatchSize=10
Integrations__Worker__PollInterval=00:00:10
```

Enable capabilities independently only after their release gates pass:

```text
Integrations__Features__EnableHumbleFax=true
Integrations__Features__EnableHumbleInboundFax=true
Integrations__Features__EnableWibbiProvisioning=true
Integrations__Features__EnableWibbiProgramPublishing=true
Integrations__Features__EnableWibbiTrackingSync=true
```

The clinic connection must also be enabled and compliance-approved. Turning off a capability stops new work and leaves already queued jobs pending for safe resumption; it does not erase local history or clinical records.

### Secret references

`IntegrationConnection.SecretReference` contains a configuration path, never a credential. For a reference such as:

```text
Integrations:Connections:<clinic-id>:HumbleFax
```

the configuration provider must expose:

```text
Integrations:Connections:<clinic-id>:HumbleFax:Username
Integrations:Connections:<clinic-id>:HumbleFax:Password
```

For environment variables, replace colons with double underscores. For Humble Fax, `Username` is the access key and `Password` is the secret key. For Wibbi, they are the license-admin username and password. Hosted secrets should be supplied by Key Vault/App Configuration integration or equivalent secret-backed configuration. Never put credential values in `ConfigurationJson`, database rows, logs, screenshots, tickets, or source control.

### Clinic connection documents

Humble Fax:

```json
{
  "baseUrl": "https://api.humblefax.com",
  "fromNumber": "15555555555"
}
```

Wibbi:

```json
{
  "baseUrl": "https://v4.api.wibbi.com",
  "entity": "vendor-issued-entity",
  "clinicLicenseId": "vendor-issued-clm-id",
  "locale": "en-US",
  "allowedLaunchHosts": ["wibbi.com", "physiotec.ca"]
}
```

Configure and verify each provider from **Settings → Clinical Integrations**. Humble webhook token rotation displays a token once. Configure the Humble callback as:

```text
https://<api-host>/api/v1/integrations/webhooks/humblefax/<one-time-token>
```

Because Humble's public documentation does not establish a signed callback contract, PTDoc treats the route token as ingress protection and retrieves the referenced fax using authenticated provider credentials. Keep WAF request limits enabled. Use polling instead of accepting callbacks if the security review rejects unsigned webhook delivery.

Connection-specific Wibbi `baseUrl` and `allowedLaunchHosts` values must be HTTPS DNS hosts within the deployment-level `Integrations:Hep:AllowedLaunchHosts` allowlist. Add an approved sandbox or production domain to deployment configuration before saving it on a clinic connection; the adapter and one-time broker deliberately enforce the same boundary.

For split Web/API deployments, set `Integrations__Hep__PublicBrokerBaseUrl` to the externally reachable HTTPS API origin. This keeps Wibbi's one-time launch broker on the API host instead of emitting a Web-host URL.

Hosted fax processing fails closed unless `Integrations__DocumentScanner__Host` points to a reachable ClamAV INSTREAM service (normally port `3310`). Development and Testing environments may use PDF signature/size validation without ClamAV; that fallback must never be used for production PHI.

Set `Integrations__Fax__RateLimitInstanceBudget` to at least the maximum number of concurrently running API worker instances sharing one Humble credential. PTDoc divides Humble's five-request-per-second credential budget across that value; do not scale beyond the configured budget without increasing it.

## Clinical workflows

### Outbound fax

1. Open Fax Center directly or the patient-scoped Fax History action.
2. Select a patient and an existing PDF chart document, enter one to three recipients, review cover content, and queue.
3. PTDoc commits the transmission and outbox work together and immediately displays `Queued`.
4. The worker streams the document to Humble. Provider acceptance, recipient status, failure, and final delivery are normalized into the PTDoc history.
5. Unknown submission outcomes enter `NeedsReconciliation` and are never blindly submitted again. An operator must verify provider history before initiating a new send.
6. Resend creates a linked transmission for unsuccessful recipients only.

Only signed notes may be used through the clinical-note fax API. Draft notes are rejected server-side.

The deprecated `POST /api/v1/integrations/fax/send` shape remains available for existing callers, but it now writes the same durable transmission/outbox records, applies patient-clinic validation and document scanning, and returns the PTDoc transmission ID. It no longer sends directly to Humble from the request thread.

### Inbound fax

1. Humble completion ingress queues authenticated retrieval.
2. PTDoc verifies the destination against the clinic number, downloads PDF content into private storage, hashes it, and places it in the clinic Inbox.
3. Admin or Front Desk staff preview the document, find the patient, select a document type, give an assignment reason, and attach it.
4. PTDoc creates a blob-backed patient document and immutable communication/timeline record. Reassignment uses the same audited endpoint and requires a reason.
5. A checkpointed five-minute `/incomingFaxes` poll with a two-minute overlap feeds the same idempotent retrieval path, recovering callbacks that are missed or disabled.

No name, email, or phone inference automatically attaches an inbound fax to a patient.

### Wibbi patient and clinician provisioning

- Patient creation/update and the Wibbi connection-scoped outbox item are committed together.
- PTDoc patient GUID is Wibbi `client_id`; PTDoc user GUID is `user_id`; PTDoc HEP GUID is `program_id` and episode `case_id`.
- First HEP publication/launch synchronously ensures clinician, patient, and episode records in case an earlier background job failed.
- On first synchronization, an active legacy `ExternalSystemMapping` for that PTDoc patient is promoted into the clinic connection before any Wibbi create call, preventing a second provider patient during migration.
- Existing mappings use `ModifyUser`/`ModifyClient`; first-time mappings use `AddUser`/`AddClient` with an update fallback for an uncertain previous create.
- Demographics remain PTDoc-owned. External identity changes are conflicts and never fuzzy-merged.

### HEP and FlowSheet

1. From a patient chart, open **Home Exercise Program**.
2. Search the Wibbi catalog within PTDoc, add stable provider exercise IDs, and enter prescription values.
3. **Save and publish** creates an immutable PTDoc revision and durable publication job.
4. Wibbi receives the entire desired revision, making retries convergent.
5. The clinician can continue in PTDoc, or use the one-time broker to open the Wibbi program/FlowSheet without a second login.
6. Tracking is refreshed every five minutes for active programs and on clinician request. Wibbi change polling uses a two-minute overlap and a five-minute checkpoint window.
7. Before tracking is requested, the adapter resolves Wibbi's numeric patient and program IDs with `GetClient` and `getPatientPrograms`, and requires an exact PTDoc `program_id` match. The IDs are cached for 15 minutes; programs are never chosen by name or list position.
8. A provider-side program change newer than PTDoc's publication becomes an explicit `ExternalProgramRevision` conflict; it never edits a signed note.

Tracking shown in PTDoc is read-only provider data with a timestamp. Clinician assessment remains separately authored in a PTDoc note.

The authenticated patient page renders the latest PTDoc prescription and exercise summary from local data before offering the one-time Wibbi launch. A provider outage can therefore remove media or interactive tracking temporarily without hiding the prescribed program stored in PTDoc.

## Authorization

| Policy | Roles |
|---|---|
| `FaxSend` | PT, PTA, Admin |
| `FaxRead` | PT, PTA, Admin, Owner, Front Desk |
| `FaxTriage` | Admin, Front Desk |
| `FaxAdmin` | Admin, Owner |
| `HepAuthor` | PT, PTA |
| `HepRead` | PT, PTA, Admin, Owner |
| `HepAdmin` | Admin, Owner |
| `PatientHepAccess` | Patient |

All policies are server enforced. Owner integration administration does not grant clinical note authorship or export rights.

## Monitoring and recovery

Monitor:

- `IntegrationOutboxItems` pending age, attempt count, and dead letters;
- Humble authentication/throttling, non-terminal fax age, delivery failures, and inbound triage age;
- Wibbi authentication, publication latency, stale checkpoints, tracking freshness, and open conflicts;
- webhook rejection/duplicates and private-storage failures.

Logs intentionally contain connection IDs, clinic IDs, internal entity IDs, operation codes, latency, and normalized result only. They must not contain names, DOB, contact values, fax numbers, file names/content, raw provider payloads, credentials, tokens, or delegated launch URLs.

Recovery rules:

- `429`, transport, and provider `5xx` failures retry with backoff and jitter.
- expired worker leases are reclaimed after restart; a reclaimed fax that had entered `Submitting` is moved to `NeedsReconciliation` instead of being sent again;
- disabled or compliance-unapproved clinic connections are excluded from leasing, so their queued work remains pending until the connection is safely re-enabled;
- authentication, disabled connections, invalid entities, and ambiguous fax submissions require operator action.
- never replay an ambiguous fax submission until provider history proves it was not accepted;
- an outbox replay is safe only when its provider operation is idempotent or reconciled;
- after an outage, watch oldest-job age and tracking checkpoint freshness until both return to normal.

Owner/Admin users can inspect dead-lettered work in **Settings → Clinical Integrations** and request a guarded replay. PTDoc refuses to replay a fax that may already have reached Humble or already has a provider ID; those records remain in reconciliation until provider history is confirmed.

## Database deployment

Apply the provider-specific `AddProductionIntegrations` migration before enabling the worker. The migration adds connection, mapping, outbox, checkpoint, conflict, webhook, fax, HEP, and tracking tables plus blob-backed patient-document references.

Deployment order:

1. apply migrations out of band;
2. configure blob storage, Redis for scale-out, and secret references;
3. deploy with provider connections disabled;
4. verify API readiness and worker telemetry;
5. save and verify one sandbox connection;
6. run synthetic fax/HEP workflows;
7. enable one approved pilot clinic;
8. expand clinic by clinic after reconciliation and UAT evidence.

## Verification commands

Follow repository policy and run these explicitly after restoring packages:

```bash
dotnet build
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter "Category=CoreCi" --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter "Category=RBAC" --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter "Category=DatabaseProvider" --verbosity normal
dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter "Category=Compliance" --verbosity normal
python3 .github/scripts/scan_secret_policy.py
```

Use vendor sandboxes and synthetic records for contract tests. Production verification must never use ad hoc real patient data.

## Remaining vendor confirmations

Before production, obtain written answers for Humble webhook signing/IP ranges, UUID search after ambiguous submission, cancellation, inbound formats, retention, and sandbox behavior. Obtain Wibbi confirmation for production limits, delegated-session lifetime/logout, webhook ordering/authentication, `GetChangesBetween` field contract, `LinkClient`, program idempotency, tracking correction/deletion semantics, media URL lifetime, and deactivation behavior.

Fallbacks remain: authenticated Humble polling/retrieval, checkpointed Wibbi polling, manual legacy-patient linking, explicit HEP conflicts, and connection disablement.
