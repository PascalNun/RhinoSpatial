# Component and Source Reference

This page is the detailed reference for RhinoSpatial components, supported source formats, placement behavior, fallbacks, and diagnostics. For a shorter introduction, start with the [README](../README.md) or the [visual workflow guide](SHOWCASE.md).

RhinoSpatial is organized around one rule:

**one selected area, multiple aligned spatial layers**

Every loader consumes the same `Spatial Context`, so maps, vector data, terrain, buildings, rasters, OSM context, and visual reference layers can share one Rhino and Grasshopper study space.

> The source-compatibility additions described here are **Unreleased**. The public 0.4.0 package and installed plugin are unchanged. See the [example recipes](../examples/COMPATIBILITY_WORKFLOWS.md) for tested inputs.

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
- OGC API Features landing/collection metadata, ATOM feed coverage and WMTS capabilities (Unreleased)
- GeoTIFF and supported world-file TIFF/PNG/JPEG imagery
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

Connect a WFS service URL to inspect the layers advertised by its `GetCapabilities` response. OGC API Features roots and collection metadata URLs are also accepted; the component follows advertised data/items links and returns collection ids and titles. Each output entry combines the service layer name and title. Use `List Item` to select one layer, or explicitly merge several entries for a multi-layer WFS request.

Public interface:

- input: `WFS URL`
- output: `Layer` list in `name | title` form

### List WMS Layers

Connect a WMS service URL or WMTS capabilities URL to inspect its imagery layers. Use `List Item` to choose the layer needed by `Load WMS`.

Public interface:

- input: `WMS URL`
- output: `Layer` list in `name | title` form

Layer discovery is a service workflow. Local vector and raster files can normally be connected directly to their loader without using a list component.

## Load WFS: vector and feature data

Use `Load WFS` for parcels, planning layers, boundaries, roads, building footprints, points, and other feature-based vector data.

The public component name is retained for Grasshopper compatibility, but the accepted source category is broader than WFS:

- WFS service URLs
- OGC API Features landing-page, collection-list, collection or items URLs
- direct GeoJSON URLs and ZIPs containing GeoJSON or complete Shapefile companion sets
- local Shapefiles (`.shp`)
- local GeoJSON (`.geojson` or GeoJSON `.json`)

Public inputs:

- `WFS URL` — service URL, OGC API Features URL, or local vector-file path
- `Layer` — one or more layer entries; optional for a single OGC API collection or sole GeoPackage feature layer and ordinary single files; choose explicitly for multiple collections/GeoPackage layers
- `Max Features` — request limit; the default `0` requests all available features within the bounded query
- `Spatial Context` — the shared selected area and placement context

The `Geometry` output is a tree grouped by layer and feature.

### Unreleased: local GeoPackage features

Connect a local `.gpkg` path to List WFS Layers and Load WFS. Catalog names identify
actual feature tables; select explicitly for multiple layers, or leave Layer
empty for a sole feature layer. Read-only queries use a standard advertised RTree
when available, otherwise a bounded scan. Declared EPSG organization codes govern
the CRS; polygon exterior/hole rings remain feature-grouped closed curves, with
points/lines/multi-geometries/collections and string attributes in the existing XY
vector contract. Z/M are not a building/elevation import. Raster tables, editing,
SQL, curved/extended geometry and automatic remote GeoPackage downloads are not
supported. See [limits and saved examples](../examples/LOW_COMPATIBILITY_WORKFLOWS.md).

### Typical WFS workflow

1. Connect the service URL to `List WFS Layers`.
2. Choose one layer with `List Item`, or merge only the layers you need.
3. Define the area with `Spatial Context`.
4. Connect the service URL, chosen layer, and shared context to `Load WFS`.

For Shapefile/GeoJSON, connect the file path directly to `WFS URL`. `Layer` can remain empty and is used only as a label when provided. OGC API Features URLs can also be connected directly; RhinoSpatial requests GeoJSON features for the context bounding box.

