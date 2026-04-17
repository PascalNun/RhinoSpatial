<p align="center">
  <img src="RhinoSpatial/Resources/RhinoSpatial_Vector_Logo.svg" alt="RhinoSpatial logo" width="320" />
</p>

# RhinoSpatial

**A simple geospatial toolkit for working with site context directly in Rhino and Grasshopper.**
RhinoSpatial helps bring spatial data directly into Rhino and Grasshopper, so you can work with real site context inside your design environment without first going through a separate GIS workflow.

Built around simple workflows, sensible defaults, and aligned outputs, RhinoSpatial is intended to support contextual modeling, concept work, and early-stage design with official geodata, imagery, terrain, and lightweight urban context.

The current RhinoSpatial scope is centered around:

- `Spatial Context`
- `WFS`
- `WMS`
- `LoD2 Buildings`
- `Terrain`
- `GeoTIFF`
- `OSM`

This core source ecosystem is considered stable in category, but still open to refinement where it improves geometry quality, output usefulness, fallback behavior, alignment consistency, and general usability.

## Why RhinoSpatial

RhinoSpatial grew out of a simple practical need: loading official geodata such as WFS-based planning and city data directly into Rhino and Grasshopper in a way that feels usable for design work.

What started as a WFS-focused workflow has gradually grown into a broader contextual geospatial toolkit built around one shared spatial context and multiple aligned data sources.

Two internal project docs track that direction more explicitly:

- `docs/PROJECT_SCOPE.md`
- `docs/ROADMAP.md`

A small example and regression baseline now lives in:

- `examples/README.md`
- `examples/VALIDATION.md`
- `examples/sources.json`

## Download

You can download the current RhinoSpatial release from the GitHub Releases page:

https://github.com/PascalNun/RhinoSpatial/releases

On the release page, open the latest release and download the attached `.zip` file.

Release packages include a small number of third-party libraries used for raster handling, topology operations, and coordinate transforms. See `THIRD-PARTY-NOTICES.md` in the repository or release archive for bundled dependency notices.

## Project Status

RhinoSpatial is currently still in an early alpha stage.

It has only been tested with a relatively small number of WFS, WMS, LoD2, terrain, and OSM-related sources so far. Behavior may still vary depending on the provider, geometry type, coordinate system, service version, or response format.

Feedback, edge cases, and additional real-world test datasets are very welcome.

## Design Philosophy

RhinoSpatial is designed to keep geospatial workflows simple.

Instead of exposing every possible data-source parameter or building a heavy GIS-style interface inside Grasshopper, the goal is to provide:

- simple workflows
- sensible defaults
- aligned outputs
- minimal interface friction
- useful geometry for design work

The focus is on practical results inside Rhino and Grasshopper, not on recreating a full GIS workflow.

RhinoSpatial should not become a mini GIS desktop application, a giant source browser, or a heavily overloaded expert interface. The goal is to keep common geospatial tasks direct, lightweight, and usable in the design environment.

## What It Supports Right Now

Current focus and capabilities include:

- WFS loading from user-provided URLs
- WMS loading from user-provided URLs or fallback imagery sources
- layer discovery through `GetCapabilities`
- shared spatial selection and placement through `Spatial Context`
- automatic SRS handling where possible
- terrain surface loading from the first WCS-backed provider
- GeoJSON-first parsing with GML fallback when needed
- early LoD2 multi-surface loading
- early OSM-based contextual loading
- georeferenced raster support through `Load GeoTIFF`

Currently supported or partially supported outputs include:

- curves for polygon and line features
- points for point features
- textured mesh previews for WMS and raster imagery
- Breps for LoD2 building surfaces
- terrain meshes aligned to the same shared study space
- grouped multi-layer output trees where appropriate
- curated contextual OSM outputs for buildings, roads, water, green areas, and rail

## Core Workflow

The core idea behind RhinoSpatial is:

**one selected area, multiple aligned spatial layers**

The intended workflow is:

1. Define the area with `Spatial Context`
2. Inspect available layers where needed
3. Load one or more aligned sources
4. Work with the combined result directly in Rhino / Grasshopper

## Components

The Grasshopper tab is organized as:

- `Context`
  - `Spatial Context`
- `Layers`
  - `List WFS Layers`
  - `List WMS Layers`
- `Sources`
  - `Load WFS`
  - `Load WMS`
  - `Load LoD2 Buildings`
  - `Load Terrain`
  - `Load GeoTIFF`
  - `Load OSM`

### Component meaning

- `Spatial Context`  
  The shared spatial picker and placement context for the whole toolkit.

- `List WFS Layers`  
  Lists available layers from a WFS service.

- `List WMS Layers`  
  Lists available layers from a WMS service.

- `Load WFS`  
  Loads official vector data.

- `Load WMS`  
  Loads imagery, orthophoto, or map context.

