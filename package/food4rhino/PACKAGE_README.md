# RhinoSpatial

Bring maps, terrain, imagery, buildings and project vectors into one aligned Rhino site study. Choose an area once with `Spatial Context`, then connect the sources needed for site analysis, context modeling or early-stage design.

This package is **RhinoSpatial 0.4.0** for Rhino 8 and Grasshopper on macOS and Windows. The repository's `main` branch contains additions for the next release; those features are separate from this public package.

## Install

In Rhino 8, run `PackageManager`, search for `RhinoSpatial` and install it. Restart Rhino or Grasshopper if requested.

For manual installation from this ZIP, follow [INSTALL.md](INSTALL.md) and keep the plugin and dependency files together.

## Make a first study

1. Open Grasshopper and place `Spatial Context`.
2. Connect a Button to `Open Map` and select a small area.
3. Add a source component and connect that same context.
4. Check the output, then add the other layers your project needs.

Good starting points are maps with vector outlines (`Load WMS` + `Load WFS`), a local raster with terrain (`Load GeoTIFF` + `Load Terrain`), or official 3D buildings (`Load LoD2 Buildings`). Use `Load OSM` for quick urban and landscape context. Google 3D Tiles are optional visual reference using your own API key.

Placement near Rhino's origin is the default. Supported project data and services can orient the map through `Reference Source`; loading the actual layer is a separate step.

## What is in 0.4.0

Supported ATOM building services can select and download tiles for the study area. Coordinate recognition, terrain placement, missing-elevation handling and local vector parsing are improved. See [CHANGELOG.md](CHANGELOG.md) for the release details; its Unreleased section, when present in a development build, describes later work.

## Guides and examples

- [Visual workflow](https://github.com/PascalNun/RhinoSpatial/blob/main/docs/SHOWCASE.md) — build a study step by step.
- [Working with project data](https://github.com/PascalNun/RhinoSpatial/blob/main/docs/WORKING_WITH_PROJECT_DATA.md) — choose sources and understand coordinates and common messages.
- [Example definitions](https://github.com/PascalNun/RhinoSpatial/tree/main/examples) — starting points you can adapt to different projects.
- [Component reference](https://github.com/PascalNun/RhinoSpatial/blob/main/docs/COMPONENT_REFERENCE.md) — exact formats, inputs and limits.
- [Website](https://pascalnun.eu/tools/rhinospatial/), [releases](https://github.com/PascalNun/RhinoSpatial/releases) and [support](https://github.com/PascalNun/RhinoSpatial/issues).

Online guides label development features explicitly. Start small and read `Status` if a source does not return the expected result. Coverage, coordinates and file metadata vary; prefer official or project-specific sources when accuracy matters.

RhinoSpatial is created by **Pascal Nünninghoff** and released under the [MIT License](https://github.com/PascalNun/RhinoSpatial/blob/main/LICENSE). The package includes `LICENSE` and `THIRD-PARTY-NOTICES.md` with the dependency notices.
