## Physically Fit PT (PTDoc) – Enterprise Healthcare Documentation Platform

**Physically Fit PT (PTDoc)** is a modern, enterprise-grade clinician documentation platform designed specifically for physical therapy practices. Built with .NET MAUI Blazor for native mobile and desktop experiences, Blazor WebAssembly for web deployment, and a comprehensive automation framework, PTDoc follows Clean Architecture principles to ensure maintainability, scalability, and healthcare compliance.

## 🎯 Key Features

- **🏥 Clinical Documentation**: Comprehensive patient notes, appointment tracking, and treatment planning
- **📱 Multi-Platform Support**: Native iOS, Android, macOS desktop, and web applications
- **📴 Offline-First Architecture**: Full functionality without connectivity, automatic sync when online
- **🔄 Real-Time Sync**: Live connectivity monitoring and data synchronization with conflict resolution
- **📄 PDF Export**: Professional patient reports and documentation export using QuestPDF
- **🗄️ SQLite Database**: Local data storage with Entity Framework Core for offline capabilities
- **🔧 Automation Tools**: Automated messaging workflows and assessment management
- **🎯 Modular Architecture**: Clean separation of concerns with domain-driven design
- **🔒 Data Security**: Local SQLite encryption support and audit trail capabilities

## Prerequisites

To set up and run PTDoc on a development machine, ensure you have the following:

