# PTDoc Agent Notes

Use these repo-specific rules before guessing structure, commands, or architecture.

## Working Agreement

- Restate the task briefly and identify only the docs needed for that task.
- Reuse existing patterns; do not refactor unrelated code.
- Check file placement against [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) before adding files.
- When asked to write, rewrite, or summarize a pull request, always read and use [`.github/pull_request_template.md`](.github/pull_request_template.md) as the required structure and context; do not invent an alternate PR-summary format.
- Do not run `dotnet build`, `dotnet test`, or other heavy verification commands automatically. Ask the user to run them and use their output to iterate.
- Exception: when the task is to fix a GitHub Actions CI failure and the user explicitly asks to rerun the CI test locally, reproduce the relevant workflow command from `.github/workflows/` on the current branch before handing back. Prefer the smallest failing job command first (for example the `Core CI` `Category=CoreCi` test filter) and report the exact command and result.
- Never create a git commit unless the user has confirmed the relevant build and tests passed, or the user gives explicit permission to commit without that confirmation.
- Update [`docs/CHANGELOG.md`](docs/CHANGELOG.md) at the end of every repository-changing session before handing off. If no entry is needed, state that explicitly.

## Release Branching

- Treat [`docs/CI.md`](docs/CI.md) as the source of truth for branching and deployment.
- Keep `main` as the production-ready branch; do not create a long-lived `production` or `prod` branch just to represent the deployed application.
- Use short-lived `release/vX.Y.Z` branches only for specific release preparation.
- Production deployments should be identified by immutable release tags such as `v1.0.0`, not by moving branches.
- If a hosting provider requires branch-based production deployment, document the exception and apply protection rules at least as strict as `main`.

## Documentation Order

When repo docs conflict with generic framework habits, follow repo docs in this order:

1. [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) and other system-design specs.
2. [`.github/copilot-instructions.md`](.github/copilot-instructions.md), [`docs/DEVELOPMENT.md`](docs/DEVELOPMENT.md), and [`docs/CI.md`](docs/CI.md).
3. [`README.md`](README.md), [`docs/SECURITY.md`](docs/SECURITY.md), [`docs/RUNTIME_TARGETS.md`](docs/RUNTIME_TARGETS.md), [`docs/EF_MIGRATIONS.md`](docs/EF_MIGRATIONS.md), [`docs/TROUBLESHOOTING.md`](docs/TROUBLESHOOTING.md), and [`docs/BUILD.md`](docs/BUILD.md).

## Critical Repo Rules

- Respect Clean Architecture boundaries:
  - `PTDoc.Core`: zero project dependencies.
  - `PTDoc.Application`: depends only on Core.
  - `PTDoc.Infrastructure`: implements Application contracts.
  - `PTDoc.Api`, `PTDoc.Web`, `PTDoc.Maui`: presentation/composition roots.
  - `PTDoc.UI`: shared Blazor UI.
- Never reference Infrastructure from Application or Core.
- For Blazor work:
  - use PascalCase component names;
  - use `[Parameter]` explicitly and do not mutate parameters after initialization;
  - include `ChildContent` in wrapper components;
  - use tokens from `src/PTDoc.UI/wwwroot/css/tokens.css` instead of hardcoded colors or spacing.
- Treat business rules, auth, and compliance logic as server-enforced concerns, not UI-only concerns.

## Setup And Common Commands

- SDK is pinned by [`global.json`](global.json) to .NET `8.0.417`.
- Install .NET MAUI workloads when working on mobile/desktop targets: `dotnet workload install maui`
- Bootstrap development secrets before running API or Web:
  - `./setup-dev-secrets.sh`
  - Windows: `./setup-dev-secrets.ps1`
- `setup-dev-secrets.sh` stores `Jwt:SigningKey`, `IntakeInvite:SigningKey`, and `Communication:RecipientHashSalt` for `src/PTDoc.Api`, plus `IntakeInvite:SigningKey` for `src/PTDoc.Web`, in user-secrets. Do not commit real keys.
- Initial repo setup: `./PTDoc-Foundry.sh`
- Create and apply the default SQLite migration: `./PTDoc-Foundry.sh --create-migration`
- Seed dev data when `src/PTDoc.Seeder/PTDoc.Seeder.csproj` exists: `./PTDoc-Foundry.sh --seed`
- `./PTDoc-Foundry.sh --help` shows the helper-script workflow and `./PTDoc-Foundry.sh --verbose` enables detailed output
- Interactive launcher: `./run-ptdoc.sh`
- Clean build, test discovery, and architecture validation: `./cleanbuild-ptdoc.sh`

## Manual Run Commands

