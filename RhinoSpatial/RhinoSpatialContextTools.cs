using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using Rhino.Geometry;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    internal static class RhinoSpatialContextTools
    {
        private const int DefaultMaxImageDimension = 6144;
        private const int DefaultMaxImagePixels = 24_000_000;
        private const double DefaultVertexTolerance = 0.001;

        public static Point3d ResolvePlacementOrigin(SpatialContext2D? spatialContext, bool useAbsoluteCoordinates, IReadOnlyList<WfsFeature> features)
        {
            if (useAbsoluteCoordinates)
            {
                return Point3d.Origin;
            }

            if (spatialContext is not null)
            {
                return new Point3d(spatialContext.PlacementOrigin.X, spatialContext.PlacementOrigin.Y, 0.0);
            }

            return RhinoSpatialOutputBuilder.CalculateLocalizingOffset(features);
        }

        public static SpatialContext2D CreateSpatialContext(
            string resolvedSrs,
            BoundingBox2D requestBoundingBox,
            BoundingBox2D? wgs84BoundingBox,
            Dictionary<string, BoundingBox2D> boundingBoxesBySrs,
            bool useAbsoluteCoordinates = false)
        {
            return new SpatialContext2D(
                resolvedSrs,
                requestBoundingBox,
                requestBoundingBox,
                wgs84BoundingBox,
                new Coordinate2D(requestBoundingBox.MinX, requestBoundingBox.MinY),
                useAbsoluteCoordinates,
                boundingBoxesBySrs);
        }

        public static bool TryResolveBoundingBoxForSrs(
            SpatialContext2D spatialContext,
            string requestedSrs,
            out BoundingBox2D boundingBox,
            out Coordinate2D placementOrigin)
        {
            boundingBox = spatialContext.RequestBoundingBox;
            placementOrigin = spatialContext.PlacementOrigin;

            var normalizedRequestedSrs = NormalizeSrsKey(requestedSrs);
            var normalizedContextSrs = NormalizeSrsKey(spatialContext.ResolvedSrs);

            if (!string.IsNullOrWhiteSpace(normalizedRequestedSrs))
            {
                if (spatialContext.BoundingBoxesBySrs.TryGetValue(normalizedRequestedSrs, out var matchingBoundingBox))
                {
                    boundingBox = matchingBoundingBox;
                    placementOrigin = new Coordinate2D(matchingBoundingBox.MinX, matchingBoundingBox.MinY);
                    return true;
                }

                if (normalizedRequestedSrs == "EPSG:7423" &&
                    spatialContext.BoundingBoxesBySrs.TryGetValue("EPSG:4326", out var wgs84VariantBoundingBox))
                {
                    boundingBox = wgs84VariantBoundingBox;
                    placementOrigin = new Coordinate2D(wgs84VariantBoundingBox.MinX, wgs84VariantBoundingBox.MinY);
                    return true;
                }

                if (normalizedRequestedSrs == normalizedContextSrs)
                {
                    return true;
                }

                if ((normalizedRequestedSrs == "EPSG:4326" || normalizedRequestedSrs == "EPSG:7423") &&
                    spatialContext.Wgs84BoundingBox is not null)
                {
                    boundingBox = spatialContext.Wgs84BoundingBox;
                    placementOrigin = new Coordinate2D(boundingBox.MinX, boundingBox.MinY);
                    return true;
                }
            }

            return false;
        }

        public static WmsRequestOptions CreateWmsRequestOptions(
            string baseUrl,
            string layerName,
            SpatialContext2D spatialContext,
            WmsCapabilitiesInfo? capabilities = null,
            string format = "image/png",
            string version = "1.3.0",
            bool transparent = true)
        {
            var imageSize = ResolveImageSize(
                spatialContext.RequestBoundingBox,
                capabilities?.MaxWidth,
                capabilities?.MaxHeight);

            return new WmsRequestOptions
            {
                BaseUrl = baseUrl,
                GetMapBaseUrl = capabilities is null || string.IsNullOrWhiteSpace(capabilities.GetMapUrl) ? null : capabilities.GetMapUrl,
                LayerName = layerName,
                BoundingBox = spatialContext.RequestBoundingBox,
                SrsName = spatialContext.ResolvedSrs,
                Width = imageSize.Width,
                Height = imageSize.Height,
                Format = format,
                Version = capabilities is null || string.IsNullOrWhiteSpace(capabilities.ServiceVersion) ? version : capabilities.ServiceVersion,
                Transparent = transparent
            };
        }

        public static PolylineCurve CreateBoundingBoxFrame(BoundingBox2D boundingBox, Coordinate2D placementOrigin, bool useAbsoluteCoordinates)
        {
            var offsetX = useAbsoluteCoordinates ? 0.0 : placementOrigin.X;
            var offsetY = useAbsoluteCoordinates ? 0.0 : placementOrigin.Y;

            var polyline = new Polyline(5)
            {
                new Point3d(boundingBox.MinX - offsetX, boundingBox.MinY - offsetY, 0.0),
                new Point3d(boundingBox.MaxX - offsetX, boundingBox.MinY - offsetY, 0.0),
                new Point3d(boundingBox.MaxX - offsetX, boundingBox.MaxY - offsetY, 0.0),
                new Point3d(boundingBox.MinX - offsetX, boundingBox.MaxY - offsetY, 0.0),
                new Point3d(boundingBox.MinX - offsetX, boundingBox.MinY - offsetY, 0.0)
            };

            return polyline.ToPolylineCurve();
        }

        public static Mesh CreateBoundingBoxMesh(BoundingBox2D boundingBox, Coordinate2D placementOrigin, bool useAbsoluteCoordinates)
        {
            return CreateTexturedBoundingBoxMesh(
                boundingBox,
                placementOrigin,
                useAbsoluteCoordinates,
                0.0f,
                0.0f,
                1.0f,
                1.0f);
        }

        public static Mesh CreateTexturedBoundingBoxMesh(
            BoundingBox2D boundingBox,
            Coordinate2D placementOrigin,
            bool useAbsoluteCoordinates,
            float minU,
            float minV,
            float maxU,
            float maxV)
        {
            var offsetX = useAbsoluteCoordinates ? 0.0 : placementOrigin.X;
            var offsetY = useAbsoluteCoordinates ? 0.0 : placementOrigin.Y;

            var mesh = new Mesh();
            mesh.Vertices.Add(boundingBox.MinX - offsetX, boundingBox.MinY - offsetY, 0.0);
            mesh.Vertices.Add(boundingBox.MaxX - offsetX, boundingBox.MinY - offsetY, 0.0);
            mesh.Vertices.Add(boundingBox.MaxX - offsetX, boundingBox.MaxY - offsetY, 0.0);
            mesh.Vertices.Add(boundingBox.MinX - offsetX, boundingBox.MaxY - offsetY, 0.0);
            mesh.Faces.AddFace(0, 1, 2, 3);

            // Rhino's texture mapping expects the standard bottom-up UV order here.
            // Using this orientation keeps the downloaded WMS image aligned with the
            // shared spatial context instead of appearing mirrored on the preview quad.
            mesh.TextureCoordinates.Add(minU, minV);
            mesh.TextureCoordinates.Add(maxU, minV);
            mesh.TextureCoordinates.Add(maxU, maxV);
            mesh.TextureCoordinates.Add(minU, maxV);

            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
        }

        public static BoundingBox2D CreatePlacedBoundingBox(BoundingBox2D boundingBox, Coordinate2D placementOrigin, bool useAbsoluteCoordinates)
        {
            if (useAbsoluteCoordinates)
            {
                return boundingBox;
            }

            return new BoundingBox2D(
                boundingBox.MinX - placementOrigin.X,
                boundingBox.MinY - placementOrigin.Y,
                boundingBox.MaxX - placementOrigin.X,
                boundingBox.MaxY - placementOrigin.Y);
        }

        public static string CreateSpatialContextKey(SpatialContext2D spatialContext)
        {
            var requestBoundingBox = spatialContext.RequestBoundingBox;
            var placementBoundingBox = spatialContext.PlacementBoundingBox;
            var placementOrigin = spatialContext.PlacementOrigin;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{NormalizeSrsKey(spatialContext.ResolvedSrs)}|{spatialContext.UseAbsoluteCoordinates}|{requestBoundingBox.MinX}|{requestBoundingBox.MinY}|{requestBoundingBox.MaxX}|{requestBoundingBox.MaxY}|{placementBoundingBox.MinX}|{placementBoundingBox.MinY}|{placementBoundingBox.MaxX}|{placementBoundingBox.MaxY}|{placementOrigin.X}|{placementOrigin.Y}");
        }

        public static bool DoBoundingBoxesIntersect(BoundingBox2D left, BoundingBox2D right)
        {
            return left.MinX <= right.MaxX &&
                   left.MaxX >= right.MinX &&
                   left.MinY <= right.MaxY &&
                   left.MaxY >= right.MinY;
        }

        public static bool TryIntersectBoundingBoxes(BoundingBox2D left, BoundingBox2D right, out BoundingBox2D intersection)
        {
            if (!DoBoundingBoxesIntersect(left, right))
            {
                intersection = new BoundingBox2D(0.0, 0.0, 0.0, 0.0);
                return false;
            }

            intersection = new BoundingBox2D(
                System.Math.Max(left.MinX, right.MinX),
                System.Math.Max(left.MinY, right.MinY),
                System.Math.Min(left.MaxX, right.MaxX),
                System.Math.Min(left.MaxY, right.MaxY));

            return intersection.MaxX > intersection.MinX && intersection.MaxY > intersection.MinY;
        }

        public static double CalculateBoundingBoxArea(BoundingBox2D boundingBox)
        {
            return System.Math.Max(0.0, boundingBox.MaxX - boundingBox.MinX) *
                   System.Math.Max(0.0, boundingBox.MaxY - boundingBox.MinY);
        }

        public static double ResolveAveragePlacedElevation(
            SpatialContext2D spatialContext,
            IEnumerable<Coordinate2D> sourcePoints,
            string sourceSrs = "EPSG:4326")
        {
            var sampledElevations = sourcePoints
                .Select(point => SpatialTerrainCache.TrySamplePlacedElevation(spatialContext, sourceSrs, point.X, point.Y, out var sampledElevation)
                    ? (double?)sampledElevation
                    : null)
                .Where(sampledElevation => sampledElevation.HasValue)
                .Select(sampledElevation => sampledElevation!.Value)
                .ToList();

            if (sampledElevations.Count == 0)
            {
                return 0.0;
            }

            return sampledElevations.Average();
        }

        public static bool TryTransformPolyline(
            IReadOnlyList<Coordinate2D> sourcePoints,
            SpatialContext2D spatialContext,
            string sourceSrs,
            double z,
            bool closePolyline,
            out Polyline polyline)
        {
            polyline = new Polyline();

            var minimumPointCount = closePolyline ? 3 : 2;
            if (sourcePoints.Count < minimumPointCount)
            {
                return false;
            }

            var transformedPoints = new List<Point3d>(sourcePoints.Count + (closePolyline ? 1 : 0));
            var offsetX = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.X;
            var offsetY = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.Y;

            foreach (var sourcePoint in sourcePoints)
            {
                if (!SpatialReferenceTransform.TryTransformXY(sourceSrs, spatialContext.ResolvedSrs, sourcePoint.X, sourcePoint.Y, out var x, out var y))
                {
                    return false;
                }

                transformedPoints.Add(new Point3d(x - offsetX, y - offsetY, z));
            }

            polyline = new Polyline(transformedPoints);
            polyline.RemoveNearlyEqualSubsequentPoints(DefaultVertexTolerance);
            polyline.DeleteShortSegments(DefaultVertexTolerance);

            if (polyline.Count < minimumPointCount)
            {
                return false;
            }

            if (!closePolyline)
            {
                return polyline.Count >= 2;
            }

            if (!polyline[0].EpsilonEquals(polyline[^1], DefaultVertexTolerance))
            {
                polyline.Add(polyline[0]);
            }

            return polyline.Count >= 4 && polyline.IsClosed;
        }

        private static (int Width, int Height) ResolveImageSize(BoundingBox2D boundingBox, int? serviceMaxWidth, int? serviceMaxHeight)
        {
            var safeMaxWidth = ResolveDimensionLimit(serviceMaxWidth);
            var safeMaxHeight = ResolveDimensionLimit(serviceMaxHeight);

            var spanX = System.Math.Max(boundingBox.MaxX - boundingBox.MinX, 1e-9);
            var spanY = System.Math.Max(boundingBox.MaxY - boundingBox.MinY, 1e-9);
            var aspectRatio = spanX / spanY;

            var width = safeMaxWidth;
            var height = safeMaxHeight;

            if (aspectRatio >= 1.0)
            {
                width = safeMaxWidth;
                height = System.Math.Max(1, (int)System.Math.Round(width / aspectRatio));

                if (height > safeMaxHeight)
                {
                    var scale = safeMaxHeight / (double)height;
                    height = safeMaxHeight;
                    width = System.Math.Max(1, (int)System.Math.Round(width * scale));
                }
            }
            else
            {
                height = safeMaxHeight;
                width = System.Math.Max(1, (int)System.Math.Round(height * aspectRatio));

                if (width > safeMaxWidth)
                {
                    var scale = safeMaxWidth / (double)width;
                    width = safeMaxWidth;
                    height = System.Math.Max(1, (int)System.Math.Round(height * scale));
                }
            }

            var pixelCount = (double)width * height;
            if (pixelCount > DefaultMaxImagePixels)
            {
                var scale = System.Math.Sqrt(DefaultMaxImagePixels / pixelCount);
                width = System.Math.Max(1, (int)System.Math.Round(width * scale));
                height = System.Math.Max(1, (int)System.Math.Round(height * scale));
            }

            return (width, height);
        }

        private static int ResolveDimensionLimit(int? serviceLimit)
        {
            if (!serviceLimit.HasValue || serviceLimit.Value <= 0)
            {
                return DefaultMaxImageDimension;
            }

            return System.Math.Min(serviceLimit.Value, DefaultMaxImageDimension);
        }

        public static string NormalizeSrsKey(string? srsName)
        {
            if (string.IsNullOrWhiteSpace(srsName))
            {
                return string.Empty;
            }

            if (srsName.Contains("25832", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25832";
            }

            if (srsName.Contains("25833", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25833";
            }

            if (srsName.Contains("27700", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:27700";
            }

            if (srsName.Contains("3857", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:3857";
            }

            if (srsName.Contains("4283", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:4283";
            }

            if (srsName.Contains("7423", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:7423";
            }

            if (srsName.Contains("7844", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:7844";
            }

            if (srsName.Contains("4326", System.StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:4326";
            }

            return srsName.Trim();
        }
    }
}
