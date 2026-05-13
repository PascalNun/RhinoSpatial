# Examples

This folder is the starting point for RhinoSpatial example workflows and regression checks.

The goal is not to collect every possible provider or service variation. The goal is to keep a small, practical set of reference workflows that helps us:

- demonstrate the intended RhinoSpatial workflow
- verify that the core source ecosystem still behaves correctly
- catch regressions when the toolkit is refined

RhinoSpatial is built around one shared `Spatial Context` and multiple aligned sources, so the examples are organized around complete study workflows rather than isolated one-off technical tests.

## Current Structure

- `README.md`
  Overview of the example strategy
- `VALIDATION.md`
  Manual regression checklist for the current core source ecosystem
- `sources.json`
  Curated reference sources and notes for repeatable testing
- `docs/TEST_SOURCES.md`
  Sorted provider/source catalogue with current probe notes and future format candidates
- `gh/`
  Example Grasshopper definitions for the current showcase/regression workflows

Related project docs:

- `docs/SHOWCASE.md`
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

The current `.gh` example set is intentionally small:

- `gh/01-wfs-wms-basics.gh`
- `gh/02-lod2-terrain-context.gh`
- `gh/03-osm-blackplan.gh`

These are meant to support:

- quick manual smoke tests
- screenshot capture for the public project presentation
- future regression checking against representative RhinoSpatial workflows

The validation checklist and source manifest remain the broader regression baseline.

If a saved working definition shows Grasshopper IO archive warnings after a component layout change, replace the affected component once and reconnect it. The committed example definitions should stay warning-free and represent the current component layout.

The Google 3D Tiles workflow is optional because it requires the user's own Google Maps API key. Example and validation notes should describe it as a reference viewer only, not as an editable data-source or export workflow.

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
