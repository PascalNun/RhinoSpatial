<p align="center">
  <img src="RhinoSpatial/Resources/RhinoSpatial_Vector_Logo.svg" alt="RhinoSpatial logo" width="320" />
</p>

# RhinoSpatial

**A simple geospatial toolkit for working with site context directly in Rhino and Grasshopper.**
RhinoSpatial helps bring spatial data directly into Rhino and Grasshopper, so you can work with real site context inside your design environment without first taking a separate GIS detour.

Built around simple workflows, sensible defaults, and aligned outputs, RhinoSpatial is intended to support site analysis, context modeling, concept studies, and early-stage design with official geodata, imagery, terrain, and lightweight urban context.

The current RhinoSpatial scope is centered around:

- `Spatial Context`
- `WFS`
- `WMS`
- `LoD2 Buildings`
- `Terrain`
- `GeoTIFF`
- `OSM`
- `3D Tiles Viewer (Google)` for visual reference context

## Why RhinoSpatial

RhinoSpatial grew out of a simple practical need: loading official geodata such as WFS-based planning and city data directly into Rhino and Grasshopper in a way that feels usable for design work.

What started as a WFS-focused workflow has gradually grown into a broader contextual geospatial toolkit built around one shared study area and multiple aligned data layers.

## Install / Download

The easiest way to install RhinoSpatial is through Rhino's Package Manager:

1. Open Rhino 8
2. Run `PackageManager`
3. Search for `RhinoSpatial`
4. Install the package and restart Rhino / Grasshopper if needed

