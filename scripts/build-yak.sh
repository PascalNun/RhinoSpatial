#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${1:-Debug}"
FRAMEWORK="net7.0"
YAK_BIN="/Applications/Rhino 8.app/Contents/Resources/bin/yak"
PLUGIN_BUILD_DIR="$ROOT_DIR/RhinoWFS/bin/$CONFIGURATION/$FRAMEWORK"
STAGING_DIR="$ROOT_DIR/package/staging/yak"
MANIFEST_SOURCE="$ROOT_DIR/package/yak/manifest.yml"

if [[ ! -x "$YAK_BIN" ]]; then
  echo "Yak CLI was not found at: $YAK_BIN" >&2
  exit 1
fi

dotnet build "$ROOT_DIR/RhinoWFS.sln" -c "$CONFIGURATION"

rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR"

cp "$MANIFEST_SOURCE" "$STAGING_DIR/manifest.yml"
cp "$ROOT_DIR/README.md" "$STAGING_DIR/README.md"
cp "$ROOT_DIR/LICENSE" "$STAGING_DIR/LICENSE"
find "$PLUGIN_BUILD_DIR" -maxdepth 1 -type f \
  ! -name '*.pdb' \
  -exec cp {} "$STAGING_DIR/" \;

(
  cd "$STAGING_DIR"
  "$YAK_BIN" build --platform mac
)

echo
echo "Yak package built in:"
echo "  $STAGING_DIR"
