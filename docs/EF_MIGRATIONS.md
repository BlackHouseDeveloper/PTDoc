# EF Core Migrations

## Overview

PTDoc uses Entity Framework Core migrations for database schema management.
Migrations are split into provider-specific assemblies (Sprint B architecture).
The `ApplicationDbContext` lives in `PTDoc.Infrastructure`; migrations live in
separate class library projects, one per provider.

## Provider Configuration

Set the active database provider in `appsettings.json` (or environment):

```json
{
  "Database": {
    "Provider": "Sqlite"
  }
}
```

Supported values:

| Value | Provider | Package |
|-------|----------|---------|
| `Sqlite` | SQLite (default, local development) | `Microsoft.EntityFrameworkCore.Sqlite` |
| `SqlServer` | Microsoft SQL Server | `Microsoft.EntityFrameworkCore.SqlServer` |
| `Postgres` | PostgreSQL | `Npgsql.EntityFrameworkCore.PostgreSQL` |

For `SqlServer` and `Postgres`, also provide:

```json
{
  "ConnectionStrings": {
    "PTDocsServer": "Server=...;Database=PTDoc;..."
  }
}
```

## Project Structure

| Project | Purpose |
|---------|---------|
| `PTDoc.Infrastructure` | `ApplicationDbContext`, interceptors, seeders |
| `PTDoc.Infrastructure.Migrations.Sqlite` | SQLite migration files |
| `PTDoc.Infrastructure.Migrations.SqlServer` | SQL Server migration files |
| `PTDoc.Infrastructure.Migrations.Postgres` | PostgreSQL migration files |
| `PTDoc.Api` | Startup project for EF CLI and runtime |

## Common Commands

### Inspect Database Context

```bash
EF_PROVIDER=sqlite dotnet ef dbcontext info \
  -p ./src/PTDoc.Infrastructure \
  -s ./src/PTDoc.Api
```

### Create New Migration (SQLite – default)

#### SQLite

```bash
EF_PROVIDER=sqlite dotnet ef migrations add MigrationName \
  -p ./src/PTDoc.Infrastructure.Migrations.Sqlite \
  -s ./src/PTDoc.Api \
  --context PTDoc.Infrastructure.Data.ApplicationDbContext
```

#### SQL Server

```bash
EF_PROVIDER=sqlserver \
  Database__ConnectionString="Server=localhost,1433;Database=PTDoc_Dev;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True" \
  Jwt__SigningKey="ef-tooling-only-placeholder-key-min-32-chars!" \
  dotnet ef migrations add MigrationName \
  -p ./src/PTDoc.Infrastructure.Migrations.SqlServer \
  -s ./src/PTDoc.Api \
  --context PTDoc.Infrastructure.Data.ApplicationDbContext
```

#### PostgreSQL

```bash
EF_PROVIDER=postgres \
  Database__ConnectionString="Host=localhost;Port=5432;Database=ptdoc_dev;Username=postgres;Password=postgres" \
  Jwt__SigningKey="ef-tooling-only-placeholder-key-min-32-chars!" \
  dotnet ef migrations add MigrationName \
  -p ./src/PTDoc.Infrastructure.Migrations.Postgres \
  -s ./src/PTDoc.Api \
  --context PTDoc.Infrastructure.Data.ApplicationDbContext
```

> **Note:** `Jwt__SigningKey` is required because `PTDoc.Api` validates it at design-time.
> The value is only used to satisfy startup validation and is **not** a real secret.
> It must be **at least 32 characters** or the startup check will throw before EF CLI can run.

### Apply Migrations

#### SQLite (local development)

```bash
EF_PROVIDER=sqlite dotnet ef database update \
  -p ./src/PTDoc.Infrastructure.Migrations.Sqlite \
  -s ./src/PTDoc.Api
```

#### SQL Server

```bash
dotnet ef database update \
  -p ./src/PTDoc.Infrastructure.Migrations.SqlServer \
  -s ./src/PTDoc.Api
```

#### PostgreSQL

```bash
dotnet ef database update \
  -p ./src/PTDoc.Infrastructure.Migrations.Postgres \
  -s ./src/PTDoc.Api
```

### Remove Last Migration

