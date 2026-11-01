# Source compatibility implementation plan

Started 2026-11-01 from 0.4.0 on `codex/source-compatibility`.
The [research audit](COMPATIBILITY_RESEARCH.md) records the full backlog.

## Authorized scope

1. **Feature completeness:** bounded OGC API and WFS pagination, explicit user
   limits, duplicate/repeated-page protection and honest completeness diagnostics.
2. **Dataset discovery:** follow OGC API landing-page and collection links; allow
   the existing layer list and vector loader to select collections without adding
   component parameters.
3. **Direct downloads:** recognize remote GeoJSON and supported vector archives,
   and extend building downloads to CityJSON. Validate formats, companion files,
   coordinate metadata and archive paths before use.
4. **Terrain ATOM:** share feed selection and validated download infrastructure
   with the building workflow; load intersecting terrain tiles, retain NoData and
   one elevation baseline, and reject unsupported or ambiguous placement.
5. **Reference metadata:** use ATOM feed coverage/CRS to orient Spatial Context
   without downloading geometry. Saved selections retain priority.

## Validation and examples

- Add deterministic HTTP fixtures for discovery, pagination, download failures,
  cache reuse, limits and cancellation, plus terrain tile placement checks.
- Run the existing six suites and the new compatibility suite, a Release build,
  JSON validation and a final diff review.
- Exercise small live provider requests where available. Record URLs, selected
  layers/collections, bounds, CRS and actual results in the source manifest and
  a copyable example guide; distinguish live results from fixture-only cases.
- Record remaining Rhino visual checks explicitly. Preserve component GUIDs,
  input/output layouts, localization and elevation rules. No Google export,
  extraction, persistent tile caching or key storage.
- Update component documentation and an Unreleased changelog. This task does not
  publish another version or change the installed plugin automatically.

## Progress

- [x] Research recorded and implementation branch created.
- [x] Feature pagination and completeness checks.
- [x] OGC API discovery and existing component integration.
- [x] Direct vector/building downloads.
- [x] Terrain ATOM loading and shared placement.
- [x] ATOM reference metadata.
- [x] Saved example URLs, documentation and final validation.

## Recorded results

- Release solution build passed with zero warnings and errors; all seven suites passed, including 26 new compatibility checks. Component GUIDs and parameter names/order were compared against the 0.4.0 baseline; source manifest JSON and unique ids were checked.
- Live results and exact reproduction inputs are saved in [the example guide](../examples/COMPATIBILITY_WORKFLOWS.md): API-root discovery, five-page capped loading, single- and two-tile DGM1 meshes, direct GeoJSON/Shapefile ZIPs and a declared-CRS CityJSON source.
- The Delft-hosted older sample failed TLS on this machine; the cjio maintainer fixture provided a successful alternative. Certificate validation was retained.
- New Rhino/Grasshopper visual checks and Windows runtime checks remain on [the manual checklist](../examples/VALIDATION.md#unreleased-source-compatibility), including progress/cancellation, saved definitions and cross-layer XY/Z alignment.
- Priorities 6–10 remain backlog items. No multi-LoD selection, CityJSON sequence/template, affine/world-file, WMTS, COG or GeoPackage implementation is claimed.
- Public release 0.4.0 and the installed plugin were not changed.
