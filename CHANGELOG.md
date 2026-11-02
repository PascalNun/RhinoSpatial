# Changelog

RhinoSpatial improves its established source categories through real project use. Earlier releases remain documented on [GitHub Releases](https://github.com/PascalNun/RhinoSpatial/releases).

## Unreleased

- Read remote classic-TIFF COG windows through bounded, validated HTTP byte ranges. Reuse LibTiff imagery/DEM codecs, usable overviews, masks and NoData; preserve affine placement, source elevations and the shared terrain baseline. Reject unsupported/range-refusing sources without whole-file fallback. Reference Source reads headers without decoding pixels.
- Add read-only local GeoPackage feature-layer discovery, declared EPSG CRS, bounded RTree/fallback spatial queries, attributes and standard WKB geometry through existing vector components. Preserve polygon-hole curves and geometry grouping; reject malformed/extended geometry without repairs. Catalog references do not scan feature geometry.
- Correct GDAL ASCII NoData tags that expose a count before their value. Add explicit WGS84 UTM north/south EPSG transformations needed by common COGs.
- Add Microsoft.Data.Sqlite.Core/platform SQLitePCLRaw managed providers, license texts and package inspection; no new native binary. Add 43 low-priority regression cases, macOS/Windows CI, [how-to/URLs](examples/LOW_COMPATIBILITY_WORKFLOWS.md) and synthetic COG/GeoPackage fixtures. Installed/public 0.4.0 remains unchanged.

- Select one suitable CityJSON LoD per object, retaining parts at that level; support streamed CityJSONSeq and affine geometry templates through existing local/archive/download paths. Reject malformed indices or ambiguous LoDs instead of returning partial rings.
- Support embedded affine GeoTIFF georeferencing and local world-file TIFF/PNG/JPEG with declared CRS, preserving rotated footprints, UV clipping, pixel centers and terrain sampling. Sidecar edits invalidate caches; projected CRS keys take priority over geographic keys.
- Support WMTS layer discovery, reference metadata, bounded native tile selection and PNG/JPEG mosaics through existing WMS components. Honor axis order, matrix limits, dimensions and advertised REST/KVP endpoints; validate completed caches and fail required tile errors explicitly.
- Add the managed `StbImageNext` 1.0.0 decoder (no native or further package dependencies), update third-party notices, and save [medium-priority recipes and synthetic examples](examples/MEDIUM_COMPATIBILITY_WORKFLOWS.md). New native Rhino/Windows checks remain pending.

- Follow OGC API next links and supported WFS paging with explicit user caps, deduplication, repeated-page protection and completeness diagnostics. Preserve raw GML for the building loader.
- Discover feature collections from OGC API landing pages, collection lists and advertised links using the existing layer-list/vector components. Layer selection is optional for a single collection and direct files.
- Load bounded, validated direct GeoJSON and Shapefile ZIP downloads, plus remote CityJSON/CityGML building files. Check companion files, CRS metadata, archive paths and expanded sizes; reuse validated downloads.
- Load terrain ATOM feeds through `Load Terrain`, using declared placement metadata and an aligned raster mosaic. Preserve source sample centers, NoData holes and the shared elevation baseline; reject incompatible grids or conflicting heights.
- Use ATOM and OGC API metadata as Spatial Context references without intentionally downloading feature data or archives. Existing saved selections retain priority.
- Correct projected Shapefile WKT authority detection so nested datum/ellipsoid EPSG codes cannot override the actual source CRS. Unsupported declared CRS metadata fails explicitly.
- Add deterministic compatibility checks to CI and save [copyable example workflows](examples/COMPATIBILITY_WORKFLOWS.md), source URLs and live probe results. New Rhino/Grasshopper visual checks remain pending; component GUIDs and input/output layouts are retained. No public release or installed-plugin replacement is included in this batch.

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
