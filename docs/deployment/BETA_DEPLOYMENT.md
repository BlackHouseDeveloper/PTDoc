# PTDoc Beta Deployment

## Overview

The beta deployment uses two Azure App Services behind Cloudflare-managed custom domains. The frontend is a .NET 8 Blazor Web App using Interactive Server rendering, so it must run on Azure App Service with web sockets enabled. It is not an Azure Static Web Apps deployment.

| Role | Azure resource | Public URL |
|------|----------------|------------|
| Frontend | `ptdoc-web-prod` | `https://ptdoc.bhdevsites.com` |
| API | `ptdoc-api-plan` | `https://api-ptdoc.bhdevsites.com` |

- Resource group: `PTDoc-prod`
- Runtime: .NET 8
- DNS provider: Cloudflare
- Hosting provider: Azure App Service
- SSL: Azure App Service Managed Certificate
- TLS binding: SNI SSL

## Azure Settings

Enable these platform settings on both App Services:

- App Service plan: Standard tier or higher so deployment slots are available
- Deployment slot: `staging`
- HTTPS Only: enabled
- Managed Certificate: bound to the Cloudflare custom domain
- TLS/SSL binding type: SNI SSL
- Always On: enabled
- App Service Health Check path: `/health/live`
- Remote Debugging: disabled
- Snapshot Debugger: disabled; remove any installed Snapshot Debugger site extension

Enable this frontend-only setting:

- Web sockets: enabled on `ptdoc-web-prod`
- ARR affinity: enabled on `ptdoc-web-prod` and its `staging` slot

Use `/health/live` for the Azure App Service health-check path. Reserve `/health/ready` for deployment validation and pre-QA smoke checks because Web readiness also verifies that the configured API upstream responds.

Use `ASPNETCORE_ENVIRONMENT=Beta` on both App Services so `appsettings.Beta.json` is loaded.

## Required App Settings

Set these app settings on the frontend App Service:

```text
ASPNETCORE_ENVIRONMENT=Beta
ReverseProxy__Clusters__apiCluster__Destinations__api__Address=https://api-ptdoc.bhdevsites.com/
EntraExternalId__ClientSecret=<from secret store>
```

Set these app settings on the API App Service:

```text
ASPNETCORE_ENVIRONMENT=Beta
Database__Provider=SqlServer
Database__AutoMigrate=false
ConnectionStrings__DefaultConnection=<Azure SQL connection string>
Jwt__SigningKey=<minimum 32 character secret>
IntakeInvite__SigningKey=<minimum 32 character secret>
BetaAccess__AllowStartupSeed=true
BetaAccess__SeedPin=<4 digit beta access PIN from secret store>
BetaAccess__SeedLockTimeoutSeconds=15
IntakeInvite__PublicWebBaseUrl=https://ptdoc.bhdevsites.com
Communication__PublicBaseUrl=https://ptdoc.bhdevsites.com
Communication__RecipientHashSalt=<random high-entropy secret>
Communication__Azure__ConnectionString=<Azure Communication Services connection string>
Communication__Azure__EmailFromAddress=<verified ACS sender>
Communication__Azure__SmsFromPhoneNumber=<ACS SMS number>
AzureStorageConnectionString=<Azure Storage connection string>
Cors__AllowedOrigins__0=https://ptdoc.bhdevsites.com
FeatureFlags__EnableAiGeneration=false
Ai__MaxOutputTokens=400
Ai__RateLimits__PermitLimit=10
Ai__RateLimits__WindowMinutes=60
BackgroundJobs__SyncRetry__Interval=00:05:00
BackgroundJobs__SyncRetry__MinRetryDelay=00:05:00
BackgroundJobs__SessionCleanup__Interval=00:30:00
```

AI generation is disabled by default in Beta to control Azure OpenAI spend. If AI generation is deliberately enabled for beta, keep the rate-limit settings above and also set the existing Azure OpenAI settings:

```text
FeatureFlags__EnableAiGeneration=true
AzureOpenAIEndpoint=<Azure OpenAI endpoint>
AzureOpenAIKey=<Azure OpenAI key>
AzureOpenAIDeployment=<deployment name>
AzureOpenAIApiVersion=<API version>
```

Do not commit real connection strings, signing keys, publish profiles, ACS credentials, Azure OpenAI keys, or Entra client secrets.

Configure the API `staging` slot with `BetaAccess__AllowStartupSeed=false` and mark it as a deployment-slot setting. Keep startup seeding enabled only on the production API slot. Mark secrets, connection strings, and slot-specific upstream addresses as deployment-slot settings so a swap does not move staging configuration into production.

## Seeded Beta Access