```bash
# SQLite example (same pattern for other providers)
EF_PROVIDER=sqlite dotnet ef migrations remove \
  -p ./src/PTDoc.Infrastructure.Migrations.Sqlite \
  -s ./src/PTDoc.Api
```

### Generate SQL Script

```bash
EF_PROVIDER=sqlite dotnet ef migrations script \
  -p ./src/PTDoc.Infrastructure.Migrations.Sqlite \
  -s ./src/PTDoc.Api \
  -o migration_sqlite.sql
```

## PTDoc-Foundry.sh

The helper script uses SQLite by default:

```bash
./PTDoc-Foundry.sh --create-migration   # creates SQLite migration
./PTDoc-Foundry.sh --seed               # seeds dev data
```

## Web Runtime Considerations

- The web runtime (`PTDoc.Web`) must NOT reference EF Core or database providers.
- All data access from the web client goes through the HTTP API.
- This maintains clean separation between client and server concerns.

## Encryption (SQLite only)

SQLite encryption via SQLCipher is available when `Database:Encryption:Enabled = true`.
See `docs/SECURITY.md` for key management requirements.
Encryption is transparent to the migrations assembly — the same
`PTDoc.Infrastructure.Migrations.Sqlite` assembly is used with or without encryption.

## Production Deployment

### Overview

Migrations are **not applied automatically in production** by default
(`Database:AutoMigrate` defaults to `false` when `ASPNETCORE_ENVIRONMENT` is not
`Development`).  Apply them explicitly during your deployment process using the
EF Core CLI commands below.

### Trigger-backed Appointments table

SQL Server migration `20260330234500_PreventAppointmentOverbooking` creates
`TR_Appointments_PreventOverlap`. The shared EF model conditionally disables
SQL Server `OUTPUT` DML for `Appointments` so inserts and updates remain
compatible with that trigger. Migration
`20260723010000_DisableAppointmentSqlOutputClause` records the model annotation
and intentionally emits no table or trigger DDL. Do not remove or disable the
overlap trigger when applying or rolling back application releases.

Migration `AddClinicSettingsAdministration` updates that trigger for all three providers. It keeps
overlap rejection as the default and bypasses it only when the new appointment row has
`AuthorizedOverlap = true`, which is set only after centralized scheduling policy evaluation.
The same migration:

- adds clinic security, TOTP/recovery, role-capability, visit-type, scheduling, reminder,
  Auto Check-In, and kiosk tables;
- seeds every existing clinic with 270 role-capability rows, 12 visit types, seven business-hour
  rows, and canonical security/scheduling/Auto Check-In policies;
- adds nullable `Appointments.VisitTypeId` and backfills all four known values (initial evaluation,
  follow-up, discharge, and re-evaluation) from the legacy `Appointments.AppointmentType` column;
- binds appointment visit-type references and kiosk enrollment-code station references through
  clinic-qualified composite foreign keys, while kiosk enrollment codes and check-in tokens also
  carry direct clinic foreign keys;
- binds each reminder dispatch to an appointment through `(ClinicId, AppointmentId)`, backed by a
  temporary unique `(ClinicId, Id)` appointment index while legacy appointment clinic IDs remain
  nullable, so reminder processing cannot cross clinic boundaries;
- binds optional clinician schedule blocks through `(ClinicId, ClinicianId)`, backed by the same
  temporary clinic-qualified index pattern on legacy nullable user clinic IDs, so block rules cannot
  retain orphaned or cross-clinic clinician references;
- leaves `Appointments.AppointmentType` in place for the dual-read/write compatibility release.

Apply schema changes before enabling the related API/UI paths. Do not make `VisitTypeId` required or
remove the legacy field until Web and MAUI have completed the compatibility release.

The SQLite provider performs an explicit transactional `Appointments` rebuild while foreign-key
enforcement is temporarily disabled, re-enables enforcement immediately afterward, and then
recreates the overlap triggers. Migration validation must run `PRAGMA foreign_key_check` and verify
both insert/update overlap triggers after upgrade and downgrade. Current provider overlap guards
always validate inserts, but update validation runs only when clinician, interval, active/cancelled
semantics, or the explicit authorization marker changes; ordinary note and metadata updates must
remain writable after an approved double booking.

### Environment Variables — Runtime API

