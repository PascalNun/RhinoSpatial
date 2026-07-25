# Component and Source Reference

This page is the detailed reference for RhinoSpatial components, supported source formats, placement behavior, fallbacks, and diagnostics. For a shorter introduction, start with the [README](../README.md) or the [visual workflow guide](SHOWCASE.md).

RhinoSpatial is organized around one rule:

**one selected area, multiple aligned spatial layers**

Every loader consumes the same `Spatial Context`, so maps, vector data, terrain, buildings, rasters, OSM context, and visual reference layers can share one Rhino and Grasshopper study space.

## Component groups

The Grasshopper tab is organized as:

- `Context`
  - `Spatial Context`
- `Layers`
  - `List WFS Layers`
  - `List WMS Layers`
- `Sources`
  - `Load WFS`
  - `Load WMS`
  - `Load LoD2 Buildings`
  - `Load Terrain`
  - `Load GeoTIFF`
  - `Load OSM`
- `Viewers`
  - `3D Tiles Viewer (Google)`

## Spatial Context

`Spatial Context` is the shared area selector and placement context for the whole toolkit.

### Core workflow

1. Connect a Button to `Open Map`.
2. Open the browser helper.
3. Draw or resize one rectangular study area.
4. Return to Grasshopper.
5. Connect the resulting `Spatial Context` to each loader.

The selected area is saved with the Grasshopper definition. When the helper is opened again, a saved selection takes priority over newly connected reference metadata.

### Inputs

- `SRS`
  Optional manual coordinate-system override. Enter a value such as `EPSG:25832` when the project coordinate system is already known.
- `Reference Source`
  Optional service URL, local file, folder, or supported ZIP archive used only to infer an initial map extent and coordinate system.
- `Reference Layer`
  Optional single service layer or coverage name. Local files do not normally need it. If multiple entries are connected, RhinoSpatial uses the source-wide extent instead.
- `Open Map`
  Opens the integrated area-selection helper. Connect a Grasshopper Button.
- `Use Absolute Coordinates`
  Keeps original source coordinates when `True`. The default `False` localizes data near the Rhino origin.

The component returns one `Spatial Context` output for all aligned source and viewer components.

### Reference Source behavior

`Reference Source` helps the map open near the project. It does not import the referenced geometry, imagery, buildings, or terrain.

Supported reference metadata includes:

- WFS, WMS, and WCS service URLs
- GeoTIFF
- Shapefile
- GeoJSON
- CityJSON
- CityGML/GML/XML
- Esri ASCII Grid
- XYZ/CSV terrain grids
- folders containing supported files
- LoD-oriented ZIP archives containing GML/XML/GeoJSON/CityJSON

The startup order is deterministic:

1. restore a saved map selection;
2. otherwise fit a readable `Reference Source` extent;
3. otherwise show a neutral world view.

If no project coordinate system can be inferred, the component reports that `EPSG:3857` is only a global fallback. It does not silently assume Frankfurt or another local project CRS.

### Localized and absolute placement

Localized placement is the default because very large real-world coordinates can cause display and modeling problems in Rhino. All sources connected to one context use the same relocation logic and remain aligned near the origin.

Enable `Use Absolute Coordinates` only when the definition needs the original source coordinates. Absolute mode keeps real XY and Z coordinates where the source provides them.

Terrain, LoD2 buildings, and localized Google 3D Tiles participate in one shared elevation baseline. The component that solves first may establish that baseline, but connection or Grasshopper solution order must not change the final vertical alignment.

## Layer discovery

### List WFS Layers

Connect a WFS service URL to inspect the layers advertised by its `GetCapabilities` response. Each output entry combines the service layer name and title. Use `List Item` to select one layer, or explicitly merge several entries for a multi-layer WFS request.

Public interface:

- input: `WFS URL`
- output: `Layer` list in `name | title` form

### List WMS Layers

Connect a WMS service URL to inspect its requestable map layers. Use `List Item` to choose the layer needed by `Load WMS`.

Public interface:

- input: `WMS URL`
- output: `Layer` list in `name | title` form

Layer discovery is a service workflow. Local vector and raster files can normally be connected directly to their loader without using a list component.

## Load WFS: vector and feature data

Use `Load WFS` for parcels, planning layers, boundaries, roads, building footprints, points, and other feature-based vector data.

The public component name is retained for Grasshopper compatibility, but the accepted source category is broader than WFS:

- WFS service URLs
- OGC API Features collection or items URLs
- local Shapefiles (`.shp`)
- local GeoJSON (`.geojson` or GeoJSON `.json`)

Public inputs:

- `WFS URL` — service URL, OGC API Features URL, or local vector-file path
- `Layer` — one or more WFS layer entries; optional for local files and OGC API Features
- `Max Features` — request limit; the default `0` requests all available features within the bounded query
- `Spatial Context` — the shared selected area and placement context

The `Geometry` output is a tree grouped by layer and feature.

