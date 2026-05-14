# Roadmap

## Overview

RhinoSpatial has moved beyond its original starting point as a WFS-focused loader and is now developing into a broader geospatial toolkit for Rhino and Grasshopper.

At this stage, the core source scope is considered defined at the category level.
The `v0.3.1-alpha` release continues the first complete alpha line where the planned source categories are present together in the plugin.
The main focus is no longer to constantly add new categories of sources, but to strengthen, refine, simplify, and polish the current system.

No additional core source categories are currently planned.

The core direction remains:

- one shared spatial context
- multiple aligned spatial layers
- simple workflows
- sensible defaults
- useful outputs for contextual modeling, studies, and early-stage design

---

## Current Core Scope

The current core RhinoSpatial ecosystem includes:

- Spatial Context
- WFS
- WMS
- LoD2 Buildings
- Terrain
- GeoTIFF
- OSM
- optional Google 3D Tiles viewing

These are considered the meaningful core source/component types of the project.

Future work should primarily deepen and improve these areas rather than expand the source scope too aggressively.

---

## Current Priorities

### 1. Stabilize and refine the core source ecosystem
The most important near-term priority is to make the existing source types robust, coherent, and pleasant to use together.

This includes:
- better consistency across source components
- clearer shared behavior through Spatial Context
- stronger alignment between sources
- more reliable provider handling
- better output quality
- more polished workflows

### 2. Continue a real-use stabilization phase
The toolkit is now in a good enough state that future improvements should come more from real use than from speculative expansion.

This includes:
- using RhinoSpatial on real projects
- collecting recurring friction points
- fixing the highest-value annoyances rather than inventing new work
- keeping the issue list short and grounded in actual workflow problems

### 3. Improve OSM as a contextual fallback
Load OSM is already part of the core scope, but it is expected to continue evolving.

The goal is not to turn OSM into a full query editor.
The goal is to make it a better contextual source for:

- Buildings
- Road
- Water
- Green
- Rail

OSM refinement may include:
- better building outputs
- better road geometry and width handling
- better green/open-space grouping
- stronger black-plan usefulness
- selected additional contextual outputs, if they remain lightweight and clearly useful

### 4. Improve reliability and persistence
The toolkit should feel dependable in day-to-day use.

Near-term reliability improvements may include:
- better persistence of Spatial Context
- cleaner state restoration when files are reopened
- stronger handling of fallback behavior
- better status messages
- better timeout / failure handling
- fewer fragile edge cases

### 5. Improve raster and imagery workflows
Raster handling is now part of the project through WMS and GeoTIFF.

This area should continue to improve, including:
- better fallback imagery strategies
- keep WMS fallback as an ordered source sequence, preferring sharper global map context before broad low-resolution imagery
- stronger raster alignment behavior
- transparency / alpha handling
- more consistent image/material behavior
- stronger GeoTIFF integration in the shared spatial workflow

### 6. Keep the project lightweight and efficient
As the toolkit grows, an ongoing goal is to keep it:

- lightweight
- understandable
- efficient
- maintainable

This includes:
- simplifying duplicated logic
- reducing unnecessary transformations
- centralizing shared spatial behavior where appropriate
- avoiding unnecessary requests or repeated work
- keeping geometry generation practical and efficient

---

## Near-Term Development Focus

### Core polish
- refine current source components
- improve consistency across outputs
- reduce rough edges in geometry behavior
- make source workflows feel more unified

### OSM refinement
- improve road surface generation
- improve output grouping
- improve figure-ground / black-plan usefulness
- evaluate clearly useful additional context outputs inside the existing OSM scope

### Terrain and elevation consistency
- continue improving terrain behavior
- keep the built-in terrain fallback broad and generic rather than tied to one local provider
- keep fallback terrain responsive by limiting it to small study areas, short request times, and clear failure messages
- support local GeoTIFF DEM paths as a first file-based terrain source, then evaluate Esri ASCII Grid or XYZ/CSV grids only if real test data makes that useful
- strengthen vertical consistency across terrain, buildings, and contextual outputs
- make elevation handling more robust without overcomplicating the workflow

### LoD2 diagnostics and provider compatibility
- keep the small internal WFS request buffer for provider BBOX edge cases
- support one `LoD2 Source` input for LoD2 WFS URLs, local CityGML/GML/XML files, local CityJSON files, folders, and ZIP archives
- keep local CityGML folder/ZIP loading bounded by Spatial Context where file bounds are available
- keep local CityGML performance honest: scan metadata first, filter buildings before conversion where possible, and report when large single files still dominate load time
- keep local Shapefile support in `Load WFS` focused on vector/source context first; evaluate whether a renamed future vector component is worth the Grasshopper compatibility cost later
- evaluate GeoPackage as a later local vector/project-data source once the dependency/parser choice is clear
- use the `Status` output to distinguish provider coverage gaps from conversion failures
- continue reducing invalid or duplicate LoD2 surfaces without inventing false building faces
- collect more cross-provider LoD2 examples before adding heavier repair or clipping behavior

### GeoTIFF maturation
- strengthen georeferenced raster behavior
- improve alignment and transparency handling
- make file-based raster workflows feel as coherent as service-based raster workflows

