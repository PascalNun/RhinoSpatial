# Food4Rhino Listing Draft

## Title

RhinoSpatial

## Short Description

Study-oriented geospatial toolkit for Rhino 8 and Grasshopper.

## Long Description

RhinoSpatial is a Grasshopper toolkit for Rhino 8 that helps you assemble aligned spatial study context quickly and with as little GIS friction as possible.

It started as a WFS-focused loader, but the direction is now broader: one shared spatial selection, multiple aligned spatial sources, and useful defaults for early-stage design work.

Current workflow:

1. Define an area with `Spatial Context`
2. Inspect available service layers where needed
3. Load aligned vector, imagery, and LoD2 building sources
4. Work with the combined result in Rhino / Grasshopper

Current components:

- `Spatial Context`
- `List WFS Layers`
- `List WMS Layers`
- `Load WFS`
- `Load WMS`
- `Load LoD2 Buildings`

Current focus:

- shared spatial context and placement logic
- WFS vector loading
- WMS imagery loading
- early LoD2 building loading
- aligned outputs for study and concept workflows
- simple UI with practical defaults instead of full GIS complexity

Planned next sources:

- `Load Terrain`
- later `Load OSM`

## Early Stage Note

RhinoSpatial is currently still in an early alpha stage.

It has only been tested with a relatively small number of real WFS, WMS, and LoD2 services so far. Behavior may still vary depending on the provider, geometry type, coordinate system, service version, or response format.

Feedback, edge cases, and additional test links are very welcome.

## Download

Current release package:

- `RhinoSpatial-0.1.1-alpha.zip`

## Repository

https://github.com/PascalNun/RhinoSpatial

## License

MIT
