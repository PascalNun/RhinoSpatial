# What to work on next

Research recorded on **2026-11-02**, against `main` at `3e71e25`.

**Implementation update:** the three high-priority items are now fixed in the development build from `main`, following the [correctness plan](CORRECTNESS_PLAN.md). The 52 new offline checks cover the original failures, a real Shapefile courtyard/island, valid compressed terrain, missing tile bytes, allocation guards, coordinate aliases, CRS84 axes and AdV source heights. The original observations below describe the audit baseline. Native Rhino validation and the other follow-ups remain pending; the public package is still 0.4.0.

The next phase should improve correctness and everyday reliability before adding another broad set of formats. The previous compatibility work is merged and its automated checks pass, but small targeted probes exposed gaps that those tests do not cover.

## Research priorities and follow-up

The [next reliability plan](RELIABILITY_PLAN.md) turns the remaining findings
into three ordered implementation batches and a native units/runtime validation
track. It records code-review evidence separately from proposed behavior and
acceptance checks; the service reliability batch is now implemented, while terrain windows, remote
BigTIFF and native validation remain follow-ups.

| Priority | Work | Benefit | Evidence |
| --- | --- | --- | --- |
| High — first | Reject incomplete terrain reads | Prevent unreadable data from becoming plausible elevations | Reproduced with a 166-byte TIFF |
| High | Preserve courtyard and interior boundaries across vector formats | Keep site outlines complete when the same data comes from different formats | Reproduced in GeoJSON and GML; matching Shapefile code pattern found |
| High | Parse coordinate identifiers strictly and consistently | Prevent mistaken placement and avoid rejecting supported coordinates just because their name is written differently | Reproduced in the shared CRS helper |
| Before release | Test full studies inside Rhino, including units and actual host runtimes | Verify physical scale, preview, materials, baking, saved definitions and assembly loading | Existing native checklist remains pending; unit handling needs investigation |
| Next | Make cancellation and resource limits consistent across older loaders | Keep large or slow requests manageable | Code inspection of WMS, WCS, OSM and local DEM paths |
| Medium | Extend remote raster handling to BigTIFF | Accept more large imagery/elevation products with the existing library | Basic decoding proved with shipped LibTiff; remote path still rejects it |
| Later | Improve cache management and reuse | Make disk use and repeated loads easier to understand | Separate caches and recompute behavior found; no urgent disk-space problem demonstrated |

The first implementation batch has addressed the three confirmed correctness issues and added regressions. Follow it with a combined Rhino study before deciding on publication. Preserve component names/connections and use the same shared context throughout.

## Original findings at the audit baseline

### 1. An unreadable terrain row can repeat old elevations

[TerrainRasterReader](../RhinoSpatial.Core/TerrainRasterReader.cs) calls `ReadScanline` without checking its boolean result. A tiny 2×2 raster contained the first row `[10,20]` and no second strip. LibTiff reported a read error; the Core reader still returned `[10,20,10,20]`.

