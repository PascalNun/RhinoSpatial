# Building and imagery formats

Development builds support additional CityJSON representations, affine raster
placement and WMTS imagery through the existing components. These features are
separate from the public 0.4.0 package. Start with
[Working with project data](../WORKING_WITH_PROJECT_DATA.md) for source selection.

## CityJSON representation

The reader selects one numeric LoD per CityObject: the highest supplied level
at or below 2.2, or the lowest higher level when none is available below that
threshold. All geometry parts at that level are retained. Hierarchy-wide LoD
selection and material/texture import are not supported.

CityJSONSeq records share header transform, CRS and template metadata while
keeping independent vertex indices. GeometryInstance uses the specified matrix
and anchor. Local files, archives and validated downloads use the same reader;
malformed indices, transforms and unsupported selected primitives fail explicitly
rather than returning partial rings.

## Raster placement

Embedded TIFF and world-file imagery use a shared finite, invertible affine
transform. Embedded spatial metadata takes precedence. Sidecars require a
resolvable CRS and participate in cache invalidation.

PixelIsArea/PixelIsPoint conventions determine sample placement. Projection,
clipping and localization preserve the true footprint and texture coordinates.
Large-image projection uses the relevant source window, with UVs relative to
the original image. Terrain uses the same affine sample centers; incompatible
terrain mosaic grids remain unsupported.

## WMTS delivery

The existing WMS components handle WMTS capabilities, layer discovery, matrix
selection and bounded tile mosaics. Requests respect coordinate axes, matrix
limits, styles, supported formats and dimension defaults. KVP requests and
advertised REST templates are supported. Unknown CRS and failed required tiles
return clear errors. Reference metadata reads capabilities without loading tiles.

## Decoder dependency

Image decoding uses managed `StbImageNext` 1.0.0. Its assembly and license must
accompany the plugin; it introduces no native image-decoder binary. See
[Third-Party Notices](../../THIRD-PARTY-NOTICES.md).

## Validation

`medium-compatibility-check` contains 27 offline cases covering LoD selection,
templates, sequences, malformed geometry, affine rotation/shear, pixel centers,
CRS, sidecar invalidation, WMTS grids, limits, URLs, cancellation and failures.
Component GUIDs and parameter layouts are unchanged.

Recorded 2026-11-02: PDOK exposed 19 layers and six matrix sets. A 36-tile JPEG
request at 0.42 m/pixel produced a 1536 × 1536 image and a 512-triangle Core mesh.
Cache reuse and image continuity were checked. A public multi-LoD file parsed
ten buildings/348 surfaces at LoD 2.2; without CRS metadata, it is a parser-only
case. The declared-CRS Delft sample parsed 160 buildings/5,563 surfaces at LoD1.
Bundled building/sequence/template files each parsed one six-surface building;
the rotated image produced a Core mesh.

The [example guide](../../examples/BUILDING_AND_IMAGERY.md) saves these
inputs and six declared-CRS fixtures. Native materials, UVs, baking, saved
definitions and installed assemblies remain on the
[Rhino checklist](../../examples/VALIDATION.md#building-and-imagery-formats-development).
