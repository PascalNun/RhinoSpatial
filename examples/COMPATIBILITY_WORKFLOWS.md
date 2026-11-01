# Source compatibility examples

These additions are **Unreleased** and require a build from the source-compatibility
branch. The public 0.4.0 package has not been replaced. URLs and results below were
checked on 2026-11-01; public servers can change.

Use the existing Grasshopper examples as starting points: `01-wms-wfs-basics.gh`
for vectors, `02-geotiff-terrain.gh` for terrain, and `03-lod2-buildings.gh` for
buildings. These recipes do not add new binary Grasshopper definitions. Keep one
shared `Spatial Context` for every layer in a study. The machine-readable inputs
are also saved in [sources.json](sources.json).

## Discover collections from an API root

Connect this URL to `List WFS Layers`, then select `lakes | Large Lakes`:

```text
https://demo.pygeoapi.io/master/
```

Connect the same URL and selected entry to `Load WFS`. Set `Max Features` to `5`
for a small check. Define a context in North America; the bounded probe used
EPSG:4326 bounds `-100,30,-50,60`. The root exposed 16 collections during testing.
For a root containing several collections, choose a layer explicitly; a sole
collection can be loaded with `Layer` empty.

To exercise pagination, use this URL directly in `Load WFS`, with `Layer` empty:

```text
https://demo.pygeoapi.io/master/collections/lakes/items?limit=1
```

The bounded probe returned five unique features across five pages and reported
the deliberate `Max Features = 5` stop. `0` follows available pages subject to
the documented safety limits. A provider without a matching total receives an
explicit completeness caveat.

An API root or collection metadata URL can also be used as `Reference Source`.
`Reference Layer = lakes` selects its extent. This only orients the map; draw a
smaller study area before loading. A saved selection still takes priority.

## Berlin DGM1 terrain from ATOM

Connect this URL to `Load Terrain → Terrain URL`; leave `Coverage` empty:

```text
https://gdi.berlin.de/data/dgm1/atom/
```

The same URL can be connected to `Spatial Context → Reference Source` to infer
EPSG:25833 and the feed coverage. No terrain archives are fetched for reference
metadata. Draw a small area near Museum Island, Berlin.

Repeatable native bounds and observed results:

| EPSG:25833 bounds (minX,minY,maxX,maxY) | Selected archives | Result |
| --- | --- | --- |
| `391300,5820100,391700,5820500` | `DGM1_390_5820.zip` | 400 × 400 samples; 160,000 mesh vertices and 159,201 quad faces; heights 30.75–36.85 m |
| `391900,5820100,392100,5820300` | `DGM1_390_5820.zip`, `DGM1_392_5820.zip` | 200 × 200 joined samples; 40,000 vertices and 39,601 quad faces; heights 31.17–42.21 m |

These are sandbox/Core mesh results, not a completed visual Rhino alignment
check. The map helper selects rectangles interactively; the precise bounds above
can be reproduced with the sandbox commands below. Each source ZIP is about
16–17 MiB, even for a small window. Subsequent requests reuse validated downloads.
Berlin publishes a declared 2 km UTM grid; sample centers retain their `.5 m`
coordinates. Neighboring grids must agree in CRS, spacing and alignment. Missing
samples remain holes; conflicting overlaps fail explicitly.

To compare buildings and terrain in Rhino, connect the **same** context to
`Load LoD2 Buildings` using:

```text
https://gdi.berlin.de/data/a_lod2/atom/
```

Leave its `Layer` empty. The building feed uses a different declared 1 km grid;
the terrain feed belongs in `Load Terrain`.

## Direct GeoJSON and Shapefile ZIP downloads

Connect either URL directly to `Load WFS → WFS URL`, with `Layer` empty:

```text
https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_110m_admin_0_countries.geojson
https://naturalearth.s3.amazonaws.com/110m_cultural/ne_110m_admin_0_countries.zip
```

Both contain EPSG:4326 country boundaries. The GeoJSON download parsed 177
features; the ZIP passed validation and its Shapefile reader returned three
bounded sample features. Use these coarse world datasets for compatibility
checks. For a small site, use an appropriately detailed local dataset: the
existing filter may exclude country outlines that surround the whole site.

ZIPs must contain complete `.shp`, `.shx`, `.dbf` and resolvable `.prj` sets.
Downloads are validated and cached; failed or unsafe archives are not promoted.
Remote data files are loader inputs. Download a file first if you want to use
its local metadata as a `Reference Source`.

## Direct CityJSON buildings

Connect this maintainer test dataset to `Load LoD2 Buildings → LoD2 Source`,
with `Layer` empty:

```text
https://raw.githubusercontent.com/cityjson/cjio/master/tests/data/delft.json
```

The live download parsed 160 buildings. Its declared CRS is EPSG:7415 and its
native XY extent is `84616.468,447422.999,85140.839,447750.636`. It contains LoD1
geometry, which the existing building reader accepts. It does not validate
multi-LoD selection, CityJSON sequences or geometry templates.

For an accurately oriented study area, save a local copy and connect that path
to `Reference Source`; use a supported map SRS such as EPSG:3857, then select
the building extent in Delft. Load the original remote URL through the building
loader. EPSG:7415 uses the supported RD New horizontal transform; source Z
values are retained without vertical-datum conversion.

The older [Delfshaven CityJSON example](https://3d.bk.tudelft.nl/opendata/cityjson/3dcities/v2.0/3-20-DELFSHAVEN.city.json)
failed TLS validation from this machine during this batch. Historical local-file
parser results remain in the catalogue; it is not a verified direct-download
example for this batch. Certificate checks were retained.

## Reproduce the live probes

Run from the repository root after a Release build:

```sh
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- compatibility-probe discover 'https://demo.pygeoapi.io/master/'
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- ogcapi 'https://demo.pygeoapi.io/master/collections/lakes/items?limit=1' '-100,30,-50,60' 5
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- compatibility-probe reference 'https://gdi.berlin.de/data/dgm1/atom/'
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- compatibility-probe terrain 'https://gdi.berlin.de/data/dgm1/atom/' EPSG:25833 391300,5820100,391700,5820500
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- compatibility-probe terrain 'https://gdi.berlin.de/data/dgm1/atom/' EPSG:25833 391900,5820100,392100,5820300
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- compatibility-probe vector 'https://raw.githubusercontent.com/nvkelso/natural-earth-vector/master/geojson/ne_110m_admin_0_countries.geojson'
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- compatibility-probe vector 'https://naturalearth.s3.amazonaws.com/110m_cultural/ne_110m_admin_0_countries.zip'
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- compatibility-probe buildings 'https://raw.githubusercontent.com/cityjson/cjio/master/tests/data/delft.json'
```

The deterministic `source-compatibility-check` command uses temporary fixtures
and makes no external requests. Live probes are deliberately separate from CI.
