#!/usr/bin/env bash
set -euo pipefail

# ============================================================================
# run-ptdoc.sh - PTDoc Launch Helper
# ============================================================================
#
# Quick launcher for PTDoc applications across different platforms.
# Automatically starts the API server when needed.
#
# Usage:
#   ./run-ptdoc.sh
#   
# Then select:
#   1) Blazor Web (browser)
#   2) Android (emulator)
#   3) iOS (simulator)
#   4) Mac Catalyst (desktop)
#
# ============================================================================

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$SCRIPT_DIR"
WEB_CSPROJ="$ROOT_DIR/src/PTDoc.Web/PTDoc.Web.csproj"
MAUI_CSPROJ="$ROOT_DIR/src/PTDoc.Maui/PTDoc.csproj"
API_CSPROJ="$ROOT_DIR/src/PTDoc.Api/PTDoc.Api.csproj"
API_URL="${API_URL:-http://localhost:5170}"
API_PORT="${API_URL##*:}"
API_PID=""

# Colors
RESET=""; BOLD=""; RED=""; GREEN=""; YELLOW=""; BLUE=""
if command -v tput >/dev/null 2>&1 && [ "$(tput colors 2>/dev/null || echo 0)" -ge 8 ]; then
  RESET="$(tput sgr0)"
  BOLD="$(tput bold)"
  RED="$(tput setaf 1)"
  GREEN="$(tput setaf 2)"
  YELLOW="$(tput setaf 3)"
  BLUE="$(tput setaf 6)"
fi

cleanup() {
  if [[ -n "$API_PID" ]]; then
    echo ""
    echo "${YELLOW}Stopping API server (PID: $API_PID)...${RESET}"
    kill "$API_PID" 2>/dev/null || true
    wait "$API_PID" 2>/dev/null || true
    echo "${GREEN}✓ API server stopped${RESET}"
  fi
}

trap cleanup EXIT INT

# Check .NET SDK
if ! command -v dotnet >/dev/null 2>&1; then
  echo "${RED}❌ .NET SDK not found. Install .NET 8.0+ and try again.${RESET}" >&2
  exit 1
fi

# Validate project files
if [[ ! -f "$WEB_CSPROJ" ]]; then
  echo "${RED}❌ Web project not found at $WEB_CSPROJ${RESET}" >&2
  exit 1
fi

if [[ ! -f "$MAUI_CSPROJ" ]]; then
  echo "${RED}❌ MAUI project not found at $MAUI_CSPROJ${RESET}" >&2
  exit 1
fi

if [[ ! -f "$API_CSPROJ" ]]; then
  echo "${RED}❌ API project not found at $API_CSPROJ${RESET}" >&2
  exit 1
fi

start_api() {
  if [[ -n "${SKIP_API:-}" ]]; then
    echo "${YELLOW}⚠ Skipping API startup (SKIP_API set)${RESET}"
    return
  fi

  # Check if API is already running
  if lsof -Pi :"$API_PORT" -sTCP:LISTEN -t >/dev/null 2>&1; then
    echo "${YELLOW}⚠ API already running on port $API_PORT${RESET}"
    return
  fi

  echo "${BLUE}🚀 Starting PTDoc API on $API_URL...${RESET}"
  dotnet run --project "$API_CSPROJ" --no-build --urls "$API_URL" >/tmp/ptdoc-api.log 2>&1 &
  API_PID=$!
  
  # Wait for API to be ready
  echo -n "${BLUE}Waiting for API to start...${RESET}"
  for i in {1..15}; do
    if lsof -Pi :"$API_PORT" -sTCP:LISTEN -t >/dev/null 2>&1; then
      echo " ${GREEN}Ready!${RESET}"
      return
    fi
    sleep 1
    echo -n "."
  done
  
  echo " ${RED}Failed${RESET}"
  echo "${RED}❌ API failed to start. Check /tmp/ptdoc-api.log for details${RESET}"
  exit 1
}

echo ""
echo "${BOLD}${BLUE}PTDoc Launcher${RESET}"
echo "${BOLD}═══════════════════════════════════════${RESET}"
echo ""
echo "Select platform to run:"
echo ""
echo "  ${GREEN}1)${RESET} Blazor Web ${BOLD}(browser)${RESET}"
echo "  ${GREEN}2)${RESET} Android ${BOLD}(emulator)${RESET}"
echo "  ${GREEN}3)${RESET} iOS ${BOLD}(simulator)${RESET}"
echo "  ${GREEN}4)${RESET} Mac Catalyst ${BOLD}(desktop)${RESET}"
echo ""
read -rp "Enter choice [1-4]: " choice

echo ""

case "$choice" in
  1)
    echo "${BLUE}Launching Blazor Web...${RESET}"
    echo ""
    dotnet run --project "$WEB_CSPROJ"
    ;;
  2)
    echo "${BLUE}Building and launching Android...${RESET}"
    echo ""
    start_api
    sleep 2
    echo ""
    echo "${BLUE}Note: Android emulator uses http://10.0.2.2:5170 to reach host API${RESET}"
    echo ""
    dotnet build -t:Run -f net8.0-android "$MAUI_CSPROJ"
    ;;
  3)
    echo "${BLUE}Building and launching iOS simulator...${RESET}"
    echo ""
    start_api
    sleep 2
    echo ""
    dotnet build -t:Run -f net8.0-ios "$MAUI_CSPROJ"
    ;;
  4)
    echo "${BLUE}Building and launching Mac Catalyst...${RESET}"
    echo ""
    start_api
    sleep 2
    echo ""
    dotnet build -t:Run -f net8.0-maccatalyst "$MAUI_CSPROJ"
    ;;
  *)
    echo "${RED}Invalid choice. Please enter 1-4${RESET}"
    exit 1
    ;;
esac
