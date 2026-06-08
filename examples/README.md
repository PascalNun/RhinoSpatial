# Examples

This folder is the starting point for RhinoSpatial example definitions and manual checks.

The goal is not to collect every possible provider or service variation. The goal is to keep a small, practical set of reference workflows that helps us:

- show how to assemble site context with RhinoSpatial
- verify that the core data layers still behave correctly
- catch regressions when the toolkit is refined

RhinoSpatial is built around one shared `Spatial Context` and multiple aligned data layers, so the examples are organized around complete study workflows rather than isolated one-off technical tests.

## Current Structure

- [Example overview](README.md)
  Overview of the example strategy
- [Manual validation checklist](VALIDATION.md)
  Manual checklist for the current core data layers
- [Curated source list](sources.json)
  Curated reference sources and notes for repeatable testing
- [Test source catalogue](../docs/TEST_SOURCES.md)
  Sorted provider/source catalogue with current probe notes and future format candidates
- [Grasshopper examples](gh/)
  Example Grasshopper definitions for the current showcase/regression workflows

Related project docs:

- [Site context workflow](../docs/SHOWCASE.md)
  Public-facing screenshots and example workflow previews

## Current Core Workflow Examples

The current example/regression set is centered around:

- `Spatial Context`
- `Load WFS`
- `Load WMS`
- `Load LoD2 Buildings`
- `Load Terrain`
- `Load GeoTIFF`
- `Load OSM`
- `3D Tiles Viewer (Google)` where a user-managed API key is available

These examples are meant to support:

- contextual site modeling
- black-plan / context-plan style studies
- early-stage design workflows
- alignment checks across multiple sources

## Current Example Definitions

The current `.gh` example set has one full reference definition plus a small
set of focused workflow definitions:

- [Download .gh: Full reference workflow](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/00-rhinospatial-reference-workflow.gh)
  Full walkthrough definition with the shared `Spatial Context` and all current
  data layers.
- [Download .gh: WMS + WFS basics](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/01-wms-wfs-basics.gh)
  Basic map imagery plus vector data workflow.
- [Download .gh: GeoTIFF + terrain](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/02-geotiff-terrain.gh)
  Local raster / terrain workflow. The GeoTIFF example is a local-file placeholder
  and expects the user to connect their own `.tif` / `.tiff` file.
- [Download .gh: LoD2 buildings](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/03-lod2-buildings.gh)
  Official LoD2 building context workflow.
- [Download .gh: OSM context](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/04-osm-context.gh)
  Lightweight OSM context workflow for quick site studies and black-plan views.
- [Download .gh: Google 3D Tiles reference](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/05-google-3d-tiles-reference.gh)
  Google 3D Tiles visual reference context. The example does not include a
  Google API key.

These are meant to support:

- quick manual smoke tests
- screenshot capture for the public project presentation
- future checks against representative RhinoSpatial workflows

The validation checklist and source manifest remain the broader regression baseline.

If a saved working definition shows Grasshopper IO archive warnings after a component layout change, replace the affected component once and reconnect it. The example definitions in this repository should stay warning-free and represent the current component layout.

The GeoTIFF workflow is a local-file workflow in this release. The example
definition should not contain a personal local file path or bundled project
raster; it should clearly ask the user to connect their own georeferenced
GeoTIFF.

The Google 3D Tiles workflow requires the user's own Google Maps API key. The
example definitions do not include an API key; if you add your own key, do not
share that edited file publicly. Example and validation notes should describe
the component as a visual reference viewer only, not as an editable data source,
import/export, bake, or offline cache workflow.

## Reference Coverage

The current source manifest mixes:

- broad fallback references that make RhinoSpatial easier to try without local setup
- non-German public WFS/WMS/WCS and sample-file references for provider compatibility
- Hessen-specific deep-dive references that have been useful during development
- Germany-wide official references where they fit the RhinoSpatial workflow well

That balance is intentional, but local references should be understood as test fixtures and examples. The main RhinoSpatial workflow should continue to prefer user-provided project data, with generic fallbacks and public regression links used only for convenience, orientation, and compatibility testing.

Hessen remains a strong practical test bed for:

- LoD2
- terrain
- cadastral workflows
- official building and parcel context

Broad and Germany-wide references are included where they strengthen:

- national-scale WFS/WMS testing
- fallback behavior checks
- broader provider validation
- examples that are not tied to a single federal state

Local CityGML/LoD2 folders and ZIP archives are useful regression inputs, but they can vary widely in file structure and size. Validation should record whether RhinoSpatial skipped files by metadata bounds, filtered buildings before conversion, or had to inspect a large single tile.