- API: `dotnet run --project src/PTDoc.Api --urls http://localhost:5170`
- Web: `dotnet run --project src/PTDoc.Web --urls http://localhost:5145`
- Mac Catalyst: `dotnet build -t:Run -f net8.0-maccatalyst src/PTDoc.Maui/PTDoc.csproj`
- iOS simulator: `dotnet build -t:Run -f net8.0-ios src/PTDoc.Maui/PTDoc.csproj`
- Android emulator: `dotnet build -t:Run -f net8.0-android src/PTDoc.Maui/PTDoc.csproj`

## Optional AI Draft Setup

- Enable local Azure OpenAI draft generation on `src/PTDoc.Api` with:
  - `FeatureFlags__EnableAiGeneration=true`
  - `AzureOpenAIEndpoint=https://<your-resource>.cognitiveservices.azure.com`
  - `AzureOpenAIKey=<your-azure-openai-resource-key>`
  - `AzureOpenAIDeployment=ptdoc-gpt-4o-mini`
  - `AzureOpenAIApiVersion=2025-01-01-preview`
  - `Ai__MaxOutputTokens=400`
- Use the base Azure resource endpoint only; do not pass the full chat-completions URL.
- After startup, verify AI config through authenticated `GET /diagnostics/runtime`, then run one authenticated saved-note AI action. Health endpoints alone do not confirm Azure generation works end to end.

## Browser QA Commands

- Install the separate Playwright browser QA project: `cd tests/PTDoc.Web.UiQa && npm install && npm run install:browsers`
- Run the responsive browser suite against local Web/API hosts: `PTDOC_WEB_BASE_URL=http://localhost:5145 PTDOC_UI_QA_USERNAME=<dev-or-beta-user> PTDOC_UI_QA_PIN=<pin> npm run test:responsive`
- Run the focused patient-document upload browser check: `PTDOC_WEB_BASE_URL=http://localhost:5145 PTDOC_UI_QA_USERNAME=<dev-or-beta-user> PTDOC_UI_QA_PIN=<pin> npm run test:patient-documents`
- Run the focused audit-remediation browser checks: `PTDOC_WEB_BASE_URL=http://localhost:5145 PTDOC_UI_QA_USERNAME=<dev-or-beta-user> PTDOC_UI_QA_PIN=<pin> npm run test:audit-remediation`
- Enable the viewport diagnostics overlay during a local Web run: `PTDOC_DEVELOPER_MODE=true dotnet run --project src/PTDoc.Web --urls http://localhost:5145`
- Manual GitHub Actions browser run: use workflow `UI Responsive QA`; use `http://localhost:5145` to let the workflow boot local hosts, or point it at a deployed environment, with repo secrets `PTDOC_UI_QA_USERNAME` and `PTDOC_UI_QA_PIN`, plus optional repo variable `PTDOC_UI_QA_NOTE_WORKSPACE_PATH`.
- Optional authenticated-session alternative: set `PTDOC_UI_QA_STORAGE_STATE` to a Playwright storage-state JSON file instead of credentials.
- Optional patient-chart override for the upload QA: set `PTDOC_UI_QA_PATIENT_CHART_PATH=/patient/<patient-id>` when a different seeded patient should be used.
- Optional PT-role override for audit-remediation note-entry coverage: set `PTDOC_UI_QA_PT_USERNAME` and `PTDOC_UI_QA_PT_PIN`.
- Optional PTA-role override for audit-remediation view/PDF coverage: set `PTDOC_UI_QA_PTA_USERNAME` and optional `PTDOC_UI_QA_PTA_PIN`.
- Optional intake-route override for audit-remediation coverage: set `PTDOC_UI_QA_INTAKE_PATH=/intake/<patient-id>` when a safe editable intake is available.
- Optional seeded note-workspace coverage: set `PTDOC_UI_QA_NOTE_WORKSPACE_PATH=/patient/<patient-id>/notes/<note-id>`
- Optional writable draft-note override for audit-remediation coverage: set `PTDOC_UI_QA_WRITABLE_NOTE_WORKSPACE_PATH=/patient/<patient-id>/notes/<note-id>` only for a safe PT-role draft note.
- Optional evaluation-draft override for audit-remediation exact-value reload/conflict checks: set `PTDOC_UI_QA_EVALUATION_DRAFT_PATH=/patient/<patient-id>/note/<note-id>` for a reversible Evaluation draft route.
- Optional viewport overlay toggle without the env var: add `?ptdocViewportDiagnostics=1` to enable it and `?ptdocViewportDiagnostics=0` to disable it on a fresh page load.
- Browser QA artifacts are written under `tests/PTDoc.Web.UiQa/test-results/` and `tests/PTDoc.Web.UiQa/playwright-report/`; do not commit them.

## Hosted Beta Workflows

