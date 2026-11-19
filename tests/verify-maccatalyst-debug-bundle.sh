#!/usr/bin/env bash
set -eu

if [[ $# != 1 || ! -d "$1/Contents" ]]; then
    echo "Usage: bash tests/verify-maccatalyst-debug-bundle.sh <path-to-PTDoc.app>" >&2
    exit 2
fi

contents="$1/Contents"
for required in \
    MonoBundle/System.Private.CoreLib.dll \
    MonoBundle/PTDoc.dll \
    MonoBundle/PTDoc.UI.dll \
    MonoBundle/libe_sqlcipher.dylib \
    Resources/LatoFont/Lato-Regular.ttf \
    Resources/LatoFont/OFL.txt \
    Resources/Communication/Templates/intake-otp-email.html; do
    if [[ ! -s "$contents/$required" ]]; then
        echo "Missing or empty Mac Catalyst bundle file: $required" >&2
        exit 1
    fi
done

echo "Mac Catalyst runtime, PTDoc assemblies, and bundled resources are present."