### UX and safety polish
- improve user trust through clear status behavior
- add safety checks where they are genuinely useful
- avoid overloading the interface
- keep the workflow simple even as functionality improves

---

## Mid-Term Priorities

### Broader provider testing
The toolkit has so far been tested only with a limited number of real sources.

A major mid-term priority is to:
- test more real-world providers
- test more service variants
- test more coordinate systems
- identify recurring incompatibilities
- improve compatibility carefully without making the codebase overly fragmented

### Documentation and presentation
As the core becomes more stable, the project should improve how it is explained and presented.

This includes:
- a stronger README
- clearer project scope documentation
- showcase video
- short tutorial material
- clearer release notes
- better public-facing project descriptions

### Fallback strategy improvement
A more deliberate fallback strategy should continue to evolve across source types.

This may include:
- stronger source hierarchy
- clearer fallback communication
- better generic imagery fallback behavior
- keep WMS request CRS selection tolerant of providers that advertise a usable alternate CRS rather than the Spatial Context's primary SRS
- more robust global terrain fallback behavior, with fast failure when a fallback cannot serve the selected context cleanly
- clearer use of OSM as a contextual fallback where richer official data is missing

### Internal cleanup and architecture polish
As the codebase matures, further work should continue on:
- removing duplicated logic
- simplifying source patterns
- strengthening shared spatial logic
- improving maintainability
- making the project easier to extend without bloating it

### Dependency and package lean-up
RhinoSpatial should stay careful about every dependency it ships.

Current dependency posture:
- keep `BitMiracle.LibTiff.NET` while GeoTIFF / TIFF raster support is active; replacing TIFF parsing with custom code would be high risk and low value
- keep `ProjNET` for CRS transformation until a clearly smaller, reliable replacement exists
- keep `SharpGLTF.Core` while the Google 3D Tiles viewer decodes GLB content; replacing glTF parsing with custom code would be fragile
- keep `NetTopologySuite` while OSM polygon cleanup/buffering/union and Shapefile geometry conversion depend on it
- keep `NetTopologySuite.IO.Esri.Shapefile` only in `RhinoSpatial.Core`; avoid duplicate top-level references in the Grasshopper UI project
- do not add GeoPackage or other broad source dependencies until there is real test data and a clear workflow need

Future lean-up checks:
- measure the actual Food4Rhino/Yak package size before each release
- keep third-party license notices current whenever a package is added, removed, or upgraded
- prefer small, well-tested internal code for simple parsing tasks, but avoid reimplementing complex spatial, TIFF, CRS, glTF, or topology libraries unless the dependency cost clearly outweighs the maintenance risk
- remove experimental references immediately if a format decision is deferred

---

## Long-Term Goals

### 1. Fully polished core toolkit
The main long-term goal is not unlimited feature expansion.
It is to make the core RhinoSpatial toolkit feel complete, reliable, lightweight, and coherent.

That means:
- the current source ecosystem should become stronger and more refined
- the user experience should feel direct and trustworthy
- the outputs should be immediately useful for real design workflows
- the project should remain simple enough to stay usable and understandable

### 2. Strong contextual modeling workflows
RhinoSpatial should become especially strong for:

- contextual site modeling
- studies
- concept work
- figure-ground / black-plan workflows
- quick spatial understanding inside Rhino and Grasshopper

### 3. Better universality through balanced source logic
Official sources should remain the preferred choice where available.
OSM, global terrain, and generic imagery fallbacks should continue to improve RhinoSpatial's broader usability as practical contextual fallbacks.

The long-term goal is a system that works well with rich official data where possible, while still remaining useful in more general contexts.

---

## Optional Viewer Policy

### Google Photorealistic 3D Tiles
Google Photorealistic 3D Tiles are present as an optional viewer, but only in a tightly limited form.

They should be treated as:

- optional
- advanced
- user-managed through the user's own API key and billing setup
- bounded by the selected Spatial Context
- clearly separate from official editable modeling data

They should not be treated as:

- a replacement for official editable data
- an authoritative project data source
- offline cached geometry
- a bake/export/import workflow
- a source for derived or extracted geometry

This means the Google 3D Tiles component should act only as a visual reference or contextual preview layer.
For preview completeness, it may include coarser parent tile content behind refined tiles when that avoids visible holes. Slight over-coverage is acceptable for this viewer; missing chunks inside the selected context are worse for the intended reference use.

It should not redefine the identity of the project.

---

## Explicit Non-Goals for Now

To keep the project focused, the following are **not** current priorities:

- turning RhinoSpatial into a full GIS desktop environment
- exposing every possible low-level source parameter
- turning Load OSM into a full OSM query editor
- endlessly expanding the number of source categories
- building a giant in-plugin resource finder before the core toolkit is polished
- prioritizing streamed 3D reference viewers over the core contextual workflow

---

## Guiding Development Principle

RhinoSpatial should continue to evolve by improving the quality of the current system, not by constantly widening the scope.

When prioritizing future work, prefer:

- robustness
- simplicity
- sensible defaults
- useful outputs
- aligned spatial behavior
- practical contextual modeling
- lightweight workflows
- polished documentation and presentation

over:

- excessive configurability
- unnecessary source expansion
- UI overload
- brittle complexity
- feature growth without stronger workflow value
