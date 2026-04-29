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

## Reference Coverage

The current source manifest mixes:

- broad fallback references that make RhinoSpatial easier to try without local setup
- Hessen-specific deep-dive references that have been useful during development
- Germany-wide official references where they fit the RhinoSpatial workflow well

That balance is intentional, but local references should be understood as test fixtures and examples. The main RhinoSpatial workflow should continue to prefer user-provided project data, with generic fallbacks used only for convenience and orientation.

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
