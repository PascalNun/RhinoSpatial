#!/usr/bin/env bash
set -e

dotnet build RhinoSpatial/RhinoSpatial.csproj

export RHINO_PACKAGE_DIRS="$PWD/RhinoSpatial/bin/Debug/net7.0"

"/Applications/Rhino 8.app/Contents/MacOS/Rhinoceros" \
  -runscript="_Grasshopper"
