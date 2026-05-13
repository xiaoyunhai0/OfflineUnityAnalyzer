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

if command -v zip >/dev/null 2>&1; then
  (cd "$ROOT/publish" && zip -qr "OfflineUnityAnalyzer-$RUNTIME.zip" "OfflineUnityAnalyzer-$RUNTIME")
else
  python3 - "$ROOT/publish" "OfflineUnityAnalyzer-$RUNTIME" <<'PY'
import os
import sys
import zipfile

publish_root, package_name = sys.argv[1], sys.argv[2]
source_dir = os.path.join(publish_root, package_name)
zip_path = os.path.join(publish_root, package_name + ".zip")

with zipfile.ZipFile(zip_path, "w", compression=zipfile.ZIP_DEFLATED) as archive:
    for current, _, files in os.walk(source_dir):
        for name in files:
            path = os.path.join(current, name)
            archive.write(path, os.path.relpath(path, publish_root))
PY
fi