- Beta QA source of truth: `docs/BETA_QA.md`; beta deployment/runbook source of truth: `docs/deployment/BETA_DEPLOYMENT.md`.
- Hosted beta URLs: Web `https://ptdoc.bhdevsites.com`, API `https://api-ptdoc.bhdevsites.com`.
- Hosted beta responsive browser QA: `cd tests/PTDoc.Web.UiQa && PTDOC_WEB_BASE_URL=https://ptdoc.bhdevsites.com PTDOC_UI_QA_USERNAME=<beta-user> PTDOC_UI_QA_PIN=<current-out-of-band-beta-pin> npm run test:responsive`
- Use `https://api-ptdoc.bhdevsites.com/health/live` for frequent availability probes and reserve `https://api-ptdoc.bhdevsites.com/health/ready` for deployment validation and pre-QA checks.
- The shared beta PIN is managed outside the repo as `BetaAccess__SeedPin`; get it from the beta environment owner and never commit or paste it into issue text, screenshots, or chat logs.
- `BetaAccess__AllowStartupSeed=true` is Beta-only and assumes the API App Service remains a controlled single-instance deployment; if scale-out is enabled, disable startup seeding first or verify the SQL lock-protected seed path after deployment.
- `BetaAccess__SeedLockTimeoutSeconds=15` bounds how long Beta startup seeding waits for the SQL Server application lock before reporting `SkippedLockContention`.
- Use the manual GitHub Actions workflows `Deploy Beta` for Azure beta deploys and `UI Responsive QA` for browser evidence outside the normal PR gate.
- Beta restart order: apply EF Core migrations out-of-band, confirm `/health/ready`, restart the API with `ASPNETCORE_ENVIRONMENT=Beta`, then verify the logs show the seed completed or deliberately skipped.
- Beta AI generation is disabled by default for cost control; if a beta pass deliberately enables it, use `Ai__RateLimits__PermitLimit` plus `Ai__RateLimits__WindowMinutes` for the committed rate-limit settings.

## Repo-Specific Environment Variables

- `PFP_DB_PATH`: overrides the SQLite database path used by `PTDoc-Foundry.sh`.
- `PTDoc_DB_PATH`: overrides the SQLite database path used by `src/PTDoc.Api`; use it when the API should target the same database file that `PTDoc-Foundry.sh --seed` created.
- `API_URL`: overrides the API URL used by `run-ptdoc.sh` and defaults to `http://localhost:5170`.
- `SKIP_API`: if set, `run-ptdoc.sh` will not auto-start the API.
- `SKIP_SECRET_SETUP`: if set, `run-ptdoc.sh` will not auto-run `setup-dev-secrets.sh` when API startup fails because signing keys are missing.
- `PTDOC_SERIAL_BUILD=1`: makes `cleanbuild-ptdoc.sh` use serialized builds (`-m:1` and `BuildInParallel=false`).
- `PTDOC_DEVELOPER_MODE=true`: enables developer diagnostics surfaces; `run-ptdoc.sh` inherits it for MAUI launches and inline MAUI runs can set it directly.
- `PTDoc_API_BASE_URL`: overrides the MAUI client base URL.
- `PUBLIC_WEB_BASE_URL`: when set for `run-ptdoc.sh`, applies the public browser-reachable Web origin to both `IntakeInvite:PublicWebBaseUrl` and `Communication:PublicBaseUrl` for generated patient links during tunnel/device testing.
- `DB_PROVIDER`: selects the database-provider smoke-test target (`sqlite`, `sqlserver`, or `postgres`) in `tests/PTDoc.Tests`.
- `CI_DB_MIGRATIONS_ALREADY_APPLIED=true`: tells the provider smoke tests not to apply runtime migrations again after the SQL Server or PostgreSQL CI-style EF CLI migration step.
- `BetaAccess__AllowStartupSeed`: enables the Beta-only startup seed path for hosted manual beta validation.
- `BetaAccess__SeedPin`: shared beta access PIN configured outside the repo for seeded beta accounts.
- `BetaAccess__SeedLockTimeoutSeconds`: bounds Beta startup seed lock waits before the app reports `SkippedLockContention`.
- `PTDOC_WEB_BASE_URL`: overrides the Playwright browser QA base URL and defaults to `http://localhost:5145`.
- `PTDOC_UI_QA_USERNAME` and `PTDOC_UI_QA_PIN`: credentials used by the browser QA suite when a route requires sign-in.
- `PTDOC_UI_QA_STORAGE_STATE`: Playwright storage-state file used instead of credentials for browser QA.
- `PTDOC_UI_QA_PATIENT_CHART_PATH`: optional patient-chart route used by the focused patient-document upload Playwright check.
- `PTDOC_UI_QA_PT_USERNAME` and `PTDOC_UI_QA_PT_PIN`: optional PT-role credentials used by the audit-remediation Playwright coverage for Start New Note flows.
- `PTDOC_UI_QA_PTA_USERNAME` and `PTDOC_UI_QA_PTA_PIN`: optional PTA-role credentials used by the audit-remediation Playwright coverage for View/PDF Tools flows.
- `PTDOC_UI_QA_INTAKE_PATH`: optional editable intake route used by the audit-remediation Playwright coverage.
- `PTDOC_UI_QA_NOTE_WORKSPACE_PATH`: optional seeded note-workspace route included in the responsive browser QA matrix.
- `PTDOC_UI_QA_WRITABLE_NOTE_WORKSPACE_PATH`: optional safe PT-role draft note route used by the audit-remediation Playwright coverage for save/reload checks.
- `PTDOC_UI_QA_EVALUATION_DRAFT_PATH`: optional reversible Evaluation draft route used by the audit-remediation Playwright coverage for exact-value reload and stale-write conflict checks.
- `PTDOC_UI_QA_CHROME_CHANNEL`: optional Chrome channel override for the browser QA Playwright project.
- `FeatureFlags__EnableAiGeneration`: enables API-side AI draft generation.
- `AzureOpenAIEndpoint`: base Azure OpenAI resource endpoint consumed by `src/PTDoc.Api`.
- `AzureOpenAIKey`: Azure OpenAI resource key for `src/PTDoc.Api`.
- `AzureOpenAIDeployment`: Azure OpenAI deployment name used for draft generation.
- `AzureOpenAIApiVersion`: Azure OpenAI API version used by the API provider.
- `Ai__MaxOutputTokens`: caps AI draft-generation output tokens.
- `Ai__RateLimits__PermitLimit`: fixed-window permit limit for hosted AI generation.
- `Ai__RateLimits__WindowMinutes`: AI generation rate-limit window length in minutes.

