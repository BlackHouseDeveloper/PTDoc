#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
fixture="$(mktemp -d)"
trap 'rm -rf "$fixture"' EXIT
cp "$root/run-ptdoc.sh" "$fixture/"
mkdir -p "$fixture/tools" "$fixture/src/"{PTDoc.Web,PTDoc.Maui,PTDoc.Api}
touch "$fixture/src/PTDoc.Web/PTDoc.Web.csproj" "$fixture/src/PTDoc.Api/PTDoc.Api.csproj" "$fixture/src/PTDoc.Maui/PTDoc.csproj"
bundles="$fixture/src/PTDoc.Maui/bin/Debug/net8.0-maccatalyst"
mkdir -p "$bundles/"{maccatalyst-arm64,maccatalyst-x64}/PTDoc.app
export LAUNCH_LOG="$fixture/launch.log"
export PATH="$fixture/tools:$PATH"
unset SKIP_API

cat > "$fixture/tools/uname" <<'SH'
#!/bin/sh
case "$1" in
    -s) echo Darwin ;;
    -m) echo "$TEST_ARCH" ;;
    *) exit 1 ;;
esac
SH
cat > "$fixture/tools/record" <<'SH'
#!/bin/sh
echo "${0##*/} $*" >> "$LAUNCH_LOG"
case "${0##*/}" in
    lsof) echo 123 ;;
    ps) echo test-api ;;
    dotnet) exit 1 ;;
esac
SH
chmod +x "$fixture/tools/"{uname,record}
for tool in codesign open lsof ps curl dotnet; do
    ln -s record "$fixture/tools/$tool"
done

for TEST_ARCH in arm64 x86_64; do
    export TEST_ARCH
    case "$TEST_ARCH" in
        arm64) rid=maccatalyst-arm64 ;;
        x86_64) rid=maccatalyst-x64 ;;
    esac
    : > "$LAUNCH_LOG"
    API_URL=http://localhost:5170/ bash "$fixture/run-ptdoc.sh" <<< 5 >/dev/null
    grep -Fxq "codesign --verify --strict -R=anchor apple generic $bundles/$rid/PTDoc.app" "$LAUNCH_LOG"
    grep -Fq "http://localhost:5170/health/live" "$LAUNCH_LOG"
    grep -Fxq "open -W $bundles/$rid/PTDoc.app" "$LAUNCH_LOG"
done

: > "$LAUNCH_LOG"
if API_URL=http://localhost:6100 bash "$fixture/run-ptdoc.sh" <<< 5 > "$fixture/output" 2>&1; then
    echo "The launcher accepted an API address the signed app cannot use." >&2
    exit 1
fi
grep -Fq "signed Debug app uses http://localhost:5170" "$fixture/output"
test ! -s "$LAUNCH_LOG"
echo "Signed Catalyst launcher checks passed for Intel, Apple Silicon and an incompatible API address."
