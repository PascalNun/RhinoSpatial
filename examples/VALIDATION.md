# Validation Checklist

This is the maintainer checklist for a release. For ordinary use, start with the
[visual workflow](../docs/SHOWCASE.md) or [project-data guide](../docs/WORKING_WITH_PROJECT_DATA.md).
The development work is merged into `main`; all eleven automated suites pass locally
and run in CI on macOS and Windows. Unchecked native items below remain pending and are not implied
by a successful console or CI run. The public package remains 0.4.0.

This is the maintainer-facing manual validation set for RhinoSpatial. It is
linked publicly for transparency, but ordinary users do not need to complete it
before using the plugin.

It is intentionally small and practical. The point is to confirm that the core data layers still feel aligned, usable, and trustworthy after changes.

## Recorded runtime check — 2026-11-02

All eleven console suites also passed using the arm64 .NET 7.0.0 runtime bundled
with Rhino 8.16 (`8.16.25042.13002`) on macOS, against source commit `c9d5e40`.
An isolated temporary test project compiled the existing Sandbox sources for
`net7.0` and referenced Core's existing `net7.0` target. This included all 52
correctness and 52 service-reliability cases; the Release build had no warnings
or errors. Project targets and the installed plugin were unchanged.

This was standalone console execution using Rhino's bundled runtime. It did not
run inside Rhino or Grasshopper, so it does not complete the interactive,
installed-package, preview, material, baking, saved-definition or model-unit
checks below. The native results below provide additional macOS evidence;
Windows host checks remain pending.

## Native macOS results — 2026-11-02

Tested the development code at `c9d5e40` in Rhino 8.16 (`8.16.25042.13002`),
Grasshopper 1.0.0008 and .NET 7.0.0 on arm64 macOS. Checks used actual
Grasshopper document solutions and serialization, with viewport and map-helper
inspection. Service requests used small synthetic loopback fixtures; these
results do not establish live-provider coverage or Windows host behavior.

42 functional checks passed:

| Area | Observed result |
| --- | --- |
| Components and saved examples | All ten component interfaces instantiated. All six example definitions opened without archive warnings, and their connections survived save/reopen. |
| Sources | WMS, WMTS, single/multipart WCS, OSM roads, GeoPackage courtyard curves, rotated imagery, COG imagery/terrain and CityJSON model/sequence/template files produced valid geometry. A synthetic ATOM building archive downloaded and produced six surfaces. |
| Interrupted loading | WMS/WCS/OSM body transfers stopped about 1.5 s after a scheduled cancellation and published no geometry. Normal-source retries completed in 0.01–0.19 s. Truncated and oversized transfers produced no geometry. |
| Masks and materials | COG terrain left the masked area open and image pixels retained alpha. WMS and rotated-image bakes retained UVs and texture references after `.3dm` save/reopen. Both textures displayed in Rhino's Rendered mode. |
| Saved state | A WMS definition solved after save/reopen. A saved area took precedence over a different file reference within that study; the browser helper displayed a restored rectangle. |

Separate checks found two correctness problems and one diagnostic problem:

- **Model-unit scale:** a 128 m WMS footprint in an EPSG:25833 context remained
  128 model units in metre, millimetre and foot documents. Physical widths were
  128 m, 0.128 m and 39.0144 m respectively. The tested localized output does not
  adapt to document units. Use a metre document for this metric workflow until
  unit handling is corrected; other source components still need unit checks.
- **Selection isolation:** two Grasshopper documents with different stored
  areas shared the helper's live selection. Recomputing the first after restoring
  the second returned the second area. Independent study selection is not reliable
  across those open definitions.
- **Terrain cancellation text:** cancellation stopped the load and retry worked,
  but the component displayed `One or more errors occurred. (A task was canceled.)`.

The scale and selection cases remain unresolved. The full combined study,
map-reference cancellation/removal, other coordinate systems, Google preview
with a user-managed key, and Windows host validation remain outstanding.
The checklist below covers those broader requirements; the results above do
not complete every variant of an item.

## General Checks

- RhinoSpatial builds cleanly from `RhinoSpatial.sln`
- sandbox probes from `docs/TEST_SOURCES.md` still run for at least one bounded WFS GetFeature, WMS GetMap, WCS GetCoverage, one CityGML file, one CityJSON file, one Shapefile, and one GeoTIFF file; capabilities-only checks do not count as full source validation
- the sandbox `tiles-selection-check` passes before manual Google 3D Tiles validation
- the sandbox `ogc-request-check` and `reference-source-check` pass before service and local-reference validation
- the sandbox `source-reader-check`, `terrain-mesh-check` and `correctness-check` pass before local vector and terrain validation
- compare a courtyard polygon and a multipolygon in GeoJSON, GML, Shapefile and GeoPackage; all boundary curves should remain with their feature and attributes
- confirm an incomplete terrain file shows an error and publishes no partial mesh; check a valid compressed raster and NoData gaps alongside it
- verify supported coordinate aliases give the same placement, CRS84 stays longitude-first, and AdV source heights are unchanged
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
- confirm the saved selection takes priority when a different reference source is still connected
- without a saved selection, connect representative WFS, WMS, and WCS references and confirm the helper fits their advertised service/layer/coverage extent
- repeat with local GeoTIFF, Shapefile, GeoJSON, CityJSON, CityGML/GML/XML, Esri ASCII Grid, and XYZ/CSV paths; also test a mixed folder and a LoD ZIP
- confirm the reference is used only to preload the map and infer an SRS; it must not load source geometry or imagery
- with no reference and no saved selection, confirm the helper opens at a neutral world view rather than Frankfurt
- with no inferable project SRS, confirm the component clearly warns that EPSG:3857 is only a fallback

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

