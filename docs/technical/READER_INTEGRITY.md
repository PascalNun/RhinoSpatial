# Reader integrity and coordinate handling

Development readers check terrain completeness, preserve polygon boundaries and
use one coordinate-identifier parser. The public package remains 0.4.0. The
[validation findings](READER_VALIDATION.md) document the original reproductions.

## Terrain completeness

Each decoded row and tile is checked before use. Dimensions, sample types and
memory requirements are validated before allocating elevations. Metadata and
decode loops share the open TIFF and reusable buffers. Valid compressed files,
edge tiles and genuine NoData remain supported; unread data cannot be replaced
by a previous row or artificial zeroes.

## Polygon boundaries

GeoJSON, GML and Shapefile readers retain exterior and interior rings, matching
GeoPackage. Curves remain associated with their source feature and attributes
in the existing Grasshopper tree. Nested GML surface patches are traversed
without duplicate rings. The outputs are boundary curves, not filled surfaces.

## Coordinate identifiers

One parser handles complete EPSG names, OGC URNs/URLs, CRS84 and supported AdV
aliases. Unknown identifiers and invalid transformations return explicit errors.
Existing axis conventions and source heights are preserved.

Vector operations and Shapefile reads prepare one transform per operation.
Supported immutable UTM definitions use a bounded cache; mutable transforms are
not shared globally. This avoids repeated setup without coupling unrelated reads.

## Validation

`correctness-check` contains 52 offline regressions for original failures and
valid neighbouring cases, including compressed terrain, edge tiles, NoData,
polygon holes/islands, coordinate aliases and source heights. The cases run in
macOS/Windows CI. Diagnostic inputs and expected outcomes are documented in the
[fixture guide](../../examples/fixtures/research/README.md).

Native preview, model units, baking, saved definitions and installed-package
behavior require the [Rhino checklist](../../examples/VALIDATION.md).
Windowed local terrain reads and remote BigTIFF remain planned work; service
cancellation is covered by [service reliability](SERVICE_RELIABILITY.md).
