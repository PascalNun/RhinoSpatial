using System.Collections.Generic;
using Rhino.Geometry;
using WfsCore;

namespace RhinoSpatial
{
    internal static class RhinoSpatialContextTools
    {
        private const int DefaultMaxImageDimension = 6144;
        private const int DefaultMaxImagePixels = 24_000_000;

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
            mesh.TextureCoordinates.Add(0.0f, 0.0f);
            mesh.TextureCoordinates.Add(1.0f, 0.0f);
            mesh.TextureCoordinates.Add(1.0f, 1.0f);
            mesh.TextureCoordinates.Add(0.0f, 1.0f);

            mesh.Normals.ComputeNormals();
            mesh.Compact();
            return mesh;
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
