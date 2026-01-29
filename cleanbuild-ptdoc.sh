#!/usr/bin/env bash

# ============================================================================
# PTDoc Clean Build Script - Enhanced Cross-Platform Build Tool
# ============================================================================
#
# This script provides a comprehensive build and test workflow for the 
# PTDoc project with colored output, detailed logging, and cross-platform 
# compatibility.
#
# Features:
# - Cross-platform compatibility (macOS, Linux, Windows with WSL)
# - Colored terminal output with fallback for unsupported terminals
# - Comprehensive build cleanup and validation
# - Detailed logging with timestamped output
# - Build step tracking and failure reporting
# - Automatic log file opening (macOS only)
#
# Usage:
#   chmod +x cleanbuild-ptdoc.sh && ./cleanbuild-ptdoc.sh
#
# Requirements:
#   - .NET 8.0 SDK or later
#   - Git (for version control operations)
#   - Bash 4.0+ (default on modern systems)
#
# ============================================================================

set -u  # Exit on undefined variables

# ---- Platform Detection & Color Configuration -----------------------------

# Detect operating system
OS_TYPE=""
case "$(uname -s)" in
    Darwin*)    OS_TYPE="macOS" ;;
    Linux*)     OS_TYPE="Linux" ;;
    MINGW*|MSYS*|CYGWIN*) OS_TYPE="Windows" ;;
    *)          OS_TYPE="Unknown" ;;
esac

# Configure colors based on terminal capabilities
if command -v tput >/dev/null 2>&1 && [ "$(tput colors 2>/dev/null || echo 0)" -ge 8 ]; then
  RESET="$(tput sgr0)"; BOLD="$(tput bold)"
  RED="$(tput setaf 1)"; GREEN="$(tput setaf 2)"; YELLOW="$(tput setaf 3)"
  BLUE="$(tput setaf 4)"; 
  CYAN="$(tput setaf 6)"; MAGENTA="$(tput setaf 5)"
else
  RESET=""; BOLD=""
  RED=""; GREEN=""; YELLOW=""; BLUE=""; CYAN=""; MAGENTA=""
fi

# ---- Initialization & Logging Setup ---------------------------------------

# Generate timestamp for this build session
timestamp="$(date +"%Y%m%d-%H%M%S")"
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$SCRIPT_DIR"
LOG_DIR="$ROOT_DIR/build-logs"
LOG_FILE="$LOG_DIR/cleanbuild-$timestamp.log"

# Create log directory if it doesn't exist
mkdir -p "$LOG_DIR"

# Logging functions
log() {
  local message="$1"
  echo "${BLUE}==>${RESET} ${BOLD}$message${RESET}" | tee -a "$LOG_FILE"
}

log_success() {
  local message="$1"
  echo "${GREEN}✓${RESET} $message" | tee -a "$LOG_FILE"
}

log_error() {
  local message="$1"
  echo "${RED}✗${RESET} $message" | tee -a "$LOG_FILE" >&2
}

log_warn() {
  local message="$1"
  echo "${YELLOW}⚠${RESET} $message" | tee -a "$LOG_FILE"
}

log_step() {
  local step="$1"
  local total="$2"
  local message="$3"
  echo "" | tee -a "$LOG_FILE"
  echo "${CYAN}[$step/$total]${RESET} ${BOLD}$message${RESET}" | tee -a "$LOG_FILE"
  echo "${CYAN}────────────────────────────────────────${RESET}" | tee -a "$LOG_FILE"
}

# ---- Header Display -------------------------------------------------------

clear
echo "${BOLD}${BLUE}╔════════════════════════════════════════════════════════╗${RESET}"
echo "${BOLD}${BLUE}║         PTDoc Clean Build & Test Script               ║${RESET}"
echo "${BOLD}${BLUE}╚════════════════════════════════════════════════════════╝${RESET}"
echo ""
echo "${BOLD}Platform:${RESET} $OS_TYPE"
echo "${BOLD}Timestamp:${RESET} $(date '+%Y-%m-%d %H:%M:%S')"
echo "${BOLD}Log File:${RESET} $LOG_FILE"
echo ""

# ---- Prerequisites Check --------------------------------------------------

log_step "1" "8" "Checking Prerequisites"

# Check .NET SDK
if ! command -v dotnet >/dev/null 2>&1; then
  log_error ".NET SDK not found"
  log_error "Install from: https://dotnet.microsoft.com/download/dotnet/8.0"
  exit 1
fi

DOTNET_VERSION=$(dotnet --version)
log_success ".NET SDK found: $DOTNET_VERSION"

