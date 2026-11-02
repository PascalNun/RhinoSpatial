# Reader validation findings

Review date: **2026-11-02**. Baseline: `3e71e25`. Small synthetic inputs reproduced
three reader defects without running Rhino. The development build now rejects
incomplete terrain, retains interior boundaries and parses coordinate identifiers
consistently; [reader integrity](READER_INTEGRITY.md) describes the behavior.
The public package remains 0.4.0.

## Current status

| Area | Evidence | Status |
| --- | --- | --- |
| Incomplete terrain | A 166-byte TIFF repeated its readable row | Fixed; offline regressions cover rows, tiles and valid compressed inputs |
| Courtyard boundaries | GeoJSON/GML returned one of two rings; matching Shapefile code inspected | Fixed; regression coverage includes Shapefile holes and islands |
| Coordinate identifiers | Invalid substring code accepted; supported UTM URN rejected | Fixed through the shared identifier parser |
| Service cancellation and resource limits | WMS/WCS/OSM request and cache review | Implemented in development; see [service reliability](SERVICE_RELIABILITY.md) |
| Local terrain memory | Full-grid decode precedes crop | Window reader planned |
| Remote BigTIFF | Basic LibTiff decoding works; remote preflight rejects the format | Bounded 64-bit remote handling planned |
| Rhino units and host behavior | Placement/runtime review | Native measurements and host tests pending |

## Original findings at the audit baseline

### 1. An unreadable terrain row can repeat old elevations

At the baseline, [TerrainRasterReader](../../RhinoSpatial.Core/TerrainRasterReader.cs) called `ReadScanline` without checking its boolean result. A tiny 2×2 raster contained the first row `[10,20]` and no second strip. LibTiff reported a read error; the Core reader still returned `[10,20,10,20]`.

