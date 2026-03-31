#!/usr/bin/env bash
# setup-dev-secrets.sh
# One-command dev secrets bootstrap for PTDoc.
# Generates cryptographically strong signing keys and stores them in dotnet user-secrets.
# Secrets are NEVER written to tracked files or printed to the terminal.
#
# Usage:
#   ./setup-dev-secrets.sh
#
# Requirements:
#   - .NET 8 SDK (dotnet CLI)
#   - openssl (available on macOS/Linux by default)

set -euo pipefail

API_PROJECT="src/PTDoc.Api/PTDoc.Api.csproj"
WEB_PROJECT="src/PTDoc.Web/PTDoc.Web.csproj"

# Colors (fall back gracefully if terminal doesn't support them)
RED='\033[0;31m'; GREEN='\033[0;32m'; YELLOW='\033[1;33m'; CYAN='\033[0;36m'; NC='\033[0m'
if ! [ -t 1 ]; then RED=''; GREEN=''; YELLOW=''; CYAN=''; NC=''; fi

echo -e "${CYAN}🔐 PTDoc - Development Secrets Bootstrap${NC}"
echo -e "${CYAN}==========================================${NC}"
echo ""

# --- Prerequisites ---
if ! command -v dotnet &>/dev/null; then
    echo -e "${RED}❌ dotnet CLI not found. Install .NET 8 SDK from https://dotnet.microsoft.com/download${NC}"
    exit 1
fi

if ! command -v openssl &>/dev/null; then
    echo -e "${RED}❌ openssl not found. Install openssl (brew install openssl on macOS).${NC}"
    exit 1
fi

# Verify we're running from the repo root
if [ ! -f "PTDoc.sln" ]; then
    echo -e "${RED}❌ PTDoc.sln not found. Run this script from the PTDoc repository root.${NC}"
    exit 1
fi

# --- Generate secrets (no output to prevent accidental logging) ---
JWT_KEY=$(openssl rand -base64 64)
INTAKE_KEY=$(openssl rand -base64 32)

# --- Store in user-secrets (never printed) ---
echo "Setting Jwt:SigningKey for PTDoc.Api..."
dotnet user-secrets set "Jwt:SigningKey" "$JWT_KEY" --project "$API_PROJECT" >/dev/null
echo -e "${GREEN}✓ Jwt:SigningKey stored in user-secrets for PTDoc.Api${NC}"

echo "Setting IntakeInvite:SigningKey for PTDoc.Api..."
dotnet user-secrets set "IntakeInvite:SigningKey" "$INTAKE_KEY" --project "$API_PROJECT" >/dev/null
echo -e "${GREEN}✓ IntakeInvite:SigningKey stored in user-secrets for PTDoc.Api${NC}"

echo "Setting IntakeInvite:SigningKey for PTDoc.Web..."
dotnet user-secrets set "IntakeInvite:SigningKey" "$INTAKE_KEY" --project "$WEB_PROJECT" >/dev/null
echo -e "${GREEN}✓ IntakeInvite:SigningKey stored in user-secrets for PTDoc.Web${NC}"

# --- Unset variables immediately ---
unset JWT_KEY
unset INTAKE_KEY

echo ""
echo -e "${GREEN}✅ Dev secrets configured successfully!${NC}"
echo ""
echo -e "${YELLOW}Next steps:${NC}"
echo "  1. Set up the database:  ./PTDoc-Foundry.sh --create-migration --seed"
echo "  2. Start the API:        dotnet run --project src/PTDoc.Api --urls http://localhost:5170"
echo "  3. Start the Web:        dotnet run --project src/PTDoc.Web"
echo "  4. Or use the launcher:  ./run-ptdoc.sh"
echo ""
echo "Note: Secrets are stored in your OS user profile (~/.microsoft/usersecrets/) and are"
echo "      never committed to git. Re-run this script any time to rotate your dev keys."
