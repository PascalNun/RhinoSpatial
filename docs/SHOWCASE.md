# RhinoSpatial Workflow

RhinoSpatial is built around one selected study area and multiple aligned
context layers. This walkthrough follows the reference definition: start with
one shared `Spatial Context`, then connect WMS, WFS, GeoTIFF, terrain, LoD2,
OSM, and Google 3D Tiles to that same context.

The URLs and files shown in the screenshots are examples. They can be replaced
with your own project data as long as the source type is supported.

Related downloadable Grasshopper definitions:

- [Download .gh: Full reference workflow](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/00-rhinospatial-reference-workflow.gh)
- [Download .gh: WMS + WFS basics](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/01-wms-wfs-basics.gh)
- [Download .gh: GeoTIFF + terrain](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/02-geotiff-terrain.gh)
- [Download .gh: LoD2 buildings](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/03-lod2-buildings.gh)
- [Download .gh: OSM context](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/04-osm-context.gh)
- [Download .gh: Google 3D Tiles reference](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/05-google-3d-tiles-reference.gh)

## 0. Spatial Context

Start with `Spatial Context`. Open the map helper, draw one rectangle, and use
the resulting context output for every data layer.

You can provide an SRS override, a WFS/WMS reference URL and layer, use the
`Open Map` button, and choose whether to keep absolute coordinates.

![Spatial Context reference definition](images/workflow-00-spatial-context.jpg)

## 1. Map and Vector Context

### WMS / Map Imagery

Use `Load WMS` to bring orthophotos, maps, or raster overlays into Rhino as an
aligned image mesh.

You can provide a WMS service URL, a layer name when the service needs one, and
an image format such as `image/png`. If the URL is left empty, RhinoSpatial can
use fallback map/imagery context for quick orientation.

![WMS map imagery workflow](images/workflow-01-wms-map-imagery.jpg)

### Vector Data / WFS

Use `Load WFS` for vector features such as parcels, planning data, roads,
boundaries, or building footprints.

Supported inputs include WFS service URLs, OGC API Features URLs, local
Shapefiles (`.shp`), local GeoJSON files (`.geojson` / `.json`), and one or
more layer names.

![WFS vector data workflow](images/workflow-02-wfs-vector-data.jpg)

## 2. Raster and Terrain

### GeoTIFF

Use `Load GeoTIFF` to place a local georeferenced raster image inside the same
study space.

Supported inputs include local GeoTIFF files (`.tif` / `.tiff`) with readable
EPSG georeferencing.

![GeoTIFF workflow](images/workflow-03-geotiff.jpg)

### Terrain

Use `Load Terrain` to create an aligned elevation mesh for the selected study
area.

You can provide WCS service URLs, local GeoTIFF DEM files, local Esri ASCII Grid
files (`.asc`), local XYZ/CSV terrain grids, and a coverage id or EPSG code
when the source needs one. If the terrain URL is left empty, RhinoSpatial can
use a small-area global terrain fallback.

![Terrain workflow](images/workflow-04-terrain.jpg)

## 3. Built and Urban Context

### LoD2 Buildings

Use `Load LoD2 Buildings` to load official 3D building and roof geometry inside
the selected Spatial Context.

You can provide LoD2 WFS service URLs, local CityGML/GML/XML files, local
CityJSON files, folders with LoD2 files, ZIP archives with LoD2 files, and
layer names when the service needs them.

![LoD2 buildings workflow](images/workflow-05-lod2-buildings.jpg)

### OSM Context

Use `Load OSM` for lightweight OpenStreetMap context and quick black-plan style
site studies.

Available context groups include buildings, roads, water, green/open space,
and rail. Leave the OSM URL empty to use the built-in public source, or provide
an Overpass API endpoint as an advanced override.

![OSM context workflow](images/workflow-07-osm-context.jpg)

## 4. Visual Reference

### Google 3D Tiles Reference

Use `3D Tiles Viewer (Google)` to preview Google Photorealistic 3D Tiles around
the selected study area. This is useful for checking the surrounding urban
context alongside the other RhinoSpatial layers.

This component uses Google Photorealistic 3D Tiles through the Google Maps
Platform Map Tiles API. To use it, create or use a Google Cloud project,
enable the [Map Tiles API](https://developers.google.com/maps/documentation/tile/get-api-key),
create an API key in Google Cloud, and paste that key into the `API Key`
input. RhinoSpatial does not include, store, or share a Google API key.

Connect the same `Spatial Context` used by the other RhinoSpatial sources, then
set `Enable` to `True` when you want to load the reference preview. Map Tiles
API requests can count toward quota and billing, so keep the viewer
disabled when you are not actively using it and check the current
[Google usage and billing documentation](https://developers.google.com/maps/documentation/tile/usage-and-billing).
Restrict your API key in Google Cloud where possible.

The preview is intended as visual reference only. It is not editable project
data, and it should not be treated as a data import, bake, export, or offline
cache. When using Google Photorealistic 3D Tiles, review the current
[Google Maps Platform Terms of Service](https://cloud.google.com/maps-platform/terms)
and [Map Tiles API Policies](https://developers.google.com/maps/documentation/tile/policies),
including attribution requirements and restrictions on caching, extraction, and
offline use.

![Google 3D Tiles reference workflow](images/workflow-06-google-3d-tiles-reference.jpg)
