# Low-priority compatibility: how-to and implementation plan

Started 2026-11-02 from `f20afe4` on `codex/low-source-compatibility`.
This implementation record covers research priorities 9–10. All three
compatibility batches are now merged into `main`; the public package remains
0.4.0. No installation or release was performed. For the general user workflow,
see [Working with project data](WORKING_WITH_PROJECT_DATA.md).

## Intended user workflow

1. Select one study area with the existing `Spatial Context`.
2. For remote COG imagery, connect an HTTP(S) TIFF URL to `Load GeoTIFF`. For a
   single-band elevation COG, use `Load Terrain`. The client reads metadata and
   relevant tiles/overviews with byte ranges and reports transferred bytes and
   selected resolution. A server that ignores ranges returns a limitation; it
   does not trigger a hidden whole-file download.
3. A remote TIFF used as `Reference Source` reads geospatial headers only, without
   decoding a raster window. Saved selections retain priority.
4. For GeoPackage vectors, connect a local `.gpkg` path to `List WFS Layers`,
   choose feature layers and use the same path/layers with `Load WFS`. A sole
   feature layer may be loaded without Layer. Use a local path
   as `Reference Source` for catalogue CRS/extents without feature loading.
5. Read source status for limits, CRS, empty coverage, unsupported extensions and
   failures. Keep all aligned components on the same context.

## Implementation order

- **Remote raster transport:** bounded seekable HTTP range source, block reuse,
  strict Content-Range checks, representation validators, byte/request/time
  budgets and cancellation. No full-download fallback or Google changes.
- **COG subset readers:** use existing LibTiff decoding through custom streams;
  discover usable tiled image/overview/mask directories, preserve exact affine
  placement and NoData, read only intersecting tiles and write a bounded image
  window or elevation raster. Keep unsupported arrangements explicit.
- **GeoPackage reader:** use Microsoft.Data.Sqlite.Core with the platform SQLite
  provider (macOS/Linux sqlite3, Windows winsqlite3), read-only private connections
  and bounded queries. Reuse NetTopologySuite WKB decoding, validate GeoPackage
  headers/CRS/standard geometry, preserve polygon-hole rings, use advertised
  RTree indexes or a bounded fallback scan, and retain existing vector grouping.
  Do not implement raster tables, editing or optional geometry extensions.
- **Integration:** add source routing behind existing components and reference
  readers without changing GUIDs or parameter layouts. Share context/CRS,
  localization and terrain baseline behavior.

## Validation and examples

- Deterministic HTTP fixtures for range/validator/cancellation failures, byte
  budgets, overview/window choice, masks, imagery and terrain placement.
- Temporary GeoPackages for multiple layers, quoted identifiers, RTree/fallback
  filtering, CRS, holes, attributes, malformed/unsupported geometries, limits
  and source immutability.
- Save a small declared-CRS GeoPackage and COG fixtures plus copyable public URLs,
  bounds, commands and actual live results. Mark unavailable sources honestly.
- Run Release build, all eight existing suites and the new low-priority suite;
  verify manifest JSON, component interfaces, diff, dependencies and package
  contents. Update notices, reference/catalogue/roadmap/changelog and manual
  Rhino/Windows checks.
- Record validation and integrate the tested result; this batch was initially stacked with the previous work and later merged into `main`.

## Progress

- [x] Startup, scope and how-to plan recorded.
- [x] Bounded range transport and remote raster readers.
- [x] Read-only GeoPackage reader and source integration.
- [x] Saved examples and documentation.
- [x] Local validation and integration into `main`. All nine suites passed in macOS/Windows CI; native Rhino validation remains manual.

## Implementation results

Release build: zero warnings/errors. All nine local suites passed, including 43
new checks. Live Sentinel/Copernicus range windows and the GDAL public GeoPackage
passed; exact URLs/bounds/results are in the how-to. Three fixtures and a loopback
range server are saved. Component GUIDs/names/parameter order match the base.

The SQLite decision uses Microsoft.Data.Sqlite.Core 8.0.32 with SQLitePCLRaw.core
2.1.13 and sqlite3/winsqlite3 providers 2.1.11. The preview package contains all
four managed assemblies, notices/license texts and no new native binaries or
local AI context. Existing release staging is untouched. Platform CI covers
macOS/Windows; installed Rhino assembly resolution/material/bake checks remain.

An incidental bug found by the NoData fixtures is fixed for local DEMs too:
LibTiff's unknown ASCII tags return count + value, so the value must be parsed
instead of the count. Explicit WGS84 UTM north/south transformations were added
after the live Sentinel test exposed that missing CRS support. No source CRS or
vertical datum is guessed. Unreferenced rounded overview grids are skipped when
their mapping is ambiguous; BigTIFF remains an explicit decoder limitation.