`Max Features` is a deliberate per-layer cap. `0` follows available pages within the bounded query. OGC API next links and WFS next links are supported; WFS 2 can also advance `STARTINDEX` by the actual returned count. Duplicate ids are removed. Identical records without stable ids remain separate, with a deduplication caveat; declared GeoJSON CRS changes between pages are rejected. Repeated pages, failed required pages and known incomplete responses fail explicitly instead of returning partial selections. Without a matching total, the status states that completeness cannot be independently verified. Legacy WFS without next links cannot be assumed pageable.

Pagination has safety limits of 100 pages, 100,000 unique features, 128 MiB total text and two minutes; a single response is limited to 16 MiB. Hitting a safety limit while more data is advertised returns an error; hitting the user's cap is reported as intentional. These bounds apply to service paging, not a promise that every server implements filtering correctly.

Direct vector downloads are limited to 128 MiB compressed, 256 MiB per expanded file and 1 GiB expanded per archive. ZIPs require `.shp`, `.shx`, `.dbf` and a resolvable `.prj` for each imported Shapefile. Unsafe paths, symlinks, duplicate paths and incomplete transfers are rejected. Validated payloads persist under `RhinoSpatial/VectorDownloadCache`; revisionless URLs refresh after one day. Source JSON is inspected by content, and GeoJSON may contain a FeatureCollection or a single Feature. Unknown declared CRS metadata fails rather than assuming the context.

### Outputs and parsing

- polygon and line features become Rhino curves;
- point features become Rhino points;
- geometry is grouped by layer and feature in the output tree.

RhinoSpatial prefers GeoJSON where available and falls back to GML where needed. Local GeoJSON defaults to `EPSG:4326` unless it contains a readable older named `crs` member.

GeoJSON features may have `properties: null`. Empty coordinate arrays produce no geometry; they never create a point at the origin. Older named CRS identifiers support EPSG codes in short, URN, and OGC URL forms, including identifiers with a separate version segment.

The compatibility-oriented WFS path retains established EPSG:4326 XY behavior and handles confirmed EPSG:4258 latitude/longitude services explicitly. Provider-specific axis order, geometry encodings, and response formats can still require compatibility work.

## Load WMS: maps and imagery

Use `Load WMS` for orthophotos, aerial imagery, map layers, and raster overlays delivered by a Web Map Service.

### Typical workflow

1. Connect a WMS URL to `List WMS Layers`.
2. Choose one requestable layer.
3. Define the area with `Spatial Context`.
4. Connect the WMS URL, layer, format, and context to `Load WMS`.

WMTS is also accepted through these existing components. Use a WMTS capabilities URL with `List WMS Layers` and `Load WMS`; the selected layer uses advertised matrix sets, styles, dimensions and PNG/JPEG formats. `GetMap URL` returns the tile request URLs in WMTS mode. Runtime status reports the selected matrix, resolution, native CRS and tile count.

One component requests one WMS/WMTS layer. Use several components when multiple image layers should remain explicit.

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

WMTS supports KVP GetTile and advertised REST templates. It chooses a resolution near a 1024-pixel study view within 64 tiles and 4,194,304 mosaic pixels (8192 per axis). Limits are 4 MiB per tile, 64 MiB per batch and two minutes per mosaic. Matrix limits and geographic axis order are respected; unsupported CRS, ambiguous dimensions and failed required tiles return explicit errors. Dimension defaults are used; an explicit source-URL query value can select a dimension without a new component parameter. Completed PNG mosaics persist under `RhinoSpatial/WmtsCache`, expire after one day, and are checked against SHA-256 before reuse. Delete the cache while idle to reclaim space. No partial mosaic is published after a required tile failure.

## Load GeoTIFF: georeferenced rasters

Use `Load GeoTIFF` for a local georeferenced image such as an orthophoto, map export, satellite image, or other raster that should align with the selected study area.

