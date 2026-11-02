# RhinoSpatial examples

Start with the definition closest to your task, connect your own project sources, and use one shared `Spatial Context` for every layer. These examples are meant to be adapted to different places and datasets.

New to the toolkit? Follow the [visual workflow](../docs/SHOWCASE.md). If you are choosing between a service, download and local file, read [Working with project data](../docs/WORKING_WITH_PROJECT_DATA.md). The [documentation index](../docs/README.md) brings the guides and references together.

## Choose a starting point

| What you want to explore | Grasshopper definition |
| --- | --- |
| The complete toolkit in one study | [00 — Full reference workflow](gh/00-rhinospatial-reference-workflow.gh) |
| A map background with parcels, boundaries or other vectors | [01 — WMS and WFS basics](gh/01-wms-wfs-basics.gh) |
| A project raster with ground elevation | [02 — GeoTIFF and terrain](gh/02-geotiff-terrain.gh) |
| Official building and roof geometry | [03 — LoD2 buildings](gh/03-lod2-buildings.gh) |
| Quick urban and landscape context | [04 — OSM context](gh/04-osm-context.gh) |
| Photogrammetric visual comparison | [05 — Google 3D Tiles reference](gh/05-google-3d-tiles-reference.gh) |

Open a file link, then use GitHub's **Download raw file** button to save the `.gh` definition. The [full example](https://github.com/PascalNun/RhinoSpatial/raw/main/examples/gh/00-rhinospatial-reference-workflow.gh) is also available as a direct download.

The raster definition contains a placeholder for your own georeferenced image. The Google example contains no API key; add your own key only to a private working copy. The viewer provides temporary visual reference, with the usage guidance in the [workflow guide](../docs/SHOWCASE.md#google-3d-tiles-reference).

## Use an example

1. Install RhinoSpatial and open the chosen definition in Grasshopper.
2. Replace source URLs, layer names or local paths with suitable project data.
3. Open `Spatial Context` and select a small area that the sources cover.
4. Check one layer, then add the others using the same context.
5. Read each component's `Status` if the result is empty or unexpected.

Keep the original definition as a reference and save your project as a separate copy. Public services can change; official or project-specific data is usually the best starting point for project work.

If a saved definition reports an IO archive warning, check that the installed plugin matches the definition. Current component names and connections are preserved. Report a repeatable warning with the version and affected component rather than assuming that it is normal.

## Additional workflows on main

The public release is **0.4.0**. The following recipes require a development build from `main`; their features are merged but have not been published in that package.

| What you want to try | Guide and sample data |
| --- | --- |
| Discover API collections, load direct vector/building downloads, or use terrain tile feeds | [Services and download workflows](COMPATIBILITY_WORKFLOWS.md) |
| Use CityJSON detail/sequence/templates, a rotated image or tiled map imagery | [Building detail and imagery workflows](BUILDING_AND_IMAGERY.md), with [small local fixtures](fixtures/medium) |
| Check slow, interrupted and oversized service requests | [Service reliability workflows](RELIABILITY_WORKFLOWS.md), with a local fixture server |
| Load a remote raster window or choose layers in a GeoPackage | [Remote rasters and GeoPackage workflows](REMOTE_RASTERS_AND_GEOPACKAGE.md), with [small local fixtures](fixtures/low) |

These recipes keep exact URLs, coordinates, sample results and commands together for reproducibility. The same workflow can be used with another supported source. Sample files are synthetic unless the guide identifies a public provider.

A city or provider named in a recipe is a worked example. Use a source that covers
your own study area and matches the format and coordinate requirements in the
reference. Availability and detail depend on that source.

For an overview of what is in the release and what is on `main`, see the [version guide](../docs/WORKING_WITH_PROJECT_DATA.md#release-and-development-features).

## Sources and validation

- [Source list](sources.json) — saved URLs, sample paths and study bounds.
- [Test source catalogue](../docs/TEST_SOURCES.md) — broader provider coverage and recorded limitations.
- [Validation checklist](VALIDATION.md) — maintainer checks for alignment, preview, materials and saved definitions.
- [Component reference](../docs/COMPONENT_REFERENCE.md) — exact supported formats and inputs.

For maintainers, validate a complete study as well as an isolated source: imagery, vectors, terrain and buildings should keep their shared placement. Record whether a result was checked in Rhino, through a core probe or only from metadata. Those checks answer different questions.

Maintainers can find small correctness regression inputs, including deliberately invalid terrain, in the [reader research fixtures](fixtures/research/README.md), with findings in the [reader validation report](../docs/technical/READER_VALIDATION.md). These are separate from the working examples above.