## Platform Notes

- `run-ptdoc.sh` auto-starts the API for Web, Android, iOS, and Mac Catalyst unless `SKIP_API` is set.
- `run-ptdoc.sh` considers the API ready only after `GET /health/live` succeeds; if the API exits because dev signing keys are missing, it will try `./setup-dev-secrets.sh` once unless `SKIP_SECRET_SETUP` is set.
- `run-ptdoc.sh` writes API startup output to `/tmp/ptdoc-api.log`; inspect it when launcher-driven API startup fails.
- Android emulator traffic should use `http://10.0.2.2:5170`; iOS simulator and Mac Catalyst use `http://localhost:5170`.
- `cleanbuild-ptdoc.sh` logs to `build-logs/cleanbuild-*.log`, runs Debug and Release builds, then runs tests discovered under `src/` and `tests/` matching `*.Tests.csproj`.

## Operational Diagnostics

- Liveness check: `curl http://localhost:5170/health/live`
- Readiness check: `curl http://localhost:5170/health/ready`
- Authenticated DB diagnostics: `curl -H "Authorization: Bearer <token>" http://localhost:5170/diagnostics/db`
- Authenticated runtime diagnostics: `curl -H "Authorization: Bearer <token>" http://localhost:5170/diagnostics/runtime`
- Inspect the active EF Core context wiring: `EF_PROVIDER=sqlite dotnet ef dbcontext info -p src/PTDoc.Infrastructure -s src/PTDoc.Api`
- Detect SQLite model drift: `EF_PROVIDER=sqlite dotnet ef migrations has-pending-model-changes -p src/PTDoc.Infrastructure.Migrations.Sqlite -s src/PTDoc.Api`
- Secret-policy tracked-file scan: `python3 .github/scripts/scan_secret_policy.py`
- Run CI-owned SecretPolicy tests only: `dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter "Category=SecretPolicy" --verbosity normal`
- Run CI-owned CoreCi tests only: `dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter "Category=CoreCi" --verbosity normal`
- Run CI-owned DatabaseProvider smoke tests only: `dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter "Category=DatabaseProvider" --verbosity normal`
- Run CI-owned Observability tests only: `dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter "Category=Observability" --verbosity normal`
- CI-owned release gate categories are `RBAC`, `Tenancy`, `OfflineSync`, `Compliance`, and `EndToEnd`; run them individually with `dotnet test tests/PTDoc.Tests/PTDoc.Tests.csproj --filter "Category=<Name>" --verbosity normal`
- Restore pinned local EF tools before SQL Server/Postgres CI repro: `dotnet tool restore`
- SQL Server/Postgres CI-style migration repro uses `dotnet tool run dotnet-ef ...` with `EF_PROVIDER`, `Database__ConnectionString`, and a placeholder `Jwt__SigningKey` to satisfy API startup validation
