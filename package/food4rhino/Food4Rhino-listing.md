# Food4Rhino Listing Draft

## Title

RhinoSpatial

## Short Description

Bring maps, terrain, buildings, imagery, and geodata into one aligned Rhino and Grasshopper site context.

## Long Description

RhinoSpatial brings real-world site context directly into Rhino and Grasshopper. Choose your study area once with `Spatial Context`, then add the layers you need. Maps, terrain, imagery, buildings, vector data, and lightweight urban context are placed in one shared spatial framework so they line up.

It is designed for architects, urban designers, landscape architects, and anyone using Rhino or Grasshopper for site analysis, context modeling, concept design, and early-stage studies. The aim is a direct, lightweight workflow with sensible defaults—not a full GIS application inside Grasshopper.

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
- LoD2 buildings from WFS, CityGML, CityJSON, folders, or ZIP archives
- lightweight OpenStreetMap context for buildings, roads, water, green areas, and rail
- Google Photorealistic 3D Tiles as an optional visual reference layer

Public fallback sources make it easier to get oriented and try the workflow, while official or project-specific data remains the preferred path for production work.

### Google 3D Tiles

The Google 3D Tiles component is an optional reference viewer. It requires your own Google Maps Platform API key and billing setup. It is intended for visual context—not as an editable, bakeable, exportable, or offline project-data source.

## Early Stage Note

RhinoSpatial is still in active alpha development. Version `0.3.3-alpha` improves layer alignment, Google 3D Tiles behavior, Spatial Context reference sources, and compatibility across WFS, WMS, WCS, LoD2, and local files.

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

Support and bug reports:

- https://github.com/PascalNun/RhinoSpatial/issues

## Download

Food4Rhino listing:

- https://www.food4rhino.com/en/app/rhinospatial?lang=en

Rhino Package Manager:

- Open Rhino 8, run `PackageManager`, enable **Include pre-releases**, search for `RhinoSpatial`, and install the package.

Current release package:

- `RhinoSpatial-0.3.3-alpha.zip`

## Repository

https://github.com/PascalNun/RhinoSpatial

## License

MIT
