# RhinoSpatial documentation

RhinoSpatial brings maps, project vectors, terrain, imagery and 3D buildings into
one Rhino and Grasshopper study. Choose an area with `Spatial Context`, connect
the sources your project needs, and work with their aligned outputs.

The public package is **0.4.0**. Some guides also describe development features
on `main`; the [version overview](WORKING_WITH_PROJECT_DATA.md#release-and-development-features)
explains which build you need.

## Using RhinoSpatial

| What you want to do | Where to start |
| --- | --- |
| Install the plugin | [Installation guide](../package/food4rhino/INSTALL.md) |
| Assemble a first site study | [Visual workflow](SHOWCASE.md) |
| Choose and connect project sources | [Working with project data](WORKING_WITH_PROJECT_DATA.md) |
| Adapt a Grasshopper definition | [Examples](../examples/README.md) |
| Look up formats, inputs and output behavior | [Component and source reference](COMPONENT_REFERENCE.md) |
| Understand what changed | [Changelog](../CHANGELOG.md) |
| Get help with a source or workflow | [Help and support](../.github/SUPPORT.md) |

## Find a component

| Part of the study | Reference |
| --- | --- |
| Study area and shared placement | [Spatial Context](COMPONENT_REFERENCE.md#spatial-context) |
| Parcels, boundaries, roads and other project vectors | [Load WFS](COMPONENT_REFERENCE.md#load-wfs-vector-and-feature-data) |
| Maps and orthophotos from a service | [Load WMS](COMPONENT_REFERENCE.md#load-wms-maps-and-imagery) |
| Georeferenced project imagery | [Load GeoTIFF](COMPONENT_REFERENCE.md#load-geotiff-georeferenced-rasters) |
| Ground and elevation | [Load Terrain](COMPONENT_REFERENCE.md#load-terrain-elevation-and-ground-surfaces) |
| Building volumes and roof geometry | [Load LoD2 Buildings](COMPONENT_REFERENCE.md#load-lod2-buildings-official-3d-building-context) |
| Quick urban and landscape context | [Load OSM](COMPONENT_REFERENCE.md#load-osm-lightweight-contextual-geometry) |
| Photogrammetric visual comparison | [Google 3D Tiles Viewer](COMPONENT_REFERENCE.md#3d-tiles-viewer-google) |

Choose data appropriate to the project and its location. Provider URLs and
sample areas in the examples demonstrate particular workflows; they do not
define the plugin's geographic coverage. Coverage and detail depend on the
source, its coordinate system and the supported format.

## Project direction and maintenance

The [project scope](PROJECT_SCOPE.md) explains the design principles and product
boundaries. The [roadmap](ROADMAP.md) records current priorities, and
[Contributing](../CONTRIBUTING.md) covers code, documentation and validation.

Maintainers can use the [test source catalogue](TEST_SOURCES.md) for exact URLs
and recorded source behavior, and the [validation checklist](../examples/VALIDATION.md)
for checks in Rhino. Automated test results and native Rhino observations are
recorded separately.

<details>
<summary>Technical documentation</summary>

These pages explain supported formats, design constraints, validation methods
and measured limits.

- [Service and download compatibility](technical/SOURCE_COMPATIBILITY.md)
- [Building and imagery formats](technical/BUILDING_AND_IMAGE_FORMATS.md)
- [Remote rasters and GeoPackage](technical/REMOTE_RASTERS_AND_GEOPACKAGE.md)
- [Reader correctness](technical/READER_INTEGRITY.md)
- [Service reliability and remaining raster work](technical/SERVICE_RELIABILITY.md)
- [Source compatibility review](technical/SOURCE_COMPATIBILITY_REVIEW.md)
- [Reader validation findings](technical/READER_VALIDATION.md)

</details>
