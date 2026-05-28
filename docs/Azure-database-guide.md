# Managing EF Core migrations and seeding for .NET 8 Blazor Server on Azure App Service

## What is probably going wrong

The usual failure pattern here is that schema work and seed-data work got blended into one runtime path. That feels convenient at first, but it becomes fragile when the app starts against a database that is one migration behind, when more than one instance starts at once, or when the running site does not have permission to change schema. EF Core’s own guidance separates production schema deployment from normal app execution, and its seeding guidance separately warns against seeding as part of ordinary multi-instance app startup.

## Recommended architecture

For this stack, let EF Core Migrations own schema changes. That is the EF Core model-first path: migrations are created from model changes, checked into source control, and then applied to the target database so the schema stays in sync while preserving data. Seed data is a different concern and should be handled separately from schema migration, not hidden inside the same startup path.

In a Blazor Server app, database work belongs on the server side. The framework is stateful, each user has a server-side circuit, and a circuit-scoped `DbContext` can be shared across components in ways that are unsafe because `DbContext` is not thread-safe. Microsoft’s Blazor guidance therefore recommends one context per operation and, when dependencies are needed, using `IDbContextFactory<TContext>` via `AddDbContextFactory`.

A practical layout is to keep entities, mappings, `MyDbContext`, and seed services in a server-side data/infrastructure project such as `YourApp.Data`, referenced by `YourApp.Server`. EF Core explicitly recommends a separate migrations project for platform-specific projects including Blazor, and the CLI supports separate target and startup projects with `--project` and `--startup-project`. If you want migrations isolated even further, add a dedicated migrations class library and optionally implement `IDesignTimeDbContextFactory<TContext>` there. If your team already manages database changes as a SQL project or DACPAC, that is a valid alternative deployment pattern, but it is separate from the EF Core migration-first path this guide recommends for this app.

## Local development and lower environments

For local development and tightly controlled lower environments, it is reasonable to run initialization code from the server app: create a scope, create a fresh context from `IDbContextFactory<MyDbContext>`, apply migrations, and then run a small idempotent seed routine. Keep that block clearly non-production. The current EF Core seeding docs recommend `UseSeeding` and `UseAsyncSeeding` for general-purpose seeding, but those APIs were introduced in EF Core 9. For a .NET 8 app that is still on EF Core 8 packages, the straightforward approach is explicit custom initialization logic instead.

Treat lookup or reference data differently from dummy or test data. Small, deterministic rows that are effectively part of the model can be managed through EF model-managed data (`HasData`), but only when they are static, migration-controlled, and not expected to change outside migrations. EF Core specifically says `HasData` is the wrong fit for temporary test data, data that depends on database state, large datasets, generated keys, external API calls, and nondeterministic values. That means environment-specific test accounts, sample orders, or demo content should not be expressed as migration-managed data.

Whatever seeding path you choose, make it idempotent. The official examples check whether a row exists before inserting it, which is the right pattern for rerunnable setup code. Also, do not use `EnsureCreated` or `EnsureCreatedAsync` on a relational database that you intend to manage with migrations: those APIs bypass migrations, do not reconcile schema later, and are explicitly discouraged when you plan to use migrations.

## Production deployment approach

For production, the default recommendation should be reviewed SQL-script deployment, not runtime `MigrateAsync()` in the web app. EF Core’s applying-migrations guidance is explicit: the recommended way to deploy migrations to a production database is by generating SQL scripts, because they can be reviewed, tuned, archived, and generated in CI. When you do not know exactly which migration level the target database is on, generate an idempotent script so it applies only the missing migrations.

For this stack, the safe deployment order is: build the app, generate the migration script, review and approve that script for production, apply it to Azure SQL Database, and only then deploy the updated Blazor Server site. That ordering is the safer default because the new site may assume the new schema exists. GitHub Actions can support that release discipline by generating the script in CI and, if needed, using environment approvals before the database step runs. This order is an application of EF Core’s production-script guidance and the general “build separately, then deploy the reviewed artifact” pattern described in Microsoft’s SQL automation guidance.

Do not make automatic runtime migrations or runtime seeding your default production behavior. EF Core says runtime migration is inappropriate for managing production databases because of concurrency, elevated permissions, rollback, and review concerns, and its seeding docs separately warn that seeding should not be part of normal app execution. If production needs reference-data changes, deploy them as reviewed SQL or run them through a tightly controlled administrative process; do not mix production schema deployment with environment-specific test data.

## Minimal examples

The C# example below is only for local development or a controlled lower environment. The YAML example shows the production ordering, but in a real production pipeline you would usually put a review or environment gate around the generated `ef-migrations.sql` before it is applied. The workflow also assumes your repository tracks `dotnet-ef` as a local tool manifest so `dotnet tool restore` resolves the correct CLI version for your EF Core packages. The action versions shown here match the current official Azure and GitHub action docs or READMEs as of May 27, 2026.

### Non-production initialization

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

builder.Services.AddDbContextFactory<MyDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await using var scope = app.Services.CreateAsyncScope();
    var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<MyDbContext>>();
    await using var db = await factory.CreateDbContextAsync();

    await db.Database.MigrateAsync();

    if (!await db.Set<ReferenceItem>().AnyAsync(x => x.Code == "en-US"))
    {
        db.Add(new ReferenceItem { Code = "en-US", Name = "English (United States)" });
        await db.SaveChangesAsync();
    }
}

app.Run();
```

### GitHub Actions workflow

```yaml
name: build-migrate-deploy

