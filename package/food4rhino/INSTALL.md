# RhinoSpatial Installation

This package is currently an alpha release of RhinoSpatial for Rhino 8.

## Included Files

- `RhinoSpatial.gha`
- `RhinoSpatial.deps.json`
- `RhinoSpatial.runtimeconfig.json`
- `RhinoSpatial.Core.dll`
- `NetTopologySuite.Features.dll`
- `NetTopologySuite.IO.Esri.Shapefile.dll`
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
- `Load LoD2 Buildings`
- `Load OSM`
- `Load GeoTIFF`
- `Load Terrain`
- `3D Tiles Viewer (Google)` if you have a user-managed Google Maps API key

## Notes

- RhinoSpatial is currently still in an alpha stage.
- The plugin has only been tested with a limited number of real WFS, WMS, LoD2, terrain, GeoTIFF, OSM, and 3D Tiles workflows so far.
- Leave imagery, terrain, or OSM source inputs empty only when you want RhinoSpatial's broad fallback behavior for quick context. The terrain fallback is intentionally limited to small study areas and short request times. For project work, prefer your own official or project-specific data sources.
- `Load WFS` accepts WFS URLs and local `.shp` files for vector context.
- `Load LoD2 Buildings` accepts a LoD2 WFS URL, local CityGML/GML/XML/CityJSON file, folder, or ZIP archive through one `LoD2 Source` input. Large local files may still take time because they have to be inspected before out-of-context buildings can be skipped.
- The Google 3D Tiles component is a viewer/reference workflow that requires the user's own Google Maps API key and should not be treated as an editable project data source, cache, bake, or export workflow.
- Behavior may still vary depending on the provider, geometry type, SRS, version, response format, API access, or local file metadata.
- Release packages also include a small number of third-party libraries. See `THIRD-PARTY-NOTICES.md` for bundled dependency notices.
