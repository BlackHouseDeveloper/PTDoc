# setup-dev-secrets.ps1
# One-command dev secrets bootstrap for PTDoc (Windows / PowerShell).
# Generates cryptographically strong signing keys and stores them in dotnet user-secrets.
# Secrets are NEVER written to tracked files or printed to the terminal.
#
# Usage (from the PTDoc repository root):
#   .\setup-dev-secrets.ps1
#
# Requirements:
#   - .NET 8 SDK (dotnet CLI)
#   - PowerShell 5.1+ or PowerShell 7+

$ErrorActionPreference = "Stop"

$ApiProject = "src/PTDoc.Api/PTDoc.Api.csproj"
$WebProject = "src/PTDoc.Web/PTDoc.Web.csproj"

Write-Host "🔐 PTDoc - Development Secrets Bootstrap" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# --- Prerequisites ---
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Error "dotnet CLI not found. Install .NET 8 SDK from https://dotnet.microsoft.com/download"
    exit 1
}

# Verify we're running from the repo root
if (-not (Test-Path "PTDoc.sln")) {
    Write-Error "PTDoc.sln not found. Run this script from the PTDoc repository root."
    exit 1
}

# --- Generate secrets using .NET cryptography (no external tools required) ---
$rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()

$jwtKeyBytes = New-Object byte[] 64
$rng.GetBytes($jwtKeyBytes)
$jwtKey = [Convert]::ToBase64String($jwtKeyBytes)

$intakeKeyBytes = New-Object byte[] 32
$rng.GetBytes($intakeKeyBytes)
$intakeKey = [Convert]::ToBase64String($intakeKeyBytes)

$rng.Dispose()

# --- Store in user-secrets (output suppressed to prevent accidental logging) ---
Write-Host "Setting Jwt:SigningKey for PTDoc.Api..."
dotnet user-secrets set "Jwt:SigningKey" $jwtKey --project $ApiProject | Out-Null
Write-Host "✓ Jwt:SigningKey stored in user-secrets for PTDoc.Api" -ForegroundColor Green

Write-Host "Setting IntakeInvite:SigningKey for PTDoc.Api..."
dotnet user-secrets set "IntakeInvite:SigningKey" $intakeKey --project $ApiProject | Out-Null
Write-Host "✓ IntakeInvite:SigningKey stored in user-secrets for PTDoc.Api" -ForegroundColor Green

Write-Host "Setting IntakeInvite:SigningKey for PTDoc.Web..."
dotnet user-secrets set "IntakeInvite:SigningKey" $intakeKey --project $WebProject | Out-Null
Write-Host "✓ IntakeInvite:SigningKey stored in user-secrets for PTDoc.Web" -ForegroundColor Green

# --- Clear secret variables immediately ---
$jwtKey = $null
$intakeKey = $null
[System.Array]::Clear($jwtKeyBytes, 0, $jwtKeyBytes.Length)
[System.Array]::Clear($intakeKeyBytes, 0, $intakeKeyBytes.Length)

Write-Host ""
Write-Host "✅ Dev secrets configured successfully!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "  1. Set up the database:  .\PTDoc-Foundry.sh --create-migration --seed  (bash) or run via WSL/Git Bash"
Write-Host "  2. Start the API:        dotnet run --project src/PTDoc.Api --urls http://localhost:5170"
Write-Host "  3. Start the Web:        dotnet run --project src/PTDoc.Web"
Write-Host "  4. Or use the launcher:  .\run-ptdoc.sh  (bash) or run via WSL/Git Bash"
Write-Host ""
Write-Host "Note: Secrets are stored in your OS user profile (%APPDATA%\Microsoft\UserSecrets\) and" -ForegroundColor DarkYellow
Write-Host "      are never committed to git. Re-run this script any time to rotate your dev keys." -ForegroundColor DarkYellow
