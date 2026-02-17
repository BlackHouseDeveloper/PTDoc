# PTDoc Build Guide

Comprehensive build instructions for all platforms and deployment scenarios.

## Prerequisites

### Development Environment
- **.NET 8.0 SDK** or later (8.0.417 enforced via global.json)
- **Platform-specific tools** (see Platform Requirements below)
- **Git** for source control
- **PTDoc-Foundry.sh** setup script (run once)

### Platform Requirements

#### Windows
- **Windows 10 version 1809+** or **Windows 11**
- **Visual Studio 2022** with .NET MAUI workload
- **Windows SDK** (latest version)

#### macOS  
- **macOS 10.15+** (Catalina or later)
- **Xcode 14+** (from App Store)
- **.NET MAUI workloads**: `dotnet workload install maui`
- **Command Line Tools**: `xcode-select --install`

#### Linux
- **Ubuntu 20.04+** or equivalent distribution
- **.NET 8.0 SDK** installed via package manager
- **Development packages**: `build-essential`

## Quick Build Commands

### Development Builds

```bash
# Clean and restore (recommended first step)
./cleanbuild-ptdoc.sh

# Build all projects (fastest)
dotnet build

# Build specific project
dotnet build src/PTDoc.Core/PTDoc.Core.csproj
dotnet build src/PTDoc.Web/PTDoc.Web.csproj
dotnet build src/PTDoc.Api/PTDoc.Api.csproj
```

### Platform-Specific Builds

#### Web Application (Browser)
```bash
# Development build
dotnet build src/PTDoc.Web/PTDoc.Web.csproj -c Debug

# Production build
dotnet build src/PTDoc.Web/PTDoc.Web.csproj -c Release

# Publish for deployment
dotnet publish src/PTDoc.Web -c Release -o ./publish-web
```

#### MAUI Applications

**macOS (Mac Catalyst)**
```bash
# Development build
dotnet build src/PTDoc.Maui/PTDoc.csproj -f net8.0-maccatalyst -c Debug

# Release build  
dotnet build src/PTDoc.Maui/PTDoc.csproj -f net8.0-maccatalyst -c Release

# Run directly
dotnet build -t:Run -f net8.0-maccatalyst src/PTDoc.Maui/PTDoc.csproj
```

**iOS (Simulator)**
```bash
# Development build
dotnet build src/PTDoc.Maui/PTDoc.csproj -f net8.0-ios -c Debug

# Run on simulator
dotnet build -t:Run -f net8.0-ios src/PTDoc.Maui/PTDoc.csproj
```

**Android (Emulator)**
```bash
# Development build
dotnet build src/PTDoc.Maui/PTDoc.csproj -f net8.0-android -c Debug

# Run on emulator
dotnet build -t:Run -f net8.0-android src/PTDoc.Maui/PTDoc.csproj
```

#### API Server
```bash
# Development build
dotnet build src/PTDoc.Api/PTDoc.Api.csproj -c Debug

# Production build
dotnet build src/PTDoc.Api/PTDoc.Api.csproj -c Release

# Publish for deployment
dotnet publish src/PTDoc.Api -c Release -o ./publish-api
```

## Configuration Management

### Environment-Specific Settings

PTDoc uses hierarchical configuration with the following precedence:

1. **Environment variables** (highest priority)
2. **appsettings.{Environment}.json**
3. **appsettings.json** (base configuration)

### Development Configuration

```json
// src/PTDoc.Api/appsettings.Development.json
{
  "Jwt": {
    "SigningKey": "development-key-minimum-32-characters",
    "AccessTokenMinutes": 15,
    "RefreshTokenDays": 30
  },
  "ConnectionStrings": {
    "DefaultConnection": "Data Source=dev.PTDoc.db"
  }
}
```

### Production Configuration

```json
// src/PTDoc.Api/appsettings.Production.json
{
  "Jwt": {
    "SigningKey": "${JWT_SIGNING_KEY}",  // Read from environment
    "AccessTokenMinutes": 15,
    "RefreshTokenDays": 30
  },
  "ConnectionStrings": {
    "DefaultConnection": "${DATABASE_CONNECTION_STRING}"
  }
}
```

## Build Optimization

### Clean Architecture Validation

The build system validates Clean Architecture dependencies:

```bash
# Core should have zero dependencies
dotnet list src/PTDoc.Core/PTDoc.Core.csproj reference

# Application should only reference Core
dotnet list src/PTDoc.Application/PTDoc.Application.csproj reference

# Infrastructure implements Application interfaces
dotnet list src/PTDoc.Infrastructure/PTDoc.Infrastructure.csproj reference
```

