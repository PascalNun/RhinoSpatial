# RhinoSpatial Project Scope

This document records RhinoSpatial's product boundaries and design principles for contributors and technically interested users. For component instructions, supported formats, and source-specific behavior, see the [Component and Source Reference](COMPONENT_REFERENCE.md).

## Purpose

RhinoSpatial is a lightweight geospatial toolkit for Rhino and Grasshopper. It brings real site context into the design environment so architects and designers can work with maps, terrain, buildings, imagery, and vector data without first making a separate GIS detour.

The project began with official WFS planning and city data. It has grown into a broader contextual toolkit while keeping one simple organizing idea:

**one selected area, multiple aligned spatial layers**

That means:

- one shared area-selection workflow;
- one shared coordinate and placement context;
- multiple service and local-file source types;
- outputs that line up in the same Rhino and Grasshopper study space.

## Intended use

RhinoSpatial is primarily intended for:

- site analysis;
- context modeling;
- concept and feasibility studies;
- figure-ground and black-plan workflows;
- early-stage architectural, urban, and landscape design;
- combining official project data with lightweight reference context.

It should make common geospatial tasks direct and useful without pretending to replace a full GIS environment.

## Core source scope

The complete planned core source set is present:

| Source area | Role in the design workflow |
| --- | --- |
| `Spatial Context` | Selects the study area and defines shared placement |
| WFS/vector | Brings in planning, parcel, boundary, road, point, and other feature data |
| WMS | Provides maps, orthophotos, aerial imagery, and raster overlays |
| LoD2 Buildings | Provides official 3D building massing and roof geometry |
| Terrain | Provides ground and elevation context |
| GeoTIFF | Places local georeferenced raster data |
| OSM | Provides lightweight buildings, roads, water, green areas, and rail |
| Google 3D Tiles | Provides optional photogrammetric visual reference context |

This source scope is stable at the category level. Future work should improve robustness, provider compatibility, output quality, performance, examples, documentation, and day-to-day usability inside it.

No additional core source categories are currently planned.

## Product principles

### Design outcomes first

The first question should be what a user needs to do in a project—not which low-level service option can be exposed.

Components should describe recognizable tasks, use sensible defaults, and return geometry or imagery that is immediately useful in Rhino.

### One shared spatial context

Every aligned layer component should:

- consume the same `Spatial Context`;
- use common coordinate and relocation logic;
- respect the selected study area;
- align in XY and, where meaningful, share a consistent elevation baseline.

Connection or Grasshopper solution order must not change the final placement.

### Localized by default

Rhino works most reliably near its origin. RhinoSpatial therefore localizes geometry and imagery by default while preserving alignment between layers.

Absolute coordinates remain available when a project explicitly needs original source positions.

### Official data first, fallback context second

The preferred source hierarchy is:

1. user-provided and official project data;
2. broad contextual fallback data for orientation;
3. clear status communication whenever a fallback is used.

OSM, global terrain, and generic imagery make RhinoSpatial easier to try and more broadly useful. They should not be presented as equivalent to richer official project sources.

Fallbacks should remain bounded and responsive. If they cannot serve the selected context quickly and safely, they should fail with a useful message instead of making Grasshopper appear stuck.

### Complexity in the background

CRS transformation, request normalization, source filtering, and compatibility logic should be handled behind a simple component interface where possible.

Low-level controls should only become public inputs when a recurring real workflow demonstrates the need. Status outputs are usually a better place for provider and diagnostic detail.

### Independent but better together

Components should remain independently useful where intended. They may become more valuable when combined through one Spatial Context, but should not rely on fragile solve order or unnecessary component dependencies.

### Honest diagnostics

Provider behavior and geospatial files vary widely. Empty output should not be ambiguous.

Status messages should help distinguish:

- no provider coverage;
- no overlap with the selected study area;
- unsupported coordinate or response behavior;
- source or API access problems;
- skipped invalid geometry;
- conversion failures;
- fallback use or fallback limits.

The tone should remain clear and calm rather than exposing raw implementation detail without interpretation.

### Lightweight by design

RhinoSpatial should avoid:

- redundant transformations and repeated requests;
- unnecessary geometry conversion outside the selected context;
- dependencies without a demonstrated workflow benefit;
- duplicated source logic;
- UI growth that makes common use harder to understand.

Lightweight does not mean replacing mature TIFF, topology, CRS, Shapefile, or glTF libraries with fragile custom parsers.

## Source-specific boundaries

### Vector data

`Load WFS` retains its public name for compatibility with existing Grasshopper definitions, but its source category is broader than web-only WFS. It also supports OGC API Features, local Shapefiles, and local GeoJSON.

These sources remain general vector context. Shapefile and GeoJSON are not treated as LoD2 building imports.

### LoD2 buildings

LoD2 loading should remain conservative and diagnostic.

RhinoSpatial may buffer a bounded WFS request to reduce provider edge misses and may use file/building bounds to skip local geometry outside the Spatial Context. It should not invent false building faces simply to make every source appear closed.

The status output should separate provider coverage and filtering from geometry conversion problems.

### Terrain

Official or project-specific terrain remains the preferred path. The built-in global fallback exists for small-area orientation, not project-grade accuracy.

Fallback terrain must use conservative study-area and request-time limits and report clearly when it cannot provide usable samples.

### OSM

OSM is part of the core scope because it provides broadly available context where official data is missing or unnecessary.

Its intended outputs are:

- Buildings
- Road
- Water
- Green
- Rail

`Load OSM` should stay curated, practical, and useful by default. It should not become a full OSM query editor.

Refinements such as better road widths, grouping, building outputs, or figure-ground usefulness remain inside the current scope as long as they do not overload the interface.

### Google 3D Tiles

Google Photorealistic 3D Tiles are strictly a visual reference viewer.

The component is:

- bounded to the selected Spatial Context;
- accessed through the user's own Google Maps API key and billing setup;
- aligned with the other contextual layers;
- intended for orientation and visual comparison.

It is not:

- official or editable project data;
- a replacement for LoD2, terrain, or vector sources;
- an import, bake, extraction, or export workflow;
- an offline cache or authoritative dataset.

Small bounds padding and coarser parent fallback are acceptable when they improve visual completeness, but replacement parent and child detail levels should not be layered together.

Users remain responsible for Google Maps Platform terms, attribution, policy, API restrictions, and billing.

The viewer should not redefine RhinoSpatial's identity. The project remains an aligned site-context toolkit whose primary sources are official, open, or user-provided project data.

## Public component stability

Grasshopper component names, GUIDs, and common connection patterns should remain stable where possible. Renaming a component or changing its parameters can break or warn in existing definitions, so the compatibility cost must be justified by a meaningful UX improvement.

Broader source behavior may be added behind an existing component when the workflow remains coherent, as with vector files in `Load WFS` and local terrain files in `Load Terrain`.

## What RhinoSpatial should be

RhinoSpatial should be:

- a compact geospatial toolkit inside Rhino and Grasshopper;
- practical for architectural and urban site work;
- understandable without requiring a GIS-specialist interface;
- reliable enough to combine several real project layers;
- confident about what it supports and honest about what it cannot infer safely;
- open to technical depth without forcing that depth into the first user interaction.

## Explicit non-goals

RhinoSpatial should not become:

- a full GIS desktop application;
- a giant provider or dataset browser;
- a low-level expert interface with every possible service parameter;
- a general OSM query editor;
- a constantly expanding collection of unrelated source types;
- a system that hides fallback quality or invents plausible-looking spatial results;
- a Google 3D Tiles download, extraction, bake, or offline-use tool.

## Decision test

When considering implementation, interface, or roadmap changes, ask:

1. Does this solve a recurring design or site-context problem?
2. Does it preserve the shared Spatial Context and aligned-output promise?
3. Can complexity remain behind a simple, understandable workflow?
4. Does it strengthen the current source scope rather than expand it without evidence?
5. Can failure and source limitations be communicated honestly?
6. Is the maintenance or dependency cost justified by real workflow value?

When priorities conflict, prefer direct usability, aligned results, sensible defaults, and maintainability over configurability, scope growth, and GIS-style interface complexity.
