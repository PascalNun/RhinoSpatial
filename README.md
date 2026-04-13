# RhinoSpatial

RhinoSpatial is a study-oriented geospatial toolkit for Rhino and Grasshopper.

The project started as a WFS-focused loader, but the direction is now broader: define one shared spatial area, then load aligned vector data, imagery, building massing, and later terrain or lightweight urban context into the same Rhino / Grasshopper study space.

## Download

You can download the current RhinoSpatial release from the GitHub Releases page:

https://github.com/PascalNun/RhinoSpatial/releases

On the release page, open the latest release and download the attached `.zip` file.

## Early Stage

RhinoSpatial is currently still in an early alpha stage.

It has only been tested with a relatively small number of WFS, WMS, and LoD2 services so far. Behavior may still vary depending on the provider, geometry type, coordinate system, service version, or response format.

Feedback, edge cases, and additional test datasets are very welcome.

## Direction

RhinoSpatial is meant to stay simple, useful, and design-friendly.

Instead of building a full GIS desktop workflow inside Grasshopper, the toolkit focuses on:

- one shared spatial context
- aligned outputs across sources
- useful defaults
- minimal GIS friction
- study usefulness over excessive configurability

The intended workflow is:

1. Define the area with `Spatial Context`
2. Inspect available layers where needed
3. Load one or more aligned sources
4. Work with the combined result in Rhino / Grasshopper

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
  - planned: `Load Terrain`
  - later: `Load OSM`

Current component meaning:

- `Spatial Context`
  The central shared spatial picker / shared placement context for the whole toolkit.
- `Load WFS`
  Official vector data.
- `Load WMS`
  Imagery / orthophoto / map context.
- `Load LoD2 Buildings`
  Building geometry / building mass / roof geometry context.

Planned next sources:

- `Load Terrain`
  Ground surface / terrain geometry, aligned through the same shared spatial context.
- `Load OSM`
  Lightweight, curated urban study context rather than a full OSM query interface.

## Current Workflow

Typical WFS workflow:

1. Connect a WFS URL to `List WFS Layers`
2. Choose a layer with `List Item`, or merge only the few layers you actually want
3. Connect a reference service and optional layer to `Spatial Context`
4. Open the map helper and define the area
5. Connect the `Spatial Context` output into `Load WFS`

Typical WMS workflow:

1. Connect a WMS URL to `List WMS Layers`
2. Choose a layer if needed
3. Use the same `Spatial Context`
4. Connect `Spatial Context` into `Load WMS`

Typical LoD2 workflow:

1. Connect the LoD2 WFS URL to `List WFS Layers`
2. Choose the building layer if needed
3. Use the same `Spatial Context`
4. Connect `Spatial Context` into `Load LoD2 Buildings`

## Default Behavior

By default, geometry is not placed at its original absolute world coordinates.

Instead, RhinoSpatial localizes geometry and imagery near the Rhino origin. This is intentional, because very large real-world coordinates can cause display and modeling problems in Rhino.

So the default behavior is:

- better Rhino usability
- easier viewing and testing
- aligned local study geometry
- the option to keep absolute coordinates when needed

## What It Supports Right Now

Current focus:

- WFS loading from user-provided URLs
- WMS loading from user-provided URLs or the fallback global imagery source
- layer discovery through `GetCapabilities`
- automatic SRS handling
- reusable shared spatial context for selection extent and placement
- GeoJSON first, with GML fallback when needed
- `Polygon`, `MultiPolygon`, `LineString`, `MultiLineString`, `Point`, and `MultiPoint` where the provider response can be interpreted correctly
- early LoD2 multi-surface loading

Current output:

- curves for polygon and line features
- points for point features
- textured mesh previews for WMS imagery
- Breps for LoD2 building surfaces
- grouped multi-layer output trees where appropriate

## Architecture

The project is split into small parts:

- `RhinoSpatial`
  The Grasshopper plugin
- `WfsCore`
  The reusable WFS / WMS / parsing core logic
- `RhinoSpatial.Sandbox`
  A small console sandbox used for testing core logic outside Grasshopper

Internally, the code is centered around the shared spatial context:

- `Spatial Context` produces the common area and placement logic
- loaders consume the same context
- outputs are meant to align correctly in the same Grasshopper space

This is now the core architectural rule for:

- WFS vector data
- WMS imagery
- LoD2 building data
- future terrain data
- later OSM context

## Notes

- The plugin tries to prefer a layer's default SRS when possible.
- `Spatial Context` is the central shared selection and placement component for RhinoSpatial.
- `Load WFS`, `Load WMS`, and `Load LoD2 Buildings` all consume the same spatial context.
- `Load Terrain` is planned as a separate aligned source and is not treated as part of LoD2 building loading.
- The map helper currently supports the SRS values that have come up most often in testing so far, including `EPSG:4326`, `EPSG:25832`, `EPSG:25833`, `EPSG:3857`, `EPSG:27700`, `EPSG:4283`, `EPSG:7423`, and `EPSG:7844`.
- `Load LoD2 Buildings` is still experimental and currently tuned around the Hessen `bu-core3d:Building` service pattern.
- Some providers behave differently, so more compatibility improvements may still be added over time.

## License

RhinoSpatial is released under the MIT License.

## Feedback

RhinoSpatial is still in an early alpha stage. Feedback, bug reports, edge cases, and additional WFS, WMS, LoD2, and future terrain test links are very welcome.
