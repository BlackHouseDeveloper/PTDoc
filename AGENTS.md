# PTDoc Agent Notes

Use these repo-specific rules before guessing structure, commands, or architecture.

## Working Agreement

- Restate the task briefly and identify only the docs needed for that task.
- Reuse existing patterns; do not refactor unrelated code.
- Check file placement against [`/Users/calvinccarter/Projects/PTDoc/docs/ARCHITECTURE.md`](/Users/calvinccarter/Projects/PTDoc/docs/ARCHITECTURE.md) before adding files.
- Do not run `dotnet build`, `dotnet test`, or other heavy verification commands automatically. Ask the user to run them and use their output to iterate.

## Documentation Order

When repo docs conflict with generic framework habits, follow repo docs in this order:

1. [`/Users/calvinccarter/Projects/PTDoc/docs/ARCHITECTURE.md`](/Users/calvinccarter/Projects/PTDoc/docs/ARCHITECTURE.md) and other system-design specs.
2. [`/Users/calvinccarter/Projects/PTDoc/.github/copilot-instructions.md`](/Users/calvinccarter/Projects/PTDoc/.github/copilot-instructions.md), [`/Users/calvinccarter/Projects/PTDoc/docs/DEVELOPMENT.md`](/Users/calvinccarter/Projects/PTDoc/docs/DEVELOPMENT.md), and [`/Users/calvinccarter/Projects/PTDoc/docs/CI.md`](/Users/calvinccarter/Projects/PTDoc/docs/CI.md).
3. [`/Users/calvinccarter/Projects/PTDoc/README.md`](/Users/calvinccarter/Projects/PTDoc/README.md), [`/Users/calvinccarter/Projects/PTDoc/docs/SECURITY.md`](/Users/calvinccarter/Projects/PTDoc/docs/SECURITY.md), [`/Users/calvinccarter/Projects/PTDoc/docs/RUNTIME_TARGETS.md`](/Users/calvinccarter/Projects/PTDoc/docs/RUNTIME_TARGETS.md), [`/Users/calvinccarter/Projects/PTDoc/docs/EF_MIGRATIONS.md`](/Users/calvinccarter/Projects/PTDoc/docs/EF_MIGRATIONS.md), [`/Users/calvinccarter/Projects/PTDoc/docs/TROUBLESHOOTING.md`](/Users/calvinccarter/Projects/PTDoc/docs/TROUBLESHOOTING.md), and [`/Users/calvinccarter/Projects/PTDoc/docs/BUILD.md`](/Users/calvinccarter/Projects/PTDoc/docs/BUILD.md).

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

- SDK is pinned by [`/Users/calvinccarter/Projects/PTDoc/global.json`](/Users/calvinccarter/Projects/PTDoc/global.json) to .NET `8.0.417`.
- Bootstrap development secrets before running API or Web:
  - `./setup-dev-secrets.sh`
  - Windows: `./setup-dev-secrets.ps1`
- `setup-dev-secrets.sh` stores `Jwt:SigningKey` for `src/PTDoc.Api` and `IntakeInvite:SigningKey` for `src/PTDoc.Web` in user-secrets. Do not commit real keys.
- Initial repo setup: `./PTDoc-Foundry.sh`
- Create and apply the default SQLite migration: `./PTDoc-Foundry.sh --create-migration`
- Seed dev data when `src/PTDoc.Seeder/PTDoc.Seeder.csproj` exists: `./PTDoc-Foundry.sh --seed`
- Interactive launcher: `./run-ptdoc.sh`
- Clean build, test discovery, and architecture validation: `./cleanbuild-ptdoc.sh`

## Manual Run Commands

- API: `dotnet run --project src/PTDoc.Api --urls http://localhost:5170`
- Web: `dotnet run --project src/PTDoc.Web`
- Mac Catalyst: `dotnet build -t:Run -f net8.0-maccatalyst src/PTDoc.Maui/PTDoc.csproj`
- iOS simulator: `dotnet build -t:Run -f net8.0-ios src/PTDoc.Maui/PTDoc.csproj`
- Android emulator: `dotnet build -t:Run -f net8.0-android src/PTDoc.Maui/PTDoc.csproj`

## Repo-Specific Environment Variables

- `PFP_DB_PATH`: overrides the SQLite database path used by `PTDoc-Foundry.sh`.
- `API_URL`: overrides the API URL used by `run-ptdoc.sh` and defaults to `http://localhost:5170`.
- `SKIP_API`: if set, `run-ptdoc.sh` will not auto-start the API.
- `PTDOC_SERIAL_BUILD=1`: makes `cleanbuild-ptdoc.sh` use serialized builds (`-m:1` and `BuildInParallel=false`).
- `PTDoc_API_BASE_URL`: overrides the MAUI client base URL.

## Platform Notes

- `run-ptdoc.sh` auto-starts the API for Android, iOS, and Mac Catalyst unless `SKIP_API` is set.
- Android emulator traffic should use `http://10.0.2.2:5170`; iOS simulator and Mac Catalyst use `http://localhost:5170`.
- `cleanbuild-ptdoc.sh` logs to `build-logs/cleanbuild-*.log`, runs Debug and Release builds, then runs tests discovered under `src/` and `tests/` matching `*.Tests.csproj`.
