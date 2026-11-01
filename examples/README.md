# RhinoSpatial Examples

These Grasshopper definitions are starting points for common RhinoSpatial site-context workflows. Download the example closest to your task, replace its service URLs or local-file placeholders with your own project sources, and keep the same shared `Spatial Context`.

For a visual introduction, see the [RhinoSpatial workflow guide](../docs/SHOWCASE.md). For exact source formats and component behavior, see the [Component and Source Reference](../docs/COMPONENT_REFERENCE.md).

## Download an example

### Full reference workflow

[Download `00-rhinospatial-reference-workflow.gh`](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/00-rhinospatial-reference-workflow.gh)

One definition showing the complete shared workflow with Spatial Context, WMS, WFS, GeoTIFF, terrain, LoD2, OSM, and Google 3D Tiles reference context.

### WMS and WFS basics

[Download `01-wms-wfs-basics.gh`](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/01-wms-wfs-basics.gh)

Start here for an aligned map image plus editable vector data such as parcels, planning layers, roads, or building footprints.

### GeoTIFF and terrain

[Download `02-geotiff-terrain.gh`](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/02-geotiff-terrain.gh)

A local raster and terrain workflow. The GeoTIFF input is intentionally a placeholder: connect your own georeferenced `.tif` or `.tiff` file. No personal local path or project raster is included.

### LoD2 buildings

[Download `03-lod2-buildings.gh`](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/03-lod2-buildings.gh)

Official 3D building and roof context from a LoD2 WFS service, ATOM download service, or supported local CityGML/CityJSON source. For automatic Berlin tiles, connect `https://gdi.berlin.de/data/a_lod2/atom/` to `LoD2 Source` and select a small Berlin area with `Spatial Context`; leave `Layer` empty.

### OSM context

[Download `04-osm-context.gh`](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/04-osm-context.gh)

Lightweight OpenStreetMap buildings, roads, water, green areas, and rail for quick site studies and figure-ground or black-plan views.

### Google 3D Tiles reference

[Download `05-google-3d-tiles-reference.gh`](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/05-google-3d-tiles-reference.gh)

Optional Google Photorealistic 3D Tiles visual context. The example does not contain an API key. Add your own Google Maps Platform key only to a private working copy and do not publish that edited definition.

## How to use the examples

1. Install the current RhinoSpatial release through Package Manager.
2. Download and open the chosen `.gh` definition.
3. Replace example URLs, layer names, local files, or API-key placeholders with sources suitable for your project.
4. Open `Spatial Context` and select a reasonably small study area.
5. Read each component's `Status` output when a provider or file does not return the expected result.

Public example services can change or become temporarily unavailable. For production work, official or project-specific sources remain the preferred path.

If an older saved definition shows Grasshopper IO archive warnings after a component layout change, replace the affected RhinoSpatial component once and reconnect it. The definitions in this repository should represent the current component layout and open without those warnings.

## Compatibility workflows (Unreleased)

The [source compatibility recipes](COMPATIBILITY_WORKFLOWS.md) save tested URLs,
collection choices, bounds and observed results for API-root discovery, paging,
Berlin DGM1 ATOM terrain, direct GeoJSON/Shapefile ZIPs and CityJSON downloads.
These require the development build; the public 0.4.0 package is unchanged.

The [medium-priority examples](MEDIUM_COMPATIBILITY_WORKFLOWS.md) add declared-CRS CityJSON/sequence/template files, a rotated world-file raster, and a verified public WMTS URL with native test bounds. These also require the development build.

## Source and testing references

- [Curated example source list](sources.json)
  Repeatable service and file references used by the example and validation workflows.
- [Test Source Catalogue](../docs/TEST_SOURCES.md)
  Broader international provider catalogue, current probe results, known limitations, and future format candidates.
- [Manual Validation Checklist](VALIDATION.md)
  Maintainer-facing regression checks for the complete toolkit.

## For maintainers

The examples serve two purposes: they help users start a real workflow, and they provide a small manual regression baseline.

The set intentionally favors complete design studies over isolated technical probes:

- contextual site modeling
- figure-ground and black-plan studies
- early-stage design workflows
- alignment checks across imagery, vector, terrain, and buildings

The source manifest combines broad fallback references, international compatibility sources, and deeper German/Hessen test data. Hessen remains useful for LoD2, terrain, cadastral, building, and parcel workflows, while international services help expose provider and coordinate-system differences.

Local CityGML folders and ZIP archives can vary greatly in structure and size. Validation should record whether RhinoSpatial skipped files by metadata bounds, filtered buildings before conversion, or had to inspect a large single tile.
