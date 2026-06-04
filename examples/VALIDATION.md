# Validation Checklist

This checklist is the manual validation set for RhinoSpatial.

It is intentionally small and practical. The point is to confirm that the core data layers still feel aligned, usable, and trustworthy after changes.

## General Checks

- RhinoSpatial builds cleanly from `RhinoSpatial.sln`
- sandbox probes from `docs/TEST_SOURCES.md` still run for at least one WFS, one WMS, one WCS, one CityGML file, one Shapefile, and one GeoTIFF file
- component grouping still reads as `Context / Layers / Sources / Viewers`
- icons load correctly for all components
- `Spatial Context` still acts as the shared starting point
- source outputs still align correctly when used together
- status messaging still feels clear and calm rather than overly technical
- opening saved example definitions does not show Grasshopper IO archive warnings
- old working definitions that still contain removed/renamed component parameters are rebuilt by replacing the affected component once

## Spatial Context

- create a `Spatial Context`
- open the map helper
- select an area
- save the Grasshopper file
- reopen the file
- confirm the selected area persists
- confirm reopening the helper redraws the saved selection

## WFS

- connect a reference WFS URL
- use `List WFS Layers`
- load one selected layer through `Load WFS`
- load multiple layers directly and confirm output branches stay grouped by layer
- connect a local `.shp` file path directly to `Load WFS` and confirm vector geometry aligns to the same `Spatial Context`
- confirm localized placement still works

## WMS

- connect a WMS URL
- use `List WMS Layers`
- load one selected layer through `Load WMS`
- confirm the image aligns to the same `Spatial Context`
- confirm fallback imagery still works when no custom source is provided; the default should try the sharper OSM-style WMS fallback before the broader NASA imagery fallback
- test at least one WMS layer whose advertised CRS differs from the Spatial Context primary SRS and confirm RhinoSpatial can request a supported alternate CRS when the context provides one

## LoD2 Buildings

- connect a LoD2 WFS source
- load buildings through `Load LoD2 Buildings`
- test a local CityGML/GML/XML building file through the same `LoD2 Source` input when available
- test a local CityJSON building file through the same `LoD2 Source` input when available
- test a folder or ZIP archive of CityGML/GML/XML tiles and confirm out-of-context files are skipped when bounds are available
- test a dense single CityGML tile with a small Spatial Context and confirm building-level prefiltering avoids parsing/converting the whole tile
- confirm the building output still aligns to the same `Spatial Context`
- confirm localized mode still behaves consistently with terrain
- confirm obvious missing-face regressions are not reintroduced
- connect the `Status` output and check whether gaps are reported as returned buildings/surfaces with skipped conversion, or as no returned LoD2 data for that area
- confirm the status reports the buffered WFS query bounds and the kept buildings still match the visible Spatial Context
- for suspected provider gaps, compare returned LoD2 local bounds and output Brep bounds; matching bounds with low conversion failures usually points to source coverage rather than dropped geometry

## Terrain

- load terrain through `Load Terrain`
- confirm terrain aligns with the same `Spatial Context`
- confirm terrain and LoD2 share a sensible local Z reference in localized mode
- confirm absolute mode still keeps real coordinates when requested
- leave the Terrain URL empty and confirm the global fallback returns usable context terrain for a small study area
- try an intentionally large Spatial Context and confirm the fallback fails quickly with a clear status message instead of hanging Grasshopper
- connect a user-provided WCS terrain source and confirm explicit source data still wins over fallback behavior
- connect a local `.tif` / `.tiff` DEM path to `Load Terrain` and confirm it produces an aligned terrain mesh or a clear non-overlap warning

## GeoTIFF

- load a georeferenced raster through `Load GeoTIFF`
- confirm raster placement aligns to the same `Spatial Context`
- confirm image/material behavior is usable in Rhino / Grasshopper
- confirm alpha/transparency handling still behaves sensibly

## OSM

- load `Buildings` only and confirm the result is usable study geometry
- load `Road` and confirm the road region is continuous and stable
- load `Water`, `Green`, and `Rail`
- confirm category outputs feel trustworthy
- confirm OSM still works gracefully when one category fails or times out
- confirm status output stays readable during partial OSM failures

## Google 3D Tiles Viewer

- connect a user-managed Google Maps API key
- enable `3D Tiles Viewer (Google)` for a small Spatial Context
- confirm preview meshes and materials appear aligned to the same area
- confirm status clearly communicates viewer behavior and bounded loading
- confirm the component remains disabled and quiet when Enable is false
- confirm the component is presented as a viewer/reference workflow, not as a bake/export/import workflow
- confirm older definitions no longer expose the removed browser/viewer-window inputs or `Viewer URL` output after replacing the component
- confirm the status reports selected tile URLs, candidate URLs, output bounds, and parent fallback behavior when fallback parent tiles are used
- confirm small over-coverage is acceptable when it prevents missing chunks inside the selected reference area

## Combined Workflow

- use one `Spatial Context`
- load WFS, WMS, LoD2, Terrain, GeoTIFF, OSM, and Google 3D Tiles reference context in the same definition as far as practical
- confirm outputs align in XY
- confirm localized placement still makes the workflow manageable near the Rhino origin
- confirm terrain/building elevation consistency remains acceptable

## Release Package

- run `scripts/build-food4rhino-zip.sh`
- confirm the generated zip includes the `.gha`, runtime files, `RhinoSpatial.Core.dll`, `README.md`, `LICENSE`, `THIRD-PARTY-NOTICES.md`, and `INSTALL.md`
- run `scripts/build-yak.sh` on a machine with Rhino's Yak CLI available
- confirm the generated Yak package includes the same user-facing docs, including `INSTALL.md`
- confirm ignored staging/package artifacts are not committed
