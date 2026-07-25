# Changelog

RhinoSpatial follows an alpha release line while its existing source categories are stabilized through real project use. Earlier releases remain documented on [GitHub Releases](https://github.com/PascalNun/RhinoSpatial/releases).

## Unreleased

### Documentation and package presentation

- Reorganized the README around the architect and designer workflow, with a shorter introduction, clearer installation path, and progressive links into technical detail.
- Added a complete [Component and Source Reference](docs/COMPONENT_REFERENCE.md) so supported formats, inputs, placement behavior, fallbacks, and diagnostics remain available without overwhelming the first page.
- Refreshed the project scope, roadmap, workflow guide, examples, validation notes, and source catalogue to match the current `0.3.3-alpha` behavior and terminology.
- Added a self-contained release-package README and corrected package instructions, links, author name, and user-facing metadata.

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
