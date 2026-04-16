# RhinoSpatial Installation

This package is currently an early alpha release of RhinoSpatial for Rhino 8.

## Included Files

- `RhinoSpatial.gha`
- `RhinoSpatial.deps.json`
- `RhinoSpatial.runtimeconfig.json`
- `RhinoSpatial.Core.dll`
- `README.md`
- `LICENSE`
- `THIRD-PARTY-NOTICES.md`

## Manual Installation

1. Close Rhino before copying plugin files.
2. Copy all plugin files from the zip into your Grasshopper Libraries folder.
3. Start Rhino 8.
4. Open Grasshopper.
5. Look for the `RhinoSpatial` tab.

If Grasshopper was already open while the file was copied, restart Rhino and Grasshopper once.

## Notes

- RhinoSpatial is currently still in an early alpha stage.
- The plugin has only been tested with a limited number of real WFS, WMS, LoD2, and terrain services so far.
- Behavior may still vary depending on the provider, geometry type, SRS, version, or response format.
- Release packages also include a small number of third-party libraries. See `THIRD-PARTY-NOTICES.md` for bundled dependency notices.
