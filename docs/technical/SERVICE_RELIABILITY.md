# Service reliability and raster limits

Development service loaders support cancellation, bounded transfers and validated
cache publication. Local terrain windowing and remote BigTIFF remain planned
extensions. The public package remains 0.4.0. Exact current limits and measured
results are in the [service guide](../../examples/RELIABILITY_WORKFLOWS.md).

## Support status

| Area | Status | Behavior or required evidence |
| --- | --- | --- |
| WMS/WCS/OSM service loading | Implemented in development | Cancellable requests, response limits, completed metadata caches and validated file reuse |
| Local terrain windows | Planned | Read selected TIFF samples without allocating a full elevation grid |
| Remote BigTIFF | Planned | Bounded 64-bit metadata and offset handling, verified with remote image/elevation fixtures |
| Native units and complete studies | Scale and selection corrected in development | macOS physical-unit and session regressions pass; Windows and full-study checks remain pending |

## Check units and the actual Rhino workflow

Use a synthetic study with a known 100 m horizontal distance, a 10 m height difference, a courtyard and a terrain NoData gap. Combine imagery, vectors, terrain and buildings through one context. Repeat in metre, millimetre and foot documents, in localized and absolute modes, then save/reopen and check supported bake paths. Change connection order to check the shared elevation baseline.

