## Physically Fit PT (PTDoc) – Enterprise Healthcare Documentation Platform

**Physically Fit PT (PTDoc)** is a modern, enterprise-grade clinician documentation platform designed specifically for physical therapy practices. Built with .NET MAUI Blazor for native mobile and desktop experiences, Blazor WebAssembly for web deployment, and a comprehensive automation framework, PTDoc follows Clean Architecture principles to ensure maintainability, scalability, and healthcare compliance.

## 🎯 Key Features

- **🏥 Clinical Documentation**: Comprehensive patient notes, appointment tracking, and treatment planning
- **📱 Multi-Platform Support**: Native iOS, Android, macOS desktop, and web applications
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
This uses the PTDoc.Seeder console project to populate the SQLite database with initial test data. By default, the data file is created at dev.PTDoc.db in the project root. If the file already exists, the seeder will add any missing data without duplicating existing entries.
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
