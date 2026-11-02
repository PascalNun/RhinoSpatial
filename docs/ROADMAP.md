# RhinoSpatial roadmap

RhinoSpatial's direction is to make everyday site-context work more dependable: choose one area, connect the project's sources, and work with aligned maps, terrain and geometry in Rhino.

The main source categories are established. Future work will focus on compatibility, output quality, performance and usability within those workflows. See [Working with project data](WORKING_WITH_PROJECT_DATA.md) for user guidance and the [project scope](PROJECT_SCOPE.md) for design principles.

## Released and in development

**0.4.0 is the public release.** It provides the shared study-area workflow, vector and map services, local project data, terrain, LoD2 buildings, OSM and optional Google 3D visual reference. Supported building ATOM feeds can select and download tiles for the study area.

The compatibility work below is merged into `main` for the next release:

| Area | What improves |
| --- | --- |
| Services and downloads | Feature paging, API collection discovery, direct vector/building downloads, terrain ATOM and source metadata |
| Building and image formats | CityJSON detail selection, sequences and templates; affine/world-file imagery; WMTS map services |
| Remote rasters and local vectors | COG image/elevation windows and read-only GeoPackage feature layers |

The Release build and all nine automated suites have passed on macOS and Windows. Preview, materials, baking, installed-package behavior and saved definitions still need the relevant Rhino checks before publication. Merging code into `main` does not change the installed plugin or published packages.

Implementation records remain available for [services and downloads](COMPATIBILITY_IMPLEMENTATION_PLAN.md), [building and imagery formats](MEDIUM_COMPATIBILITY_PLAN.md), and [COG/GeoPackage](LOW_COMPATIBILITY_PLAN.md). Their original priority labels describe the research order, not the user-facing structure of the toolkit.

## Immediate correctness work

The [next-work research](NEXT_WORK_RESEARCH.md) reproduces three issues that the
current green suites do not cover: incomplete terrain reads can repeat a row,
GeoJSON/GML can lose courtyard boundaries, and the shared coordinate helper can
misread an invalid identifier. Fix these and add regressions before another
round of format expansion. The research includes tiny inputs, evidence levels
and proposed acceptance checks; the fixes are not implemented yet.

Then check complete studies in Rhino, including model units, actual host runtimes,
materials and saved definitions. Consistent cancellation and bounded reads in
older source paths are the next reliability work. BigTIFF range handling is a
promising format follow-up: the shipped decoder passed a basic BigTIFF probe,
while the current remote reader still rejects the format.

## Now: validate complete studies

The next release should be judged through complete workflows as well as isolated source tests.

- Compare imagery, vectors, terrain and buildings using one shared context, in localized and absolute placement.
- Check saved definitions, map selection restoration, layer choices and cancellation in Rhino.
- Check materials, transparency, image clipping, geometry grouping and supported bake behavior.
- Verify installation and assembly resolution on macOS and Windows.
- Keep Google 3D Tiles a temporary reference preview, with the same shared placement rules.

The [validation checklist](../examples/VALIDATION.md) records the remaining work. Console checks and metadata probes provide useful evidence, but do not establish Rhino visual correctness.

## Next: improve what projects reveal

### Consistent workflows

Keep source components consistent in naming, grouping, status messages and failure handling. Improve recurring friction in complete studies, including saved state and predictable XY/Z alignment. Small, well-evidenced fixes are more useful than a growing set of speculative options.

### More representative sources

Test actual feature, image and elevation requests across different providers, coordinate systems and file structures. Keep changes in shared readers and placement logic where possible. Distinguish missing source coverage from filtering, parsing and geometry-conversion failures.

Continue collecting varied CityGML and CityJSON examples. Retain conservative building conversion: source problems should remain visible rather than being hidden by invented surfaces.

### Useful outputs

Improve road interpretation, figure-ground studies and grouping within the curated OSM workflow. Keep image materials and transparency consistent across raster components. Strengthen terrain diagnostics and shared elevation behavior, while keeping broad fallbacks small and responsive.

### Performance and maintenance

Avoid repeated requests, unnecessary transformations and geometry conversion outside the selected area. Measure costs before adding dependencies, share source/placement logic, and keep the Grasshopper components focused on the user's task.

### Documentation

Keep introductions practical and broad enough to work with different project sources. Explain necessary technical terms where they first matter; keep protocol details, budgets and test commands in reference pages. Update instructions and examples alongside behavior, and clearly separate the public release from development features.

## Later: additions with a demonstrated need

Possible follow-ups include more representative CityJSON variants, additional GeoPackage extensions, or COG arrangements such as BigTIFF after bounded 64-bit remote metadata handling is established. Output refinements should improve a recurring architectural or site-context task.

These are candidates, not promises or reasons to expand the interface before a useful project example exists.

## Dependencies and releases

Use mature libraries for TIFF decoding, topology, coordinate transforms, vector parsing and glTF. Managed image and SQLite dependencies are part of the development build; their licenses and package expectations must stay documented.

Before a release, inspect Food4Rhino and Yak package contents and size, verify intentional assemblies and supported platforms, update notices, and rebuild from the intended commit. The [contributor guide](../CONTRIBUTING.md) and [release checklist](../examples/VALIDATION.md) provide the checks.

## Product boundaries

RhinoSpatial remains a compact toolkit for aligned site context. It should keep a manageable interface, sensible defaults and clear source limitations. A full GIS desktop, unrestricted OSM query editor or large provider browser is outside the current scope. Google 3D Tiles remain a visual reference workflow with no import, extraction, bake, export or offline-cache path.