### Typical WFS workflow

1. Connect the service URL to `List WFS Layers`.
2. Choose one layer with `List Item`, or merge only the layers you need.
3. Define the area with `Spatial Context`.
4. Connect the service URL, chosen layer, and shared context to `Load WFS`.

For local vector files, connect the file path directly to `WFS URL`. `Layer` can remain empty and is used only as a label when provided. OGC API Features URLs can also be connected directly; RhinoSpatial requests GeoJSON features for the context bounding box.

`Max Features` limits the request when a provider supports it. Use `0` to request all features available within the bounded query, but keep broad or dense study areas conservative.

### Outputs and parsing

- polygon and line features become Rhino curves;
- point features become Rhino points;
- geometry is grouped by layer and feature in the output tree.

RhinoSpatial prefers GeoJSON where available and falls back to GML where needed. Local GeoJSON defaults to `EPSG:4326` unless it contains a readable older named `crs` member.

The compatibility-oriented WFS path retains established EPSG:4326 XY behavior and handles confirmed EPSG:4258 latitude/longitude services explicitly. Provider-specific axis order, geometry encodings, and response formats can still require compatibility work.

## Load WMS: maps and imagery

Use `Load WMS` for orthophotos, aerial imagery, map layers, and raster overlays delivered by a Web Map Service.

### Typical workflow

1. Connect a WMS URL to `List WMS Layers`.
2. Choose one requestable layer.
3. Define the area with `Spatial Context`.
4. Connect the WMS URL, layer, format, and context to `Load WMS`.

One component requests one WMS layer. Use several components when multiple image layers should remain explicit.

Public inputs:

- `WMS URL` — optional service URL; leave empty for the built-in fallback sequence
- `Layer` — optional requestable layer name; leave empty to let RhinoSpatial choose a usable layer
- `Spatial Context` — the shared selected area and placement context
- `Format` — requested image MIME type; defaults to `image/png`

### Outputs

- an aligned image mesh;
- a Rhino display material with the downloaded image attached;
- the cached image path;
- the final `GetMap` request URL.

If the URL is empty, RhinoSpatial uses an ordered orientation fallback: it first tries a sharper global OpenStreetMap-style WMS and then broader NASA GIBS imagery. Fallback imagery is quick context, not a substitute for official or project-specific imagery.

When a layer does not support the context's primary SRS but advertises another coordinate system already known by the context, RhinoSpatial may request the supported alternative and place the result back into the same local study area.

WMS 1.3 geographic requests use the authority axis order required by the standard.

## Load GeoTIFF: local georeferenced rasters

Use `Load GeoTIFF` for a local georeferenced image such as an orthophoto, map export, satellite image, or other raster that should align with the selected study area.

Connect a `.tif` or `.tiff` path and the shared `Spatial Context`.

Public inputs:

- `GeoTIFF` — local georeferenced raster path
- `Spatial Context` — the shared selected area and placement context

Outputs include:

- an aligned image mesh;
- a display material with the raster attached;
- the source image path;
- a status message.

The file must contain readable geospatial metadata. If RhinoSpatial cannot determine the coordinate system or the raster does not overlap the selected context, it returns a clear warning rather than guessing a placement.

## Load Terrain: elevation and ground surfaces

Use `Load Terrain` to create ground geometry aligned with the same study area as imagery, vectors, and buildings.

Supported sources include:

- WCS services
- local GeoTIFF DEM (`.tif` or `.tiff`)
- Esri ASCII Grid (`.asc`)
- regular XYZ or CSV point grids with x, y, z columns
- the built-in quick global land-elevation fallback

Public inputs:

- `Terrain URL` — WCS URL or local DEM/grid path; leave empty for the quick global fallback
- `Coverage` — optional WCS coverage id or EPSG override for local text grids
- `Spatial Context` — the shared selected area and placement context

Outputs are the aligned `Terrain` mesh list and a `Status` message.

`Coverage` identifies a WCS coverage where required. For local text grids it can also provide an EPSG override such as `EPSG:25832`; otherwise RhinoSpatial uses the current context SRS.

The built-in fallback is intended for small-area orientation and short request times. It fails quickly with a useful status when the area is too large, no usable elevation samples are available, or the request cannot complete promptly. Project work should prefer official or project-specific terrain.

The WCS path supports ordinary single-part GeoTIFF responses and multipart GeoTIFF responses. Where capabilities omit a WGS84 extent, RhinoSpatial can use `DescribeCoverage` metadata to orient the context.

## Load LoD2 Buildings: official 3D building context

Use `Load LoD2 Buildings` for official building massing, roof forms, and 3D urban context.

One `LoD2 Source` input accepts:

- a LoD2 WFS service URL
- local CityGML/GML/XML
- local CityJSON
- a folder containing supported building files
- a ZIP archive containing supported building files

For WFS sources, select a building layer when needed. For local data, `Layer` can stay empty or be used as an output label.

Public inputs:

- `LoD2 Source` — WFS URL, supported local file, folder, or ZIP path
- `Layer` — optional WFS layer name or local output label
- `Spatial Context` — the shared selected area and placement context

Outputs are `Buildings` as a tree grouped by layer and building, plus `Status`.

### Filtering and performance

WFS mode requests a small buffered area to reduce provider BBOX edge misses, then keeps buildings that intersect the actual Spatial Context.

Local folder and ZIP workflows inspect file-level and building-level bounds where available. Files or buildings outside the selected context can be skipped before expensive Rhino geometry conversion. Very large single files may still take longer because they must first be inspected locally.

### Outputs and diagnostics

Buildings are converted to Breps grouped by layer and building. The `Status` output distinguishes:

- returned and kept buildings;
- request, query, source, and output bounds;
- converted and skipped surfaces;
- provider coverage gaps;
- local file/context mismatches;
- duplicate or invalid source surfaces;
- conversion failures.

LoD2 processing remains conservative: RhinoSpatial should not invent missing building faces simply to force every source into a closed solid.

## Load OSM: lightweight contextual geometry

Use `Load OSM` for fast OpenStreetMap context and figure-ground, black-plan, or general site studies.

The component uses the Overpass API. Its source URL input is an advanced endpoint override; leave it empty for the built-in RhinoSpatial source.

Public inputs are:

- `OSM URL` — optional advanced Overpass endpoint override
- `Spatial Context` — the shared selected area and placement context
- `Buildings` — enabled by default
- `Road` — enabled by default
- `Water` — disabled by default
- `Green` — disabled by default
- `Rail` — disabled by default

Available geometry outputs are:

- `Buildings`
- `Road`
- `Water`
- `Green`
- `Rail`

The component also returns a `Status` message.

OSM is community-maintained and should be treated as contextual reference unless the project explicitly accepts it as a source. The component is intentionally curated and should not become a full OSM query editor.

## 3D Tiles Viewer (Google)

`3D Tiles Viewer (Google)` displays bounded Google Photorealistic 3D Tiles as visual reference context.

It requires:

- the user's own Google Maps Platform project;
- an enabled Map Tiles API;
- a user-managed API key and billing setup;
- the shared `Spatial Context`;
- `Enable` set to `True`.

Public inputs are `API Key`, `Spatial Context`, and `Enable`. Outputs are `Status`, `Active`, `Meshes`, and index-aligned `Materials`.

The component requests and decodes temporary preview meshes and materials for the current Grasshopper session. It does not provide baking, export, extraction, offline caching, or reuse as editable project geometry.

### Detail selection and alignment

RhinoSpatial selects one coherent refinement frontier for the study area so replacement parent and child tiles are not shown together. If a refined branch cannot be decoded, the entire affected branch can fall back to its nearest usable parent. Small bounds padding is acceptable when it avoids missing visual chunks around the selected area.

Decoded tile attribution is displayed on the component and included in its status. Projection-frame, bounds, traversal, candidate, and parent-fallback diagnostics help separate provider coverage, decode, and placement problems.

In localized mode, usable Google mesh vertices can establish or reuse the same shared elevation baseline as terrain and LoD2 buildings. Component connection order must not create a vertical separation.

The viewer is subject to the current [Google Maps Platform Terms of Service](https://cloud.google.com/maps-platform/terms), [Map Tiles API Policies](https://developers.google.com/maps/documentation/tile/policies), and [usage and billing rules](https://developers.google.com/maps/documentation/tile/usage-and-billing). Users are responsible for API restrictions, attribution, billing, and policy compliance.

## Coordinate systems and alignment

RhinoSpatial tries to use a source layer's default coordinate system when possible and transforms supported source data into the shared context.

The map helper currently supports the project-selection SRS values encountered most often in testing, including:

- `EPSG:4326`
- `EPSG:25832`
- `EPSG:25833`
- `EPSG:3857`
- `EPSG:27700`
- `EPSG:4283`
- `EPSG:7423`
- `EPSG:7844`

Source alignment also recognizes Dutch RD New `EPSG:28992` and its NAP compound form `EPSG:7415`. These can transform into a supported shared context without becoming map-helper project-selection options.

CRS support is practical rather than exhaustive. When a provider, format, or project coordinate system cannot be handled safely, the preferred behavior is a clear status message rather than a plausible-looking but incorrect placement.

## Source priority and fallback policy

RhinoSpatial generally prefers:

1. user-provided and official project data;
2. broad contextual fallback data for orientation;
3. clear communication whenever fallback behavior is active.

Fallback data should not be presented as equivalent to richer official sources. It should remain bounded and responsive; when a fallback cannot serve the selected context cleanly, it should fail with useful status text instead of leaving Grasshopper apparently stuck.

For representative providers, known limitations, and regression notes, see the [Test Source Catalogue](TEST_SOURCES.md). For release checks, see the [Validation Checklist](../examples/VALIDATION.md).
