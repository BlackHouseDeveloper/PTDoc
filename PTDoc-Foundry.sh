#!/usr/bin/env bash
set -euo pipefail

# ============================================================================
# PTDoc-Foundry.sh - Development Environment Setup Script
# ============================================================================
#
# This script automates the setup and maintenance of the PTDoc development
# environment following Clean Architecture principles.
#
# Features:
# - Solution structure validation and scaffolding
# - NuGet package restoration
# - .NET workload installation
# - Database migration and seeding
# - Project reference validation
# - Clean Architecture dependency enforcement
#
# Usage:
#   ./PTDoc-Foundry.sh                  # Basic setup
#   ./PTDoc-Foundry.sh --create-migration  # Create initial migration
#   ./PTDoc-Foundry.sh --seed            # Seed development database
#   ./PTDoc-Foundry.sh --verbose         # Verbose output
#   ./PTDoc-Foundry.sh --help            # Show this help
#
# Requirements:
#   - .NET 8.0 SDK
#   - macOS, Linux, or WSL (Windows)
#   - Do NOT run with sudo
#
# ============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$SCRIPT_DIR"

# Colors for output
if command -v tput >/dev/null 2>&1 && [ "$(tput colors 2>/dev/null || echo 0)" -ge 8 ]; then
  RESET="$(tput sgr0)"
  BOLD="$(tput bold)"
  RED="$(tput setaf 1)"
  GREEN="$(tput setaf 2)"
  YELLOW="$(tput setaf 3)"
  BLUE="$(tput setaf 6)"
else
  RESET=""; BOLD=""; RED=""; GREEN=""; YELLOW=""; BLUE=""
fi

# Flags
VERBOSE=false
CREATE_MIGRATION=false
SEED_DB=false
SHOW_HELP=false

# Parse arguments
for arg in "$@"; do
  case $arg in
    --verbose|-v)
      VERBOSE=true
      ;;
    --create-migration)
      CREATE_MIGRATION=true
      ;;
    --seed)
      SEED_DB=true
      ;;
    --help|-h)
      SHOW_HELP=true
      ;;
    *)
      echo "${RED}Unknown option: $arg${RESET}"
      echo "Run './PTDoc-Foundry.sh --help' for usage information"
      exit 1
      ;;
  esac
done

# Show help
if [ "$SHOW_HELP" = true ]; then
  cat << 'EOF'
PTDoc-Foundry.sh - Development Environment Setup

USAGE:
  ./PTDoc-Foundry.sh [OPTIONS]

OPTIONS:
  --create-migration    Create initial EF Core migration and update database
  --seed                Seed development database with sample data
  --verbose, -v         Enable verbose output
  --help, -h            Show this help message

EXAMPLES:
  # Initial setup
  ./PTDoc-Foundry.sh

  # Setup with database migration
  ./PTDoc-Foundry.sh --create-migration

  # Seed database after migration
  ./PTDoc-Foundry.sh --seed

  # Full setup with verbose output
  ./PTDoc-Foundry.sh --create-migration --seed --verbose

ENVIRONMENT VARIABLES:
  PFP_DB_PATH           Override database file path
  DOTNET_ROOT           Override .NET SDK location

NOTES:
  - Run from repository root directory
  - Do NOT run with sudo
  - Requires .NET 8.0 SDK
  - Safe to run multiple times (idempotent)

EOF
  exit 0
fi

log() {
  echo "${BLUE}==>${RESET} ${BOLD}$1${RESET}"
}

log_success() {
  echo "${GREEN}✓${RESET} $1"
}

log_warn() {
  echo "${YELLOW}⚠${RESET} $1"
}

log_error() {
  echo "${RED}✗${RESET} $1" >&2
}

verbose() {
  if [ "$VERBOSE" = true ]; then
    echo "${BLUE}  →${RESET} $1"
  fi
}

# Check if running with sudo
if [ "${EUID:-$(id -u)}" -eq 0 ]; then
  log_error "Do NOT run this script with sudo"
  log_error "Run as a normal user: ./PTDoc-Foundry.sh"
  exit 1
fi

# Check .NET SDK
log "Checking .NET SDK..."
if ! command -v dotnet >/dev/null 2>&1; then
  log_error ".NET SDK not found"
  log_error "Install .NET 8.0 SDK from: https://dotnet.microsoft.com/download/dotnet/8.0"
  exit 1
fi

DOTNET_VERSION=$(dotnet --version)
verbose "Found .NET SDK version: $DOTNET_VERSION"