You can also open the [RhinoSpatial page on Food4Rhino](https://www.food4rhino.com/en/app/rhinospatial?lang=en).

For manual installation, download the current release from [GitHub Releases](https://github.com/PascalNun/RhinoSpatial/releases). On the release page, open the latest release and download the attached `.zip` file.

Release packages include a small number of third-party libraries used for raster handling, topology operations, and coordinate transforms. See the [third-party notices](THIRD-PARTY-NOTICES.md) in the repository or release archive for bundled dependency notices.

## Getting Started

The quickest way to try RhinoSpatial is:

1. Install RhinoSpatial through Rhino's Package Manager, or use the manual zip package from GitHub Releases
2. Open Grasshopper and place `Spatial Context`
3. Define an area once
4. Connect that same `Spatial Context` to one or more data layers such as:
   - `Load WFS`
   - `Load WMS`
   - `Load LoD2 Buildings`
   - `Load Terrain`
   - `Load GeoTIFF`
   - `Load OSM`
   - `3D Tiles Viewer (Google)` if you have a user-managed Google Maps API key

The toolkit works best when one selected study area drives multiple aligned layers.

For a guided visual walkthrough, open the [RhinoSpatial workflow](docs/SHOWCASE.md). For downloadable Grasshopper example definitions, open the [example overview](examples/README.md). Manual zip installation details are available in the [installation notes](package/food4rhino/INSTALL.md).

## Workflow Preview

RhinoSpatial is organized around one selected area that drives each aligned context layer.
Define the study area once with `Spatial Context`, then bring WMS, WFS, GeoTIFF, terrain, LoD2, OSM, and Google 3D Tiles reference context into the same Rhino / Grasshopper space.

![Spatial Context reference definition](docs/images/workflow-00-spatial-context.jpg)

For the full step-by-step visual walkthrough, open the [RhinoSpatial workflow](docs/SHOWCASE.md).

## Documentation

- [RhinoSpatial workflow](docs/SHOWCASE.md)
- [Example overview and downloadable `.gh` definitions](examples/README.md)
- [Manual validation checklist](examples/VALIDATION.md)
- [Curated source list](examples/sources.json)
- [Test source catalogue](docs/TEST_SOURCES.md)
- [Project scope](docs/PROJECT_SCOPE.md)
- [Roadmap](docs/ROADMAP.md)

## Project Status

RhinoSpatial is currently still in an alpha stage.

The `v0.3.2-alpha` release continues the first complete alpha line where the planned core data layers are present as a coherent toolkit. This release focuses on source compatibility and stabilization: broader vector inputs, cleaner LoD2 source handling, updated test-source documentation, and leaner packaging. The source categories are considered stable at the category level, while individual providers, geometry conversion paths, fallbacks, and documentation are still expected to improve through real-world use.

As of `v0.3.2-alpha`, the originally planned core source set is present: WFS, WMS, LoD2 Buildings, Terrain, GeoTIFF, OSM, and the Google 3D Tiles reference viewer all share the same Spatial Context workflow.

It has only been tested with a relatively small number of WFS, WMS, LoD2, terrain, GeoTIFF, OSM, and 3D Tiles workflows so far. Behavior may still vary depending on the provider, geometry type, coordinate system, service version, response format, API access, or local file metadata.

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

- WFS loading from user-provided URLs, plus OGC API Features, local Shapefile, and local GeoJSON vector loading through the same component
- WMS loading from user-provided URLs or the built-in fallback map/imagery source sequence
- layer discovery through `GetCapabilities`
- shared spatial selection and placement through `Spatial Context`
- automatic SRS handling where possible
- terrain mesh loading from user-provided WCS services, local GeoTIFF DEM files, or the built-in quick global land-elevation fallback
- GeoJSON-first parsing with GML fallback when needed
- early LoD2 multi-surface loading
- early OSM-based contextual loading
- georeferenced raster support through `Load GeoTIFF`
- Google Photorealistic 3D Tiles reference viewing from a user-managed Google Maps API key

Currently supported or partially supported outputs include:

- curves for polygon and line features
- points for point features
- textured mesh previews for WMS and raster imagery
- Breps for LoD2 building surfaces
- terrain meshes aligned to the same shared study space
- grouped multi-layer output trees where appropriate
- curated contextual OSM outputs for buildings, roads, water, green areas, and rail
- decoded Google 3D Tiles preview meshes with aligned materials for contextual viewing

## Core Workflow

The core idea behind RhinoSpatial is:

**one selected area, multiple aligned spatial layers**

The intended workflow is:

1. Define the area with `Spatial Context`
2. Inspect available layers where needed
3. Load one or more aligned sources
4. Work with the combined result directly in Rhino / Grasshopper

In practice, a simple first session often looks like:

1. Use `Spatial Context` to define the study area
2. Add `Load WMS` or `Load GeoTIFF` for raster context
3. Add `Load WFS`, `Load OSM`, or `Load LoD2 Buildings` for geometry
4. Add `Load Terrain` if you want ground context in the same aligned space

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
- `Viewers`
  - `3D Tiles Viewer (Google)`

### Component meaning

- `Spatial Context`
  The shared spatial picker and placement context for the whole toolkit.

- `List WFS Layers`
  Lists available layers from a WFS service.

- `List WMS Layers`
  Lists available layers from a WMS service.

- `Load WFS`
  Loads official vector data from WFS services, OGC API Features GeoJSON item endpoints, or local Shapefile / GeoJSON sources.

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

- `3D Tiles Viewer (Google)`
  Views bounded Google Photorealistic 3D Tiles as contextual preview meshes aligned to the same Spatial Context. This component requires the user's own Google Maps API key and is treated as a reference viewer, not as a normal editable source/import/export workflow.

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
3. Connect a reference service and layer to `Spatial Context` when needed
4. Open the map helper and define the area
5. Connect the `Spatial Context` output into `Load WFS`

For local vector files, connect a `.shp`, `.geojson`, or GeoJSON `.json` file path directly to the `WFS URL` input of `Load WFS`. The `Layer` input can be left empty in this mode and is used only as a label if provided. OGC API Features collection/items URLs can also be connected directly to the same input; RhinoSpatial requests GeoJSON features for the Spatial Context bbox and aligns them like the other vector sources.

### Typical WMS workflow

1. Connect a WMS URL to `List WMS Layers`
2. Choose a layer if needed
3. Use the same `Spatial Context`
4. Connect `Spatial Context` into `Load WMS`

### Typical LoD2 workflow

1. Connect one `LoD2 Source` into `Load LoD2 Buildings`
2. Use a LoD2 WFS URL, local `.gml` / `.xml` / `.citygml` / `.cityjson` / `.json` file, folder, or `.zip` archive
3. Choose the WFS building layer if needed, or use `Layer` as a label for local sources
4. Use the same `Spatial Context`

`Load LoD2 Buildings` can load from a LoD2 WFS service or from local CityGML/GML/XML/CityJSON building data. Local CityGML sources can be a single file, a folder, or a ZIP archive containing multiple CityGML/GML/XML files. WFS mode requests a small buffered area from the service, then keeps the returned buildings that intersect the actual Spatial Context. Local CityGML mode scans the source, skips files outside the Spatial Context when file bounds are available, filters buildings against the same Spatial Context, and uses the same Brep conversion and elevation alignment path. The `Status` output reports returned buildings, kept buildings, request/query or source bounds, converted surfaces, skipped surfaces, and output bounds so provider coverage gaps can be separated from conversion problems.

For local folders and ZIP archives, RhinoSpatial first tries to use file-level and building-level bounds so it can avoid converting CityGML geometry outside the selected area. Very large single files can still take longer than a WFS request because the file has to be inspected locally before geometry can be filtered.

### Typical terrain workflow

1. Define the area with `Spatial Context`
2. Connect a terrain WCS source, local GeoTIFF DEM, Esri ASCII Grid, or regular XYZ/CSV grid file path to `Load Terrain`, or leave the URL empty for the built-in global land-elevation fallback
3. Load a terrain mesh aligned to the same local study space as the other sources

The built-in fallback is intentionally limited to small study areas and short request times. If the selected area is too large, the fallback service has no usable elevation samples, or the request takes too long, `Load Terrain` fails quickly with a status message instead of blocking Grasshopper for a long time. For project work, connect an explicit official or project-specific terrain source. Local `.tif` / `.tiff`, `.asc`, `.xyz`, and `.csv` DEM/grid files can be connected directly to the same Terrain URL input. For text grids, use the `Coverage` input as an EPSG override such as `EPSG:25832`; otherwise RhinoSpatial assumes the current Spatial Context SRS.

### Typical OSM workflow

1. Define the area with `Spatial Context`
2. Connect `Spatial Context` into `Load OSM`
3. Load curated OSM context such as buildings, roads, water, green areas, and rail into the same aligned workflow

### Typical 3D Tiles viewer workflow

1. Define the area with `Spatial Context`
2. Connect your own Google Maps API key to `3D Tiles Viewer (Google)`
3. Enable the viewer only for the bounded area you want to inspect
4. Use the preview meshes/materials as visual context alongside the other aligned RhinoSpatial sources

The Google component is intentionally a viewer/reference workflow. It requests Google Maps Platform Map Tiles API content directly for the current Grasshopper preview, outputs temporary preview meshes/materials, and does not provide offline caching, export, baking, or reuse as editable project geometry. Users are responsible for their own Google Maps Platform project, billing, API key, and compliance with the current [Google Maps Platform Terms of Service](https://cloud.google.com/maps-platform/terms) and [Map Tiles API Policies](https://developers.google.com/maps/documentation/tile/policies).

To avoid visible holes where fine 3D tile coverage is incomplete, RhinoSpatial may keep coarser parent tile content as a fallback behind refined tiles. This can make the preview extend slightly beyond the selected Spatial Context, which is preferred for visual checking over under-loading missing chunks inside the reference area.

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
- Google 3D Tiles visual reference context

## Notes

- RhinoSpatial tries to prefer a layer's default SRS when possible.
- `Spatial Context` is the central shared selection and placement component for the whole toolkit.
- `Load WFS`, `Load WMS`, `Load LoD2 Buildings`, `Load Terrain`, `Load GeoTIFF`, and `Load OSM` are all intended to work within the same shared spatial workflow.
- `Load WFS` also accepts local `.shp` files as vector sources. The component name stays stable for existing Grasshopper definitions, but the source model is broader than web-only WFS.
- `Load Terrain` and `Load LoD2 Buildings` share the same localized elevation baseline when absolute coordinates are off, so terrain and buildings sit on the same local Z reference.
- `Load WMS` uses explicit user-provided services when connected. If no WMS URL is provided, it tries a sharper global OpenStreetMap WMS fallback first and then falls back to NASA GIBS global imagery if needed. When a selected WMS layer does not support the Spatial Context's primary SRS but does advertise another CRS that the context already knows, RhinoSpatial requests that supported CRS and still places the image in the shared local study area.
- `Load Terrain` is a separate aligned source and is not treated as part of LoD2 loading. It accepts WCS services, local GeoTIFF DEM files, local Esri ASCII Grid files, and regular XYZ/CSV terrain grids. If no terrain URL is provided, it uses a quick global land-elevation fallback for small-context orientation; project work should still prefer user-provided or official terrain where available.
- `Load LoD2 Buildings` uses one source input for WFS URLs, local CityGML/GML/XML files, local CityJSON files, folders, and ZIP archives. It exposes detailed status diagnostics to make provider coverage, local tile filtering, bbox filtering, duplicate surfaces, and conversion failures easier to distinguish.
- The Google 3D Tiles component is a reference viewer with user-managed API access. It outputs temporary contextual preview meshes/materials for viewing, but should not be treated as a substitute for official editable project data, and should not be used as an offline cache/export workflow.
- The map helper currently supports the SRS values that have come up most often in testing so far, including `EPSG:4326`, `EPSG:25832`, `EPSG:25833`, `EPSG:3857`, `EPSG:27700`, `EPSG:4283`, `EPSG:7423`, and `EPSG:7844`.
- `Load LoD2 Buildings` is still experimental and provider compatibility will need more real-world testing.
- Shapefile, local GeoJSON, and OGC API Features are currently supported as vector sources through `Load WFS`, not as LoD2 building import. GeoPackage remains a later local vector/source candidate.
- Some providers behave differently, so more compatibility improvements will likely be added over time.

## License

RhinoSpatial is released under the MIT License.

## Feedback

RhinoSpatial is still in an alpha stage.

Feedback, bug reports, edge cases, and additional WFS, WMS, LoD2, terrain, GeoTIFF, OSM, and 3D Tiles test notes are very welcome.
