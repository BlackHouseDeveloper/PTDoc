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
API_LOG_FILE="/tmp/ptdoc-api.log"
DEV_SECRETS_SCRIPT="$ROOT_DIR/setup-dev-secrets.sh"
DEV_SECRETS_LOG_FILE="/tmp/ptdoc-dev-secrets.log"
SECRETS_BOOTSTRAPPED="false"

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

  get_port_listener_pids() {
    lsof -nP -iTCP:"$API_PORT" -sTCP:LISTEN -t 2>/dev/null \
      | sort -u \
      | tr '\n' ' ' \
      | sed 's/[[:space:]]*$//'
  }

  describe_port_listeners() {
    local pids
    pids="$(get_port_listener_pids)"

    if [[ -z "$pids" ]]; then
      echo "none"
      return
    fi

    local details=""
    local pid
    for pid in $pids; do
      local cmd
      cmd="$(ps -p "$pid" -o comm= 2>/dev/null || true)"
      if [[ -n "$cmd" ]]; then
        details+="$cmd($pid) "
      else
        details+="pid:$pid "
      fi
    done

    echo "${details% }"
  }

  is_api_healthy() {
    if ! command -v curl >/dev/null 2>&1; then
      return 1
    fi

    curl --silent --show-error --fail --max-time 2 \
      "${API_URL%/}/health/live" >/dev/null 2>&1
  }

  api_failed_for_missing_secrets() {
    [[ -f "$API_LOG_FILE" ]] || return 1
    grep -Eiq "JWT signing key has not been configured|IntakeInvite:SigningKey has not been configured" "$API_LOG_FILE"
  }

  bootstrap_dev_secrets() {
    if [[ "$SECRETS_BOOTSTRAPPED" == "true" ]]; then
      return 1
    fi

    if [[ -n "${SKIP_SECRET_SETUP:-}" ]]; then
      echo "${RED}❌ Dev secrets are missing and SKIP_SECRET_SETUP is set.${RESET}"
      return 1
    fi

    if [[ ! -f "$DEV_SECRETS_SCRIPT" ]]; then
      echo "${RED}❌ Dev secrets script not found at $DEV_SECRETS_SCRIPT${RESET}"
      return 1
    fi

    echo "${YELLOW}⚠ Missing dev secrets detected. Running setup-dev-secrets.sh...${RESET}"
    if bash "$DEV_SECRETS_SCRIPT" >"$DEV_SECRETS_LOG_FILE" 2>&1; then
      SECRETS_BOOTSTRAPPED="true"
      echo "${GREEN}✓ Dev secrets configured. Retrying API startup...${RESET}"
      return 0
    fi

    echo "${RED}❌ Failed to bootstrap dev secrets. Check $DEV_SECRETS_LOG_FILE${RESET}"
    tail -n 40 "$DEV_SECRETS_LOG_FILE" || true
    return 1
  }

  # Check if API is already running
  if lsof -Pi :"$API_PORT" -sTCP:LISTEN -t >/dev/null 2>&1; then
    listener_details="$(describe_port_listeners)"

    if is_api_healthy; then
      echo "${YELLOW}⚠ API already running and healthy at ${API_URL%/} (listeners: $listener_details)${RESET}"
      return
    fi

    echo "${RED}❌ Port $API_PORT is already in use (listeners: $listener_details) but PTDoc API health check failed at ${API_URL%/}/health/live.${RESET}"
    echo "${RED}   Stop the existing process or change API_URL before rerunning.${RESET}"
    exit 1
  fi

  for attempt in 1 2; do
    echo "${BLUE}🚀 Starting PTDoc API on $API_URL...${RESET}"
    dotnet run --no-build --project "$API_CSPROJ" --urls "$API_URL" >"$API_LOG_FILE" 2>&1 &
    API_PID=$!

    # Wait for API to be ready (listening + health endpoint)
    echo -n "${BLUE}Waiting for API to become healthy...${RESET}"
    for _ in {1..30}; do
      if ! kill -0 "$API_PID" 2>/dev/null; then
        echo " ${RED}Failed${RESET}"

        if [[ "$attempt" -eq 1 ]] && api_failed_for_missing_secrets && bootstrap_dev_secrets; then
          break
        fi

        echo "${RED}❌ API process exited early. Check $API_LOG_FILE${RESET}"
        tail -n 40 "$API_LOG_FILE" || true
        exit 1
      fi

      if is_api_healthy; then
        echo " ${GREEN}Ready!${RESET}"
        return
      fi

      sleep 1
      echo -n "."
    done

    # Health-wait timed out. Kill the stalled process so the port is freed
    # before the next attempt starts (or before we exit on the final attempt).
    echo " ${YELLOW}Timed out${RESET}"
    kill "$API_PID" 2>/dev/null || true
    wait "$API_PID" 2>/dev/null || true

    if [[ "$attempt" -eq 2 ]]; then
      echo "${RED}❌ API failed to become healthy. Check $API_LOG_FILE for details${RESET}"
      tail -n 40 "$API_LOG_FILE" || true
      exit 1
    fi

    echo "${YELLOW}⚠️  API did not become healthy within 30s — retrying...${RESET}"
  done
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
    start_api
    echo ""
    dotnet run --project "$WEB_CSPROJ"
    ;;
  2)
    echo "${BLUE}Building and launching Android...${RESET}"
    echo ""
    start_api
    echo ""
    echo "${BLUE}Note: Android emulator uses http://10.0.2.2:5170 to reach host API${RESET}"
    echo ""
    dotnet build -t:Run -f net8.0-android "$MAUI_CSPROJ"
    ;;
  3)
    echo "${BLUE}Building and launching iOS simulator...${RESET}"
    echo ""
    start_api
    echo ""
    dotnet build -t:Run -f net8.0-ios "$MAUI_CSPROJ"
    ;;
  4)
    echo "${BLUE}Building and launching Mac Catalyst...${RESET}"
    echo ""
    start_api
    echo ""
    dotnet build -t:Run -f net8.0-maccatalyst "$MAUI_CSPROJ"
    ;;
  *)
    echo "${RED}Invalid choice. Please enter 1-4${RESET}"
    exit 1
    ;;
esac
