# Remote rasters and GeoPackage

Development builds support remote COG imagery/elevation windows and local
GeoPackage feature layers. The public package remains 0.4.0. The
[workflow guide](../../examples/REMOTE_RASTERS_AND_GEOPACKAGE.md) contains source URLs,
limits, sample bounds and small fixtures.

## Remote raster access

An HTTP(S) TIFF URL connects to `Load GeoTIFF` for imagery or `Load Terrain` for
single-band elevation. A bounded seekable range stream validates Content-Range,
representation identifiers and byte/request/time budgets. Blocks are reused
within an operation. Range refusal returns an error without a whole-file fallback.

The reader uses LibTiff through that stream, selects suitable tiled directories,
overviews and masks, and decodes intersecting tiles. Output preserves affine
placement and NoData. Status reports resolution and transferred bytes. A remote
TIFF reference reads geospatial headers without decoding pixels; saved map
selections take precedence.

Ambiguous unreferenced overview grids are skipped. Remote BigTIFF is unsupported
by the current preflight; the pinned library can decode basic local BigTIFF.
The distinction and test evidence are documented in
[reader validation](READER_VALIDATION.md).

## GeoPackage features

Connect a local `.gpkg` path to the existing vector components. Layer discovery
lists feature tables; a sole layer can load without an explicit Layer value.
Reference metadata uses catalogue CRS/extents without reading feature geometry.

Connections are read-only and private. Queries use advertised RTree indexes or
a bounded fallback scan. The reader validates GeoPackage headers, declared CRS
and standard WKB, retains attributes and polygon-hole curves, and preserves the
existing feature grouping. Raster tables, editing, SQL inputs and optional curved
or extended geometry are unsupported.

## SQLite dependencies

The managed dependencies are `Microsoft.Data.Sqlite.Core` 8.0.32,
`SQLitePCLRaw.core` 2.1.13 and sqlite3/winsqlite3 providers 2.1.11. macOS/Linux
uses platform sqlite3; Windows uses winsqlite3. The package contains four managed
SQLite assemblies and uses the host's native library. An existing initialized
provider is retained. Licenses are in [Third-Party Notices](../../THIRD-PARTY-NOTICES.md).

## Placement and missing values

COG windows and GeoPackage features share the existing context, coordinate and
localization rules. Source coordinates and vertical datums are not guessed.
Explicit WGS84 UTM north/south transforms support common satellite rasters.

For GDAL ASCII NoData tags exposed by LibTiff as count plus value, the reader
parses the value rather than the count. Local DEMs use the same correction.

## Validation

`low-compatibility-check` contains 43 offline cases: range validation, mutation,
limits, cancellation, windows, overviews, masks, image/elevation placement and
GeoPackage layers, spatial indexes, CRS, holes, attributes, malformed geometry
and source immutability. Component identities and parameter order are retained.

Recorded live checks loaded Sentinel/Copernicus range windows and a GDAL
GeoPackage. Exact results, three synthetic fixtures and a loopback range server
are in the [example guide](../../examples/REMOTE_RASTERS_AND_GEOPACKAGE.md).
Installed Rhino assembly resolution, materials and baking require the
[native checklist](../../examples/VALIDATION.md#remote-rasters-and-geopackage-development).