### Build Performance

**Multi-core compilation:**
```bash
dotnet build -maxcpucount:8
```

**Disable parallel build (troubleshooting):**
```bash
dotnet build -maxcpucount:1
```

**Incremental builds:**
```bash
# Normal (fastest)
dotnet build

# Force full rebuild
dotnet build --no-incremental
```

## Testing

### Run All Tests

```bash
# Run all tests
dotnet test

# Run with detailed output
dotnet test --verbosity detailed

# Note: Test projects to be added in future development phases
```

### Test Coverage

```bash
# Install coverage tool
dotnet tool install --global dotnet-coverage

# Run tests with coverage
dotnet test --collect:"XPlat Code Coverage"
```

## Database Setup

### Initial Migration

```bash
# Using helper script (recommended)
./PTDoc-Foundry.sh --create-migration

# Manual command
EF_PROVIDER=sqlite dotnet ef migrations add Initial \
  -p src/PTDoc.Infrastructure \
  -s src/PTDoc.Api
```

### Apply Migrations

```bash
EF_PROVIDER=sqlite dotnet ef database update \
  -p src/PTDoc.Infrastructure \
  -s src/PTDoc.Api
```

### Seed Development Data

```bash
./PTDoc-Foundry.sh --seed
```

## Deployment

### Web Application Deployment

```bash
# Publish Web app
dotnet publish src/PTDoc.Web -c Release -o ./publish-web

# Deploy to hosting service (example: Azure App Service)
az webapp deployment source config-zip \
  --resource-group PTDoc-RG \
  --name ptdoc-web \
  --src publish-web.zip
```

### API Deployment

```bash
# Publish API
dotnet publish src/PTDoc.Api -c Release -o ./publish-api

# Deploy to hosting service
az webapp deployment source config-zip \
  --resource-group PTDoc-RG \
  --name ptdoc-api \
  --src publish-api.zip
```

### MAUI App Store Deployment

**iOS App Store:**
```bash
# Archive for App Store
dotnet build src/PTDoc.Maui/PTDoc.csproj \
  -f net8.0-ios \
  -c Release \
  -p:ArchiveOnBuild=true \
  -p:RuntimeIdentifier=ios-arm64
```

**Google Play Store:**
```bash
# Build signed APK
dotnet publish src/PTDoc.Maui/PTDoc.csproj \
  -f net8.0-android \
  -c Release \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore=ptdoc.keystore \
  -p:AndroidSigningKeyAlias=ptdoc \
  -p:AndroidSigningKeyPass=${KEYSTORE_PASSWORD}
```

## Troubleshooting

### Common Build Errors

**Error: SDK not found**
```bash
# Verify .NET SDK
dotnet --version

# Check global.json requirements
cat global.json
```

**Error: Workload not installed**
```bash
# Install MAUI workload
dotnet workload install maui

# Repair workloads
dotnet workload repair
```

**Error: Project reference not found**
```bash
# Restore all projects
dotnet restore PTDoc.sln

# Clean and rebuild
./cleanbuild-ptdoc.sh
```

### Platform-Specific Issues

**macOS: Xcode not configured**
```bash
sudo xcode-select --switch /Applications/Xcode.app
xcode-select --install
```

**Android: SDK not found**
```bash
export ANDROID_HOME=$HOME/Library/Android/sdk
export PATH=$PATH:$ANDROID_HOME/platform-tools
```

**iOS: Provisioning profile error**
- Open project in Xcode
- Update signing certificates
- Rebuild from command line

## Performance Benchmarks

Typical build times on Apple Silicon Mac:

| Configuration | Time |
|--------------|------|
| Clean build (all projects) | ~45s |
| Incremental build | ~5s |
| Full rebuild with tests | ~60s |
| MAUI Mac Catalyst | ~30s |
| MAUI iOS | ~40s |
| MAUI Android | ~35s |

## Continuous Integration

For CI/CD setup, see:
- [CI.md](CI.md) - CI/CD guardrails and workflows
- [DEVELOPMENT.md](DEVELOPMENT.md) - Development workflows

## Additional Resources

- [EF_MIGRATIONS.md](EF_MIGRATIONS.md) - Database migrations guide
- [RUNTIME_TARGETS.md](RUNTIME_TARGETS.md) - Platform-specific considerations
- [TROUBLESHOOTING.md](TROUBLESHOOTING.md) - Detailed troubleshooting guide
- [README.md](../README.md) - Getting started guide
