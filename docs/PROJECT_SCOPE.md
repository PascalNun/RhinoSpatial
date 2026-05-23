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
- **3D Tiles Viewer (Google)** as an optional viewer, not a normal source/import workflow

These form the core source ecosystem of the project and are considered stable at the source-category level. The `v0.3.1-alpha` release continues the first complete alpha line where this planned source set is present together; future work should mainly refine robustness, quality, provider compatibility, examples, and documentation inside this scope.

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

`Load WFS` also accepts local `.shp` files as vector sources. The public component name is kept stable for Grasshopper compatibility, but the intended source category is broader: feature/vector data that can be aligned to the shared Spatial Context.

### WMS
Imagery, orthophoto, and map context delivered as web map services.

Typical use cases:
- orthophotos
- map overlays
- contextual raster imagery

If no explicit WMS URL is connected, `Load WMS` may use an ordered fallback map/imagery sequence for quick orientation. Fallback imagery should be clearly reported as fallback context and should never be presented as equivalent to official project imagery.

When a WMS layer does not support the Spatial Context's primary SRS but does advertise another CRS already available in the context, RhinoSpatial may request the supported CRS and place the image back into the same local study area. This keeps international WMS providers more usable without asking users to manage low-level CRS details.

### LoD2 Buildings
Official building massing / roof geometry context where available.

Typical use cases:
- more accurate 3D building context
- roof forms
- official building geometry in study models

LoD2 loading should stay conservative and diagnostic. The component may request a small buffered area to reduce WFS BBOX edge misses, but the user-facing study area remains the Spatial Context. It should accept one LoD2 source input that can be a WFS URL, local CityGML/GML/XML file, local CityJSON file, folder, or ZIP archive, using the same Spatial Context filtering, Brep conversion, and elevation alignment workflow across service-based and local LoD2. Status output should make it clear whether a gap is likely provider coverage, WFS filtering, local file/context mismatch, skipped out-of-context local tiles, duplicate source surfaces, or RhinoSpatial conversion failure.

### Terrain
Ground surface / elevation / terrain geometry.

Typical use cases:
- terrain meshes
- site base surfaces
- contextual ground reference for buildings and other layers

The preferred terrain path is still user-provided or official project data where available. This can be an explicit WCS service, a local `.tif` / `.tiff` GeoTIFF DEM file, a local `.asc` Esri ASCII Grid, or a regular `.xyz` / `.csv` terrain grid. The built-in global land-elevation fallback exists to make first tests and general context workflows easier, not to replace better local datasets.

The fallback should stay conservative: small study areas, short request times, clear failure messages, and no hidden claim of project-grade accuracy. If the selected area is too large or the fallback source has no usable samples, the component should fail quickly and tell the user to connect an explicit terrain source.

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

### 3D Tiles Viewer (Google)
Optional user-managed contextual 3D viewer for Google Photorealistic 3D Tiles.

Typical use cases:
- visual urban context
- quick massing/orientation checks
- comparing official editable layers with broad photogrammetric surroundings

The component requires the user's own Google Maps API key. It is a reference viewer workflow and should not be presented as an official editable data source, import/export path, or offline cache.

The viewer may keep coarser parent tile content available behind finer tiles when Google tile refinement leaves visible holes. This is an intentional reference-preview tradeoff: slight over-coverage is acceptable, but under-loading missing chunks inside the selected context is not useful for visual checking.

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

1. **user-provided and official project data first**
2. **practical broad fallback second**
3. **clear status communication**
4. **no misleading assumptions about data quality**

This means:
- official sources remain the ideal where available
- OSM, global terrain, fallback imagery, and similar strategies improve general usability
- fallback data should not be presented as equivalent to richer official data
- the user should be able to understand when fallback behavior is being used
- fallbacks should be responsive; if they cannot load quickly and clearly, they should fail with useful status text instead of making Grasshopper feel stuck

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
- optional Google 3D Tiles viewing

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

No additional core source categories are currently planned.

The current priority is to make the existing system feel more complete, dependable, and polished inside its present scope.

## Policy Note: Google 3D Tiles

Google Photorealistic 3D Tiles are available in RhinoSpatial as an optional viewer component.

This feature should be treated as:

- optional
- advanced
- user-managed through the user's own API key and billing setup
- bounded to the selected Spatial Context
- clearly separate from official editable project data

Important:
Google 3D Tiles should not be treated as a normal import source.

RhinoSpatial should not use Google Photorealistic 3D Tiles as:
- a replacement for official editable source data
- an offline authoritative dataset
- offline cached geometry
- a bake/export workflow
- a source for derived or extracted geometry

The intended role of Google 3D Tiles in RhinoSpatial is only:
- visual reference
- contextual preview
- optional advanced background layer

Users are responsible for their own Google Maps Platform project, billing, API key, and compliance with the current Google Maps Platform terms and Map Tiles API policies.

This feature should not redefine the identity of RhinoSpatial.
The core identity remains a lightweight, study-oriented geospatial toolkit built around Spatial Context, WFS, WMS, LoD2 Buildings, Terrain, GeoTIFF, and OSM.

## Current Planning Position

The core source scope of RhinoSpatial is considered to be sufficiently defined.

Future work should primarily focus on:

- refining and strengthening the existing source types
- improving robustness and consistency
- improving documentation and communication
- improving service resilience and day-to-day usability
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