When the API runs with `ASPNETCORE_ENVIRONMENT=Beta`, startup seeds a small, idempotent access fixture for manual beta validation. These accounts are not seeded in Production.
Because Beta uses `Database__AutoMigrate=false`, apply database migrations out-of-band before starting or redeploying the API. If the database is unreachable or migrations are pending, the API logs a warning and skips Beta access seeding for that startup.
Startup seeding is enabled only through `BetaAccess__AllowStartupSeed=true`, and is approved only while the Beta API App Service remains a controlled single-instance deployment. The seeder waits up to `BetaAccess__SeedLockTimeoutSeconds` for the SQL Server application lock. A `-1`, `-2`, or `-3` lock result is reported as `SkippedLockContention`; it is not a database failure. The single sanitized startup outcome is one of `Completed`, `AlreadyCurrent`, `SkippedLockContention`, `SkippedConfiguration`, `SkippedDatabase`, or `Failed` and includes duration plus the numeric lock result when available.
The shared Beta seed PIN is not committed; configure it through the API App Service setting `BetaAccess__SeedPin` and rotate it from Azure when needed. The current Beta PIN is managed in Azure settings.

Keep Beta on a single App Service instance with autoscale disabled unless a release explicitly documents a different operating model. If scale-out is enabled, disable startup seeding first or verify the SQL lock behavior immediately after deployment.

| Username | Email | Role |
|----------|-------|------|
| `january.beta` | `january.beta@physicallyfitpt.test` | Admin |
| `dani.beta` | `dani.beta@physicallyfitpt.test` | PT |
| `pta.beta` | `pta.beta@physicallyfitpt.test` | PTA |
| `patient.beta` | `patient.beta@physicallyfitpt.test` | Patient |

The seeded clinic is `Physically Fit Physical Therapy` with slug `pfpt-beta`. The Beta seeder is authoritative for these test accounts so access remains predictable after redeploys.

The same Beta startup seed also creates a small idempotent PFPT patient directory fixture under the seeded clinic. These records use non-real `.test` email addresses and deterministic MRNs `BETA-PT-001` through `BETA-PT-004` so Beta users can validate Patients search by name, MRN, and email, open patient profiles, and start the existing intake invite workflow without creating live patient data first.

Use [PTDoc Beta QA](../BETA_QA.md) for the tester checklist, known limitations, bug report format, and beta pass/fail gate.

Required Beta database order:

1. Apply EF Core migrations out-of-band to Azure SQL.
2. Confirm `https://api-ptdoc.bhdevsites.com/health/ready` is healthy.
3. Start or restart the API App Service with `ASPNETCORE_ENVIRONMENT=Beta`.
4. Confirm the API logs show the Beta seed completed, or show a deliberate skip because another lock holder was active.
5. Validate the seeded Admin, PT, PTA, and Patient users can sign in with the configured Beta PIN.
6. Restart the API again when needed and verify no duplicate seeded users, clinic, or patient fixtures are created.

## GitHub Actions Deployment

Use the manual `Deploy Beta` workflow. It builds and tests once, deploys the API to `staging`, validates and swaps the API, then deploys and swaps Web. Web deployment does not begin if the API deployment or post-swap checks fail.

Required GitHub secrets:

```text
AZURE_CLIENT_ID
AZURE_TENANT_ID
AZURE_SUBSCRIPTION_ID
PTDOC_BETA_SEED_PIN
```

The first three values identify the beta-environment Azure OIDC deployment identity. `AZURE_CLIENT_ID` must be the application (client) ID of the federated identity, `AZURE_TENANT_ID` must be that identity's directory tenant, and `AZURE_SUBSCRIPTION_ID` must be the subscription containing `PTDoc-prod`. The federated credential must trust this exact GitHub environment subject:

```text
repo:BlackHouseDeveloper/PTDoc:environment:beta
```

Grant the identity the built-in `Website Contributor` role at the `PTDoc-prod` resource-group scope. The workflow must read the App Service plans and app configuration, deploy both apps and their staging slots, and swap slots; app-only deployment access without plan-read access is insufficient. If the resource group later contains unrelated Web resources, replace the resource-group assignment with `Website Contributor` on both target apps plus `Reader` on their backing App Service plans. Do not grant subscription-wide Contributor access and do not restore publish-profile credentials.

An Azure administrator can validate the identity and assignment before dispatching the workflow. Use the real IDs without committing or pasting them into issue text:

