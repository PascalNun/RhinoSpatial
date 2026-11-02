# Changelog

RhinoSpatial improves its established source categories through real project use. Earlier releases remain documented on [GitHub Releases](https://github.com/PascalNun/RhinoSpatial/releases).

## Unreleased

The following work is merged into `main` for the next release. The public package
remains 0.4.0. See [Working with project data](docs/WORKING_WITH_PROJECT_DATA.md)
for feature availability and the general workflow.

### Sources and workflows

- **Project vectors:** discover OGC API collections, follow supported feature pages, load direct GeoJSON/Shapefile downloads and read local GeoPackage layers. Keep feature grouping and attributes, and report incomplete results and request limits clearly.
- **Maps and imagery:** use WMTS through the existing map components and place world-file TIFF/PNG/JPEG images, including rotated footprints and clipped texture coordinates.
- **Terrain:** load supported ATOM tile feeds as aligned mosaics, retaining missing elevations and rejecting incompatible grids or conflicting overlap heights.
- **Remote rasters:** read supported COG image and elevation windows with bounded byte ranges, suitable overviews and masks. Unsupported files or servers return clear errors without a whole-file fallback.
- **3D buildings:** load direct building downloads and additional CityJSON variants, including detail selection, sequences and geometry templates.
- **Map orientation:** use additional API, feed, WMTS, GeoPackage and COG reference metadata without importing geometry or imagery. Saved selections retain priority.

### Placement and correctness

- Reject incomplete terrain reads before returning a raster, and validate dimensions and allocation limits before decoding.
- Preserve courtyard boundaries across vector readers, without duplicate rings in nested GML surface patches.
- Recognize supported coordinate identifiers consistently, including EPSG names, OGC URNs/URLs, CRS84 and AdV aliases. Improve projected Shapefile CRS detection and WGS84 UTM transformations; unsupported transformations fail explicitly.
- Keep affine pixel placement and terrain sampling consistent, refresh placement after sidecar edits and read GDAL missing-value metadata correctly.
- Carry cancellation and deadlines through WMS, WCS and OSM loading so interrupted work does not fall back or replace newer output. Reject incomplete Overpass results and avoid caching reduced category results as complete studies.
- Validate downloaded images and coverages before cache reuse, preserve existing texture assets and avoid reusing failed metadata requests. Stream WCS multipart data with less memory and reuse coordinate transformations during conversion.

### Documentation, dependencies and validation

- Add a [documentation index](docs/README.md), organize technical references by subject and refresh workflow guides around the complete toolkit. Keep provider-specific examples and detailed format information in supporting material.
- Save worked source examples, synthetic fixtures, reproduction commands and observed results in the [example guides](examples/README.md#additional-workflows-on-main) and [test catalogue](docs/TEST_SOURCES.md).
- Document source formats, reader behavior and resource limits in the [technical documentation](docs/README.md#project-direction-and-maintenance). Windowed local terrain reads and remote BigTIFF remain future work.
- Add managed StbImageNext 1.0.0, Microsoft.Data.Sqlite.Core 8.0.32 and SQLitePCLRaw platform providers. Update dependency notices, license texts and package inspection; no new native SQLite binary is bundled.
- Add offline regression coverage for source discovery, file formats, reader correctness and service reliability, with CI on macOS and Windows. Native preview, materials, baking and installed-package checks remain on the release checklist. Component names, GUIDs and input/output layouts are retained.

## 0.4.0 — 2026-11-01

Version 0.4.0 is the recommended public release and is published as GitHub Latest. Component names, GUIDs and normal input/output connections are retained. Provider-specific compatibility continues to evolve.

### Automatic LoD2 ATOM downloads

- Added ATOM download services to the existing `Load LoD2 Buildings` source input, preserving the component GUID and input/output layout.
- Follow dataset feeds and pagination, select intersecting tiles from supported spatial metadata or declared UTM kilometre grids, and reuse the existing CityGML ZIP import and shared placement path.
- Added persistent validated archive caching, download progress, cancellation/time limits and explicit diagnostics for missing spatial information, failed transfers and unsupported archives.
- Queue progress updates asynchronously to prevent a Rhino UI/Grasshopper worker deadlock during ATOM loading on macOS.
- Diagnose explicitly advertised terrain downloads as unsuitable for the building loader before evaluating their tile grid.
- Recognize standard AdV UTM32/UTM33 horizontal and compound height-reference identifiers while retaining source heights.
- Added deterministic ATOM regressions and live single- and adjacent-tile checks, including download reuse and filtered CityGML parsing. Interactive loading on macOS was confirmed after the progress fix; detailed cross-layer alignment remains on the manual checklist. Exact inputs and results are preserved in the [recorded integration case](docs/TEST_SOURCES.md#berlin-atom-integration-validation-2026-11-01).

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
- Replaced an implicit project location with a saved-selection/reference-extent/neutral-world startup sequence and an explicit fallback-SRS warning.
- Improved WCS coverage discovery and multipart GeoTIFF extraction, WMS/WFS axis and HTTPS handling, LoD2 CRS inheritance, and EPSG:28992/7415 transforms.
- Expanded tested-source documentation, validation checks, tutorial screenshots, and downloadable Grasshopper examples.

### Compatibility

- Rhino 8 and Grasshopper.
- Google 3D Tiles still require a user-managed Google Maps Platform API key and are intended only as visual reference context.
- This remains an alpha release; provider-specific services and unusual CRS/geometry combinations can still expose edge cases.
