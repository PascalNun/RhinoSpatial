#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
CONFIGURATION="${1:-Release}"
FRAMEWORK="net7.0"
PLUGIN_BUILD_DIR="$ROOT_DIR/RhinoSpatial/bin/$CONFIGURATION/$FRAMEWORK"
STAGING_DIR="$ROOT_DIR/package/staging/food4rhino"
PROJECT_VERSION="$(sed -nE 's:.*<Version>([^<]+)</Version>.*:\1:p' "$ROOT_DIR/RhinoSpatial/RhinoSpatial.csproj" | head -n 1)"
VERSION="${PROJECT_VERSION}-alpha"
ZIP_NAME="RhinoSpatial-${VERSION}.zip"

dotnet build "$ROOT_DIR/RhinoSpatial.sln" -c "$CONFIGURATION"

rm -rf "$STAGING_DIR"
mkdir -p "$STAGING_DIR"

find "$PLUGIN_BUILD_DIR" -maxdepth 1 -type f \
  ! -name '*.pdb' \
  -exec cp {} "$STAGING_DIR/" \;

cp "$ROOT_DIR/package/food4rhino/PACKAGE_README.md" "$STAGING_DIR/README.md"
cp "$ROOT_DIR/CHANGELOG.md" "$STAGING_DIR/CHANGELOG.md"
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