# Check for solution file
SLN_FILE="$ROOT_DIR/PTDoc.sln"
if [ ! -f "$SLN_FILE" ]; then
  log_error "PTDoc.sln not found in $ROOT_DIR"
  exit 1
fi
log_success "Solution file found: PTDoc.sln"

# ---- Clean Build Artifacts ------------------------------------------------

log_step "2" "8" "Cleaning Build Artifacts"

log "Removing bin/ and obj/ directories..."
find "$ROOT_DIR/src" -type d \( -name "bin" -o -name "obj" \) -exec rm -rf {} + 2>/dev/null || true
log_success "Build artifacts cleaned"

# ---- NuGet Restore --------------------------------------------------------

log_step "3" "8" "Restoring NuGet Packages"

if dotnet restore "$SLN_FILE" >> "$LOG_FILE" 2>&1; then
  log_success "NuGet packages restored successfully"
else
  log_error "NuGet restore failed"
  log_error "Check log file: $LOG_FILE"
  exit 1
fi

# ---- Build Solution -------------------------------------------------------

log_step "4" "8" "Building Solution (Debug)"

if dotnet build "$SLN_FILE" --no-restore --configuration Debug >> "$LOG_FILE" 2>&1; then
  log_success "Debug build completed successfully"
else
  log_error "Debug build failed"
  log_error "Check log file: $LOG_FILE"
  exit 1
fi

# ---- Build Solution (Release) ---------------------------------------------

log_step "5" "8" "Building Solution (Release)"

if dotnet build "$SLN_FILE" --no-restore --configuration Release >> "$LOG_FILE" 2>&1; then
  log_success "Release build completed successfully"
else
  log_error "Release build failed"
  log_error "Check log file: $LOG_FILE"
  exit 1
fi

# ---- Run Tests ------------------------------------------------------------

log_step "6" "8" "Running Tests"

# Find test projects
TEST_PROJECTS=$(find "$ROOT_DIR/src" -name "*.Tests.csproj" 2>/dev/null || true)

if [ -n "$TEST_PROJECTS" ]; then
  log "Found test project(s)"
  if dotnet test "$SLN_FILE" --no-build --configuration Debug >> "$LOG_FILE" 2>&1; then
    log_success "All tests passed"
  else
    log_error "Some tests failed"
    log_error "Check log file: $LOG_FILE"
    exit 1
  fi
else
  log_warn "No test projects found"
fi

# ---- Validate Project References ------------------------------------------

log_step "7" "8" "Validating Clean Architecture"

log "Checking dependency rules..."

# Core should have no project references
CORE_REFS=$(dotnet list "$ROOT_DIR/src/PTDoc.Core/PTDoc.Core.csproj" reference 2>/dev/null | grep -c "csproj" || echo "0")
if [ "$CORE_REFS" -eq 0 ]; then
  log_success "Core: No dependencies (correct)"
else
  log_error "Core: Has $CORE_REFS dependencies (should be 0)"
fi

# Application should only reference Core
if [ -f "$ROOT_DIR/src/PTDoc.Application/PTDoc.Application.csproj" ]; then
  APP_REFS=$(dotnet list "$ROOT_DIR/src/PTDoc.Application/PTDoc.Application.csproj" reference 2>/dev/null || echo "")
  if echo "$APP_REFS" | grep -q "PTDoc.Core" && ! echo "$APP_REFS" | grep -q "PTDoc.Infrastructure"; then
    log_success "Application: References Core only (correct)"
  else
    log_warn "Application: Check dependencies"
  fi
fi

# ---- Build Summary --------------------------------------------------------

log_step "8" "8" "Build Summary"

echo "" | tee -a "$LOG_FILE"
echo "${GREEN}${BOLD}✓ Build completed successfully!${RESET}" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"
echo "${BOLD}Summary:${RESET}" | tee -a "$LOG_FILE"
echo "  • Platform: $OS_TYPE" | tee -a "$LOG_FILE"
echo "  • .NET SDK: $DOTNET_VERSION" | tee -a "$LOG_FILE"
echo "  • Build Config: Debug + Release" | tee -a "$LOG_FILE"
echo "  • Log File: $LOG_FILE" | tee -a "$LOG_FILE"
echo "" | tee -a "$LOG_FILE"

# Open log file on macOS
if [ "$OS_TYPE" = "macOS" ]; then
  log "Opening build log..."
  open "$LOG_FILE" 2>/dev/null || true
fi

echo "${BOLD}Next steps:${RESET}"
echo "  1. Run API: ${CYAN}dotnet run --project src/PTDoc.Api${RESET}"
echo "  2. Run Web: ${CYAN}dotnet run --project src/PTDoc.Web${RESET}"
echo "  3. Run Tests: ${CYAN}dotnet test${RESET}"
echo ""
