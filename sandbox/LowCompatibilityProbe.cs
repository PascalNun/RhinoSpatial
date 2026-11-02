using System.Globalization;
using RhinoSpatial.Core;

namespace RhinoSpatial.Sandbox;

internal static class LowCompatibilityProbe
{
    internal static async Task RunAsync(string[] args)
    {
        if (args.Length < 3)
            throw new ArgumentException(
                "low-probe cog|dem <url> [srs minX,minY,maxX,maxY] | gpkg <file> [layer srs minX,minY,maxX,maxY] | examples <directory>"
            );
        SpatialContext2D Context(int srsIndex, int boundsIndex)
        {
            var b = args[boundsIndex]
                .Split(',')
                .Select(v => double.Parse(v, CultureInfo.InvariantCulture))
                .ToArray();
            if (b.Length != 4)
                throw new ArgumentException("Expected four bbox ordinates.");
            return Lod2AtomChecks.Context(args[srsIndex], new(b[0], b[1], b[2], b[3]));
        }
        if (args[1] is "cog" or "dem")
        {
            var reader = new CogReader();
            var info = await reader.ReadMetadataAsync(args[2]);
            Console.WriteLine(
                $"{info.SrsName}; {info.Width}x{info.Height}; {info.BoundingBox}; affine {info.GeoTransform}"
            );
            if (args.Length < 5)
                return;
            var context = Context(3, 4);
            if (args[1] == "cog")
            {
                var result = await reader.ReadImageAsync(args[2], context);
                var mesh = RasterImageMeshBuilder.Build(result.Image, context);
                Console.WriteLine(
                    $"Window {result.Image.Width}x{result.Image.Height}; overview width {result.OverviewWidth}; {result.RangeRequests} requests, {result.TransferredBytes} bytes; {mesh.Faces.Count} mesh faces; PNG {result.Image.LocalFilePath}"
                );
            }
            else
            {
                var result = await reader.ReadTerrainAsync(args[2], context);
                var raster = result.Raster;
                Console.WriteLine(
                    $"DEM window {raster.Width}x{raster.Height}; overview width {result.OverviewWidth}; {result.RangeRequests} requests, {result.TransferredBytes} bytes; NoData {raster.NoDataValue}; finite samples {raster.Elevations.Count(float.IsFinite)}"
                );
            }
        }
        else if (args[1] == "gpkg")
        {
            foreach (var layer in GeoPackageReader.ReadLayers(args[2]))
                Console.WriteLine(
                    $"{layer.Name} | {layer.Title} | {layer.SourceSrs} | {layer.Bounds}"
                );
            if (args.Length < 6)
                return;
            var result = GeoPackageReader.ReadFeatures(args[2], args[3], Context(4, 5));
            Console.WriteLine(
                $"Features {result.Features.Count}; scanned {result.ScannedCount}; RTree {result.UsedSpatialIndex}; rings {result.Features.Sum(f => f.Geometry.OuterRings.Count)}"
            );
        }
        else
            throw new ArgumentException("Unknown low-probe mode.");
    }
}
