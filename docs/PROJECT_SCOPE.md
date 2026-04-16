# Project Scope

## Overview

RhinoSpatial is a simple, study-oriented geospatial toolkit for Rhino and Grasshopper.

Its purpose is to bring spatial data directly into the design environment, so users can work with real site context inside Rhino and Grasshopper without first needing to go through a separate GIS workflow.

The project started from a practical workflow need: loading official geodata such as WFS-based planning and city data directly into Rhino and Grasshopper in a way that feels usable for design work. From that starting point, it has grown into a broader contextual toolkit for working with aligned geospatial layers in the same study space.

## Core Idea

The core idea of RhinoSpatial is:

**one selected area, multiple aligned spatial layers**

This means:
- one shared spatial selection workflow
- one shared placement / relocation logic
- multiple source types that can be loaded into the same Rhino / Grasshopper context
- outputs that align spatially and are useful for studies, concept work, and early-stage design

## Project Goals

RhinoSpatial is intended to:

- make spatial data easier to use directly inside Rhino and Grasshopper
- reduce the need for a separate GIS detour in common design workflows
- support contextual modeling with official and open geospatial data
- keep common workflows simple and practical
- provide useful outputs with sensible defaults
- stay lightweight and understandable rather than overloaded

## Design Philosophy

RhinoSpatial is designed around:

- simple workflows
- sensible defaults
- aligned outputs
- minimal UI overload
- practical usefulness in design workflows
- complexity handled in the background where possible

The goal is not to expose every possible parameter or to recreate a full GIS application inside Grasshopper.

The goal is to make common geospatial tasks feel direct, usable, and reliable inside the design environment.

## Core Workflow Model

The intended user workflow is:

1. Define an area with **Spatial Context**
2. Inspect available layers where needed
3. Load one or more aligned source types
4. Work with the combined result directly in Rhino / Grasshopper

This workflow should remain the core organizing logic of the project.

## Core Functionality

The following source / component types are considered part of the core RhinoSpatial scope:

- **Spatial Context**
- **List WFS Layers**
- **List WMS Layers**
- **Load WFS**
- **Load WMS**
- **Load LoD2 Buildings**
- **Load Terrain**
- **Load GeoTIFF**
- **Load OSM**

These form the core source ecosystem of the project and are considered stable at the source-category level.

## Meaning of the Core Source Types

### Spatial Context
The shared spatial picker and placement context for the whole toolkit.

It defines the selected area and the common spatial reference for all aligned outputs.

### WFS
Official vector data and similar feature-based geospatial services.

Typical use cases:
- planning data
- parcels
- building footprints
- roads
- administrative or thematic vector layers

### WMS
Imagery, orthophoto, and map context delivered as web map services.

Typical use cases:
- orthophotos
- map overlays
- contextual raster imagery

### LoD2 Buildings
Official building massing / roof geometry context where available.

Typical use cases:
- more accurate 3D building context
- roof forms
- official building geometry in study models

### Terrain
Ground surface / elevation / terrain geometry.

Typical use cases:
- terrain meshes
- site base surfaces
- contextual ground reference for buildings and other layers

### GeoTIFF
Georeferenced raster files aligned to the same shared spatial workflow.

Typical use cases:
- local raster datasets
- georeferenced images
- file-based raster context

### OSM
Lightweight, curated contextual urban geometry.

Typical use cases:
- building context
- roads
- water
- green/open areas
- rail
- fast black-plan / site-context style studies

## OSM Scope

OSM is part of the core scope because it makes RhinoSpatial more widely usable across many regions.

Official geodata is often more precise and richer where available, but OSM provides a practical, broadly available contextual fallback.

The intended OSM outputs are:

- **Buildings**
- **Road**
- **Water**
- **Green**
- **Rail**

Buildings are the highest priority.  
The rest should support contextual modeling, figure-ground studies, black-plan style workflows, and general site understanding.

Important:
Load OSM should **not** become a full OSM query editor or an overloaded low-level GIS interface.