This is a data-correctness bug, not just an error-message improvement. It was reproduced without loading Rhino or allocating a large raster. The library documents a failed scanline read as `false`; see [LibTiff ReadScanline](https://bitmiracle.github.io/libtiff.net/help/api/BitMiracle.LibTiff.Classic.Tiff.html#BitMiracle_LibTiff_Classic_Tiff_ReadScanline_System_Byte___System_Int32_).

**Acceptance:** incomplete scanlines/tiles fail before any terrain result is published. Check decoded byte counts, validate dimensions before allocating, preserve genuine NoData, and test valid/compressed/truncated inputs. Do not silently substitute a previous row or zeroes.

### 2. Some vector readers discard inner rings

A polygon with two rings produced only one ring in both [GeoJSON](../RhinoSpatial.Core/GeoJsonReader.cs) and [GML](../RhinoSpatial.Core/GmlReader.cs). The [Shapefile reader](../RhinoSpatial.Core/ShapefileFeatureReader.cs) also appends only the polygon's exterior on inspection; a Shapefile runtime reproduction was not performed in this audit.

These outputs are curves, so the observed bug is a missing courtyard boundary—not a demonstrated filled-surface problem. The GeoPackage path already retains interior-ring curves, making behavior inconsistent between formats. [GeoJSON's standard](https://www.rfc-editor.org/rfc/rfc7946.html#section-3.1.6) explicitly includes interior rings.

**Acceptance:** equivalent GeoJSON, GML, Shapefile and GeoPackage inputs retain every valid boundary and feature association. Test multiple polygons, holes and nested islands; preserve the existing geometry-tree contract. Keep 2D vectors separate from building/height import.

### 3. Coordinate-name handling can accept the wrong ID

The shared [SpatialReferenceTransform](../RhinoSpatial.Core/SpatialReferenceTransform.cs) normalizes several codes by substring. These direct probes returned:

```text
EPSG:9994326 -> EPSG:4326; transform accepted
urn:ogc:def:crs:EPSG::32632 -> unchanged URN; transform rejected
EPSG:32632 -> EPSG:32632; transform accepted
```

The first input must not become WGS84 merely because it contains `4326`. The second names a supported coordinate system in a different standard form; GDAL's [CRS API documentation](https://gdal.org/en/stable/api/ogrspatialref.html) describes OGC URN support. This probe demonstrates shared-helper behavior; some source-specific readers already normalize identifiers first, so it is not evidence that every source path fails identically.

**Acceptance:** parse complete authority/code identifiers, support the agreed short/URN/URL forms, retain the known AdV aliases, reject unknown or malformed identifiers and avoid changing established axis-order behavior accidentally. Verify explicit unsupported-CRS errors and existing coordinate regression cases.

## Checks needed inside Rhino

### Units and whole-study behavior

The [placement code](../RhinoSpatial/RhinoSpatialContextTools.cs) constructs points from transformed coordinates. A repository search found document tolerance use but no `ModelUnitSystem`/`UnitScale` handling in the plugin source. This is a strong reason to test scale in metre, millimetre and foot templates; it is **not yet a reproduced Rhino units bug**.

Use a known-length rectangle and building height, compare imagery/vectors/terrain together, change units, save/reopen and test supported bake paths. Establish an explicit units policy before changing behavior. Automatic scaling must not quietly break the promise of original coordinates in absolute mode. McNeel documents the relevant [unit conversion API](https://developer.rhino3d.com/api/rhinocommon/type/rhino.rhinomath/).

### Actual host runtimes and packaged assemblies

The plugin targets .NET 7 while the sandbox runs .NET 10. Passing the latter does not exercise the installed Grasshopper assembly in Rhino. McNeel's [current runtime guidance](https://developer.rhino3d.com/guides/rhinocommon/moving-to-dotnet-core/) recommends .NET 8 for Rhino 8 and .NET 10 for Rhino 9.

Define the minimum supported Rhino 8 version, verify both platforms and package loading under the actual host, and plan Rhino 9 separately. This is a compatibility review, not evidence that the current target is broken and not a reason to retarget everything to .NET 10 without preserving Rhino 8 support.

## Responsiveness and large inputs

The older [WMS](../RhinoSpatial.Core/WmsClient.cs), [WCS](../RhinoSpatial.Core/WcsClient.cs) and [OSM](../RhinoSpatial.Core/OsmClient.cs) APIs do not take a cancellation token through their request methods. They do have finite HTTP timeouts; this audit does not claim unbounded waits. However, cancelling a Grasshopper task cannot consistently stop those underlying requests.

WCS buffers the full response as a byte array. Local terrain allocates `width × height` elevations and reads the full raster; terrain ATOM crops after that read. A small selected area therefore does not itself cap the initial decode/allocation. This is a code-based memory/performance risk; no OOM/crash was induced, and it is not a diagnosis of the earlier Rhino crash.

**Acceptance:** thread cancellation through requests and decode/mesh loops, bound streamed response sizes, validate dimensions/decoded bytes before allocation, and read only the needed local raster window where possible. Include progress and clear limit messages without adding component inputs. Test slow responses, cancellation, oversized headers and interrupted reads. GDAL's [windowed raster API](https://gdal.org/en/stable/tutorials/raster_api_tut.html) is a useful established pattern, not a recommendation to add GDAL as a dependency.

## BigTIFF is more approachable than previously recorded

The current remote [CogReader](../RhinoSpatial.Core/CogReader.cs) explicitly rejects BigTIFF and its preflight assumes classic TIFF directory layouts. However, **the already pinned BitMiracle.LibTiff.NET 2.4.660 successfully wrote and read a tiny BigTIFF**: header marker 43, second row `[30,40]` read successfully. The library's [Open modes](https://bitmiracle.github.io/libtiff.net/help/api/BitMiracle.LibTiff.Classic.Tiff.html) include BigTIFF creation. Earlier notes attributing the restriction to the decoder were too broad; documentation is corrected in this research update.

This suggests a focused extension to bounded 64-bit header/directory handling, not an immediate library replacement. Prove tiled remote image/DEM access, masks/overviews, malicious counts and offsets above 4 GiB without whole downloads. The tiny unreferenced test proves decoder capability only. [GDAL's COG documentation](https://gdal.org/en/stable/drivers/raster/cog.html) describes classic TIFF and BigTIFF products and their layouts.

## Later improvements

Cache folders have separate policies, and there is no shared user-facing size/cleanup overview. The inspected cache sample was modest, so this is not an urgent storage incident. Investigate a size summary, safe limits and reuse of validated COG windows. Keep baked/saved texture assets separate from disposable downloads before adding automatic cleanup; removing an image used by a saved model could break its material.

STAC asset links could eventually simplify choosing imagery/elevation files; the [STAC specification](https://stacspec.org/en/about/stac-spec/) supplies asset metadata. Any integration needs a clear rule for choosing imagery versus elevation, dates and coverage. It should stay within existing source workflows rather than become a large provider browser. Fix data correctness first.

Generic suggestions such as “add road widths or building levels” are not a fresh backlog item: the OSM output builder already interprets width/lane and height/level tags. Improve those only with a representative bad result or a specific design need.

## Evidence and limits

- Reviewed the current code, roadmap, manual validation, build targets and CI. No open GitHub issues were listed at the time of the audit.
- Used a temporary .NET 10 console project referencing the existing Core assembly; no production code was modified.
- Saved [tiny diagnostic inputs](../examples/fixtures/research/README.md) and these observed results:

```text
GeoJSON polygon: input=2 rings, output=1
GML polygon: input=2 rings, output=1
Truncated 2x2 terrain: read error, returned [10,20,10,20]
Unknown EPSG:9994326: normalized to EPSG:4326
URN EPSG::32632: rejected while EPSG:32632 is accepted
Pinned LibTiff BigTIFF: marker 43, successful row [30,40]
```

The observations above are the original research results. The follow-up implementation now retains both courtyard rings, rejects the truncated terrain file, leaves `EPSG:9994326` unsupported, and accepts the supported UTM URN. A valid local BigTIFF still reads correctly. All ten suites pass locally, including the 52 correctness checks; CI runs the same checks on macOS and Windows. Native Rhino scale/material/assembly behavior remains unverified by these probes. No version, installed plugin, dependency or published package was changed.
