# Service and download compatibility

Development builds extend source discovery, feature loading and file downloads
through the existing components. The public package remains 0.4.0. For the user
workflow, see [Working with project data](../WORKING_WITH_PROJECT_DATA.md).

## Feature discovery and completeness

OGC API landing pages expose collections through their advertised links. The
layer list and vector loader use those collections without additional component
parameters. WFS and OGC API requests follow supported pages within the selected
area and request limits.

The user's feature cap is distinct from a service or safety limit. Duplicate ids,
repeated pages and incomplete required pages are detected; status distinguishes
an intentional cap from an unknown total or incomplete response. Raw GML remains
available for building imports. Exact request limits and axis conventions are
in the [component reference](../COMPONENT_REFERENCE.md).

## Validated file downloads

Remote GeoJSON, supported vector archives and building files use bounded
downloads and validated cache entries. Validation covers response content,
Shapefile companions, declared coordinates, archive paths and expanded sizes.
Redirects and misleading filename extensions do not bypass format checks.

A remote building file must provide usable coordinate metadata. Unsupported
content, missing companions and incomplete transfers return explicit errors.

## Terrain feeds and reference metadata

Terrain ATOM uses shared feed selection and download infrastructure with
terrain-specific payload checks. Selected tiles retain their sample positions,
NoData and shared elevation baseline. Unsupported placement, incompatible grids
and conflicting overlap heights fail explicitly.

Feed and API references read coverage and coordinate metadata to orient the map.
They do not import geometry or imagery. Saved selections take precedence over
reference extents.

## Validation

`source-compatibility-check` contains 26 deterministic cases for discovery,
pagination, caps, totals, duplicates, redirects, cancellation, downloads,
archives, projected coordinates, terrain mosaics and metadata-only references.
Component GUIDs and input/output layouts are retained from 0.4.0.

The [worked examples](../../examples/COMPATIBILITY_WORKFLOWS.md) record exact live
inputs: API-root discovery, five-page capped feature loading, single- and
two-tile terrain, GeoJSON/Shapefile downloads and declared-CRS CityJSON. A
Delft-hosted sample failed TLS; a cjio fixture supplied the successful download
example. Both outcomes are retained in the source catalogue.

Progress, cancellation, saved definitions, installed assemblies and cross-layer
alignment require the [Rhino checks](../../examples/VALIDATION.md#service-and-download-workflows-development).
Related format behavior is documented in [building and imagery formats](BUILDING_AND_IMAGE_FORMATS.md)
and [remote rasters and GeoPackage](REMOTE_RASTERS_AND_GEOPACKAGE.md).
