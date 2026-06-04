# Food4Rhino Listing Draft

## Title

RhinoSpatial

## Short Description

A simple geospatial toolkit for working with site context directly in Rhino and Grasshopper.

## Long Description

RhinoSpatial helps you bring spatial data directly into Rhino and Grasshopper, so you can work with real site context inside your design environment without first going through a separate GIS workflow.

It is built around simple workflows, sensible defaults, and aligned outputs for contextual modeling, concept work, and early-stage design.

RhinoSpatial currently supports workflows for:

- `WFS`
- `WMS`
- `LoD2 buildings`
- `terrain`
- `GeoTIFF-based raster placement`
- local Shapefile vector loading
- local CityGML / CityJSON building loading
- `lightweight OSM context`
- Google 3D Tiles reference viewing with a user-managed API key

The goal is to keep geospatial workflows practical, lightweight, and directly usable inside Rhino and Grasshopper. User-provided project data remains the main path; broad fallbacks are included only to make first tests and contextual work easier.

RhinoSpatial grew out of a practical need: bringing official geodata and site context directly into Rhino and Grasshopper in a way that feels usable for design work. It is intended to keep that process simple, lightweight, and useful, with aligned outputs and minimal setup.

Typical workflow:

1. define the area once with `Spatial Context`
2. inspect layers when needed
3. load one or more aligned sources
4. work directly with the combined site context in Rhino and Grasshopper

Built-in fallbacks currently include global imagery for WMS-style context, a quick global land-elevation fallback for small terrain previews, and public OSM access for lightweight urban context. Fallbacks are convenience/context tools; official or project-specific sources remain the preferred path for production work.

`Load WFS` accepts WFS URLs, OGC API Features URLs, local `.shp` files, and local `.geojson` / GeoJSON `.json` files for vector context.

`Load LoD2 Buildings` accepts one source input for LoD2 WFS URLs, local CityGML/GML/XML/CityJSON files, folders, and ZIP archives. Local file loading is filtered by the selected Spatial Context where metadata and building bounds make that possible, but very large local tiles may still take longer than service-based requests.

The Google 3D Tiles component is a viewer/reference workflow. It requires the user's own Google Maps Platform API key and billing setup, and should not be treated as an editable project-data source, offline cache, bake, or export workflow.

## Early Stage Note

RhinoSpatial is currently still in an alpha stage.

The `0.3.2-alpha` release is a stabilization and source-compatibility release in the first complete alpha line. The planned core source set is present together: WFS, WMS, LoD2 Buildings, Terrain, GeoTIFF, OSM, and the Google 3D Tiles Viewer. This version improves local vector input support, LoD2 source handling, test-source documentation, package cleanup, and dependency notices. It has only been tested with a relatively small number of real workflows so far. Behavior may still vary depending on the provider, geometry type, coordinate system, service version, response format, API access, or local file metadata.

Feedback, edge cases, and additional test links are very welcome.

## Download

Food4Rhino listing:

- https://www.food4rhino.com/en/app/rhinospatial?lang=en

Rhino Package Manager:

- Open Rhino 8, run `PackageManager`, search for `RhinoSpatial`, and install the package.

Current release package:

- `RhinoSpatial-0.3.2-alpha.zip`

## Repository

https://github.com/PascalNun/RhinoSpatial

## License

MIT