It should remain:
- curated
- lightweight
- practical
- useful by default

Within this scope, OSM can still be refined further where helpful, for example:
- improving geometry quality
- improving road outputs
- improving black-plan usefulness
- adjusting output grouping
- adding clearly useful additional context outputs if they strengthen the workflow without overloading the UI

Examples of possible future refinements inside the OSM scope may include:
- better road width interpretation
- better green grouping
- black-plan oriented output refinements
- selected additional contextual outputs such as hedges, if they prove useful and still fit the lightweight design philosophy

These kinds of improvements are considered **refinement within the core scope**, not a change of scope.

## Source Hierarchy and Data Logic

RhinoSpatial should generally prefer:

1. **official / local source first**
2. **practical fallback second**
3. **clear status communication**
4. **no misleading assumptions about data quality**

This means:
- official sources remain the ideal where available
- OSM and other fallback strategies improve general usability
- fallback data should not be presented as equivalent to richer official data
- the user should be able to understand when fallback behavior is being used

## Important Technical Principles

### Shared Spatial Logic
The project is centered around a shared spatial context.

All aligned source components should:
- consume the same spatial context
- use the same placement / relocation logic
- produce outputs that align correctly in Rhino / Grasshopper

### Independent Usability
Components should work independently where intended.

They may become better together, but they should not feel fragile or unnecessarily dependent on other components.

### Sensible Output Behavior
The toolkit should prefer useful outputs with sensible default behavior.

Examples:
- default local placement near the Rhino origin
- practical fallback dimensions where source data is incomplete
- usable geometry even when source data is imperfect
- robust context generation over excessive configurability

### Lightweight by Design
The project should remain as lightweight and efficient as reasonably possible.

This means:
- avoiding unnecessary complexity
- avoiding redundant logic
- reducing duplicated transformations and request handling
- keeping source workflows understandable
- improving performance where it strengthens usability without harming clarity

## What RhinoSpatial Should Be

RhinoSpatial should be:

- a simple geospatial toolkit
- directly usable inside Rhino and Grasshopper
- focused on contextual site modeling
- useful for studies, concept work, and early-stage design
- practical, not overloaded
- confident, but grounded in a real workflow need

## What RhinoSpatial Should Not Become

RhinoSpatial should not become:

- a full GIS desktop application inside Grasshopper
- a giant multi-provider data browser
- an overloaded expert interface with excessive low-level controls
- a tool that forces unnecessary setup and friction
- a project that constantly expands by adding more and more unrelated source types

## Scope Boundaries

The meaningful long-term core scope of RhinoSpatial is currently considered to be:

- Spatial Context
- WFS
- WMS
- LoD2 Buildings
- Terrain
- GeoTIFF
- OSM

At this point, the project should focus more on:

- robustness
- consistency
- simplification
- performance
- fallback quality
- documentation
- UX polish
- source refinement inside the current scope

rather than continuously expanding the number of fundamental source categories.

## Later Goal: Google 3D Tiles

A Google 3D Tiles reference importer is considered a legitimate **later goal**, but **not part of the current core functionality**.

It should be understood as:

- optional
- later
- advanced
- user-managed via the user's own API key / billing setup
- reference-oriented rather than core modeling logic

Google 3D Tiles should not redefine the identity of RhinoSpatial.

If added later, it should remain:
- clearly separated from the core source model
- explicit in its dependencies
- optional in use
- secondary to the core contextual workflow

## Current Planning Position

The core source scope of RhinoSpatial is considered to be sufficiently defined.

Future work should primarily focus on:

- refining and strengthening the existing source types
- improving robustness and consistency
- improving documentation and communication
- keeping the toolkit lightweight and design-friendly
- polishing the current workflow rather than continuously expanding it

## Working Principle for Future Decisions

When making future implementation, architecture, or roadmap decisions, prefer:

- direct usability
- simplicity
- sensible defaults
- aligned outputs
- practical contextual modeling
- lightweight workflows

over:

- excessive configurability
- unnecessary scope growth
- hidden complexity
- GIS-style overload
