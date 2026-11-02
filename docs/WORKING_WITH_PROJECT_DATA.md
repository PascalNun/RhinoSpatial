# Working with project data

A useful site study often combines several sources: an orthophoto, parcel lines, a ground surface and building geometry. RhinoSpatial brings them into the same workspace through one `Spatial Context`.

This guide explains how to choose and connect those sources. For the first map selection, follow the [visual workflow](SHOWCASE.md). For exact file formats and component options, use the [component reference](COMPONENT_REFERENCE.md).

## Choose the result first

| Your task | Start with | What you receive |
| --- | --- | --- |
| Set up a site plan | `Load WMS` + `Load WFS` | An image background with vector curves and points |
| Build a terrain study | `Load Terrain` | An elevation mesh aligned to the study area |
| Add surrounding building volumes | `Load LoD2 Buildings` | Building and roof geometry, where the source provides it |
| Use a project orthophoto or map export | `Load GeoTIFF` | An image mesh and material in its geospatial position |
| Make a quick figure-ground study | `Load OSM` | Curated building, road and landscape context |
| Compare against photogrammetry | `3D Tiles Viewer (Google)` | Temporary visual reference alongside the other layers |

Choose the sources that suit the study. A building-footprint file provides outlines; a 3D building dataset provides heights and roof geometry. An image of a map provides a background; vector data provides curves and points you can work with.

## Understand what you have

### A service URL

A service lets RhinoSpatial request data for the selected area. A provider's catalogue or information page often links to one of these services:

- **WFS or OGC API Features:** vector data such as parcels, boundaries and roads.
- **WMS or WMTS:** maps and imagery. WMTS support is in development.
- **WCS:** raster data, often elevation.
- **ATOM:** a list of downloadable data tiles. Building feeds are supported in 0.4.0; terrain feeds are added in development.

Use the service or capabilities URL supplied by the provider. The address of its ordinary web map or information page may not be a data endpoint.

For vectors and maps, connect the URL to `List WFS Layers` or `List WMS Layers`, choose a layer with Grasshopper's `List Item`, and connect the same URL and selection to the loader. A layer is a named dataset within the service, such as parcels or a particular orthophoto.

### A download link

A direct download points to a file or archive. Development builds accept supported vector and building downloads through their existing loaders. A remote Cloud Optimized GeoTIFF, or **COG**, can supply a selected image or elevation window without a whole-file download fallback.

A `.tif` URL does not by itself guarantee suitable remote access. The server and file must support the range-based workflow described in the reference. If a source is unsupported, use a suitable local file or a different supported service.

For GeoPackage, download the file first and connect its local path. Automatic remote GeoPackage loading is not included.

### A local file or folder

Connect the path to the component for the kind of data it contains:

- vector files go to `Load WFS`;
- georeferenced images go to `Load GeoTIFF`;
- elevation grids go to `Load Terrain`;
- CityGML or CityJSON building data goes to `Load LoD2 Buildings`.

Keep companion files together. Shapefiles need their companion files, and an image using a world file needs both its placement information and a known coordinate system. A GeoPackage can contain several feature layers; in development, use `List WFS Layers` to choose which to load.

The [format reference](COMPONENT_REFERENCE.md) explains supported archives, companion files and source-specific requirements.

## Connect one shared area

1. Place `Spatial Context`.
2. Optionally connect a supported source to `Reference Source` to orient the map.
3. Open the map and select a small area around the project.
4. Connect that same context to every loader in the study.
5. Load one layer and inspect it before adding the next.

`Reference Source` helps choose an area; it does not import the source data. If the definition already has a saved area, that selection wins. Change the area deliberately rather than expecting a different reference to replace it.

Start small, especially with detailed buildings or high-resolution terrain. Expand the study once the source and placement are working as expected.

## Keep coordinates and heights consistent

A **coordinate system** tells RhinoSpatial what the source's numbers mean and where they belong. You may see it called CRS or SRS, with an identifier such as `EPSG:25833`. Use the identifier supplied with the data. Changing a label does not convert coordinates.

RhinoSpatial reads coordinate information where available and transforms supported sources into the shared context. A missing or unsupported coordinate system needs to be resolved before the data can be placed reliably. The reference documents the cases where an explicit override or context fallback is supported.

Keep localized placement for ordinary modeling near Rhino's origin. Use `Use Absolute Coordinates` when the project specifically needs original coordinates, and keep all layers on the same context.

Terrain and 3D buildings can have different height references or levels of accuracy. RhinoSpatial shares a placement baseline, but that does not make different datasets equivalent or convert every vertical datum. A surface model may include trees and buildings; a bare-earth terrain model aims to describe the ground. Check the source description before relying on its heights.

## Read the result

Read `Status` when a component does not return what you expect. It can distinguish an empty study area from an unsupported format, a source failure or a request limit.

| What you see | What to check next |
| --- | --- |
| No data or no overlap | Check the selected area, layer and dataset coverage. Try a smaller area known to contain data. |
| Missing or unsupported coordinates | Check the source metadata and any required companion files. Use a supported coordinate system or prepare the data in a GIS. |
| A size or request limit | Reduce the area, choose fewer layers, or set a deliberate feature limit. |
| A download or service failure | Check the exact endpoint, access requirements and provider availability. |
| A layer that looks displaced | Confirm the shared context and coordinate information. Check height references if the issue is vertical. |
| Missing patches in terrain | Check whether the source has missing elevation samples. RhinoSpatial keeps those gaps rather than replacing them with zero-height terrain. |

A successful metadata check means the source can be identified; it does not prove that its imagery or geometry has loaded. Inspect the actual output before building the rest of the study around it.

## Release and development features

The public package is **0.4.0**. The following additions are merged into `main` for the next release and require a development build:

| Area | Added on `main` |
| --- | --- |
| Vectors | API-root discovery, bounded feature paging, direct vector downloads, local GeoPackage layers and retained courtyard boundary curves across vector files |
| Imagery | WMTS services, world-file TIFF/PNG/JPEG placement and remote COG image windows |
| Terrain | Supported ATOM tile feeds, remote COG elevation windows, shared affine placement and explicit errors for incomplete TIFF reads |
| Buildings | Direct building downloads, CityJSON detail selection, sequences and geometry templates |
| Map references | Additional source metadata, GeoPackage catalog extents, remote COG headers and consistent supported coordinate identifiers |

Development also adds consistent service cancellation, bounded downloads and validated cache reuse. See the [service reliability guide](../examples/RELIABILITY_WORKFLOWS.md) for examples and limits.

The development work has automated checks on macOS and Windows. Rhino preview, materials, baking and saved-definition checks remain part of release validation. See the [changelog](../CHANGELOG.md) and [validation checklist](../examples/VALIDATION.md) for detail.

## Try an example, then use your own data

Start with the [example overview](../examples/README.md) to choose a reusable Grasshopper definition. Small sample files and recorded public URLs help check a workflow before connecting project data.

The examples cover several providers and formats. They are reference cases, not a requirement to use a particular city or service. Follow the same area, source, layer and output steps with your own supported data. Provider-specific URLs, exact test bounds and technical probe results stay in the linked example recipes and [test catalogue](TEST_SOURCES.md).