- `Load LoD2 Buildings`  
  Loads building geometry, building massing, and roof geometry context.

- `Load Terrain`  
  Loads ground surface / terrain geometry aligned through the same shared spatial context.

- `Load GeoTIFF`  
  Loads georeferenced raster files into the same spatial workflow.

- `Load OSM`  
  Loads lightweight, curated urban context for fast study workflows.

## OSM Direction

`Load OSM` is intended as a lightweight contextual source, not as a full OSM query editor.

Its role is to quickly generate useful study geometry for the selected area, including:

- `Buildings`
- `Road`
- `Water`
- `Green`
- `Rail`

Buildings are the primary priority. Roads, water, green areas, and rail are meant to support black-plan style workflows, quick contextual modeling, and site understanding without overloading the UI with too many low-level options.

OSM is part of the core RhinoSpatial scope, and it is expected to keep evolving inside that scope through better geometry, better grouping, and stronger black-plan usefulness.

## Typical Workflows

### Typical WFS workflow

1. Connect a WFS URL to `List WFS Layers`
2. Choose a layer with `List Item`, or merge only the layers you actually want
3. Connect a reference service and optional layer to `Spatial Context`
4. Open the map helper and define the area
5. Connect the `Spatial Context` output into `Load WFS`

### Typical WMS workflow

1. Connect a WMS URL to `List WMS Layers`
2. Choose a layer if needed
3. Use the same `Spatial Context`
4. Connect `Spatial Context` into `Load WMS`

### Typical LoD2 workflow

1. Connect the LoD2 WFS URL to `List WFS Layers`
2. Choose the building layer if needed
3. Use the same `Spatial Context`
4. Connect `Spatial Context` into `Load LoD2 Buildings`

### Typical terrain workflow

1. Define the area with `Spatial Context`
2. Connect the terrain source to `Load Terrain`
3. Load terrain aligned to the same local study space as the other sources

### Typical OSM workflow

1. Define the area with `Spatial Context`
2. Connect `Spatial Context` into `Load OSM`
3. Load curated OSM context such as buildings, roads, water, green areas, and rail into the same aligned workflow

## Default Behavior

By default, geometry is not placed at its original absolute world coordinates.

Instead, RhinoSpatial localizes geometry and imagery near the Rhino origin. This is intentional, because very large real-world coordinates can cause display and modeling problems in Rhino.

So the default behavior is:

- better Rhino usability
- easier viewing and testing
- aligned local study geometry
- the option to keep absolute coordinates when needed

## Architecture

The project is split into small parts:

- `RhinoSpatial`  
  The Grasshopper plugin

- `RhinoSpatial.Core`  
  The reusable geospatial core for WFS, WMS, terrain, LoD2, OSM, raster handling, and shared coordinate logic

- `RhinoSpatial.Sandbox`  
  A small console sandbox used for testing core logic outside Grasshopper, with sample fixtures kept outside the main plugin projects

Internally, the project is centered around the shared spatial context:

- `Spatial Context` produces the common area and placement logic
- loaders consume the same context
- outputs are intended to align correctly in the same Rhino / Grasshopper study space

This shared spatial logic is the core architectural rule for:

- WFS vector data
- WMS imagery
- LoD2 building data
- terrain data
- georeferenced raster data
- OSM context

Later extension goals may exist, but they should not redefine the current product identity. For example, a Google 3D Tiles reference importer may still be considered later, optional, and advanced rather than part of the core system.

## Notes

- RhinoSpatial tries to prefer a layer's default SRS when possible.
- `Spatial Context` is the central shared selection and placement component for the whole toolkit.
- `Load WFS`, `Load WMS`, `Load LoD2 Buildings`, `Load Terrain`, `Load GeoTIFF`, and `Load OSM` are all intended to work within the same shared spatial workflow.
- `Load Terrain` and `Load LoD2 Buildings` share the same localized elevation baseline when absolute coordinates are off, so terrain and buildings sit on the same local Z reference.
- `Load Terrain` is a separate aligned source and is not treated as part of LoD2 loading.
- The map helper currently supports the SRS values that have come up most often in testing so far, including `EPSG:4326`, `EPSG:25832`, `EPSG:25833`, `EPSG:3857`, `EPSG:27700`, `EPSG:4283`, `EPSG:7423`, and `EPSG:7844`.
- `Load LoD2 Buildings` is still experimental and currently tuned around the Hessen `bu-core3d:Building` service pattern.
- Some providers behave differently, so more compatibility improvements will likely be added over time.

## License

RhinoSpatial is released under the MIT License.

## Feedback

RhinoSpatial is still in an early alpha stage.

Feedback, bug reports, edge cases, and additional WFS, WMS, LoD2, terrain, GeoTIFF, and OSM test links are very welcome.
