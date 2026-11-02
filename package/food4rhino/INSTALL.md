# Install RhinoSpatial

RhinoSpatial 0.4.0 runs in Rhino 8 and Grasshopper on macOS and Windows. This guide describes the public package. Newer features on the repository's `main` branch need a development build; see the [version guide](https://github.com/PascalNun/RhinoSpatial/blob/main/docs/WORKING_WITH_PROJECT_DATA.md#release-and-development-features).

## Package Manager

1. Open Rhino 8 and run `PackageManager`.
2. Search for `RhinoSpatial` and install it.
3. Restart Rhino or Grasshopper if requested.
4. Open Grasshopper and look for the `RhinoSpatial` tab.

Version 0.4.0 is available without enabling pre-releases. You can also find the package on [Food4Rhino](https://www.food4rhino.com/en/app/rhinospatial?lang=en).

## Manual ZIP installation

Download the release ZIP from [GitHub Releases](https://github.com/PascalNun/RhinoSpatial/releases).

1. Close Rhino before replacing plugin files.
2. Copy the plugin and its accompanying files into your Grasshopper Libraries folder.
3. Keep the `.gha` and all dependency `.dll` files together. When updating, replace the previous RhinoSpatial files as a set so the assemblies match.
4. Start Rhino 8, open Grasshopper and look for the `RhinoSpatial` tab.

Typical Libraries locations are:

- **macOS:** `~/Library/Application Support/McNeel/Rhinoceros/8.0/Plug-ins/Grasshopper (<version>)/Libraries`
- **Windows:** `%AppData%\Grasshopper\Libraries`

Restart Rhino if the files were copied while Grasshopper was open.

### Package files

The public package contains `RhinoSpatial.gha`, `RhinoSpatial.Core.dll`, dependency assemblies, runtime/dependency metadata and documentation. Copy the complete set rather than only the `.gha`.

The 0.4.0 libraries include BitMiracle.LibTiff.NET, NetTopologySuite and its feature/Shapefile helpers, ProjNET and SharpGLTF. Their notices are included in `THIRD-PARTY-NOTICES.md`, alongside `LICENSE`, `README.md` and `CHANGELOG.md`. Development builds can contain additional dependencies listed in the repository notices.

## First study

1. Place `Spatial Context` in Grasshopper.
2. Connect a Button to `Open Map` and select a small area.
3. Add one source component and connect the same context.
4. Inspect the result, then add the other layers you need.

Try maps with project vectors, terrain with buildings, or OSM for quick context. You can orient the map from a supported `Reference Source` before choosing the area. Data is placed near Rhino's origin by default.

The [visual workflow](https://github.com/PascalNun/RhinoSpatial/blob/main/docs/SHOWCASE.md) and [example definitions](https://github.com/PascalNun/RhinoSpatial/tree/main/examples) provide starting points. [Working with project data](https://github.com/PascalNun/RhinoSpatial/blob/main/docs/WORKING_WITH_PROJECT_DATA.md) explains source choices, coordinates and common status messages.

## If a layer does not load

Read the component's `Status`, then check the source, selected layer and study-area coverage. Start with a smaller area when detailed data takes longer than expected. Broad fallback imagery and terrain help with orientation; use official or project-specific data when the study needs it.

Google 3D Tiles require your own Google Maps Platform setup and provide temporary visual reference. Follow the [viewer guidance](https://github.com/PascalNun/RhinoSpatial/blob/main/docs/SHOWCASE.md#google-3d-tiles-reference); this is not an editable, bake or export workflow.

For installation problems, report the plugin/Rhino versions, operating system and exact message through [GitHub Issues](https://github.com/PascalNun/RhinoSpatial/issues). Keep private project data and API keys out of reports.
