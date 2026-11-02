# Reader research inputs

These tiny synthetic files support the [next-work research](../../../docs/NEXT_WORK_RESEARCH.md), recorded against `3e71e25` on 2026-11-02. They are diagnostic cases, separate from the ready-to-use site examples. The truncated file is intentionally invalid. Neither raster has geospatial placement metadata.

| File | Purpose | Observed result before a fix |
| --- | --- | --- |
| `courtyard.geojson` | One polygon with an exterior and an interior boundary | Core reader returned one ring instead of two |
| `courtyard.gml` | The same two-ring structure in a WFS/GML feature | Core reader returned one ring instead of two |
| `truncated-dem.tif` | 2×2 unsigned 16-bit raster; only the first row, `[10,20]`, is present | Core terrain reader logged a read error but returned `[10,20,10,20]` |
| `tiny-bigtiff.tif` | Valid 2×2 BigTIFF with values `[10,20,30,40]` | Shipped LibTiff 2.4.660 read the second row successfully |

The GeoJSON case can be inspected with the existing probe:

```sh
dotnet run --project RhinoSpatial.Sandbox.csproj -c Release -- geojson examples/fixtures/research/courtyard.geojson
```

It currently reports one geometry item; a fix should retain both boundaries.
For the other cases, the research used the Core APIs directly:

```csharp
GmlReader.ReadFeatures(File.ReadAllText(gmlPath), "probe");
TerrainRasterReader.ReadRaster(truncatedPath, "probe", "EPSG:25833");
// For BigTIFF decoding: Tiff.Open(bigTiffPath, "r") and ReadScanline(buffer, 1).
```

The explicit CRS in the truncated-raster call is a test argument, not a claim that the unreferenced file is project data. The BigTIFF case proves basic decoder capability only; it does not prove remote range handling, tiled COGs, overviews or offsets above 4 GiB. No failing regression tests or production fixes are added by this research record.
