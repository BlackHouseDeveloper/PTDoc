# EF Core Migrations

## Overview

PTDoc uses Entity Framework Core migrations for database schema management. The Infrastructure layer contains the database context and migrations.

## Provider Configuration

- EF tooling should use a relational provider at design time
- In-Memory provider is not supported for migrations
- Use `EF_PROVIDER=sqlite` to ensure SQLite provider is selected

## Common Commands

### Inspect Database Context

```bash
EF_PROVIDER=sqlite dotnet ef dbcontext info \
  -p ./src/PTDoc.Infrastructure \
  -s ./src/PTDoc.Api
```

### Create New Migration

```bash
EF_PROVIDER=sqlite dotnet ef migrations add MigrationName \
  -p ./src/PTDoc.Infrastructure \
  -s ./src/PTDoc.Api
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

- **PTDoc.Infrastructure** - Contains `ApplicationDbContext` and migrations
- **PTDoc.Api** - Startup project for EF CLI tools
- **Migrations folder** - `src/PTDoc.Infrastructure/Data/Migrations/`

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
