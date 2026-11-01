# Changelog

RhinoSpatial improves its established source categories through real project use. Earlier releases remain documented on [GitHub Releases](https://github.com/PascalNun/RhinoSpatial/releases).

## 0.4.0 — 2026-11-01

Version 0.4.0 is the recommended public release and is published as GitHub Latest. Component names, GUIDs and normal input/output connections are retained. Provider-specific compatibility continues to evolve.

### Automatic LoD2 ATOM downloads

- Added ATOM download services to the existing `Load LoD2 Buildings` source input, preserving the component GUID and input/output layout.
- Follow dataset feeds and pagination, select intersecting tiles from supported spatial metadata or declared UTM kilometre grids, and reuse the existing CityGML ZIP import and shared placement path.
- Added persistent validated archive caching, download progress, cancellation/time limits and explicit diagnostics for missing spatial information, failed transfers and unsupported archives.
- Queue progress updates asynchronously to prevent a Rhino UI/Grasshopper worker deadlock during ATOM loading on macOS.
- Diagnose explicitly advertised CSV/raster downloads as unsuitable for the building loader before evaluating their tile grid, including Berlin's DGM1 terrain feed.
- Recognize standard AdV UTM32/UTM33 horizontal CRS names, including Berlin's compound height-reference identifier, while retaining source heights.
- Added deterministic ATOM regression checks and a live Berlin probe. Museum Island and a two-tile boundary area were exercised through download, cache reuse and filtered CityGML parsing; interactive loading in Rhino on macOS was confirmed after correcting the progress deadlock; detailed cross-layer alignment remains on the manual checklist.

### Terrain and vector correctness

- Corrected Esri ASCII Grid cell-center placement and reference extents, optional NoData headers, and wrapped cell values; malformed dimensions and incomplete or excess data now return clear errors.
- Reject irregular XYZ/CSV terrain spacing and non-finite positions instead of distorting their placement.
- Transform terrain from every supported source CRS into the shared context, preserve downsampled raster edges, and leave NoData areas open instead of creating artificial flat surfaces. Unsupported WCS coordinate systems and non-overlapping coverage fail explicitly.
- Prevent missing terrain elevations from being reused as invalid building-height samples.
- Accept GeoJSON null attributes and empty geometries without fabricating origin points; correctly read versioned EPSG URN and URL identifiers.
- Added deterministic source-reader and terrain-mesh regression checks. Rhino/Grasshopper visual validation is still required for the changed terrain output.

### Documentation and package presentation

- Reorganized the README around the architect and designer workflow, with a shorter introduction, clearer installation path, and progressive links into technical detail.
- Added a complete [Component and Source Reference](docs/COMPONENT_REFERENCE.md) so supported formats, inputs, placement behavior, fallbacks, and diagnostics remain available without overwhelming the first page.
- Refreshed the project scope, roadmap, workflow guide, examples, validation notes, and source catalogue to match the established toolkit scope and current terminology.
- Added a self-contained release-package README and corrected package instructions, links, author name, and user-facing metadata.
- Updated version and installation guidance for 0.4.0; the managed Yak package now targets Rhino 8 on macOS and Windows.
- Added contributor/support guidance, issue forms and a pull request template, and included all six regression suites in GitHub validation.

## 0.3.3-alpha — 2026-07-18

### Highlights

- Stabilized Google Photorealistic 3D Tiles traversal around one coherent refinement frontier, with branch-level coarser-parent fallback instead of mixing unrelated detail levels.
- Added geographic bounds checks and richer projection/traversal diagnostics for missing, oversized, or out-of-context 3D Tiles content.
- Made the shared elevation baseline deterministic so Google 3D Tiles, LoD2 buildings, and terrain remain aligned regardless of Grasshopper connection or solution order.
- Broadened Spatial Context reference metadata to cover WFS, WMS, WCS, GeoTIFF, Shapefile, GeoJSON, CityJSON, CityGML/GML/XML, Esri ASCII Grid, XYZ/CSV terrain grids, folders, and LoD ZIP archives.
- Replaced the implicit Frankfurt startup assumption with a saved-selection/reference-extent/neutral-world startup sequence and an explicit fallback-SRS warning.
- Improved WCS coverage discovery and multipart GeoTIFF extraction, WMS/WFS axis and HTTPS handling, LoD2 CRS inheritance, and EPSG:28992/7415 transforms.
- Expanded tested-source documentation, validation checks, tutorial screenshots, and downloadable Grasshopper examples.

### Compatibility

- Rhino 8 and Grasshopper.
- Google 3D Tiles still require a user-managed Google Maps Platform API key and are intended only as visual reference context.
- This remains an alpha release; provider-specific services and unusual CRS/geometry combinations can still expose edge cases.
