#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${1:-Release}"
FRAMEWORK="net7.0"
PLUGIN_BUILD_DIR="$ROOT_DIR/RhinoSpatial/bin/$CONFIGURATION/$FRAMEWORK"
STAGING_DIR="$ROOT_DIR/package/staging/food4rhino"
VERSION="0.2.2-alpha"
ZIP_NAME="RhinoSpatial-${VERSION}.zip"

dotnet build "$ROOT_DIR/RhinoSpatial.sln" -c "$CONFIGURATION"

find "$PLUGIN_BUILD_DIR" -maxdepth 1 -type f \( -name 'RhinoWFS*' -o -name 'WfsCore*' \) -delete

rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR"

find "$PLUGIN_BUILD_DIR" -maxdepth 1 -type f \
  ! -name '*.pdb' \
  -exec cp {} "$STAGING_DIR/" \;

cp "$ROOT_DIR/README.md" "$STAGING_DIR/README.md"
cp "$ROOT_DIR/LICENSE" "$STAGING_DIR/LICENSE"
cp "$ROOT_DIR/THIRD-PARTY-NOTICES.md" "$STAGING_DIR/THIRD-PARTY-NOTICES.md"
cp "$ROOT_DIR/package/food4rhino/INSTALL.md" "$STAGING_DIR/INSTALL.md"

(
  cd "$STAGING_DIR"
  rm -f "$ZIP_NAME"
  zip -r "$ZIP_NAME" . -x '*.zip' >/dev/null
)

echo
echo "Food4Rhino release zip built:"
echo "  $STAGING_DIR/$ZIP_NAME"
