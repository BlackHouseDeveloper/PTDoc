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
IntakeInvite__PublicWebBaseUrl=https://ptdoc.bhdevsites.com
Communication__PublicBaseUrl=https://ptdoc.bhdevsites.com
Communication__RecipientHashSalt=<random high-entropy secret>
Communication__Azure__ConnectionString=<Azure Communication Services connection string>
Communication__Azure__EmailFromAddress=<verified ACS sender>
Communication__Azure__SmsFromPhoneNumber=<ACS SMS number>
AzureStorageConnectionString=<Azure Storage connection string>
Cors__AllowedOrigins__0=https://ptdoc.bhdevsites.com
```

If AI generation is enabled for beta, also set the existing Azure OpenAI settings:

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

| Username | Email | Role | PIN |
|----------|-------|------|-----|
| `january.beta` | `january.beta@physicallyfitpt.test` | Admin | `1234` |
| `dani.beta` | `dani.beta@physicallyfitpt.test` | PT | `1234` |
| `pta.beta` | `pta.beta@physicallyfitpt.test` | PTA | `1234` |
| `patient.beta` | `patient.beta@physicallyfitpt.test` | Patient | `1234` |

The seeded clinic is `Physically Fit Physical Therapy` with slug `pfpt-beta`. The Beta seeder is authoritative for these test accounts so access remains predictable after redeploys.

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
- Confirm `http://ptdoc.bhdevsites.com` redirects to HTTPS.
- Confirm `http://api-ptdoc.bhdevsites.com/health` redirects to HTTPS.
- Confirm frontend API calls use `https://api-ptdoc.bhdevsites.com`.
- Confirm seeded Beta users can sign in with PIN `1234`.
- Confirm a new signup receives the pending administrator approval message instead of a generic login failure.
- Confirm no beta network calls use `localhost`, `127.0.0.1`, `devtunnels.ms`, or temporary `azurewebsites.net` URLs.
- Confirm Blazor Interactive Server connections stay established after login and navigation.
