# Roadmap

## Overview

RhinoSpatial has moved beyond its original starting point as a WFS-focused loader and is now developing into a broader geospatial toolkit for Rhino and Grasshopper.

At this stage, the core source scope is considered defined at the category level.  
The main focus is no longer to constantly add new categories of sources, but to strengthen, refine, simplify, and polish the current system.

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

### 2. Improve OSM as a contextual fallback
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

### 3. Improve reliability and persistence
The toolkit should feel dependable in day-to-day use.

Near-term reliability improvements may include:
- better persistence of Spatial Context
- cleaner state restoration when files are reopened
- stronger handling of fallback behavior
- better status messages
- better timeout / failure handling
- fewer fragile edge cases

### 4. Improve raster and imagery workflows
Raster handling is now part of the project through WMS and GeoTIFF.

This area should continue to improve, including:
- better fallback imagery strategies
- stronger raster alignment behavior
- transparency / alpha handling
- more consistent image/material behavior
- stronger GeoTIFF integration in the shared spatial workflow

### 5. Keep the project lightweight and efficient
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
- strengthen vertical consistency across terrain, buildings, and contextual outputs
- make elevation handling more robust without overcomplicating the workflow

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
- more robust terrain fallback planning
- clearer use of OSM as a contextual fallback where richer official data is missing

### Internal cleanup and architecture polish
As the codebase matures, further work should continue on:
- removing duplicated logic
- simplifying source patterns
- strengthening shared spatial logic
- improving maintainability
- making the project easier to extend without bloating it

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
OSM should continue to improve RhinoSpatial's broader usability as a practical contextual fallback.

The long-term goal is a system that works well with rich official data where possible, while still remaining useful in more general contexts.

---

## Later Optional Goal

### Google 3D Tiles reference importer
A Google 3D Tiles importer is considered a legitimate later goal.

However, it should be understood as:

- optional
- advanced
- later
- user-managed through the user's own API key and billing setup
- reference-oriented rather than part of the core modeling logic

This is a real future goal, but it is not part of the current core functionality.

It should not redefine the identity of RhinoSpatial.
If added later, it should remain clearly separate from the core source system and should act as an advanced visual reference layer.

---

## Explicit Non-Goals for Now

To keep the project focused, the following are **not** current priorities:

- turning RhinoSpatial into a full GIS desktop environment
- exposing every possible low-level source parameter
- turning Load OSM into a full OSM query editor
- endlessly expanding the number of source categories
- building a giant in-plugin resource finder before the core toolkit is polished
- prioritizing photorealistic streaming sources over the core contextual workflow

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
