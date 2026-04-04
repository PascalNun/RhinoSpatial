# RhinoWFS Installation

This package is currently an early alpha release of RhinoWFS for Rhino 8 on macOS.

## Included Files

- `RhinoWFS.gha`
- `RhinoWFS.deps.json`
- `RhinoWFS.runtimeconfig.json`
- `WfsCore.dll`
- `README.md`
- `LICENSE`

## Manual Installation

1. Close Rhino before copying plugin files.
2. Copy all plugin files from the zip into your Grasshopper Libraries folder.
3. Start Rhino 8.
4. Open Grasshopper.
5. Look for the `RhinoWFS` tab.

If Grasshopper was already open while the file was copied, restart Rhino and Grasshopper once.

## Notes

- RhinoWFS is currently still in an early alpha stage.
- The plugin has only been tested with a limited number of real WFS services so far.
- Behavior may still vary depending on the WFS provider, geometry type, SRS, version, or response format.