Connect a `.tif`/`.tiff`, `.png`, `.jpg` or `.jpeg` path and the shared `Spatial Context`. PNG/JPEG and ordinary TIFF need a matching world file (`.pgw`, `.jgw`, `.tfw`, long-extension variants or `.wld`) and a resolvable `.prj` unless usable geospatial metadata is embedded.

Public inputs:

- `GeoTIFF` — local georeferenced raster path
- `Spatial Context` — the shared selected area and placement context

Outputs include:

- an aligned image mesh;
- a display material with the raster attached;
- the source image path;
- a status message.

The file must contain readable geospatial metadata. If RhinoSpatial cannot determine the coordinate system or the raster does not overlap the selected context, it returns a clear warning rather than guessing a placement.

Embedded TIFF georeferencing takes priority. The reader supports finite invertible affine transforms, including rotation, shear and reflection. World-file coefficients refer to pixel centers; GeoTIFF PixelIsArea/PixelIsPoint conventions are honored. Reference extents use all four footprint corners. A bounded mesh grid projects the actual footprint through the shared context, clips it with interpolated UVs, and localizes it without stretching the image over its bounding rectangle. Sidecar changes invalidate cached metadata and component results. A world file without a resolvable CRS fails explicitly.

### Unreleased: remote COG imagery

The same GeoTIFF input accepts an HTTP(S) `.tif/.tiff` URL, including URL query
parameters. The client validates byte ranges and a stable representation, selects
usable overviews/intersecting tiles, preserves masks/NoData and writes only the
selected PNG window. Status reports resolution, request count and transferred
bytes. The existing mesh/material/image-path outputs retain affine placement,
UV clipping and localization. Remote inputs revalidate on a new solve. Reference
Source reads headers only; saved selections retain priority. BigTIFF, striped
files, unsupported masks/codecs and range refusal return explicit errors. See
[the complete budgets and recipes](../examples/LOW_COMPATIBILITY_WORKFLOWS.md).

## Load Terrain: elevation and ground surfaces

Use `Load Terrain` to create ground geometry aligned with the same study area as imagery, vectors, and buildings.

Supported sources include:

- WCS services
- ATOM feeds containing supported GeoTIFF, ASC or regular XYZ/CSV terrain tiles
- local GeoTIFF DEM (`.tif` or `.tiff`)
- **unreleased:** remote single-band COG DEM via HTTP byte ranges
- Esri ASCII Grid (`.asc`)
- regular XYZ or CSV point grids with x, y, z columns
- the built-in quick global land-elevation fallback

Public inputs:

- `Terrain URL` — WCS URL, terrain ATOM feed, remote COG DEM (development) or local DEM/grid path; leave empty for the quick global fallback
- `Coverage` — optional WCS coverage id or EPSG override for local text grids
- `Spatial Context` — the shared selected area and placement context

Outputs are the aligned `Terrain` mesh list and a `Status` message.

`Coverage` identifies a WCS coverage where required. For local text grids it can also provide an EPSG override such as `EPSG:25832`; otherwise RhinoSpatial uses the current context SRS.

GeoTIFF terrain vertices use the same affine pixel centers as imagery, and cached elevation sampling inverts that transform. Rotated local DEMs are supported; terrain ATOM mosaics still require compatible north-up grids and reject other grids explicitly.

Terrain coordinates are transformed from the source SRS into the shared context before localization, including when the source uses a different projected CRS. Unsupported transformations and non-overlapping WCS coverage return explicit errors. Missing elevation samples leave holes in the surface, including during downsampling; RhinoSpatial does not replace them with zero-height terrain. A raster without connected usable cells returns an error.