These variables are read by the API at startup:

| Variable | Purpose | Example |
|----------|---------|---------|
| `ASPNETCORE_ENVIRONMENT` | Set to `Production` for production deployments | `Production` |
| `Database__Provider` | Database provider (`SqlServer` or `Postgres`) | `SqlServer` |
| `ConnectionStrings__PTDocsServer` | Full runtime connection string | `Server=db;Database=PTDoc;...` |
| `Jwt__SigningKey` | JWT signing secret (≥ 32 chars) | *(from secrets manager)* |
| `Database__AutoMigrate` | Override auto-migrate behavior (optional) | `false` |

### Environment Variables — EF Core CLI (`dotnet ef`)

The `DesignTimeDbContextFactory` reads these variables when you run `dotnet ef` commands:

| Variable | Purpose | Example |
|----------|---------|---------|
| `EF_PROVIDER` | Provider for `dotnet ef` tools | `sqlserver` or `postgres` |
| `Database__ConnectionString` | Connection string for EF CLI only | `Server=db;Database=PTDoc;...` |

> **Security:** Never commit connection strings or signing keys to the repository.
> Inject them via environment variables, container secrets, or a secrets manager
> (e.g. Azure Key Vault, AWS Secrets Manager, HashiCorp Vault).

### Applying Migrations — SQL Server

```bash
EF_PROVIDER=sqlserver \
  Database__ConnectionString="Server=prod-db;Database=PTDoc;Integrated Security=True;" \
  dotnet ef database update \
  -p src/PTDoc.Infrastructure.Migrations.SqlServer \
  -s src/PTDoc.Api
```

### Applying Migrations — PostgreSQL

```bash
EF_PROVIDER=postgres \
  Database__ConnectionString="Host=prod-db;Port=5432;Database=ptdoc;Username=ptdoc;Password=..." \
  dotnet ef database update \
  -p src/PTDoc.Infrastructure.Migrations.Postgres \
  -s src/PTDoc.Api
```

### Generating a SQL Script for Review

Generate an idempotent SQL script to review before applying to production:

```bash
# SQL Server
EF_PROVIDER=sqlserver \
  Database__ConnectionString="..." \
  dotnet ef migrations script --idempotent \
  -p src/PTDoc.Infrastructure.Migrations.SqlServer \
  -s src/PTDoc.Api \
  -o migration_sqlserver.sql

# PostgreSQL
EF_PROVIDER=postgres \
  Database__ConnectionString="..." \
  dotnet ef migrations script --idempotent \
  -p src/PTDoc.Infrastructure.Migrations.Postgres \
  -s src/PTDoc.Api \
  -o migration_postgres.sql
```

### Enabling Auto-Migrate in Production (Optional)

If your deployment pipeline manages database lifecycle automatically (e.g.
a container orchestrator that guarantees exactly-one startup), you can enable
automatic migration at startup:

```json
// appsettings.Production.json  — or set via environment variable
{
  "Database": {
    "AutoMigrate": true
  }
}
```

Or via environment variable:

```bash
Database__AutoMigrate=true
```

> **Warning:** Only enable this when you have exactly one API instance starting
> at a time. Concurrent startup with auto-migration can cause race conditions.

### Rollback

```bash
# Revert to a specific migration (SQL Server example)
EF_PROVIDER=sqlserver \
  Database__ConnectionString="..." \
  dotnet ef database update PreviousMigrationName \
  -p src/PTDoc.Infrastructure.Migrations.SqlServer \
  -s src/PTDoc.Api
```

## Troubleshooting

### Error: "No DbContext was found"

Make sure you're specifying both the project (`-p`) and startup project (`-s`):
```bash
EF_PROVIDER=sqlite dotnet ef dbcontext list \
  -p ./src/PTDoc.Infrastructure.Migrations.Sqlite \
  -s ./src/PTDoc.Api
```

### Error: "The Entity Framework tools version is older than the runtime"

Update EF Core tools:
```bash
dotnet tool update --global dotnet-ef
```

### Error: "Unable to create a DbContext"

Ensure `appsettings.Development.json` has a valid connection string and the
database directory exists. For SQL Server / Postgres, ensure
`ConnectionStrings:PTDocsServer` is configured.
