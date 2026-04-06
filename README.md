# RhinoWFS

RhinoWFS is a Grasshopper plugin for Rhino 8 that loads WFS data directly into Grasshopper.

Many municipalities and public agencies publish planning and GIS data as WFS services. This plugin is meant to make that data easier to access inside Grasshopper without needing a separate GIS workflow first.

## Download

You can download the current RhinoWFS release from the GitHub Releases page:

https://github.com/PascalNun/RhinoWFS/releases

On the release page, open the latest release and download the attached `.zip` file.

## Early Stage

RhinoWFS is currently still in an early alpha stage.

So far, it has only been tested with a relatively small number of WFS services, datasets, and response patterns. Behavior may still vary depending on the WFS provider, geometry type, coordinate system, service version, or response format.

At the moment, the plugin has only been tested with a handful of real service links and example files, so additional testing is still very important.

Feedback, edge cases, and additional test datasets are very welcome.

## Why

The goal of RhinoWFS is to stay simple.

Instead of building a full GIS application inside Grasshopper, the plugin focuses on a small, practical workflow:

1. Connect a WFS URL
2. List the available layers
3. Choose one or more layers
4. Optionally define a bounding box
5. Load the vector geometry into Grasshopper

## Current Workflow

The plugin currently uses three Grasshopper components:

- `WFS Layers`
  Lists the available WFS layers from a given WFS URL.
- `WFS Bounding Box`
  Opens a small helper map in the browser and outputs a bounding box plus the matching SRS.
- `Load WFS`
  Loads one or more selected layers and outputs the geometry into Grasshopper.

Typical workflow:

1. Connect a WFS URL to `WFS Layers`
2. Choose a layer with `List Item`, or merge only the few layers you actually want to load
3. Connect the WFS URL and layer to `WFS Bounding Box`
4. Use the map helper if you want to limit the area
5. Connect the chosen layer into `Load WFS`
6. Optionally connect the bounding box and SRS from `WFS Bounding Box` into `Load WFS`

## Default Behavior

By default, the geometry is not placed at its original absolute world coordinates.

Instead, RhinoWFS localizes the geometry near the Rhino origin. This is intentional, because very large real-world coordinates can cause display and modeling problems in Rhino.

If needed, you can switch `Load WFS` to `Use Absolute Coordinates`.

So the default behavior is:

- better Rhino usability
- easier viewing and testing
- geometry can still be rebuilt, transformed, or relocated later in Grasshopper if needed

## What It Supports Right Now

Current focus:

- WFS loading from user-provided URLs
- layer discovery through `GetCapabilities`
- optional bounding box filtering
- automatic SRS handling
- GeoJSON first, with GML fallback when needed
- `Polygon`, `MultiPolygon`, `LineString`, `MultiLineString`, `Point`, and `MultiPoint` where the provider response can be interpreted correctly
- Grasshopper geometry output for curves and points

Current output:

- curves for polygon and line features
- points for point features
- multi-layer output grouped by `{layerIndex; featureIndex}`


## How It Works

At a high level:

1. RhinoWFS reads the WFS capabilities
2. It lists available layers
3. It builds a `GetFeature` request
4. It tries to download GeoJSON first and falls back to GML when needed
5. It parses the returned features
6. It extracts polygon rings, line strings, or points
7. It converts those into Rhino/Grasshopper geometry

If multiple layers are loaded, the geometry output is grouped in a tree by:

- layer
- feature

So the structure is:

- `{layerIndex; featureIndex}`

This keeps multi-layer output easier to work with.

## Architecture

The project is split into small parts:

- `RhinoWFS`
  The actual Grasshopper plugin
- `WfsCore`
  The reusable WFS loading and parsing logic
- `RhinoWFS.Sandbox`
  A small console sandbox used for testing core logic outside Grasshopper

Internally, the plugin is also split by responsibility:

- WFS request building
- WFS download
- capabilities parsing
- GeoJSON parsing
- Rhino/Grasshopper geometry conversion
- browser-based bounding box helper

This keeps the core logic reusable and keeps the Rhino-specific code separate from the WFS-specific code.

## Notes

- The plugin tries to prefer the layer's default SRS when possible.
- The bounding box helper currently supports the common SRS values that have come up in testing so far, including `EPSG:4326`, `EPSG:25832`, `EPSG:25833`, `EPSG:3857`, `EPSG:27700`, `EPSG:4283`, and `EPSG:7844`.
- `Load WFS` blocks unlimited requests without a bounding box, and it warns when a request is still unbounded.
- `Load WFS` also blocks the accidental case where the full `WFS Layers` output is connected directly into the `Layer` input with many layers selected.
- Some WFS services behave differently, so more compatibility improvements may still be added over time.

## License

RhinoWFS is released under the MIT License.

## Feedback

RhinoWFS is still in an early alpha stage. Feedback, bug reports, edge cases, and additional WFS test links are very welcome.
