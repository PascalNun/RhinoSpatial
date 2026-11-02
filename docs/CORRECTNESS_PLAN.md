# Correctness and everyday reliability

This is the implementation plan for the three high-priority findings in [the research review](NEXT_WORK_RESEARCH.md). It also covers efficiency and keeping the code easy to maintain. These changes are for the development build from `main`; they do not replace the published 0.4.0 package.

1. **Reject incomplete terrain data.** Check each decoded row and tile before using it. Validate raster dimensions, sample types and memory requirements before allocating the elevation array. Reuse the open TIFF for metadata and reuse decode buffers. Keep valid compressed files, edge tiles and NoData working.
2. **Keep courtyard boundaries.** Preserve exterior and interior rings in GeoJSON, GML and Shapefiles, as GeoPackage already does. Keep the existing feature grouping and Grasshopper outputs. Read GML surface patches once, without duplicating their rings.
3. **Read coordinate identifiers precisely.** Use one shared parser for complete EPSG identifiers, OGC URNs and URLs, CRS84 aliases and the supported AdV names. Reject unsupported transformations explicitly. Keep the established axis-order rules and source heights. Prepare vector transformations once per batch rather than once per vertex.

Add offline regressions for both the failures and valid neighbouring cases. Then run the Release build, every Sandbox check, documentation links and the component-interface comparison. Integrate passing work into `main` and check the macOS and Windows CI jobs. A native Rhino visual check remains a separate validation step; this work does not publish or install a release.

## Implementation and validation

The three items are implemented in the development build. Polygon boundaries use the existing feature curve collection, keeping the Grasshopper tree contract. One identifier parser replaces the duplicated substring helpers; vector batches and Shapefile reads prepare their transform once. Supported UTM definitions use a bounded cache; transform instances belong to each operation. Terrain decoding reuses one buffer and reads metadata from the same open TIFF. No dependency was added.

`correctness-check` adds 52 offline regressions. The Release build and all ten suites pass locally; the existing macOS/Windows CI workflow includes the new suite. Original diagnostic inputs and expected outcomes are documented in the [fixture guide](../examples/fixtures/research/README.md).

Native Rhino preview, actual model units, baking, saved definitions and installed-package validation remain pending. Full-file local DEM windowing, broader cancellation and remote BigTIFF are later work. No plugin was installed and no release or version changed.
