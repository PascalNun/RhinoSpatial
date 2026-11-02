# Next reliability work

Planning baseline: `main` at `5ad091d`, 2026-11-02. The three [correctness fixes](CORRECTNESS_PLAN.md) are complete, with 52 new regressions and all ten suites passing on macOS and Windows. The work below is planned, not implemented. The public package remains 0.4.0.

The next improvements should make ordinary studies easier to stop, retry and load efficiently. Keep the existing components and one shared spatial context. Prefer small shared helpers and measured improvements over a broad rewrite.

## Order and deliverables

| Order | Work | Deliverable |
| --- | --- | --- |
| Before placement changes; complete before release | Verify units and complete-study behavior in Rhino | Recorded measurements, actual host/runtime details and a clear units policy if a change is needed |
| Next code batch | Cancellation, bounded downloads and reliable cache publication | WMS, WCS and OSM stop work consistently and retry without reusing incomplete files |
| Following code batch | Read local terrain windows | A small selected area avoids allocating the full TIFF elevation grid |
| Then | Remote BigTIFF | Bounded access to supported large TIFF imagery/elevation products, after proving decoder and offset handling |

Native validation can proceed alongside Core work. It is a release requirement, not a reason to block independent transport fixes. Each code batch should have its own regressions, documentation and passing integration into `main`.

## Check units and the actual Rhino workflow

Use a synthetic study with a known 100 m horizontal distance, a 10 m height difference, a courtyard and a terrain NoData gap. Combine imagery, vectors, terrain and buildings through one context. Repeat in metre, millimetre and foot documents, in localized and absolute modes, then save/reopen and check supported bake paths. Change connection order to check the shared elevation baseline.

Record physical measurements as well as coordinate values. The absence of explicit document-unit conversion in the inspected placement code is an investigation finding, not yet a reproduced units bug. If scaling needs correction, define how model units interact with absolute source coordinates before implementing it. Apply an agreed correction in shared placement, including image meshes and height sampling; avoid independent fixes in each loader.

