# EF Core Migrations

## Overview

PTDoc uses Entity Framework Core migrations for database schema management. The Infrastructure layer contains the database context and migrations.

## Provider Configuration

PTDoc supports three database providers, selected via the `EF_PROVIDER` environment variable:

| `EF_PROVIDER` value | Provider | Use case |
|---|---|---|
| `sqlite` (default) | SQLite / SQLCipher | Local development, MAUI offline |
| `sqlserver` | Microsoft SQL Server | Cloud / production (future) |
| `postgres` | PostgreSQL via Npgsql | Cloud alternative (future) |

- EF tooling requires a relational provider at design time — In-Memory is not supported for migrations.
- The `DesignTimeDbContextFactory` in `PTDoc.Infrastructure` reads `EF_PROVIDER` automatically when running EF CLI commands.
- SQLite is the current production-ready provider with full migration history.
- SQL Server and PostgreSQL are validated for schema compatibility via CI (Sprint C).

## Common Commands

### Inspect Database Context

```bash
EF_PROVIDER=sqlite dotnet ef dbcontext info \
  -p ./src/PTDoc.Infrastructure \
  -s ./src/PTDoc.Api
```

### Create New Migration (SQLite – default)

```bash
EF_PROVIDER=sqlite dotnet ef migrations add MigrationName \
  -p ./src/PTDoc.Infrastructure \
  -s ./src/PTDoc.Api
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

```bash
EF_PROVIDER=sqlite dotnet ef database update \
  -p ./src/PTDoc.Infrastructure \
  -s ./src/PTDoc.Api
```

### Remove Last Migration

```bash
EF_PROVIDER=sqlite dotnet ef migrations remove \
  -p ./src/PTDoc.Infrastructure \
  -s ./src/PTDoc.Api
```

### Generate SQL Script

```bash
EF_PROVIDER=sqlite dotnet ef migrations script \
  -p ./src/PTDoc.Infrastructure \
  -s ./src/PTDoc.Api \
  -o migration.sql
```

## Project Structure

- **PTDoc.Infrastructure** - Contains `ApplicationDbContext`, `DesignTimeDbContextFactory`, and migrations
- **PTDoc.Api** - Startup project for EF CLI tools
- **SQLite migrations** - `src/PTDoc.Infrastructure/Data/Migrations/` (production-ready)
- **SQL Server migrations** - `src/PTDoc.Infrastructure/Data/Migrations/SqlServer/` (future)
- **PostgreSQL migrations** - `src/PTDoc.Infrastructure/Data/Migrations/Postgres/` (future)

## Web Runtime Considerations

- The web runtime (PTDoc.Web) must NOT reference EF Core or database providers
- All data access from web client goes through HTTP API
- This maintains clean separation between client and server concerns

## Troubleshooting

### Error: "No DbContext was found"

Make sure you're specifying both the project (`-p`) and startup project (`-s`):
```bash
EF_PROVIDER=sqlite dotnet ef dbcontext list \
  -p ./src/PTDoc.Infrastructure \
  -s ./src/PTDoc.Api
```

### Error: "The Entity Framework tools version is older than the runtime"

Update EF Core tools:
```bash
dotnet tool update --global dotnet-ef
```

### Error: "Unable to create a DbContext"

Ensure `appsettings.Development.json` has a valid connection string and the database directory exists.
