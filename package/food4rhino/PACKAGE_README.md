# RhinoSpatial

RhinoSpatial is a Grasshopper toolkit for bringing maps, terrain, buildings, imagery, and geodata into one aligned Rhino site context.

Define a study area once with `Spatial Context`, then connect the data layers needed for site analysis, context modeling, concept design, or early-stage studies.

## Install

The recommended installation method is Rhino 8's Package Manager:

1. Open Rhino 8.
2. Run `PackageManager`.
3. Enable **Include pre-releases** while RhinoSpatial remains in alpha.
4. Search for `RhinoSpatial`.
5. Install the package and restart Rhino or Grasshopper if requested.

For manual installation from this package, read [INSTALL.md](INSTALL.md).

## First workflow

1. Open Grasshopper and place `Spatial Context`.
2. Connect a Button to `Open Map`.
3. Select one reasonably small study area.
4. Connect the same `Spatial Context` output to one or more source components.

Good starting points are:

- `Load WMS` and `Load WFS` for imagery and vector data
- `Load GeoTIFF` and `Load Terrain` for local raster and elevation context
- `Load LoD2 Buildings` for official 3D building geometry
- `Load OSM` for lightweight urban context
- `3D Tiles Viewer (Google)` for optional visual reference using your own Google Maps API key

By default, RhinoSpatial localizes data near Rhino's origin so aligned geometry remains practical to view and model.

## Documentation and support

- [Website and user guide](https://pascalnun.eu/tools/rhinospatial/)
- [GitHub documentation](https://github.com/PascalNun/RhinoSpatial)
- [Visual workflow](https://github.com/PascalNun/RhinoSpatial/blob/main/docs/SHOWCASE.md)
- [Downloadable examples](https://github.com/PascalNun/RhinoSpatial/tree/main/examples)
- [Component and Source Reference](https://github.com/PascalNun/RhinoSpatial/blob/main/docs/COMPONENT_REFERENCE.md)
- [Releases](https://github.com/PascalNun/RhinoSpatial/releases)
- [Issues and support](https://github.com/PascalNun/RhinoSpatial/issues)

RhinoSpatial is in active alpha development. Provider-specific services, coordinate systems, geometry types, API access, and local-file metadata can still expose edge cases. For project work, prefer official or project-specific data sources.

RhinoSpatial is released under the [MIT License](https://github.com/PascalNun/RhinoSpatial/blob/main/LICENSE). Bundled dependency licenses are documented in the [Third-Party Notices](https://github.com/PascalNun/RhinoSpatial/blob/main/THIRD-PARTY-NOTICES.md). Both files are also included beside this README in the release package.