Record the Rhino build, operating system, active .NET runtime and loaded plugin/dependency versions. Use a separate test setup and copies of saved definitions. Verify packaged SQLite/image dependencies in the Rhino host on macOS and Windows. Windows console CI does not establish Windows Rhino compatibility. Assess Rhino 9 separately; do not retarget or drop Rhino 8 support solely because the Sandbox runs on .NET 10. See [McNeel's runtime guidance](https://developer.rhino3d.com/guides/rhinocommon/moving-to-dotnet-core/).

## Batch 1: stop and retry reliably

### Evidence and scope

`WmsClient`, `WcsClient` and `OsmClient` have finite HTTP timeouts, but their older public request methods do not take a caller cancellation token. WCS buffers the whole coverage response. WMS writes an image to its final cache filename before the transfer is complete. OSM retry/category/stale-cache handling catches general exceptions; cancellation must remain distinguishable when token support is added. These are code-review findings, not a reproduced diagnosis of the earlier Rhino crash.

Cover service metadata, layer listing, reference metadata, downloads, retries and the corresponding Grasshopper callers. Include checks around parsing and geometry batches. Preserve existing WFS axis behavior, WMS image-size retries, WCS multipart support and explicit OSM category-failure reporting.

### Implementation sequence

1. Add injectable HTTP clients and token-aware overloads while retaining existing public method signatures. Reuse or extend `SourceHttp` and existing download/cache patterns; keep protocol-specific parsing and retry decisions in their clients.
2. Use headers-first HTTP requests and bounded stream reads. Pass cancellation through body reads, temporary-file writes and retry delays. Apply an operation deadline across retries and fallback endpoints, rather than restarting the entire allowance for each attempt. Cancellation needs to cover the body transfer as well as receiving headers; the [HTTP API](https://learn.microsoft.com/en-us/dotnet/api/system.net.http.httpclient.sendasync?view=net-7.0) provides token-aware requests.
3. Stream binary payloads to temporary files. Validate transfer length when declared, actual format and relevant image/raster metadata before promoting the file. Check decoded dimensions as well as encoded bytes. Preserve a good cached file when a replacement fails. Dispose responses and remove temporary files on every failure path.
4. Keep WCS multipart handling bounded. Prefer a bounded spool file and offsets to the coverage part, avoiding several full-response byte-array copies. Do not trade single-part correctness for multipart support or add an unbounded fallback.
5. Cache completed metadata successfully, with bounded lifetime/entry count. Do not retain faulted or canceled tasks. Use cancellation-aware per-source coordination so canceling one caller does not poison another caller's request. Preserve case-sensitive URL path/query identity. Keep simultaneous writers from exposing or overwriting incomplete results.
6. Thread the operation token through the components and their synchronous fallback paths. Check cancellation before fallback, retry, expensive conversion and publication. Audit whether request identity/generation is also needed to prevent an older solve from replacing newer output. Keep progress dispatch asynchronous to preserve the macOS deadlock fix.
7. Report cancellation, timeout, size limits and provider errors distinctly. Deliberate cancellation must stop endpoint retries and stale-cache fallback. Ordinary OSM category failures may still produce the existing explicitly reported reduced result; do not turn that behavior into a silent partial-success policy for other sources.

### Budget selection

Before fixing new public limits, measure encoded bytes, decoded dimensions and record counts for the existing bounded regression sources, including a large WMS catalogue and multipart WCS. Then record named limits for metadata, image pixels, coverage bytes, OSM records/coordinates, attempts and total elapsed work. Test just below and above each limit, including responses without a trustworthy Content-Length. A compressed-byte limit alone is not a decoded-memory bound. Keep error bodies bounded too.

### Acceptance

- A delayed response can be canceled during headers, body transfer and retry delay. The next operation succeeds without restarting Rhino.
- Cancellation never becomes an automatic endpoint retry or a stale-data success. An obsolete solve cannot publish over the current one.
- Interrupted, oversized and malformed responses leave no reusable partial file. A retry, concurrent reader/writer and existing good cache entry behave correctly.
- Canceling one metadata waiter does not cause another caller to consume a canceled cached task.
- Valid WMS image-size fallback, WCS single/multipart terrain and reported OSM category failures retain their behavior.
- Offline HTTP fixtures assert bounded reads and publication state rather than fragile stopwatch thresholds. Record local measurements separately.

## Batch 2: load only the needed terrain

### Evidence and scope

Local TIFF terrain still calls `TerrainRasterReader.ReadRaster` for the full grid. The ATOM TIFF path also reads the full raster before `TerrainRasterMosaic.Crop`. The recent allocation guards prevent oversized elevation arrays; they do not make that path windowed. Text grids already have a `ReadWindow` path used by ATOM, which can be reused where appropriate.

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

## Batch 3: extend remote rasters to BigTIFF

The pinned LibTiff already passes a tiny local BigTIFF test. That does not prove remote tiled imagery/elevation, masks, overviews or offsets above 4 GiB. Begin with a feasibility fixture using the pinned library and existing range stream; change a dependency only if that probe demonstrates a need.

Extend the bounded header/directory preflight for the [BigTIFF layout](https://libtiff.gitlab.io/libtiff/specification/bigtiff.html), retaining LibTiff as the decoder. Validate offset size and reserved fields, 64-bit counts/offsets, checked arithmetic, directory cycles, file bounds and metadata-allocation limits before handing directories to the decoder. Keep existing range, representation-validator, time and output budgets. Do not add a whole-file download fallback.

Use a sparse virtual HTTP fixture with selected blocks above 4 GiB, without allocating or downloading a multi-gigabyte file. Test both byte orders, supported image/DEM tiles, masks/overviews, header-only references, malicious counts, truncation, mutation, cancellation and range refusal. Retain classic-TIFF coverage. Correct the current remote error's implication that the decoder itself cannot read BigTIFF; diagnostics should name the actual unsupported path or layout.

## Examples, documentation and completion

Keep exact inputs and results in the existing [source catalogue](TEST_SOURCES.md) and example recipes. Batch 1 should add reproducible delayed/truncated HTTP fixtures; batch 2 a georeferenced terrain window/NoData example; batch 3 a synthetic BigTIFF range fixture. Label generated, public-provider and native Rhino evidence separately. Reuse recorded public URLs and do not label them newly verified without running the request.

For each implemented batch: run the Release build, all existing suites plus the new regressions, the component-contract comparison, documentation links and relevant native checks. Record the measured result and any untested host behavior. Integrate passing work into `main`, verify macOS/Windows CI, and update the [release checklist](../examples/VALIDATION.md). Planning does not authorize installation, runtime retargeting or a new publication.

Cache size/cleanup controls remain later work. Preserve texture assets referenced by saved or baked models before designing eviction. New provider browsers, STAC selection UI and unrelated source categories are outside this batch.
