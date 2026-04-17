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

Typical Grasshopper Libraries locations are:

- macOS:
  `~/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/Grasshopper (<version>)/Libraries`
- Windows:
  `%AppData%\Grasshopper\Libraries`

## First Run

The usual first workflow is:

1. Place `Spatial Context`
2. Define the study area once
3. Connect that same `Spatial Context` into one or more source components

Good first components to try are:

- `Load WMS`
- `Load WFS`
- `Load OSM`
- `Load GeoTIFF`

## Notes

- RhinoSpatial is currently still in an early alpha stage.
- The plugin has only been tested with a limited number of real WFS, WMS, LoD2, terrain, GeoTIFF, and OSM-related sources so far.
- Behavior may still vary depending on the provider, geometry type, SRS, version, or response format.
- Release packages also include a small number of third-party libraries. See `THIRD-PARTY-NOTICES.md` for bundled dependency notices.
