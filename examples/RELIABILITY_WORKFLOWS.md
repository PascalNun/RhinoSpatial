# Stop, retry and check source loading

These examples require a development build from `main`. The public package remains 0.4.0. WMS, WCS and OSM reliability is implemented; local terrain windows and remote BigTIFF remain follow-ups in the [technical reference](../docs/technical/SERVICE_RELIABILITY.md).

## Automated checks

From the repository root:

```sh
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release -- reliability-check
```

The 52 offline cases cover cancellation during headers/body/retry, late responses, metadata caches, concurrent downloads, checksum corruption, response and decoded-image limits, common image formats, WCS single/multipart data and OSM category failures. They use controlled HTTP responses and temporary files, removed afterwards. No public service is required.

## Try a slow or interrupted source in Rhino

Start the supplied loopback server:

```sh
python3 examples/serve_reliability.py --port 8769 --delay 8
```

The server provides a synthetic colour-pattern image, the existing synthetic terrain fixture and one generated OSM road. It tests transfers, not service-side spatial processing. WCS returns the whole fixture: use EPSG:25833 bounds `392000,5819872,392128,5820000`. For OSM, enable Road and disable the other categories; the generated road lies inside the submitted query bounds.

| Case | WMS URL, layer `site` | WCS URL, coverage `dem` | OSM URL |
| --- | --- | --- | --- |
| Normal | `http://127.0.0.1:8769/normal/wms?SERVICE=WMS` | `http://127.0.0.1:8769/normal/wcs?SERVICE=WCS` | `http://127.0.0.1:8769/normal/osm` |
| Slow body | `http://127.0.0.1:8769/slow/wms?SERVICE=WMS` | `http://127.0.0.1:8769/slow/wcs?SERVICE=WCS` | `http://127.0.0.1:8769/slow/osm` |
| Interrupted body | `http://127.0.0.1:8769/truncated/wms?SERVICE=WMS` | `http://127.0.0.1:8769/truncated/wcs?SERVICE=WCS` | `http://127.0.0.1:8769/truncated/osm` |
| Oversized declaration | `http://127.0.0.1:8769/oversized/wms?SERVICE=WMS` | `http://127.0.0.1:8769/oversized/wcs?SERVICE=WCS` | `http://127.0.0.1:8769/oversized/osm` |

WMS image-size retry is at `/retry/wms?SERVICE=WMS`; WCS multipart data at `/multipart/wcs?SERVICE=WCS`. Stop the server with Ctrl+C. After a completed load, use a fresh suffix such as `/slow-2/wms?SERVICE=WMS` to test a new cache identity.

Start a slow load, cancel the Grasshopper solution or change the selected area, then retry a normal source. A canceled solve must not replace newer output. An interrupted download must not become a cache hit. Repeat with saved definitions and on both platforms. These interactive checks remain pending; console fixtures do not prove Rhino UI or preview behavior.

## Recorded public-service checks

Checked on 2026-11-02 using the new Core clients:

| Source | Observed result |
| --- | --- |
| GeoMet WMS catalogue | 39,668,951 response bytes; 7,606 layers parsed; approximately 13.5 s |
| NRW `nw_dgm`, native EPSG:25832 | 100 × 100 raster, 10,000 finite samples, 1,907-byte TIFF; approximately 1.8 s |
| USGS `DEP3Elevation`, native EPSG:3857 | 100 × 100 raster, 10,000 finite samples, 65,924-byte extracted TIFF; approximately 6.0 s |

The USGS HTTP response was also inspected directly: HTTP 200, `multipart/related`, boundary `wcs`, 68,591 body bytes. A forced EPSG:4326 request returned HTTP 404 and was not accepted as coverage; the recorded native-coordinate request succeeded. The terrain component uses advertised native coverage coordinates. The Overpass POST probe returned HTTP 502, so OSM validation here uses offline fixtures.

```sh
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release -- probe wms 'https://geo.weather.gc.ca/geomet?SERVICE=WMS&REQUEST=GetCapabilities'

dotnet run --project RhinoSpatial.Sandbox.csproj -c Release -- wcscoverage 'https://www.wcs.nrw.de/geobasis/wcs_nw_dgm?SERVICE=WCS&REQUEST=GetCapabilities' nw_dgm '392000,5819900,392100,5820000' EPSG:25832

dotnet run --project RhinoSpatial.Sandbox.csproj -c Release -- wcscoverage 'https://elevation.nationalmap.gov/arcgis/services/3DEPElevation/ImageServer/WCSServer?SERVICE=WCS&REQUEST=GetCapabilities' DEP3Elevation '-8238310,4969950,-8238210,4970050' EPSG:3857
```

These are metadata/download/parser checks, not Rhino alignment or scale measurements. Public services and timings can change. Source bytes are not committed.

## Limits and cache behavior

| Resource | Default limit |
| --- | --- |
| Service metadata | 64 MiB; accommodates the measured large WMS catalogue |
| WMS image | 64 MiB encoded; 16,777,216 decoded pixels |
| WCS response, including multipart framing | 256 MiB; existing terrain allocation guards still apply |
| Overpass response | 32 MiB, 500,000 elements and two million input/combined output coordinates |
| Error response | 64 KiB read; at most 1,024 characters in HTTP diagnostics |
| Request including body | WMS requests and WFS metadata: 30 s; WCS/OSM: 60 s |
| Complete source operation | Two minutes across discovery, retries and fallback, with processing checkpoints |

These limits do not bound total Rhino memory. Synchronous library/native conversions cannot always be interrupted mid-call; cancellation is checked at processing boundaries. Sequential local text-grid reading and local TIFF windowing remain follow-up work.

Completed metadata has a five-minute lifetime, at most 64 entries and a 128 MiB text-weight budget per cache. That weight is an admission estimate, not measured parsed-object memory. URL path/query case is retained. Canceled/faulted tasks are not cached; another waiter can retry independently. WFS metadata uses the same bounded helper because reference discovery may probe it before WMS/WCS.

Image/coverage downloads use versioned filenames and length/SHA-256 receipts. Only successfully transferred and validated files can become cache hits. Failed temporary files are removed and concurrent callers coordinate publication. Old texture files are preserved, but unverified legacy files are not trusted as new hits. This work adds no automatic disk cleanup or deletion of saved/baked image assets.

OSM retains explicitly reported category failures and older-data fallback for ordinary provider failures. Fresh data lasts five minutes. Its cache is limited to 64 entries, four million retained coordinates and 128 MiB of estimated geometry/tag storage. Cancellation and resource limits stop the operation. Incomplete-query remarks and reduced category results are not cached as complete successful studies.

## Efficiency measurement

A controlled 16,777,361-byte multipart response containing a tiny valid TIFF plus padding allocated about 301,088 managed bytes and took 35–60 ms locally. Prepared fixture input is excluded; the assertion checks that transport allocation stays below the response size. This isolates transfer/extraction, not large-raster decoding or native geometry. Timing is recorded rather than used as a fragile pass/fail threshold.

WCS uses reusable buffers and spool files instead of whole-response and whole-part byte-array copies. Terrain mesh conversion prepares its coordinate transform once per mesh. Complete the [native checklist](VALIDATION.md) before a release.
