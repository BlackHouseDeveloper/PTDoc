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

### Create New Migration

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
