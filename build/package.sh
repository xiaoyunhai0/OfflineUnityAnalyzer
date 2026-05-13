#!/usr/bin/env bash
set -euo pipefail

CONFIGURATION="${CONFIGURATION:-Release}"
RUNTIME="${RUNTIME:-win-x64}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH_ROOT="$ROOT/publish/OfflineUnityAnalyzer-$RUNTIME"

rm -rf "$PUBLISH_ROOT" "$PUBLISH_ROOT.zip"
mkdir -p "$PUBLISH_ROOT"

dotnet publish "$ROOT/src/OfflineUnityAnalyzer.App/OfflineUnityAnalyzer.App.csproj" \
  -c "$CONFIGURATION" \
  -r "$RUNTIME" \
  --self-contained true \
  -o "$PUBLISH_ROOT"

dotnet publish "$ROOT/src/OfflineUnityAnalyzer.Cli/OfflineUnityAnalyzer.Cli.csproj" \
  -c "$CONFIGURATION" \
  -r "$RUNTIME" \
  --self-contained true \
  -o "$PUBLISH_ROOT"

cp -R "$ROOT/assets" "$PUBLISH_ROOT/assets"
cp -R "$ROOT/templates" "$PUBLISH_ROOT/templates"
cp -R "$ROOT/docs" "$PUBLISH_ROOT/docs"
cp "$ROOT/samples/config.sample.json" "$PUBLISH_ROOT/config.sample.json"
cp "$ROOT/README.md" "$PUBLISH_ROOT/README.md"

(cd "$ROOT/publish" && zip -qr "OfflineUnityAnalyzer-$RUNTIME.zip" "OfflineUnityAnalyzer-$RUNTIME")
