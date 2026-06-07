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

- HTTPS Only: enabled
- Managed Certificate: bound to the Cloudflare custom domain
- TLS/SSL binding type: SNI SSL

Enable this frontend-only setting:

- Web sockets: enabled on `ptdoc-web-prod`

Use `/health/live` for the Azure App Service health-check path if health checks are enabled. Reserve `/health/ready` for deployment validation and pre-QA smoke checks because readiness can exercise dependencies that cost more to probe frequently.

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
Ai__RateLimits__RequestsPerHour=10
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

## Seeded Beta Access

When the API runs with `ASPNETCORE_ENVIRONMENT=Beta`, startup seeds a small, idempotent access fixture for manual beta validation. These accounts are not seeded in Production.
Because Beta uses `Database__AutoMigrate=false`, apply database migrations out-of-band before starting or redeploying the API. If the database is unreachable or migrations are pending, the API logs a warning and skips Beta access seeding for that startup.
Startup seeding is enabled only through `BetaAccess__AllowStartupSeed=true`, and is approved only while the Beta API App Service remains a controlled single-instance deployment. The seeder also acquires a SQL Server application lock before writes so an accidental overlapping startup skips seeding instead of racing duplicate inserts. If Beta is scaled beyond one instance, either disable startup seeding or keep the lock-protected path in place and verify the logs after each deployment.
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

Use the manual `Deploy Beta` workflow. It builds, tests, publishes, and deploys the Web and API artifacts separately.

Required GitHub secrets:

```text
AZURE_WEBAPP_PUBLISH_PROFILE_PTDOC_WEB_PROD
AZURE_WEBAPP_PUBLISH_PROFILE_PTDOC_API_PLAN
```

The workflow publishes:

```bash
dotnet publish src/PTDoc.Web/PTDoc.Web.csproj -c Release -o ./publish/web
dotnet publish src/PTDoc.Api/PTDoc.Api.csproj -c Release -o ./publish/api
```

## Smoke Checklist

- Open `https://ptdoc.bhdevsites.com`.
- Open `https://api-ptdoc.bhdevsites.com/health`.
- Confirm `https://api-ptdoc.bhdevsites.com/health/live` is healthy for frequent platform probes.
- Confirm `http://ptdoc.bhdevsites.com` redirects to HTTPS.
- Confirm `http://api-ptdoc.bhdevsites.com/health` redirects to HTTPS.
- Confirm frontend API calls use `https://api-ptdoc.bhdevsites.com`.
- Confirm the API App Service is still single-instance before relying on startup seeding.
- Confirm `Database__AutoMigrate=false`, `BetaAccess__AllowStartupSeed=true`, and `BetaAccess__SeedPin` are configured in Azure.
- Confirm AI generation remains disabled unless a beta pass explicitly needs it, and if enabled, confirm `Ai__RateLimits__RequestsPerHour=10` and `Ai__RateLimits__WindowMinutes=60`.
- Confirm `https://api-ptdoc.bhdevsites.com/health/ready` is healthy before validating seeded access.
- Confirm seeded Beta users can sign in with the configured `BetaAccess__SeedPin`.
- Confirm a new signup receives the pending administrator approval message instead of a generic login failure.
- Confirm no beta network calls use `localhost`, `127.0.0.1`, `devtunnels.ms`, or temporary `azurewebsites.net` URLs.
- Confirm Blazor Interactive Server connections stay established after login and navigation.
