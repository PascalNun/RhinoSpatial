# Validation Checklist

This checklist is the manual regression set for RhinoSpatial.

It is intentionally small and practical. The point is to confirm that the core source ecosystem still feels aligned, usable, and trustworthy after changes.

## General Checks

- RhinoSpatial builds cleanly from `RhinoSpatial.sln`
- component grouping still reads as `Context / Layers / Sources`
- icons load correctly for all components
- `Spatial Context` still acts as the shared starting point
- source outputs still align correctly when used together
- status messaging still feels clear and calm rather than overly technical

## Spatial Context

- create a `Spatial Context`
- open the map helper
- select an area
- save the Grasshopper file
- reopen the file
- confirm the selected area persists
- confirm reopening the helper redraws the saved selection

## WFS

- connect a reference WFS URL
- use `List WFS Layers`
- load one selected layer through `Load WFS`
- load multiple layers directly and confirm output branches stay grouped by layer
- confirm localized placement still works

## WMS

- connect a WMS URL
- use `List WMS Layers`
- load one selected layer through `Load WMS`
- confirm the image aligns to the same `Spatial Context`
- confirm fallback imagery still works when no custom source is provided

## LoD2 Buildings

- connect a LoD2 WFS source
- load buildings through `Load LoD2 Buildings`
- confirm the building output still aligns to the same `Spatial Context`
- confirm localized mode still behaves consistently with terrain
- confirm obvious missing-face regressions are not reintroduced

## Terrain

- load terrain through `Load Terrain`
- confirm terrain aligns with the same `Spatial Context`
- confirm terrain and LoD2 share a sensible local Z reference in localized mode
- confirm absolute mode still keeps real coordinates when requested
- leave the Terrain URL empty and confirm the global fallback returns usable context terrain
- connect a user-provided WCS terrain source and confirm explicit source data still wins over fallback behavior

## GeoTIFF

- load a georeferenced raster through `Load GeoTIFF`
- confirm raster placement aligns to the same `Spatial Context`
- confirm image/material behavior is usable in Rhino / Grasshopper
- confirm alpha/transparency handling still behaves sensibly

## OSM

- load `Buildings` only and confirm the result is usable study geometry
- load `Road` and confirm the road region is continuous and stable
- load `Water`, `Green`, and `Rail`
- confirm category outputs feel trustworthy
- confirm OSM still works gracefully when one category fails or times out
- confirm status output stays readable during partial OSM failures

## Google 3D Tiles Viewer

- connect a user-managed Google Maps API key
- enable `3D Tiles Viewer (Google)` for a small Spatial Context
- confirm preview meshes and materials appear aligned to the same area
- confirm status clearly communicates viewer behavior and bounded loading
- confirm the component remains disabled and quiet when Enable is false

## Combined Workflow

- use one `Spatial Context`
- load WFS, WMS, LoD2, Terrain, GeoTIFF, OSM, and optional 3D Tiles viewer context in the same definition as far as practical
- confirm outputs align in XY
- confirm localized placement still makes the workflow manageable near the Rhino origin
- confirm terrain/building elevation consistency remains acceptable
