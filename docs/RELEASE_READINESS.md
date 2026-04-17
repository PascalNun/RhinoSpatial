# Release Readiness

This checklist is the practical gate before cutting a new RhinoSpatial alpha.

The goal is not bureaucratic perfection. The goal is to make sure a release is
calm, trustworthy, and representative of the current core toolkit.

## Build And Packaging

- `dotnet build RhinoSpatial.sln` succeeds with no unexpected warnings or errors
- Food4Rhino zip builds successfully
- Yak package builds successfully
- release package includes `THIRD-PARTY-NOTICES.md`
- no temporary files or local editor noise are included in staged artifacts

## Core Smoke Test

Use one shared `Spatial Context` where practical.

- `Spatial Context`
  - selected area persists after save/reopen
  - map helper redraws the saved selection correctly
- `Load WFS`
  - single layer loads cleanly
  - multi-layer loading still branches by layer
- `Load WMS`
  - imagery aligns with the same selected area
  - fallback imagery still works when intended
- `Load LoD2 Buildings`
  - building geometry loads without obvious face-loss regressions
  - localized placement still behaves sensibly
- `Load Terrain`
  - terrain aligns with the same area
  - localized mode still shares a sensible local Z reference
- `Load GeoTIFF`
  - raster aligns correctly
  - clipped overlap behavior still works
  - non-overlapping files do not pretend to create valid preview geometry
- `Load OSM`
  - Buildings, Road, Water, Green, and Rail still load sensibly
  - partial OSM failure still reads gracefully in the status output

## Presentation Check

- README still matches the actual current scope
- Food4Rhino listing still matches the product direction
- icons load correctly for all components
- screenshots, if updated, reflect the current UI and outputs

## Release Recommendation

If the build, package, and core smoke test all feel stable, RhinoSpatial is in a
good state for another alpha release.

If one source still behaves inconsistently, prefer fixing that first instead of
shipping a release with known avoidable friction.
