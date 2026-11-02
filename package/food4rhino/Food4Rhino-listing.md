# Food4Rhino listing copy

This copy describes the public **0.4.0** package. Development additions on `main`
are not included in this listing until a matching release is published. Editing
this repository file does not change the live Food4Rhino page.

## Title

RhinoSpatial

## Short Description

Bring maps, terrain, buildings, imagery, and geodata into one aligned Rhino and Grasshopper site context.

## Long Description

RhinoSpatial brings real-world site context directly into Rhino and Grasshopper. Choose your study area once with `Spatial Context`, then add the layers you need. Maps, terrain, imagery, buildings, vector data, and lightweight urban context are placed in one shared spatial framework so they line up.

It is designed for architects, urban designers and landscape architects working on site analysis, context models, concept design and early-stage studies. Familiar Grasshopper components and one shared area selection keep the workflow direct.

### One area, multiple aligned layers

A typical workflow is simple:

1. Define the project area once with `Spatial Context`
2. Add maps, terrain, buildings, imagery, or vector data
3. Combine the aligned results directly in Rhino and Grasshopper

RhinoSpatial can work with:

- official vector and planning data from WFS or OGC API Features
- local Shapefile and GeoJSON project data
- maps, orthophotos, and imagery from WMS or GeoTIFF
- terrain from WCS, GeoTIFF DEM, Esri ASCII Grid, or XYZ/CSV grids
- LoD2 buildings from WFS, ATOM download feeds, CityGML, CityJSON, folders, or ZIP archives
- lightweight OpenStreetMap context for buildings, roads, water, green areas, and rail
- Google Photorealistic 3D Tiles as an optional visual reference layer

Public fallback sources make it easier to get oriented and try the workflow, while official or project-specific data remains the preferred path for production work.

### Google 3D Tiles

The Google 3D Tiles component is an optional reference viewer. It requires your own Google Maps Platform API key and billing setup. It is intended for visual context—not as an editable, bakeable, exportable, or offline project-data source.

## Current Release

Version **0.4.0** adds automatic building-tile downloads from supported ATOM services and improves coordinate recognition, terrain placement, missing-elevation handling and local vector parsing. Completed downloads can be reused. Full details are in the [release notes](https://github.com/PascalNun/RhinoSpatial/releases/tag/v0.4.0).

The complete planned source set is now present, but individual providers, coordinate systems, geometry types, and file metadata can still expose edge cases.

Feedback, edge cases, and additional test links are very welcome.

Created by Pascal Nünninghoff.

## Categories

- Architecture
- Environmental Design
- Import & Export
- Landscape
- Urban Planning & City Modeling

## App GUID

`1b7de011-9623-49c8-b867-3a2116c9549f`

## Website

User guide and examples:

- https://pascalnun.eu/tools/rhinospatial/
- [Working with project data](https://github.com/PascalNun/RhinoSpatial/blob/main/docs/WORKING_WITH_PROJECT_DATA.md) — general source choices and workflows, with release/development features labeled.

Support and bug reports:

- https://github.com/PascalNun/RhinoSpatial/issues

## Download

Food4Rhino listing:

- https://www.food4rhino.com/en/app/rhinospatial?lang=en

Rhino Package Manager:

- Open Rhino 8, run `PackageManager`, search for `RhinoSpatial`, and install the package.

Current release package:

- `RhinoSpatial-0.4.0.zip`

## Repository

https://github.com/PascalNun/RhinoSpatial

## License

MIT