Record physical measurements as well as coordinate values. Localized projected
output is converted at the Rhino output boundary; requests, saved contexts and
terrain caches retain CRS units. Absolute mode keeps original numbers, while
geographic contexts keep angular XY. Source geometry is copied before conversion,
including cached image meshes and temporary Google preview. Native macOS tests
cover repeated metre/millimetre/foot solves across six loaders and textured baking
in feet. See the [regressions](../../examples/VALIDATION.md#placement-and-selection-regressions--2026-11-02).

Each Spatial Context owns a temporary map session. Saved selections remain on the
component; browser updates carry the session identifier and run on the UI thread.
Removing a component revokes its session, including pending callbacks. Re-adding
or copying it creates a new session without replacing another context's area.

Record the Rhino build, operating system, active .NET runtime and loaded plugin/dependency versions. Use a separate test setup and copies of saved definitions. Verify packaged SQLite/image dependencies in the Rhino host on macOS and Windows. Windows console CI does not establish Windows Rhino compatibility. Assess Rhino 9 separately; do not retarget or drop Rhino 8 support solely because the Sandbox runs on .NET 10. See [McNeel's runtime guidance](https://developer.rhino3d.com/guides/rhinocommon/moving-to-dotnet-core/).

## Cancellation and cache publication

Requests carry a caller token through metadata, body reads, retries and processing.
Components capture each solve's token and check it before fallback or publication.
Map reference work has a separate deadline and stops when superseded or removed.
WFS metadata shares the bounded helper used during reference discovery.

### Request and publication rules

1. Injectable HTTP clients and token-aware overloads preserve existing public signatures. Protocol parsing and retry decisions remain in the source clients.
2. Headers-first requests enforce actual response limits and an operation deadline across retries and fallback endpoints. Tokens cover body transfers and retry delays as well as response headers; see the [.NET HTTP API](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.sendasync?view=net-7.0).
3. Binary payloads stream to temporary files. Length, format and relevant image/raster metadata are checked before publication. Failed replacements preserve existing good assets; responses are disposed and temporary files removed on failure.
4. WCS multipart data uses bounded spool files and selected-part offsets rather than multiple full-response byte arrays. Both single-part and multipart coverage remain supported.
5. Metadata caches hold completed values with bounded lifetime and entry count. Per-source coordination is cancellation-aware; faulted/canceled tasks are not reused. Case-sensitive URL paths and queries remain distinct.
6. Conversion loops check cancellation at processing boundaries. Progress uses asynchronous UI dispatch to avoid blocking the Grasshopper worker on the UI thread.
7. Cancellation, timeout, resource limits and provider errors remain distinct. Deliberate cancellation stops retries and stale-data fallback. Ordinary OSM category failures can still produce an explicitly reported reduced result.

### Budget selection

The measured large WMS catalogue was approximately 40 MB, so the metadata budget is 64 MiB. The [limit table and live results](../../examples/RELIABILITY_WORKFLOWS.md#limits-and-cache-behavior) record transfer, decode, geometry and deadline limits. Tests cover known/unknown Content-Length, corruption, cache coordination and resource guards. A compressed-byte limit alone is not a decoded-memory bound; error bodies are bounded too. The recorded Overpass probe returned HTTP 502; OSM validation uses offline fixtures.

### Validation requirements

- A delayed response can be canceled during headers, body transfer and retry delay. Subsequent requests can complete independently.
- Cancellation never becomes an automatic endpoint retry or a stale-data success. An obsolete solve cannot publish over the current one.
- Interrupted, oversized and malformed responses leave no reusable partial file. A retry, concurrent reader/writer and existing good cache entry behave correctly.
- Canceling one metadata waiter does not cause another caller to consume a canceled cached task.
- Valid WMS image-size fallback, WCS single/multipart terrain and reported OSM category failures retain their behavior.
- Offline HTTP fixtures assert bounded reads and publication state rather than fragile stopwatch thresholds. Record local measurements separately.

Offline fixtures cover requests, file publication and cache recovery. Interactive
retry, obsolete-solve output and map transitions require the pending native
Rhino checks.

## Proposed local terrain windows

### Evidence and scope

Local TIFF terrain still calls `TerrainRasterReader.ReadRaster` for the full grid. The ATOM TIFF path also reads the full raster before `TerrainRasterMosaic.Crop`. Allocation guards prevent oversized elevation arrays; they do not make that path windowed. Text grids already have a `ReadWindow` path used by ATOM, which can be reused where appropriate.

Start with a shared TIFF window reader used by direct local terrain and ATOM. Keep the full-raster API for callers that need it. Avoid changing TIFF decoding libraries or inventing a second placement/NoData policy.

### Implementation sequence

1. Read georeferencing and dimensions first. Map the study area conservatively into the source pixel grid using shared CRS and inverse-affine logic. Account for rotation, shear and border samples needed for meshes and height sampling. Detect non-overlap before allocating elevation data.
2. Allocate the selected sample window within existing limits. Decode each intersecting tile or strip once, reuse buffers and copy only the needed samples. A compressed strip can be wider than the selected area; enforce block budgets and state that limitation rather than promising arbitrary random access.
3. Preserve exact pixel-center origin, affine coefficients, sample spacing, NoData and the shared elevation baseline. Keep the current rejection of incompatible ATOM mosaic grids. Do not introduce automatic coarsening that hides missing samples.
4. Pass cancellation through block reads and sample-copy loops. Key reusable source windows by file revision/sidecars, CRS and pixel window; keep source samples separate from context-dependent Rhino geometry. Maintain correct invalidation when a sidecar changes.
5. Reuse the text-grid window reader in the direct local path where it improves retained memory. Do not promise random byte access for sequential ASCII/CSV files.

### Acceptance and efficiency evidence

Compare selected samples and placement against the full reader for north-up, rotated, sheared, different-CRS and edge windows. Cover tiles, compressed strips, NoData, non-overlap, selected-block truncation, cancellation and adjacent ATOM seams. An unread block outside the selected area need not be decoded; invalid structural metadata still fails.

Use a large synthetic raster with a small study window and record decoded block counts, allocated sample count, managed allocation measurements and elapsed time, both cold and repeated. Memory should reflect the output window and necessary decode blocks, not a full elevation array. Report measurements without claiming they bound total native Rhino/decoder memory.

## Proposed remote BigTIFF support

The pinned LibTiff already passes a tiny local BigTIFF test. That does not prove remote tiled imagery/elevation, masks, overviews or offsets above 4 GiB. Begin with a feasibility fixture using the pinned library and existing range stream; change a dependency only if that probe demonstrates a need.

Extend the bounded header/directory preflight for the [BigTIFF layout](https://libtiff.gitlab.io/libtiff/specification/bigtiff.html), retaining LibTiff as the decoder. Validate offset size and reserved fields, 64-bit counts/offsets, checked arithmetic, directory cycles, file bounds and metadata-allocation limits before handing directories to the decoder. Keep existing range, representation-validator, time and output budgets. Do not add a whole-file download fallback.

Use a sparse virtual HTTP fixture with selected blocks above 4 GiB, without allocating or downloading a multi-gigabyte file. Test both byte orders, supported image/DEM tiles, masks/overviews, header-only references, malicious counts, truncation, mutation, cancellation and range refusal. Retain classic-TIFF coverage. Correct the current remote error's implication that the decoder itself cannot read BigTIFF; diagnostics should name the actual unsupported path or layout.

## Validation and measurements

`reliability-check` contains 52 offline cases, including delayed/truncated HTTP
responses, cancellation, resource limits, concurrent downloads and cache recovery.
The [source catalogue](../TEST_SOURCES.md) and [service guide](../../examples/RELIABILITY_WORKFLOWS.md)
record public requests, exact inputs and measured transport allocations.

Local-window validation requires a georeferenced NoData fixture and sample/block
comparisons with the full reader. BigTIFF validation requires synthetic 64-bit
range fixtures. Generated fixtures, provider requests and native Rhino results
are separate evidence. Measurements cover the tested allocations and transfers,
not total Rhino or decoder memory.

The [release checklist](../../examples/VALIDATION.md) covers component compatibility,
native preview, units, saved definitions and installed assemblies. Cache cleanup
must protect textures referenced by saved or baked models; automatic asset
removal is not part of the current cache behavior.