```bash
az account show --subscription "<AZURE_SUBSCRIPTION_ID>" --query '{id:id,tenantId:tenantId,state:state}'
az ad sp show --id "<AZURE_CLIENT_ID>" --query '{appId:appId,objectId:id}'
az role assignment list \
  --assignee-object-id "<SERVICE_PRINCIPAL_OBJECT_ID>" \
  --all \
  --include-inherited \
  --query "[?roleDefinitionName=='Website Contributor'].{role:roleDefinitionName,scope:scope}"
```

Set `SERVICE_PRINCIPAL_OBJECT_ID` to the `objectId` returned by the preceding `az ad sp show` command; it is not the application (client) ID stored in `AZURE_CLIENT_ID`.

If the assignment is absent, an Azure administrator can add the resource-group-scoped role with the service principal's object ID:

```bash
az role assignment create \
  --assignee-object-id "<SERVICE_PRINCIPAL_OBJECT_ID>" \
  --assignee-principal-type ServicePrincipal \
  --role "Website Contributor" \
  --scope "/subscriptions/<AZURE_SUBSCRIPTION_ID>/resourceGroups/PTDoc-prod"
```

After confirming the Azure values, store the client, tenant, and subscription IDs as secrets on the GitHub `beta` environment, wait for Azure RBAC propagation, and rerun `Deploy Beta`. `No subscriptions found` from `azure/login` means the configured identity cannot see the configured subscription: recheck the three IDs and the role assignment. Do not set `allow-no-subscriptions: true`; every subsequent validation, deployment, and slot-swap command requires subscription-backed Azure Resource Manager access. `PTDOC_BETA_SEED_PIN` is masked by GitHub and is used only for the post-API-swap seeded-role login smoke check.

Deployment order and rollback contract:

1. Apply database migrations out-of-band.
2. Deploy API artifact to the `staging` slot and verify staging `/health/live` and `/health/ready`.
3. Swap API `staging` to production, verify production health, and sign in all four seeded roles.
4. Reverse the API swap immediately if any post-swap check fails. Do not deploy Web after an API failure.
5. Deploy Web artifact to `staging`, with its API upstream configured for the production API, and verify Web `/health/live` and `/health/ready`.
6. Swap Web `staging` to production and verify both Web health endpoints.
7. Reverse the Web swap immediately if post-swap health or SignalR validation fails.

After changing Azure diagnostics settings, restart one service at a time and verify `Production Breakpoint Instrumentation Method` initialization errors are absent across two restarts. Keep normal Application Insights request and dependency telemetry enabled.

The workflow publishes:

```bash
dotnet publish src/PTDoc.Web/PTDoc.Web.csproj -c Release -o ./publish/web
dotnet publish src/PTDoc.Api/PTDoc.Api.csproj -c Release -o ./publish/api
```

## Smoke Checklist

- Open `https://ptdoc.bhdevsites.com`.
- Confirm `https://ptdoc.bhdevsites.com/health/live` and `/health/ready` are healthy.
- Open `https://api-ptdoc.bhdevsites.com/health`.
- Confirm `https://api-ptdoc.bhdevsites.com/health/live` is healthy for frequent platform probes.
- Confirm `http://ptdoc.bhdevsites.com` redirects to HTTPS.
- Confirm `http://api-ptdoc.bhdevsites.com/health` redirects to HTTPS.
- Confirm frontend API calls use `https://api-ptdoc.bhdevsites.com`.
- Confirm the API App Service is still single-instance before relying on startup seeding.
- Confirm `Database__AutoMigrate=false`, production `BetaAccess__AllowStartupSeed=true`, staging `BetaAccess__AllowStartupSeed=false`, `BetaAccess__SeedLockTimeoutSeconds=15`, and `BetaAccess__SeedPin` are configured in Azure.
- Confirm AI generation remains disabled unless a beta pass explicitly needs it, and if enabled, confirm `Ai__RateLimits__PermitLimit=10` and `Ai__RateLimits__WindowMinutes=60`. The legacy key `Ai__RateLimits__RequestsPerHour` is still accepted for existing environments, but new settings should use `PermitLimit`.
- Confirm `https://api-ptdoc.bhdevsites.com/health/ready` is healthy before validating seeded access.
- Confirm seeded Beta users can sign in with the configured `BetaAccess__SeedPin`.
- Confirm a new signup receives the pending administrator approval message instead of a generic login failure.
- Confirm no beta network calls use `localhost`, `127.0.0.1`, `devtunnels.ms`, or temporary `azurewebsites.net` URLs.
- Confirm Blazor Interactive Server connections stay established after login and navigation.
- Confirm reconnect UI appears during an induced interruption and offers Retry or Reload when the circuit cannot resume.
- Confirm exactly one acceptable Beta seed outcome is logged for the production startup and no new Production Breakpoint instrumentation error appears after two controlled restarts.
