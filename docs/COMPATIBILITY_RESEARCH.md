# Compatibility research — 2026-11-01

Reviewed RhinoSpatial 0.4.0 at `2bd239ee409fdb06f2aa49aa81c2cffbcc1d2be5` against official provider and format documentation. This is a code/documentation audit, not a live reproduction of every provider failure. Priorities are recommendations; no runtime behavior was changed.

Implementation of priorities 1–5 is tracked in the [implementation plan](COMPATIBILITY_IMPLEMENTATION_PLAN.md) and [example guide](../examples/COMPATIBILITY_WORKFLOWS.md). Priorities 6–8 are implemented in the [medium-priority follow-up](MEDIUM_COMPATIBILITY_PLAN.md), with [saved examples](../examples/MEDIUM_COMPATIBILITY_WORKFLOWS.md). Priorities 9–10 are implemented in the [low-priority follow-up](LOW_COMPATIBILITY_PLAN.md), with [COG/GeoPackage how-to examples](../examples/LOW_COMPATIBILITY_WORKFLOWS.md). The evidence below describes the audited 0.4.0 baseline, before those changes.

## Highest-value improvements

| Priority | Gap | Current evidence | Proposed behavior and acceptance check |
| --- | --- | --- | --- |
| 1 | Bounded feature pagination and completeness diagnostics | `RhinoSpatial.Core/OgcApiFeaturesClient.cs:25` performs one request, then parses features without following collection links. `WfsClient.cs:157` sends feature limits but has no paging loop. | Follow OGC API next links and supported WFS paging until the user cap or end of results. Detect repeated pages, deduplicate IDs, preserve bounds, and report truncation. Test a server whose page limit is smaller than the requested feature count. Existing MaxFeatures remains a deliberate safety limit. |
| 2 | Terrain ATOM downloads | `docs/COMPONENT_REFERENCE.md:275` explicitly excludes terrain ATOM. `LoadTerrainComponent.cs` routes WCS and local files. Berlin officially publishes DGM1 through ATOM. | Reuse bounded feed discovery/download infrastructure with terrain-specific archive validation, tile placement and mosaicking. Verify adjacent DGM tiles, CRS, seams, overlapping edges and NoData. Do not reuse the building filename convention without validating terrain metadata. |
| 3 | OGC API landing-page and collection discovery | `OgcApiFeaturesClient.cs:44` recognizes paths containing /collections/ or /items. `VectorSourceResolver.cs:61` classifies other HTTP URLs as WFS. | Accept the API landing page, follow its data/collection links and expose collection titles/extents. Test a root URL with two collections and relative links; avoid guessing URL paths. |
| 4 | ATOM metadata as Spatial Context Reference Source | `docs/COMPONENT_REFERENCE.md:281` explicitly says this is unsupported. | Use feed extent/CRS metadata to orient the map without downloading geometry. Test missing and ambiguous metadata, geographic axis order and preservation of saved-selection priority. |
| 5 | Direct data-download URLs and packaged vector files | `VectorSourceResolver.cs:37–63` supports local SHP/GeoJSON; a remote .geojson URL falls through to WFS. `Lod2AtomClient.cs:294` explicitly excludes remote CityJSON. | Detect response type/content before service routing; safely download bounded GeoJSON and appropriate archives, including Shapefile companion files. Extend building downloads to validated CityJSON separately. Test redirects, misleading extensions, archive limits and missing CRS. |
| 6 | CityJSON LoD selection and sequence support | `CityJsonReader.cs:24` parses a single JSON document. Lines 44–51 traverse all geometry entries without selecting their lod. There is no CityJSONFeature sequence or GeometryInstance template handling. | First select one suitable LoD per object, rather than combining all supplied LoDs. Then add .city.jsonl streaming and template resolution where building datasets require them. Multi-LoD duplication is a code-derived risk, not a reproduced visual bug. Test a two-LoD building, a sequence header with shared transform, and a template instance. |

## Useful follow-on work

| Priority | Gap | Current evidence | Proposed behavior and acceptance check |
| --- | --- | --- | --- |
| 7 | Broader raster georeferencing | `GeoTiffReader.cs:32` requires an embedded EPSG code, and line 84 explicitly rejects rotated/sheared transforms. No world-file reader was found. | Add supported affine transforms and sidecar georeferencing (.tfw/.jgw/.pgw with a resolvable CRS). Test rotated imagery and pixel-center conventions. A world file alone does not identify the CRS; fail clearly instead of assuming one. |
| 8 | WMTS imagery | Existing imagery clients implement WMS; no WMTS or tile-matrix-set client was found in the reviewed C# code. | Support an additional delivery mechanism within imagery: read capabilities, choose a suitable tile matrix/resolution, request only intersecting tiles and align the mosaic. Test a projected tile matrix, not just Web Mercator. This is not a request to add caching to Google 3D Tiles. |
| 9 | Remote Cloud Optimized GeoTIFF access | GeoTIFF readers open local paths with Tiff.Open; no HTTP range reader was found. | Read appropriate overviews and spatial windows from a remote COG using bounded byte-range requests. A compatible local COG may already work as an ordinary TIFF; the missing capability is efficient remote subset access. Test transferred byte budgets and a server without range support. |
| 10 | GeoPackage vector input | VectorSourceKind and resolver contain WFS, OGC API Features, local Shapefile and local GeoJSON, but no .gpkg route or reader. | Add read-only feature-layer discovery and bounded spatial reads from a GeoPackage. Start with vector features, not every optional GeoPackage extension. Test multiple layers, CRS metadata and polygon holes. This is a larger optional format extension; defer until a real project supplies a useful test dataset. |

## Official references

- [OGC API Features Core: landing pages, collections and paging](https://docs.ogc.org/is/17-069r3/17-069r3.html)
- [Berlin DGM1 official ATOM service metadata](https://gdi.berlin.de/geonetwork/srv/api/records/fa02f9e1-a0df-3be1-b0aa-bc624c0c7ff5)
- [Berlin DGM1 provider documentation](https://gdi.berlin.de/data/dgm1/docs/dgm1.pdf)
- [CityJSON specification: LoD and geometry templates](https://www.cityjson.org/specs/)
- [CityJSON sequences and CityJSONFeature](https://www.cityjson.org/cityjsonseq/)
- [OGC GeoTIFF georeferencing standard](https://docs.ogc.org/is/19-008r4/19-008r4.html)
- [Esri world-file documentation](https://pro.arcgis.com/en/pro-app/3.6/help/data/imagery/world-files-for-raster-datasets.htm)
- [OGC WMTS standard](https://www.ogc.org/standards/wmts/)
- [OGC Cloud Optimized GeoTIFF standard](https://docs.ogc.org/is/21-026/21-026.html)
- [OGC GeoPackage specification](https://www.geopackage.org/spec/)

## Suggested order

Start with pagination/completeness and terrain ATOM. Add landing-page discovery and ATOM reference metadata next. Keep these inside the current components where practical, preserve their GUIDs and ordinary connections, and verify with bounded provider requests plus deterministic fixtures. Choose subsequent format work from actual user datasets, rather than treating this list as a mandatory expansion roadmap.
