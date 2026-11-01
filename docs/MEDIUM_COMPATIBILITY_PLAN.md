# Medium-priority compatibility plan

Started 2026-11-01 from `8aff1e8` on `codex/medium-source-compatibility`.
This batch builds on draft PR #6; its PR will target that branch until #6 merges.
Scope is research priorities 6–8. COG and GeoPackage remain later work.

1. **CityJSON:** select one numeric LoD per object (prefer the highest supplied LoD at or below 2.2, otherwise the lowest higher LoD), retain all geometry parts at that LoD, resolve template instances with the specified matrix/anchor, and stream CityJSONSeq records using shared header transform/CRS/templates. Support local, ZIP and validated direct downloads without new component parameters. Fail explicitly on malformed indices/transforms or unsupported primitives instead of producing partial rings.
2. **Raster georeferencing:** share a finite, invertible affine transform for embedded TIFF and world-file imagery. Respect area/point raster conventions, prefer embedded spatial metadata, require a resolvable CRS for sidecars and include sidecars in cache invalidation. Preserve the true raster footprint/UV mapping during projection, clipping and localization. Use the same transform for terrain sample centers; reject unsupported terrain mosaic grids explicitly.
3. **WMTS:** integrate capabilities/layer discovery, advertised matrix sets, bounded tile selection and mosaic loading with existing WMS components and reference metadata. Respect CRS axis order, matrix limits, styles, formats and default dimensions. Support KVP and advertised REST tile templates. Keep unknown CRS or failed required tiles explicit; do not add a provider catalogue or affect Google tiles.

## Validation and examples

- Deterministic checks for mixed LoDs, templates, sequences, invalid geometry, affine rotation/shear, pixel-center placement, missing CRS, cache invalidation, WMTS projected matrices, limits, request URLs, cancellation and failures.
- Small live provider/file probes where available; save URLs, layer/matrix choices, CRS/bounds and observed outcomes in an example guide and source manifest. Keep fixtures and live evidence distinct.
- Release build, all existing suites plus the medium-priority suite, JSON/interface/diff checks, and GitHub validation.
- Update reference/catalogue/roadmap/changelog and manual Rhino/Windows checklist. Preserve component GUIDs and parameter layouts, shared context/localization/elevation and metadata-only reference intent.
- Commit, push and open a separate draft PR. No installation, merge, version bump or public publication is part of this batch.

## Progress

- [x] Scope, upstream dependency and implementation plan recorded.
- [x] CityJSON LoD selection, templates, sequences and source routing.
- [x] Affine/world-file imagery and terrain placement.
- [x] WMTS discovery, bounded imagery and reference integration.
- [x] Examples, documentation and validation.

## Recorded results — 2026-11-02

- Implemented all three items behind existing components. Component GUIDs and input/output parameter names/order match the previous batch.
- All eight local suites passed, including 27 medium-priority checks. Release solution build passed with zero warnings/errors. Source manifest JSON, unique ids, fixture paths and diff checks passed.
- [Saved recipes](../examples/MEDIUM_COMPATIBILITY_WORKFLOWS.md) include the verified PDOK WMTS URL/layer/native bbox, a public multi-LoD parser fixture, and six bundled declared-CRS synthetic files. Model/sequence/template each parsed one building/six surfaces; the rotated image produced a Core mesh.
- Live PDOK: 19 layers, six supported sets, 36 JPEG tiles at 0.42 m/pixel, 1536 × 1536 mosaic, final clipped Core mesh 512 triangles. Cache reuse and mosaic continuity verified. Public cjio multi-LoD: ten buildings/348 surfaces at LoD 2.2; missing CRS is explicitly labeled parser-only. Existing declared-CRS Delft sample still parsed 160 buildings/5,563 surfaces at LoD1.
- Final raster review added source-window projection for large images, with a global-geographic-to-projected-context regression; UV coverage remains relative to the original image.
- Dependency: managed `StbImageNext` 1.0.0, no native/further package dependencies. Notices updated. An explicitly unreleased temporary package preview contained 17 files / 3,156,137 compressed bytes; the 62,976-byte decoder and dependency manifest were present, stale decoder and AI files absent. Public release staging was preserved.
- New interactive Rhino material/UV/bake/saved-definition and Windows checks remain in [the manual checklist](../examples/VALIDATION.md#unreleased-medium-priority-compatibility). Headless/Core probes are not proof of those native surfaces.
- COG and GeoPackage remain later work. No merge, version bump, public release or installed-plugin replacement occurred.