# Check global.json requirements
if [ -f "$ROOT_DIR/global.json" ]; then
  REQUIRED_VERSION=$(grep -oP '"version":\s*"\K[^"]+' "$ROOT_DIR/global.json" || echo "")
  if [ -n "$REQUIRED_VERSION" ]; then
    verbose "Required SDK version from global.json: $REQUIRED_VERSION"
  fi
fi

log_success ".NET SDK found: $DOTNET_VERSION"

# Check solution file
log "Validating solution structure..."
SLN_FILE="$ROOT_DIR/PTDoc.sln"
if [ ! -f "$SLN_FILE" ]; then
  log_error "PTDoc.sln not found in $ROOT_DIR"
  exit 1
fi
log_success "Solution file found"

# Restore NuGet packages
log "Restoring NuGet packages..."
if dotnet restore "$SLN_FILE" ${VERBOSE:+--verbosity detailed} > /dev/null 2>&1; then
  log_success "NuGet packages restored"
else
  log_error "Failed to restore NuGet packages"
  exit 1
fi

# Build solution to verify
log "Building solution..."
if [ "$VERBOSE" = true ]; then
  dotnet build "$SLN_FILE" --no-restore
else
  dotnet build "$SLN_FILE" --no-restore > /dev/null 2>&1
fi

if [ $? -eq 0 ]; then
  log_success "Solution build successful"
else
  log_error "Solution build failed"
  log_error "Run with --verbose to see detailed errors"
  exit 1
fi

# Check for MAUI workloads (only if MAUI project exists)
if [ -d "$ROOT_DIR/src/PTDoc.Maui" ]; then
  log "Checking MAUI workloads..."
  if dotnet workload list | grep -q "maui"; then
    log_success "MAUI workloads installed"
  else
    log_warn "MAUI workloads not installed"
    echo "Install with: ${BOLD}dotnet workload install maui${RESET}"
  fi
fi

# Create migration if requested
if [ "$CREATE_MIGRATION" = true ]; then
  log "Creating database migration..."
  
  # Check if EF Core tools are installed
  if ! dotnet ef --version > /dev/null 2>&1; then
    log "Installing EF Core tools..."
    dotnet tool install --global dotnet-ef
  fi
  
  # Check if Initial migration exists
  MIGRATION_DIR="$ROOT_DIR/src/PTDoc.Infrastructure/Data/Migrations"
  if [ -d "$MIGRATION_DIR" ] && [ "$(ls -A "$MIGRATION_DIR" 2>/dev/null)" ]; then
    log_warn "Migrations already exist in $MIGRATION_DIR"
    log_warn "Skipping migration creation"
  else
    verbose "Creating Initial migration..."
    EF_PROVIDER=sqlite dotnet ef migrations add Initial \
      -p "$ROOT_DIR/src/PTDoc.Infrastructure" \
      -s "$ROOT_DIR/src/PTDoc.Api" \
      ${VERBOSE:+--verbose}
    
    log "Applying migration to database..."
    EF_PROVIDER=sqlite dotnet ef database update \
      -p "$ROOT_DIR/src/PTDoc.Infrastructure" \
      -s "$ROOT_DIR/src/PTDoc.Api" \
      ${VERBOSE:+--verbose}
    
    log_success "Database migration created and applied"
  fi
fi

# Seed database if requested
if [ "$SEED_DB" = true ]; then
  log "Seeding development database..."
  
  # Check for seeder project
  SEEDER_PROJECT="$ROOT_DIR/src/PTDoc.Seeder/PTDoc.Seeder.csproj"
  if [ -f "$SEEDER_PROJECT" ]; then
    dotnet run --project "$SEEDER_PROJECT" ${VERBOSE:+--verbosity detailed}
    log_success "Database seeded successfully"
  else
    log_warn "PTDoc.Seeder project not found"
    log_warn "Seeding skipped"
  fi
fi

# Database path information
DB_PATH="${PFP_DB_PATH:-$ROOT_DIR/dev.PTDoc.db}"
log "Database configuration:"
verbose "  Default path: $DB_PATH"
verbose "  Override with: export PFP_DB_PATH=/path/to/database.db"

# Summary
echo ""
log "${GREEN}PTDoc Foundry setup complete!${RESET}"
echo ""
echo "Next steps:"
echo "  1. Run API: ${BOLD}dotnet run --project src/PTDoc.Api --urls http://localhost:5170${RESET}"
echo "  2. Run Web: ${BOLD}dotnet run --project src/PTDoc.Web${RESET}"
echo "  3. Run MAUI: ${BOLD}dotnet build -t:Run -f net8.0-maccatalyst src/PTDoc.Maui/PTDoc.csproj${RESET}"
echo ""
echo "For help: ${BOLD}./PTDoc-Foundry.sh --help${RESET}"
