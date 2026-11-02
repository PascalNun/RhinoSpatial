# Building detail and imagery workflows

These examples cover CityJSON detail/sequence/templates, world-file imagery and
WMTS maps. The features are merged into `main` for the next release and need a
development build; the public package remains 0.4.0. Start with the general
[project-data guide](../docs/WORKING_WITH_PROJECT_DATA.md) to choose a workflow.
Checks below were recorded on 2026-11-02.

Keep one `Spatial Context` for a study. Existing definitions `02-geotiff-terrain.gh`
and `03-lod2-buildings.gh` can be reused by replacing source inputs. Component
names, GUIDs and parameter layouts are retained.

## Small local examples with declared coordinates

The [fixtures/medium](fixtures/medium) folder contains synthetic test data, not
survey data or real aerial imagery:

| File | Connect to | Expected result |
| --- | --- | --- |
| [building-lods.city.json](fixtures/medium/building-lods.city.json) | `Load LoD2 Buildings → LoD2 Source` | One building, six surfaces, selected LoD 2.2; the alternate LoD1 shell is omitted |
| [building-sequence.city.jsonl](fixtures/medium/building-sequence.city.jsonl) | Same input | Same six surfaces, using the sequence header's transform and CRS |
| [building-template.city.json](fixtures/medium/building-template.city.json) | Same input | One six-surface building from a unit-cube template, scaled and translated from its native anchor |
| [rotated-raster.png](fixtures/medium/rotated-raster.png) | `Load GeoTIFF → GeoTIFF` | A colored raster with its true rotated/sheared footprint and UV mapping |

Keep `rotated-raster.pgw` and `rotated-raster.prj` beside the PNG. All fixtures use
EPSG:25833 near Berlin; a shared study rectangle
`391296,5820096,391314,5820110` covers them. Use a fixture path as
`Spatial Context → Reference Source` to orient a fresh map, then select the area.
Leave the building `Layer` input empty. Saved map selections still take priority.
The synthetic building base is 32 m; its source Z values remain unchanged before
the normal shared-baseline placement.

The PNG is 4 × 4 pixels. Its world-file center for pixel (0,0) is
`391300,5820106`; its column vector is `(2,0.5)` and row vector `(1,-2)`.
The image is clipped to the study area after projection, preserving texture
coordinates. A world file alone cannot identify a CRS: removing the `.prj`
must produce an explicit error, rather than assuming the context's CRS.

Recreate and inspect these files from the repository root:

```sh
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release -- medium-probe examples examples/fixtures/medium
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- medium-probe cityjson examples/fixtures/medium/building-lods.city.json
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- medium-probe cityjson examples/fixtures/medium/building-sequence.city.jsonl
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- medium-probe cityjson examples/fixtures/medium/building-template.city.json
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- medium-probe raster examples/fixtures/medium/rotated-raster.png
```

## Public CityJSON multi-LoD parser example

```text
https://raw.githubusercontent.com/cityjson/cjio/master/tests/data/multi_lod.json
```

The maintainer fixture parsed as 10 buildings and 348 selected surfaces, all at
LoD 2.2. It has **no declared CRS**, so this is a parser example. It does not prove
alignment, and the direct-download validator intentionally rejects it until
coordinate metadata is supplied. Use the bundled declared-CRS fixtures above
for an end-to-end loader example. The public Delft fixture from the previous
guide remains a declared-CRS download example.

Selection is per CityObject: choose the highest numeric LoD at or below 2.2, or
the lowest higher LoD if none is available below that threshold. Retain all
geometry parts at the selected level. This does not add hierarchy-wide LoD
selection, material/texture import or a public LoD selector.

The [official CityJSONSeq page](https://www.cityjson.org/cityjsonseq/) links real
sequence samples. Its Delft data host failed TLS during the recorded check on 2026-11-02;
those downloads remain unverified. The bundled
sequence exercises shared transform/CRS and independent record vertex indices.

## Live WMTS imagery from PDOK

Connect this capabilities URL to `List WMS Layers`, select `2025_ortho25`, and
connect the same URL/layer to `Load WMS`:

```text
https://service.pdok.nl/hwh/luchtfotorgb/wmts/v1_0?SERVICE=WMTS&REQUEST=GetCapabilities
```

The existing WMS component names remain for saved-definition compatibility.
WMTS metadata can also be used as `Reference Source`, with the layer id as
`Reference Layer`; no tiles are requested for this reference step.

The bounded live check used EPSG:28992 bounds
`84600,447400,85200,447900` near Delft. It discovered 19 layers and six supported
matrix sets, selected native matrix set `EPSG:28992`, matrix `13`, resolution
0.42 m/pixel, and loaded 36 JPEG tiles into a 1536 × 1536 PNG mosaic. The clipped
Core mesh had 512 triangles. The saved mosaic was visually inspected for tile
continuity; this is not a completed Rhino material/alignment test.

For the map helper, use EPSG:3857, orient near Delft and draw a small area. The
provider advertises compatible matrix sets; grids are read from capabilities.
The precise native check can be reproduced with:

```sh
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release --no-build -- medium-probe wmts 'https://service.pdok.nl/hwh/luchtfotorgb/wmts/v1_0?SERVICE=WMTS&REQUEST=GetCapabilities' 2025_ortho25 EPSG:28992 84600,447400,85200,447900
```

Read the component's runtime status for matrix/resolution/tile counts. The
existing `GetMap URL` output contains the WMTS tile request URLs in this mode.
Default dimensions are used; a provider with an ambiguous dimension needs an
explicit value in the source URL query. Failed required tiles return an error
and do not publish a partial mosaic.

## Validation limits

The `medium-compatibility-check` suite uses deterministic fixtures, including
embedded affine GeoTIFFs, PixelIsPoint, projected/geographic WMTS, matrix limits,
tile order, cache corruption, alpha and cancellation. It makes no external
requests. Rhino preview, texture orientation, baking, saved definitions and
installed-package validation on macOS/Windows remain on [the manual checklist](VALIDATION.md).