The reproduction demonstrates incorrect elevations without running Rhino or allocating a large raster. The library documents a failed scanline read as `false`; see [LibTiff ReadScanline](https://bitmiracle.github.io/libtiff.net/help/api/BitMiracle.LibTiff.Classic.Tiff.html#BitMiracle_LibTiff_Classic_Tiff_ReadScanline_System_Byte___System_Int32_).

**Acceptance:** incomplete scanlines/tiles fail before any terrain result is published. Check decoded byte counts, validate dimensions before allocating, preserve genuine NoData, and test valid/compressed/truncated inputs. Do not silently substitute a previous row or zeroes.

### 2. Some vector readers discard inner rings

A polygon with two rings produced only one ring in both [GeoJSON](../../RhinoSpatial.Core/GeoJsonReader.cs) and [GML](../../RhinoSpatial.Core/GmlReader.cs). The baseline [Shapefile reader](../../RhinoSpatial.Core/ShapefileFeatureReader.cs) also appended only the polygon's exterior on inspection; a Shapefile runtime reproduction was not performed in this audit.

These outputs are curves, so the observed bug is a missing courtyard boundary—not a demonstrated filled-surface problem. The GeoPackage path already retains interior-ring curves, making behavior inconsistent between formats. [GeoJSON's standard](https://www.rfc-editor.org/rfc/rfc7946.html#section-3.1.6) explicitly includes interior rings.

**Acceptance:** equivalent GeoJSON, GML, Shapefile and GeoPackage inputs retain every valid boundary and feature association. Test multiple polygons, holes and nested islands; preserve the existing geometry-tree contract. Keep 2D vectors separate from building/height import.

### 3. Coordinate-name handling can accept the wrong ID

The shared [SpatialReferenceTransform](../../RhinoSpatial.Core/SpatialReferenceTransform.cs) normalized several codes by substring at the baseline. These direct probes returned:

```text
EPSG:9994326 -> EPSG:4326; transform accepted
urn:ogc:def:crs:EPSG::32632 -> unchanged URN; transform rejected
EPSG:32632 -> EPSG:32632; transform accepted
```

The first input must not become WGS84 merely because it contains `4326`. The second names a supported coordinate system in a different standard form; GDAL's [CRS API documentation](https://gdal.org/en/stable/api/ogrspatialref.html) describes OGC URN support. This probe demonstrates shared-helper behavior; some source-specific readers already normalize identifiers first, so it is not evidence that every source path fails identically.

**Acceptance:** parse complete authority/code identifiers, support the documented short/URN/URL forms, retain the known AdV aliases, reject unknown or malformed identifiers and avoid changing established axis-order behavior accidentally. Verify explicit unsupported-CRS errors and existing coordinate regression cases.

## Checks needed inside Rhino

### Units and whole-study behavior

The [placement code](../../RhinoSpatial/RhinoSpatialContextTools.cs) constructs points from transformed coordinates. A repository search found document tolerance use but no `ModelUnitSystem`/`UnitScale` handling in the plugin source. This is a strong reason to test scale in metre, millimetre and foot templates; it is **not yet a reproduced Rhino units bug**.

Use a known-length rectangle and building height, compare imagery/vectors/terrain together, change units, save/reopen and test supported bake paths. Establish an explicit units policy before changing behavior. Automatic scaling must not quietly break the promise of original coordinates in absolute mode. McNeel documents the relevant [unit conversion API](https://developer.rhino3d.com/api/rhinocommon/type/rhino.rhinomath/).

### Actual host runtimes and packaged assemblies

The plugin targets .NET 7 while the sandbox runs .NET 10. Passing the latter does not exercise the installed Grasshopper assembly in Rhino. McNeel's [current runtime guidance](https://developer.rhino3d.com/guides/rhinocommon/moving-to-dotnet-core/) recommends .NET 8 for Rhino 8 and .NET 10 for Rhino 9.

Define the minimum supported Rhino 8 version, verify both platforms and package loading under the actual host, and plan Rhino 9 separately. This is a compatibility review, not evidence that the current target is broken and not a reason to retarget everything to .NET 10 without preserving Rhino 8 support.

## Responsiveness and large inputs

At the baseline, [WMS](../../RhinoSpatial.Core/WmsClient.cs), [WCS](../../RhinoSpatial.Core/WcsClient.cs) and [OSM](../../RhinoSpatial.Core/OsmClient.cs) APIs lacked caller cancellation through their request methods, although HTTP timeouts were finite. Development clients now propagate cancellation and apply response limits.

The baseline WCS client buffered the full response; development now spools it to disk. Local terrain allocates `width × height` elevations and reads the full raster; terrain ATOM crops after that read. A small selected area therefore does not itself cap the initial decode/allocation. The local-raster finding is a code-based memory/performance risk; these tests did not induce an out-of-memory failure or establish the cause of an application crash.

**Validation:** cover cancellation through requests and decode/mesh loops, bounded streamed responses, dimensions/decoded bytes and selected local raster windows. Cases include slow responses, oversized headers and interrupted reads. GDAL's [windowed raster API](https://gdal.org/en/stable/tutorials/raster_api_tut.html) illustrates the windowing pattern; RhinoSpatial's TIFF decoder remains LibTiff.

## BigTIFF support boundaries

The current remote [CogReader](../../RhinoSpatial.Core/CogReader.cs) explicitly rejects BigTIFF and its preflight assumes classic TIFF directory layouts. However, **the already pinned BitMiracle.LibTiff.NET 2.4.660 successfully wrote and read a tiny BigTIFF**: header marker 43, second row `[30,40]` read successfully. The library's [Open modes](https://bitmiracle.github.io/libtiff.net/help/api/BitMiracle.LibTiff.Classic.Tiff.html) include BigTIFF creation. The current restriction is in remote header/directory handling, rather than basic decoder capability.

This suggests a focused extension to bounded 64-bit header/directory handling, not an immediate library replacement. Prove tiled remote image/DEM access, masks/overviews, malicious counts and offsets above 4 GiB without whole downloads. The tiny unreferenced test proves decoder capability only. [GDAL's COG documentation](https://gdal.org/en/stable/drivers/raster/cog.html) describes classic TIFF and BigTIFF products and their layouts.

## Later improvements

Cache folders have separate policies, and there is no shared user-facing size/cleanup overview. The inspected cache sample was modest, so this is not an urgent storage incident. Investigate a size summary, safe limits and reuse of validated COG windows. Keep baked/saved texture assets separate from disposable downloads before adding automatic cleanup; removing an image used by a saved model could break its material.

STAC asset links could eventually simplify choosing imagery/elevation files; the [STAC specification](https://stacspec.org/en/about/stac-spec/) supplies asset metadata. Any integration needs a clear rule for choosing imagery versus elevation, dates and coverage. It should stay within existing source workflows rather than become a large provider browser. Fix data correctness first.

The OSM output builder already interprets width/lane and height/level tags. Further changes need representative source data and a defined output requirement.

## Evidence and limits

- Reviewed reader and placement code, build targets and validation coverage.
- Reproductions used a .NET 10 console project referencing the baseline Core assembly.
- Saved [tiny diagnostic inputs](../../examples/fixtures/research/README.md) and these observed results:

```text
GeoJSON polygon: input=2 rings, output=1
GML polygon: input=2 rings, output=1
Truncated 2x2 terrain: read error, returned [10,20,10,20]
Unknown EPSG:9994326: normalized to EPSG:4326
URN EPSG::32632: rejected while EPSG:32632 is accepted
Pinned LibTiff BigTIFF: marker 43, successful row [30,40]
```

The observations above are the original research results. The follow-up implementation now retains both courtyard rings, rejects the truncated terrain file, leaves `EPSG:9994326` unsupported, and accepts the supported UTM URN. A valid local BigTIFF still reads correctly. The 52 correctness cases run in macOS/Windows CI. Standalone checks also passed using Rhino 8.16's bundled .NET 7 runtime; see the [runtime record](../../examples/VALIDATION.md#recorded-runtime-check--2026-11-02). Native Rhino scale/material/assembly behavior remains unverified by these probes.
