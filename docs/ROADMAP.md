# RhinoSpatial Roadmap

This roadmap is written for contributors and technically interested users. It describes what is already established, what is being improved now, and what may follow later. User instructions and supported-source details live in the [Component and Source Reference](COMPONENT_REFERENCE.md).

## Direction

RhinoSpatial has grown from a WFS-focused loader into a broader geospatial toolkit for Rhino and Grasshopper.

The core source scope is now defined:

- Spatial Context
- vector data through WFS, OGC API Features, Shapefile, and GeoJSON
- WMS imagery
- LoD2 buildings
- terrain
- GeoTIFF
- OSM context
- Google 3D Tiles reference viewing

No additional core source categories are currently planned. The priority is to make these workflows more dependable, coherent, efficient, and pleasant to use on real projects.

The guiding product idea remains:

**one selected area, multiple aligned spatial layers**

## Current baseline

The current alpha line already provides:

- one shared `Spatial Context` with saved area selection;
- optional map orientation from WFS, WMS, WCS, and supported local sources;
- a neutral world view instead of an implicit Frankfurt default;
- localized placement near Rhino's origin or optional absolute coordinates;
- vector loading from WFS, OGC API Features, Shapefile, and GeoJSON;
- WMS imagery with an ordered orientation fallback;
- terrain from WCS, GeoTIFF DEM, Esri ASCII Grid, XYZ/CSV grids, and a bounded global fallback;
- LoD2 buildings from WFS, CityGML/GML/XML, CityJSON, folders, and ZIP archives;
- GeoTIFF raster placement;
- curated OSM buildings, roads, water, green areas, and rail;
- bounded Google Photorealistic 3D Tiles reference viewing;
- a shared elevation baseline that does not depend on Grasshopper solve order;
- public examples, a visual workflow, a user guide, a tested-source catalogue, and release validation checks.

These are the starting conditions for future work, not unfinished roadmap promises.

## Now: real-use stabilization

The most useful next improvements should come from real project use rather than speculative expansion.

### Core workflow consistency

- collect recurring friction from complete study workflows;
- keep source components consistent in naming, status behavior, grouping, and failure handling;
- protect XY and Z alignment across all source combinations;
- improve persistence and state restoration where real definitions expose problems;
- prefer a small number of high-value fixes over a large speculative issue list.

### Provider compatibility

- test more bounded, real feature/image/coverage requests rather than capabilities documents alone;
- add compatibility fixes only when a real provider or file demonstrates the need;
- keep axis-order, CRS, HTTPS, service-version, and response-format handling centralized where possible;
- return useful diagnostics instead of silently producing empty or misplaced geometry.

### LoD2 reliability

- distinguish provider coverage gaps from filtering or conversion failures;
- retain the small internal WFS request buffer for BBOX edge cases;
- keep folder and ZIP loading bounded by file/building metadata where available;
- improve invalid or duplicate surface handling conservatively;
- avoid inventing false faces merely to force closed solids;
- continue collecting CityGML and CityJSON examples across providers and coordinate systems.

### Documentation freshness

- keep the README useful to architects and designers before introducing implementation details;
- keep exact formats, coordinate behavior, fallbacks, and diagnostics in the component reference;
- refresh screenshots when public component labels change;
- keep the website, Food4Rhino, GitHub, Package Manager, and release notes consistent;
- update examples and the tested-source catalogue whenever supported behavior changes.

## Next: focused quality improvements

### OSM context

`Load OSM` should remain curated and lightweight rather than becoming a full query editor.

Useful refinements include:

- better road width and surface interpretation;
- stronger figure-ground and black-plan outputs;
- clearer grouping;
- improved building geometry where source data supports it;
- selected additional contextual outputs only when they remain broadly useful and do not overload the interface.

### Raster and imagery

- improve transparency and alpha handling;
- keep WMS materials and GeoTIFF materials consistent;
- improve raster alignment diagnostics;
- keep the WMS fallback order useful and clearly reported;
- continue testing providers that require an alternate advertised request CRS.

### Terrain and elevation

- strengthen vertical consistency across terrain, LoD2 buildings, and Google reference meshes;
- keep the global fallback small, quick, and honest about its limitations;
- improve WCS and local DEM error reporting;
- avoid repeated transformations or requests that do not improve the result.

### Performance and maintainability

- reduce duplicated request, transform, filtering, and placement logic;
- centralize shared spatial behavior without hiding the workflow;
- avoid unnecessary requests and geometry conversion outside the selected context;
- measure package size and runtime cost before adding dependencies;
- keep Grasshopper components small even when the underlying source handling becomes more capable.

## Later: carefully bounded candidates

Later work should still strengthen the existing source categories rather than widen the product indiscriminately.

Possible candidates include:

- GeoPackage as an additional local vector/project-data format after a deliberate SQLite and geometry-parser decision;
- stronger CityJSON coverage after more representative real sources are tested;
- Cloud Optimized GeoTIFF only after remote range requests, caching, and policy behavior are designed;
- optional output refinements that clearly improve architectural site workflows.

These are candidates, not commitments or release blockers.

## Dependency posture

RhinoSpatial should remain careful about each shipped dependency, but mature spatial libraries should not be replaced by fragile custom code simply to reduce the file count.

Current posture:

- keep `BitMiracle.LibTiff.NET` while TIFF and GeoTIFF support is active;
- keep `ProjNET` for coordinate transformations until a clearly smaller, reliable replacement exists;
- keep `SharpGLTF.Core` while the Google viewer decodes GLB content;
- keep `NetTopologySuite` for OSM topology, buffering, unions, and Shapefile geometry;
- keep `NetTopologySuite.IO.Esri.Shapefile` only in `RhinoSpatial.Core`;
- do not add broad format dependencies without real test data and a clear workflow need.

Before each release:

- measure the actual Food4Rhino and Yak package;
- confirm the included DLLs are intentional;
- keep third-party notices current;
- remove deferred experimental references;
- prefer small internal parsers only for genuinely simple formats.

## Explicit non-goals

RhinoSpatial is not currently trying to:

- become a full GIS desktop environment inside Grasshopper;
- expose every low-level service parameter;
- turn `Load OSM` into a general OSM query editor;
- build a giant in-plugin provider catalogue;
- continually add unrelated source categories;
- prioritize streamed 3D reference viewing over the editable contextual workflow;
- turn Google 3D Tiles into an import, bake, extraction, export, or offline-cache path.

## Long-term outcome

The long-term goal is a compact toolkit that feels trustworthy in everyday design work:

- choose an area once;
- connect the sources available for the project;
- receive useful geometry, imagery, and terrain in one aligned Rhino space;
- understand clearly when a provider, file, coordinate system, or fallback limits the result.

When priorities compete, prefer robustness, simplicity, aligned outputs, sensible defaults, and practical contextual modeling over configurability, scope growth, and UI complexity.