- **macOS** (Apple Silicon or Intel) – *PTDoc is primarily developed and tested on macOS.*
- **.NET 8.0 SDK** – Download from Microsoft’s [.NET downloads](https://dotnet.microsoft.com/en-us/download/dotnet/8.0). Make sure `dotnet --version` shows a 8.x SDK.
- **Xcode** (latest) – Required for iOS and Mac Catalyst projects (install from the App Store). After installing, run `sudo xcode-select --switch /Applications/Xcode.app`.
- **.NET MAUI Workloads** – After installing the .NET SDK, install MAUI workloads by running:  
  ```bash
  dotnet workload install maui
  ```

This will set up Android, iOS, and MacCatalyst targets for .NET MAUI.

**IDE (optional)** – You can use Visual Studio 2022 (Windows or Mac) or Visual Studio Code. VS Code works well for editing, but launching the MAUI app might require CLI commands or VS for Mac.

> **Note**: Do not run any setup scripts with sudo. All development tasks should be run with normal user permissions.

## Getting Started

### 1. Clone the Repository

Start by cloning the PTDoc repository from GitHub:

```bash
git clone https://github.com/BlackHouseDeveloper/PTDoc.git
cd PTDoc
```

### 2. Initial Setup and Database Configuration
PTDoc includes a setup script PTDoc-Foundry.sh to scaffold the solution and prepare the local database:
To ensure the project is fully set up (restore NuGet packages, ensure correct .NET 8 targets, etc.), you can run the setup script:
bash
Copy code
./PTDoc-Foundry.sh
This will add any missing projects, enforce .NET 8.0 as the target framework for all projects, and set up boilerplate code. It’s safe to run multiple times (the script checks for existing files and won’t overwrite your code or data).
Creating the initial database migration: The first time you set up PTDoc, you’ll need to create the initial EF Core migration (which sets up the SQLite schema). Run:
bash
Copy code
./PTDoc-Foundry.sh --create-migration
This will install the EF Core tools (if not already installed), add an Initial migration in the PTDoc.Infrastructure/Data/Migrations folder (if one doesn’t exist), and apply it to create a local SQLite database.

**Seeding the development database:** To insert sample data (e.g. a couple of patients and reference codes), run:
bash
Copy code
./PTDoc-Foundry.sh --seed
This command uses the internal seeding logic built into `PTDoc-Foundry.sh` to populate the SQLite database with initial test data. By default, the data file is created at dev.PTDoc.db in the project root. If the file already exists, the script will add any missing data without duplicating existing entries.
#### Verify the Database Path

The database is created at `dev.PTDoc.db` in the repo root. The API resolves its SQLite path in this order:

1. `PFP_DB_PATH` environment variable (CI/CD or container override)
2. `ConnectionStrings:DefaultConnection` in `appsettings.{Environment}.json`
3. Fallback `Data Source=PTDoc.db`

For development, `appsettings.Development.json` already points at `dev.PTDoc.db`, so the API immediately serves the seeded data.

### 3. Running the Application

PTDoc supports multiple deployment targets for different use cases:

**Quick Launch Script:**
```bash
./run-ptdoc.sh
```

This interactive script lets you choose:
1. Blazor Web (browser)
2. Android (emulator)
3. iOS (simulator)
4. Mac Catalyst (desktop)

The script automatically starts the API server when needed.

#### Desktop Application (Mac Catalyst)

Run as a native macOS desktop application:

```bash
dotnet build -t:Run -f net8.0-maccatalyst src/PTDoc.Maui/PTDoc.csproj
```

**Alternative**: Open `PTDoc.sln` in Visual Studio 2022 (Mac) and run the PTDoc.Maui project targeting Mac Catalyst.

#### Mobile Applications

Deploy to iOS or Android once the API is running:

```bash
# Terminal 1 – start the API
dotnet run --project src/PTDoc.Api --urls http://localhost:5170

# Terminal 2 – launch a platform target
dotnet build -t:Run -f net8.0-ios PTDoc/PTDoc.csproj
dotnet build -t:Run -f net8.0-android PTDoc/PTDoc.csproj
```

The MAUI bootstraper chooses the correct base URL:

- iOS simulator → http://localhost:5170
- Android emulator → http://10.0.2.2:5170

Override with `PTDoc_API_BASE_URL` if you need to target a different host.

#### Web Application (Browser)

Run the Blazor WebAssembly client for browser-based access:

```bash
dotnet run --project PTDoc.Web/PTDoc.Web.csproj
```

This starts a local development server. Open the displayed URL (typically `http://localhost:5145`) in your browser.

**Note**: The web version talks to the same API instance. Configure the base URL in `wwwroot/appsettings.json` (or at deployment time) to point at the correct environment.

#### Development Tips

- **Hot Reload**: Supported in Visual Studio and VS Code for MAUI projects
- **Web Hot Reload**: Automatic when using `dotnet run` with the web project
- **Code Changes**: Re-run `dotnet build` or restart the development server after changes

### 4. Enterprise Architecture & Project Structure

PTDoc follows Clean Architecture principles with clear separation of concerns:

```
PTDoc.Core         → Domain entities (ZERO dependencies)
PTDoc.Application  → Interfaces/contracts (depends only on Core)
PTDoc.Infrastructure → Implementations (depends on Application)
PTDoc.Api          → REST API, JWT auth
PTDoc.Web          → Blazor WebAssembly web app
PTDoc.Maui         → .NET MAUI Blazor app (Android, iOS, macOS)
PTDoc.UI           → Shared Blazor components (reusable)
```

**Project Details:**
- **PTDoc.Core** - Domain entities (business models) with no EF Core or UI dependencies
- **PTDoc.Application** - Interfaces, DTOs, and contracts (depends only on Core)
  - **ISyncService** - Data synchronization state and operations
  - **IConnectivityService** - Network connectivity monitoring
- **PTDoc.Infrastructure** - Implements persistence (EF Core ApplicationDbContext), domain services, and data access
  - **SyncService** - Offline-first sync with localStorage persistence
  - **ConnectivityService** - Browser-based connectivity detection
- **PTDoc.Api** - REST API with JWT authentication for backend services
- **PTDoc.Web** - Blazor WebAssembly app for running PTDoc in a web browser
- **PTDoc.Maui** - .NET MAUI Blazor app (multi-targeted for Android, iOS, macOS)
- **PTDoc.UI** - Shared Blazor components used by both Web and MAUI projects
  - **GlobalHeader** - Menu, sync controls, and connectivity status

### 5. Using the PTDoc-Foundry.sh Script

As mentioned, the repository includes a script (`PTDoc-Foundry.sh`) to automate environment setup tasks. Here's a quick reference on using it:

**Usage:**
- Running the script with no arguments will scaffold or update the solution structure (adding missing files, normalizing project settings). It's idempotent – it won't overwrite existing code, and you can run it after pulling updates to ensure your local projects match the expected configuration.
- `--create-migration`: Generates the initial EF Core migration (if not already present) and updates the database. This is typically done once at project setup. If an initial migration already exists, the script will skip creating a duplicate.
- `--seed`: Populates the database with sample data. Safe to run multiple times; it only inserts data if not already present (e.g., it won't add duplicate patients if you run it again).
- `--help`: Shows usage info. You can also open the script in a text editor – it's heavily commented to explain each step it performs.

### 6. Design System and Styling

PTDoc uses a token-driven design system aligned to the Figma Make Prototype v5:
- **Design Tokens:** See `src/PTDoc.UI/wwwroot/css/tokens.css` for color, spacing, typography, and other design tokens
- **Global Styles:** Base styles in `src/PTDoc.UI/wwwroot/css/app.css`
- **Component Styles:** Scoped CSS files alongside components (`.razor.css`)
- **Theme Support:** Light and dark themes with token switching

For detailed styling guidelines, see `docs/style-system.md` and `docs/context/ptdoc-figma-make-prototype-v5-context.md`.

---

## Contributing

**Database not found / issues:** Ensure you have run the migration (`--create-migration`) and seeding steps. The SQLite database file `dev.PTDoc.db` should be present in the project root and referenced by `appsettings.Development.json`. Override it via `PFP_DB_PATH` environment variable if you need a different location.

**iOS build issues:** If building for iOS, make sure you've opened the project in Xcode at least once to accept any license agreements, and that you have an iOS simulator selected. You might also need to adjust code signing settings in Xcode for the iOS target if you deploy to a physical device.

**Hot Reload/Live Reload:** When running the MAUI app, .NET Hot Reload should work if you launch from Visual Studio. For the Blazor Web app, code changes generally require rebuilding (`dotnet run` will pick up changes on restart). Develop with the approach that suits you (for quick UI iteration, the Web app is convenient; for testing native features, use the MAUI app).

For detailed troubleshooting, see `docs/TROUBLESHOOTING.md`.

### 8. Offline-First Architecture

PTDoc is built with offline-first capabilities to ensure clinicians can document care anywhere, without risking data loss:

**Connectivity Monitoring:**
- Real-time online/offline detection using browser Network Information API
- Visual status indicator in the global header (Online/Offline badge)
- Automatic sync triggers when connectivity restores

**Data Synchronization:**
- Last sync timestamp tracking with elapsed time display ("3m 25s ago")
- Manual "Sync Now" button (disabled when offline or already syncing)
- Persisted sync state survives app restarts
- Event-driven state management for reactive UI updates

**Current Implementation:**
- `ISyncService` - Synchronization state and operations
- `IConnectivityService` - Network connectivity monitoring
- Local SQLite database for offline data storage
- Simulated sync operations (actual cloud sync coming soon)

**Future Enhancements:**
- Full EF Core + cloud API sync implementation
- Conflict resolution for multi-device scenarios
- Sync queue for pending operations
- Background sync with configurable intervals

For detailed offline sync specifications, see `docs/PTDocs+_Offline_Sync_Conflict_Resolution.md`.

For any other issues, please check the repository's issue tracker or contact the maintainers.

---

## Contributing

We welcome contributions to PTDoc! Our enterprise-grade development environment includes automated workflows to ensure healthcare compliance and code quality.

### Quick Start for Contributors

1. **Fork and Clone**: Fork the repository and clone your fork
2. **Environment Setup**: Run `./PTDoc-Foundry.sh` to set up development environment
3. **Validate Setup**: Run `gh workflow run mcp-copilot-setup-validation.yml -f validation_scope=full`
4. **Create Branch**: Use conventional naming (`feat/`, `fix/`, `docs/`, `clinical/`)
5. **Make Changes**: Follow coding standards and write tests
6. **Auto-formatting**: Our MCP workflows will auto-format code on PR submission
7. **Test Thoroughly**: Run `dotnet test` and verify all platforms build
8. **Submit PR**: Include clear description and reference any related issues

### Enterprise Development Standards

- **Code Style**: Auto-enforced via MCP workflows with StyleCop and Roslynator
- **Healthcare Compliance**: HIPAA considerations validated in all changes
- **Testing**: Comprehensive test coverage with platform-specific validation
- **Architecture**: Clean Architecture with healthcare-focused domain modeling
- **Accessibility**: WCAG 2.1 AA/AAA compliance for inclusive healthcare technology
- **Documentation**: Clinical documentation standards and API documentation

### Automated Quality Assurance

Our MCP framework automatically:
- **Validates environment setup** for all contributors
- **Formats code** according to healthcare software standards
- **Tests accessibility compliance** for clinical users
- **Validates PDF reports** for clinical documentation standards
- **Checks database changes** for HIPAA compliance
- **Generates comprehensive release notes** with clinical impact assessment

### Healthcare-Specific Contribution Guidelines

- **Clinical Workflow Impact**: Assess how changes affect clinical workflows
- **Patient Data Privacy**: Ensure all changes maintain HIPAA compliance
- **Accessibility**: Test with screen readers and keyboard navigation
- **Multi-platform Testing**: Validate changes across iOS, Android, macOS, and web
- **Clinical Terminology**: Use standardized medical terminology and coding systems

For detailed contribution guidelines and healthcare-specific development practices, see:
- `docs/DEVELOPMENT.md` - Comprehensive development guide
- `docs/ARCHITECTURE.md` - Technical architecture and patterns
- `docs/PTDocs+_Offline_Sync_Conflict_Resolution.md` - Offline-first specifications
- `.github/copilot-instructions.md` - Copilot and MCP usage guide

### Community & Support

- **Discussions**: Join community discussions for questions and ideas
- **Healthcare Provider Support**: Specialized support for clinical workflow questions
- **Security Issues**: Report security vulnerabilities via GitHub Security Advisories
- **Documentation**: Comprehensive guides in the `docs/` directory

---

*Happy documenting! 🏥📱*
