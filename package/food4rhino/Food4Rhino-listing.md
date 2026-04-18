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
- `lightweight OSM context`

The goal is to keep geospatial workflows practical, lightweight, and directly usable inside Rhino and Grasshopper.

RhinoSpatial grew out of a practical need: bringing official geodata and site context directly into Rhino and Grasshopper in a way that feels usable for design work. It is intended to keep that process simple, lightweight, and useful, with aligned outputs and minimal setup.

Typical workflow:

1. define the area once with `Spatial Context`
2. inspect layers when needed
3. load one or more aligned sources
4. work directly with the combined site context in Rhino and Grasshopper

## Early Stage Note

RhinoSpatial is currently still in an early alpha stage.

It has only been tested with a relatively small number of real WFS, WMS, LoD2, terrain, GeoTIFF, and OSM-related sources so far. Behavior may still vary depending on the provider, geometry type, coordinate system, service version, or response format.

Feedback, edge cases, and additional test links are very welcome.

## Download

Current release package:

- `RhinoSpatial-0.2.2-alpha.zip` 

## Repository

https://github.com/PascalNun/RhinoSpatial

## License

MIT
