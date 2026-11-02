# Low-priority compatibility: how to use and verify

These workflows are **Unreleased**, on `codex/low-source-compatibility`, building
on the two earlier compatibility batches. The installed/public 0.4.0 is unchanged.
The implementation plan was recorded before coding in
[LOW_COMPATIBILITY_PLAN.md](../docs/LOW_COMPATIBILITY_PLAN.md).

## Start with small, repeatable examples

Keep one `Spatial Context` for all components. The [fixtures/low](fixtures/low)
files are synthetic EPSG:25833 examples near Berlin, not survey data or imagery.
Select `392000,5819968,392032,5820000` for the 32 × 32 m test area.
A fixture path can orient a fresh Reference Source; saved selections still win.

1. Start the fixture server from the repository root:

   ```sh
   python3 examples/serve_range.py
   ```

2. Connect `http://127.0.0.1:8765/imagery-mask.cog.tif` to the existing
   `Load GeoTIFF → GeoTIFF` input and connect the context. Expect a 32 × 32 pixel
   colored window with a transparent mask hole. The image keeps its real affine
   footprint and is clipped/localized with the shared context.
3. Connect `http://127.0.0.1:8765/elevation-mask.cog.tif` to `Load Terrain`.
   Expect a sloping elevation grid with a missing corner sample and an internal
   mask hole. Source samples, cell centers and the shared elevation baseline are
   retained. The imagery and DEM cover the same grid.
4. Connect the local [local-layers.gpkg](fixtures/low/local-layers.gpkg) path to
   `List WFS Layers → WFS Service URL`. Choose `buildings` or `survey "points"`
   using List Item. Connect that selection and the same file path to `Load WFS`.
   In the test area, `buildings` returns one feature and two closed boundary
   curves: the exterior and courtyard. Survey points returns one point. The
   geometry tree and per-feature attributes follow the normal vector contract.
   All vector output is XY; Z/M are not a terrain/building-height import.
5. For a sole GeoPackage feature layer, Layer can be empty. Multiple layers need
   explicit selections. A GeoPackage Reference Source uses catalog CRS/extents
   for all feature layers, without reading feature geometry. Missing metadata
   fails explicitly rather than scanning to invent an extent.

`python -m http.server` does not consistently implement byte ranges; use the
included fixture server. Stop it with Ctrl-C. Existing definitions
`gh/01-wms-wfs-basics.gh` and `gh/02-geotiff-terrain.gh` can be reused by replacing
source inputs; component names, GUIDs and input/output order are unchanged.

Recreate fixtures and run deterministic checks:

```sh
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release -- low-probe examples examples/fixtures/low
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release -- low-compatibility-check
```

## Public URLs verified on 2026-11-02

Use EPSG:25833 bounds `391000,5819900,391100,5820000` for both remote rasters.
These are small Berlin test windows; image date/cloud conditions and DSM height
semantics remain properties of the source.

**Sentinel imagery:**

```text
https://sentinel-cogs.s3.us-west-2.amazonaws.com/sentinel-s2-l2a-cogs/32/U/QD/2024/6/S2A_32UQD_20240609_0_L2A/TCI.tif
```

Connect to Load GeoTIFF. The live probe read an 11 × 12 pixel window from the
10980 × 10980 EPSG:32632 image using 41 range requests and 2,686,976 transferred
bytes, producing 556 clipped mesh triangles. Explicit WGS84 UTM EPSG:32601–32660
and 32701–32760 transformations are supported; the source is not relabeled as
ETRS89. See the [provider catalogue](https://registry.opendata.aws/sentinel-2-l2a-cogs/)
and its data terms.

**Copernicus surface elevation:**

```text
https://copernicus-dem-30m.s3.amazonaws.com/Copernicus_DSM_COG_10_N52_00_E013_00_DEM/Copernicus_DSM_COG_10_N52_00_E013_00_DEM.tif
```

Connect to Load Terrain. The live probe read a 4 × 4 window, 16 finite samples,
from the 2400 × 3600 EPSG:4326 raster with 47 requests / 3,080,192 bytes. This
[dataset is a DSM](https://registry.opendata.aws/copernicus-dem/), including
vegetation/buildings; it is not a bare-earth DTM. Published heights are retained
without a vertical-datum conversion. Observe the provider's license.

**Small public GeoPackage parser example:**

```text
https://raw.githubusercontent.com/OSGeo/gdal/master/autotest/ogr/data/gpkg/poly_golden.gpkg
```

Download this 106,496-byte GDAL test file to a local `.gpkg`; connect the local
path, not the download URL. Select `poly`, EPSG:27700 bounds
`478315,4762880,481646,4765611`. The live probe returned 10 features and 10 rings
through its RTree. This is GDAL regression data with declared test coordinates,
not a real project-site recommendation. Native Rhino placement remains manual.

Copyable sandbox probes:

```sh
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release -- low-probe cog '<imagery-url>' EPSG:25833 391000,5819900,391100,5820000
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release -- low-probe dem '<dem-url>' EPSG:25833 391000,5819900,391100,5820000
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release -- low-probe gpkg /path/to/poly_golden.gpkg poly EPSG:27700 478315,4762880,481646,4765611
```

## Deliberate limits

COG reads require HTTP 206 with correct Content-Range and a stable strong ETag or
Last-Modified for reads spanning blocks. There is no whole-file fallback. Classic
tiled TIFF imagery/one-band DEMs, exact georeferencing, usable overviews, standard
internal 1/8-bit masks and NoData are supported through LibTiff codecs. BigTIFF,
striped rasters, unsupported codecs/orientation/mask layouts fail explicitly.
Ambiguous unreferenced rounded overview grids are skipped, keeping usable levels.
An image with NoData needs contiguous 8/16-bit gray/palette/RGB samples.

Each operation is limited to two minutes, 64 MiB of transferred ranges, 512 range
requests, four million output pixels, 256 tiles per selected directory, 64 image
directories and bounded decoder allocations. Header-only references have a 4 MiB /
64-request limit. A 64 KiB range block can incidentally include nearby pixel bytes,
but references never decode pixels. A source's internal tile size controls how
many bytes are needed even for a small window.

Only selected PNG windows persist under `RhinoSpatial/CogWindows` in the user's
local application-data folder. A solve revalidates the remote representation;
range blocks are reused in memory for that operation. Delete the window folder
while idle to reclaim space. No whole remote TIFF is stored there.

GeoPackage support is local, read-only, standard feature tables and standard WKB
points/lines/polygons/multi-geometries/collections. Holes remain separate closed
curves with the feature's grouping. Optional curved/extended geometry, raster
tables, editing, SQL input and automatic remote GeoPackage downloads are outside
this batch. Declared EPSG organization codes determine the CRS, not a guessed
interpretation of a local SRS id. Catalog references require transformable extents.

Queries use an advertised RTree when the platform supports it, otherwise a bounded
scan. Budgets include 128 catalog layers, 256 columns, 500,000 scanned rows per
layer, 16 MiB per geometry/SQLite value, 128 MiB geometry bytes per layer, four
million coordinates, 100,000 output features and eight million attribute
characters per component selection. Text values are capped at 16,384 characters;
BLOB attributes are labeled by byte count. `Max Features` remains a reported
per-layer cap. Limits/malformed geometry fail instead of publishing partial data.

Microsoft.Data.Sqlite uses the host's SQLite: macOS/Linux `sqlite3`, Windows 10+
`winsqlite3`. Existing initialized providers are retained. No new native library
is bundled. CI checks both platform paths; Rhino visual/Windows host validation
is still a release gate, listed in [VALIDATION.md](VALIDATION.md).
