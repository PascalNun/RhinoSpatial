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

## Current Core Workflow Examples

The current example/regression set is centered around:

- `Spatial Context`
- `Load WFS`
- `Load WMS`
- `Load LoD2 Buildings`
- `Load Terrain`
- `Load GeoTIFF`
- `Load OSM`

These examples are meant to support:

- contextual site modeling
- black-plan / context-plan style studies
- early-stage design workflows
- alignment checks across multiple sources

## Planned Expansion

This folder is intentionally lightweight for now.

The next likely additions are:

- one or more actual `.gh` example definitions
- a small set of safe example raster files for `Load GeoTIFF`
- more provider notes for real-world compatibility checks

Until then, the validation checklist and source manifest are the main regression baseline.

## Reference Coverage

The current source manifest mixes:

- Hessen-specific deep-dive references that have been useful during development
- Germany-wide official references where they fit the RhinoSpatial workflow well

That balance is intentional.

Hessen remains a strong practical test bed for:

- LoD2
- terrain
- cadastral workflows
- official building and parcel context

Germany-wide references are included where they strengthen:

- national-scale WFS/WMS testing
- fallback planning
- broader provider validation
- examples that are not tied to a single federal state