on:
  push:
    branches: [ main ]

permissions:
  contents: read
  id-token: write

jobs:
  build-migrate-deploy:
    runs-on: ubuntu-latest

    steps:
      - uses: actions/checkout@v6

      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: '8.0.x'

      - name: Restore
        run: dotnet restore

      - name: Build
        run: dotnet build src/YourApp.Server/YourApp.Server.csproj -c Release --no-restore

      - name: Restore EF tool
        run: dotnet tool restore

      - name: Publish app and generate idempotent EF script
        run: |
          mkdir -p artifacts/sql artifacts/app
          dotnet publish src/YourApp.Server/YourApp.Server.csproj -c Release --no-build -o artifacts/app
          dotnet ef migrations script --idempotent \
            --project src/YourApp.Data \
            --startup-project src/YourApp.Server \
            --context MyDbContext \
            --output artifacts/sql/ef-migrations.sql

      # Azure SQL Database prerequisites:
      # - either "Allow Azure Services and resources to access this server" is enabled, or
      # - this Azure identity can add/remove temporary firewall rules for the runner IP.
      - name: Azure login
        uses: azure/login@v3
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}

      - name: Apply EF migration script to Azure SQL Database
        uses: azure/sql-action@v2.3
        with:
          connection-string: ${{ secrets.AZURE_SQL_CONNECTION_STRING }}
          path: artifacts/sql/ef-migrations.sql

      - name: Deploy Blazor Server app to Azure App Service
        uses: azure/webapps-deploy@v3
        with:
          app-name: your-azure-webapp-name
          package: artifacts/app
```

`azure/webapps-deploy` also supports publish-profile deployment, but current Azure App Service guidance recommends [OpenID Connect](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect) because it uses short-lived tokens instead of a long-lived deployment secret. See also [Deploy to Azure App Service by using GitHub Actions](https://learn.microsoft.com/en-us/azure/app-service/deploy-github-actions).

## Important caveats

Azure SQL Database and Azure SQL Managed Instance behave differently for deployment connectivity. The temporary firewall-rule pattern documented by [`azure/sql-action`](https://github.com/Azure/sql-action) is specific to Azure SQL Database. For Azure SQL Managed Instance, the workflow must already have network access to the instance; the public endpoint uses port `3342`, and private-endpoint patterns often require a self-hosted runner. If connectivity is missing, `sql-action` can misinterpret that as an Azure SQL Database-style firewall problem and fail while trying to add a rule that does not apply. See the [Azure SQL Action README](https://github.com/Azure/sql-action) and its [connection guide](https://github.com/Azure/sql-action/blob/master/CONNECTION.md).

There are real authentication tradeoffs in GitHub Actions. For App Service, Microsoft recommends [OIDC for Azure authentication](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect), while publish profiles remain supported. For database deployment, SQL authentication is the simplest connection-string path but leaves a username and password in GitHub secrets. [`azure/sql-action`](https://github.com/Azure/sql-action) also supports Microsoft Entra service-principal authentication and an `Active Directory Default` connection-string pattern for managed-identity-style flows, which can reduce long-lived secrets but requires Azure-side federation setup and database-side user/role setup. See the [Azure SQL Action connection guide](https://github.com/Azure/sql-action/blob/master/CONNECTION.md).

If your initial data volume becomes unusually large, treat that as a separate optional data-load concern, not the default seeding path for app startup and not `HasData`. EF Core documents that large managed datasets bloat migration snapshots and degrade performance; see [Data Seeding](https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding). In other words, bulk-loading is an optimization you add deliberately later, usually with reviewed SQL or dedicated loading tooling, while keeping production migration scripts focused on schema and tightly controlled reference rows. Above all, never let environment-specific test or demo data leak into the production migration or production seed path.

## Verified official references

- EF Core Migrations Overview  
  `https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/`

- Applying Migrations  
  `https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/applying`

- Managing Migrations  
  `https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/managing`

- Using a Separate Migrations Project  
  `https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/projects`

- Data Seeding  
  `https://learn.microsoft.com/en-us/ef/core/modeling/data-seeding`

- ASP.NET Core Blazor with EF Core  
  `https://learn.microsoft.com/en-us/aspnet/core/blazor/blazor-ef-core`

- Deploy to Azure App Service by using GitHub Actions  
  `https://learn.microsoft.com/en-us/azure/app-service/deploy-github-actions`

- SQL projects automation  
  `https://learn.microsoft.com/en-us/sql/tools/sql-database-projects/sql-projects-automation`

- Azure SQL Action README  
  `https://github.com/Azure/sql-action`

- Azure SQL Action connection guide  
  `https://github.com/Azure/sql-action/blob/master/CONNECTION.md`

- Tutorial: Deploy an ASP.NET Core app and Database to Azure Container Apps using GitHub Actions  
  `https://learn.microsoft.com/en-us/visualstudio/azure/end-to-end-deployment-entity-framework-core-github-actions?view=visualstudio`

- EF Core tools reference for the .NET CLI  
  `https://learn.microsoft.com/en-us/ef/core/cli/dotnet`

- Authenticate to Azure from GitHub Actions by OpenID Connect  
  `https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure-openid-connect`

- GitHub Actions checkout action  
  `https://github.com/actions/checkout`

- GitHub Actions setup-dotnet action  
  `https://github.com/actions/setup-dotnet`

- Azure Login action  
  `https://github.com/Azure/login`