- load a supported WFS building layer through `Load LoD2 Buildings`
- repeat with local CityGML/GML/XML, CityJSON, a folder and a supported archive
- confirm buildings align with the shared context and terrain in localized and absolute placement
- confirm saved definitions open with the same component GUID and three inputs/two outputs
- check building and surface grouping, roof geometry and conversion diagnostics; do not treat source coverage gaps as conversion failures
- compare kept source bounds and output Brep bounds when investigating missing geometry
- use a small area inside a dense tile and confirm out-of-context files/buildings are filtered before expensive conversion
- confirm buffered WFS query bounds and final output remain consistent with the selected area
- run `lod2-atom-check`, then load a supported building feed and inspect tile selection, progress and cache/download counts
- repeat an ATOM load normally and from the Grasshopper recompute menu on macOS; confirm progress callbacks do not deadlock the UI
- select an area across a known tile boundary and confirm both required tiles load without a misleading partial result
- repeat the solution and restart Rhino; confirm completed building archives are reused
- compare feed-based buildings with matching imagery and vectors; repeat with another supported context CRS
- confirm missing tile metadata, unsupported archives, service failures and interrupted downloads produce clear errors

Use the [building source catalogue](../docs/TEST_SOURCES.md#lod2--citygml-buildings)
for reproducible provider cases. These checks apply to the supported building
workflow across sources; the cases below retain exact inputs for regression work.

<details>
<summary>Recorded source cases for maintainers</summary>

- Connect `https://gdi.berlin.de/data/a_lod2/atom/` directly to `LoD2 Source`, leave `Layer` empty, and select Museum Island in an EPSG:25833 context. The [integration record](../docs/TEST_SOURCES.md#berlin-atom-integration-validation-2026-11-01) saves exact bounds and expected tile names.
- Select an area crossing northing 5820000 to exercise adjacent building tiles. Compare with matching official vector and imagery sources under the same context.
- Run the bounded Sachsen-Anhalt EPSG:4258 probe: geographic axis order must not produce a false empty result, and enclosing `srsDimension="3"` metadata must produce XYZ rings.
- Load the Rotterdam EPSG:28992 or Den Haag EPSG:7415 CityJSON sample: Dutch RD New XY must use an explicit transform rather than bounding-box scaling.

</details>

## Terrain

- load terrain through `Load Terrain`
- confirm terrain aligns with the same `Spatial Context`
- confirm terrain and LoD2 share a sensible local Z reference in localized mode
- confirm absolute mode still keeps real coordinates when requested
- leave the Terrain URL empty and confirm the global fallback returns usable context terrain for a small study area
- try an intentionally large Spatial Context and confirm the fallback fails quickly with a clear status message instead of hanging Grasshopper
- connect a user-provided WCS terrain source and confirm explicit source data still wins over fallback behavior
- confirm ordinary single-part GeoTIFF responses (NRW/PDOK) and multipart GeoTIFF responses (USGS 3DEP) both produce readable terrain rasters
- use a WCS whose capabilities omit WGS84 bounds and confirm `DescribeCoverage` native bounds still preload the Spatial Context map
- connect a local `.tif` / `.tiff` DEM path to `Load Terrain` and confirm it produces an aligned terrain mesh or a clear non-overlap warning
- compare equivalent corner-origin and center-origin ASCII grids; their terrain vertices should coincide at cell centers and the reference map should fit the full raster footprint
- combine terrain in EPSG:25832 or EPSG:28992 with a different supported Spatial Context CRS and confirm imagery/vector alignment in both localized and absolute modes
- load terrain containing NoData holes and confirm no artificial flat patches or invalid OSM building heights appear; repeat at a resolution requiring downsampling and confirm the last row and column remain included
- confirm incomplete ASCII grids, irregular XYZ spacing, and terrain with no usable surface return clear status messages

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
- confirm the status reports the selected refinement frontier, candidate count, target geometric error, per-tile projection-frame counts, output bounds, and branch-level parent fallback behavior
- confirm decoded tile attribution appears on the component and in the status output
- confirm replacement parent and child tiles are not displayed together; when a refined branch fails, confirm the affected branch is replaced by its nearest usable parent
- confirm small bounds padding is acceptable when it prevents missing chunks inside the selected reference area
- confirm a small Spatial Context stops traversal near its target geometric error without reaching a traversal safety limit
- in localized mode, connect/solve Google before LoD2 and then repeat with LoD2 before Google; confirm both orders use one shared elevation baseline and remain vertically aligned

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


## Service and download workflows (development)

Use [COMPATIBILITY_WORKFLOWS.md](COMPATIBILITY_WORKFLOWS.md) and record the Rhino
version/platform and actual results. These host checks remain pending:

- [ ] Existing saved definitions open without changed GUIDs or parameter-layout warnings.
- [ ] API-root layer listing, explicit multi-collection choice and optional single-collection Layer work in Grasshopper.
- [ ] Paging status distinguishes the user cap, unknown totals and an incomplete/failed page.
- [ ] The recorded terrain-feed single- and two-tile windows load with responsive progress; cancellation and retry work.
- [ ] DGM1 mesh, building roofs and WMS agree in XY and Z under one localized context, regardless of solve order.
- [ ] NoData holes stay open and no seam appears at the terrain tile boundary.
- [ ] ATOM/API references orient a fresh map without geometry downloads, while saved selections retain priority.
- [ ] Direct GeoJSON, Shapefile ZIP and CityJSON URLs load; invalid CRS/companions return clear messages.
- [ ] Repeat the relevant checks on Windows before a public release.


## Building and imagery formats (development)

Use the [building and imagery guide](BUILDING_AND_IMAGERY.md). These native checks remain pending:

- [ ] Open existing definitions with unchanged GUIDs and parameter layouts on macOS and Windows.
- [ ] Load each bundled CityJSON/sequence/template example: one six-surface building at the expected coordinates, with no alternate-LoD overlay.
- [ ] Load the rotated PNG with both sidecars, compare its footprint to matching vectors, clip the study area, and verify UV orientation, alpha, preview and baking.
- [ ] Edit a world file while keeping the raster unchanged; recompute and confirm both placement and reference extent update.
- [ ] Verify embedded rotated/PixelIsPoint DEM alignment and cached OSM/building height sampling in localized and absolute modes.
- [ ] List and load the recorded WMTS imagery through existing WMS components, check the selected layer/matrix diagnostics and compare imagery to buildings/vectors in one context.
- [ ] Confirm WMTS Reference Source reads capabilities without tile downloads and saved selections retain priority.
- [ ] Cancel a tile batch and retry; confirm no partial image is presented and cached results remain usable.

## Remote rasters and GeoPackage (development)

The remote raster and GeoPackage suite covers 43 offline cases.
The [technical reference](../docs/technical/REMOTE_RASTERS_AND_GEOPACKAGE.md) and
[how-to](REMOTE_RASTERS_AND_GEOPACKAGE.md) record source URLs, test bounds, limits
and three reproducible fixtures. Live Sentinel/Copernicus range subsets and the
GDAL public GeoPackage were verified; these are not native Rhino screenshots.

Before release:

- [ ] Open an existing definition in Rhino 8 on macOS and Windows; verify unchanged connections/GUIDs and responsive cancellation.
- [ ] Use the fixture range server: compare imagery/terrain mask holes, correct pixel locations, shared baseline and reload behavior in local/absolute placement.
- [ ] Test public Sentinel and Copernicus windows; inspect materials, clipping and bake from the selected PNG window.
- [ ] List/select both GeoPackage layers, verify courtyard and attribute/feature grouping, sole-layer behavior and multi-layer output branches.
- [ ] Use GeoPackage/COG Reference Source on a fresh map, then confirm saved selections retain priority and no geometry is imported.
- [ ] Test installed-package assembly resolution in Rhino on both platforms. Windows platform-provider sandbox CI is useful but does not replace Rhino host validation.

## Reader integrity and placement

[Reader validation](../docs/technical/READER_VALIDATION.md) documents the original
reproductions and regression coverage. The [reliability reference](../docs/technical/SERVICE_RELIABILITY.md)
describes service loading and planned raster extensions. The native checks below
remain pending.

- [ ] Incomplete scanline/tile reads fail without repeating old elevations or publishing partial terrain.
- [ ] Equivalent GeoJSON/GML/Shapefile/GeoPackage polygons retain courtyard boundaries and feature grouping.
- [ ] Unknown CRS identifiers are rejected; supported short/URN/URL forms resolve consistently without an axis-order regression.
- [ ] A known-length/height study is checked in metre, millimetre and foot documents, including save/reopen and supported bake paths; confirm the intended units policy before changing absolute-coordinate behavior.
- [ ] Packaged assemblies are loaded under the actual supported Rhino 8 host runtimes on both platforms; assess Rhino 9 separately rather than assuming the .NET 10 sandbox proves host compatibility.

## Service reliability (development)

Use the [loopback fixture guide](RELIABILITY_WORKFLOWS.md). These host checks remain pending:

- [ ] Cancel WMS, WCS and OSM during a slow body transfer, retry a normal source and verify that no interrupted asset becomes a cache hit.
- [ ] Change the study area while loading; the older solve must not replace current geometry, preview or terrain sampling data.
- [ ] Check request-size errors, WMS size retry, WCS multipart data and explicitly reported OSM category failures.
- [ ] Start map-reference work, change its source or remove the component, and verify that the superseded request does not reopen or reset the map.
- [ ] Repeat with saved definitions and supported installed assemblies on macOS and Windows; keep unit/scale and baking checks separate.