Esri ASCII Grid samples are placed at cell centers, while reference metadata describes the full cell footprint. Matching `xllcorner`/`yllcorner` and `xllcenter`/`yllcenter` headers are supported. `NODATA_value` is optional and defaults to `-9999`. Cell values may wrap across text lines; their total must match `ncols × nrows`. Invalid headers, truncated data, and extra cell values are rejected. These conventions follow the [Esri ASCII raster format](https://help.arcgis.com/en/arcgisdesktop/10.0/help/009t/009t0000000z000000.htm).

XYZ/CSV terrain requires finite, evenly spaced X and Y coordinates. Sparse regular grids retain missing cells as NoData; irregularly spaced samples return an error instead of being stretched onto an artificial grid.

For ATOM, connect the feed directly and leave `Coverage` empty. Text grids require a declared native feed CRS; GeoTIFFs use embedded metadata. Tiles are selected from supported spatial metadata or explicitly declared UTM grids. Compatible north-up grids are cropped to the study area and merged before mesh construction, so adjacent samples and the elevation baseline remain consistent. Incompatible spacing/CRS/alignment and conflicting overlap heights fail explicitly. The mosaic is limited to 4,194,304 cells and eight million aggregate cropped source samples. See the [Berlin DGM1 example](../examples/COMPATIBILITY_WORKFLOWS.md#berlin-dgm1-terrain-from-atom). Downloads use the shared ATOM limits below and persist under `RhinoSpatial/TerrainAtomCache`.

The built-in fallback is intended for small-area orientation and short request times. It fails quickly with a useful status when the area is too large, no usable elevation samples are available, or the request cannot complete promptly. Project work should prefer official or project-specific terrain.

The WCS path supports ordinary single-part GeoTIFF responses and multipart GeoTIFF responses. Where capabilities omit a WGS84 extent, RhinoSpatial can use `DescribeCoverage` metadata to orient the context.

## Load LoD2 Buildings: official 3D building context

Use `Load LoD2 Buildings` for official building massing, roof forms, and 3D urban context.

One `LoD2 Source` input accepts:

- a LoD2 WFS service URL
- an ATOM download-service URL with selectable CityGML/CityJSON tiles
- a direct supported building-file or ZIP/GMZ download URL
- local CityGML/GML/XML
- local CityJSON
- a folder containing supported building files
- a ZIP archive containing supported building files

For WFS sources, select a building layer when needed. For ATOM and local data, `Layer` can stay empty or be used as an output label.

Public inputs:

- `LoD2 Source` — WFS/ATOM URL, direct supported download URL, local file, folder, or ZIP path
- `Layer` — optional WFS layer name or local output label
- `Spatial Context` — the shared selected area and placement context

Outputs are `Buildings` as a tree grouped by layer and building, plus `Status`.

### Automatic ATOM downloads

Connect the service URL and a `Spatial Context`; no tile naming, manual download or extraction is required. For example, Berlin's official LoD2 service is `https://gdi.berlin.de/data/a_lod2/atom/`. RhinoSpatial follows linked dataset feeds and pagination, selects intersecting tiles, downloads those ZIPs, and uses the existing CityGML filtering and Brep conversion path. Existing inputs, outputs and component identity are retained.

Tile selection supports GeoRSS Simple boxes/polygons and GeoRSS GML envelopes, including native projected bounds. GeoRSS Simple coordinates are latitude/longitude independently of a dataset's CRS category. GML uses its declared CRS and authority axis order; an omitted CRS defaults to WGS84 latitude/longitude. Relative links and `xml:base` are resolved against the response URL.

Some feeds, including Berlin's, publish a single dataset extent and many download links. These require additional tile information. The supported grid convention is a declared square grid size, an EPSG:25832 or EPSG:25833 category, an enclosing geographic extent, and filenames ending in `_EEE_NNNN` with southwest UTM coordinates in kilometres. Berlin buildings explicitly link a `1x1 Km` tile index; terrain uses a declared `2x2 Km` index. Coarse enclosing coverage allows a one-declared-tile margin for edge/buffer tiles; distant contradictory grids are rejected. This rule depends on metadata and the supported naming convention, not the service hostname. Arbitrary numeric filenames or whole-dataset bounds are never sufficient on their own. A feed with missing, ambiguous or unsupported spatial metadata returns a limitation before downloading building archives. Start from the service feed so its enclosing extent remains available.

ATOM and direct building download URLs support CityGML (`.gml`, `.citygml`, `.xml`) and CityJSON (`.json`, `.cityjson`), including ZIP/GMZ containers. Downloaded CityJSON/CityJSONSeq must declare the header `metadata.referenceSystem`. Standard CityJSONSeq (`.city.jsonl` / `.jsonl`) and affine row-major GeometryInstance templates are supported. Local CityJSON/file/folder/ZIP workflows remain supported. Downloaded CityGML must declare a source CRS that can transform into the context; no guessed CRS or approximate bounding-box placement is used. Standard AdV names such as `urn:adv:crs:ETRS89_UTM33*DE_DHHN2016_NH` resolve to UTM XY; published heights are retained without a vertical-datum conversion.

Use a building dataset, not a terrain feed: Berlin's `/data/a_lod2/atom/` supplies LoD2 buildings, while `/data/dgm1/atom/` supplies CSV terrain grids. Explicit CSV/raster download MIME types are rejected before grid selection with a source-format explanation. Use the DGM1 feed directly with `Load Terrain`; its terrain-specific parser and mosaic path are separate from the building reader.

Completed ZIPs persist in the user's local application-data folder under `RhinoSpatial/Lod2AtomCache`. Repeated solutions reuse them, including after restarting Rhino. The cache key includes the download URL and feed entry's `updated` value. With no revision value, archives refresh after one day. Cached length and SHA-256 are checked before reuse. Downloads are written to temporary files, checked for complete transfer and supported readable payloads, and promoted only on success. Failures can be retried; successfully completed tiles remain cached. Delete this cache directory while RhinoSpatial is idle to reclaim space or force fresh downloads. Feed metadata is refreshed after five minutes within the running process.

The component displays tile/download progress while working. `Status` reports selected tile filenames, downloaded and cached counts, cache location, parsed buildings, source CRS and conversion diagnostics. A failed required download or CityGML parse returns an error rather than presenting a partial area as complete.

Limits are 64 feeds, 8 subfeed levels, 50,000 download entries and 32 selected tiles per solution; 512 MiB compressed per archive, 256 MiB per expanded file and 1 GiB total expanded per archive. Feed requests time out after 30 seconds, discovery after two minutes, individual downloads after three minutes and the download batch after five minutes. Reduce the study area or use a more specific spatially indexed feed if a limit is reached. ATOM feed coverage and native CRS are supported by `Spatial Context → Reference Source`. Use a service feed containing coverage; a dataset feed lacking coverage reports that limitation. Reference processing reads feed metadata without downloading archives. Saved map selections retain priority.

### CityJSON detail, templates and sequences

For each CityObject, import all geometry parts at one selected numeric LoD: the highest at or below 2.2, or the lowest higher value if none is available below that threshold. Geometry without any LoD retains legacy behavior; mixing declared and unspecified levels fails because a complete representation cannot be chosen safely. Hierarchy-wide LoD selection and material/texture import are not added.

CityJSONSeq records are streamed with independent vertex indices and shared header transform, CRS and templates. GeometryInstance vertices use the declared matrix followed by the transformed reference-point anchor; template vertices are not quantized a second time. Invalid indices, duplicate building ids, malformed later records and unsupported selected primitives fail explicitly. Limits are 64 × 1024² characters per record, 512 × 1024² stream characters, 100,000 returned building objects and four million selected source surface points. Local/ZIP/direct downloads use the same reader. Reference metadata can stop at a declared header extent without constructing geometry. See the [medium-priority examples](../examples/MEDIUM_COMPATIBILITY_WORKFLOWS.md).

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

### Unreleased reference metadata additions

Local GeoPackage Reference Source uses all feature-layer catalog extents and
transformable declared EPSG metadata without querying geometry. Missing extents
fail rather than trigger a scan. Remote COG Reference Source reads geospatial TIFF
headers through a tighter range budget without decoding/importing pixels. These
follow the existing rule that saved selections take priority. Explicit WGS84 UTM
north/south EPSG:32601–32660 / 32701–32760 are supported transformations; source
heights are not vertically reinterpreted.
