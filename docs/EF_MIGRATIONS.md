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
  --context ApplicationDbContext
```

#### SQL Server

```bash
dotnet ef migrations add MigrationName \
  -p ./src/PTDoc.Infrastructure.Migrations.SqlServer \
  -s ./src/PTDoc.Api \
  --context ApplicationDbContext
```

#### PostgreSQL

```bash
dotnet ef migrations add MigrationName \
  -p ./src/PTDoc.Infrastructure.Migrations.Postgres \
  -s ./src/PTDoc.Api \
  --context ApplicationDbContext
```

### Create New Migration (SQL Server)

```bash
EF_PROVIDER=sqlserver \
  Database__ConnectionString="Server=localhost,1433;Database=PTDoc_Dev;User Id=sa;Password=YourStrong!Passw0rd;TrustServerCertificate=True" \
  dotnet ef migrations add MigrationName \
  -p ./src/PTDoc.Infrastructure \
  -s ./src/PTDoc.Api \
  --output-dir Data/Migrations/SqlServer
```

### Create New Migration (PostgreSQL)

```bash
EF_PROVIDER=postgres \
  Database__ConnectionString="Host=localhost;Port=5432;Database=ptdoc_dev;Username=postgres;Password=postgres" \
  dotnet ef migrations add MigrationName \
  -p ./src/PTDoc.Infrastructure \
  -s ./src/PTDoc.Api \
  --output-dir Data/Migrations/Postgres
```

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

### Required Environment Variables

| Variable | Purpose | Example |
|----------|---------|---------|
| `ASPNETCORE_ENVIRONMENT` | Set to `Production` for production deployments | `Production` |
| `Database__Provider` | Database provider (`SqlServer` or `Postgres`) | `SqlServer` |
| `ConnectionStrings__PTDocsServer` | Full connection string | `Server=db;Database=PTDoc;...` |
| `Jwt__SigningKey` | JWT signing secret (≥ 32 chars) | *(from secrets manager)* |

> **Security:** Never commit connection strings or signing keys to the repository.
> Inject them via environment variables, container secrets, or a secrets manager
> (e.g. Azure Key Vault, AWS Secrets Manager, HashiCorp Vault).

### Applying Migrations — SQL Server

```bash
Database__Provider=SqlServer \
  Database__ConnectionString="Server=prod-db;Database=PTDoc;Integrated Security=True;" \
  dotnet ef database update \
  -p src/PTDoc.Infrastructure.Migrations.SqlServer \
  -s src/PTDoc.Api
```

### Applying Migrations — PostgreSQL

```bash
Database__Provider=Postgres \
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
Database__Provider=SqlServer \
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
