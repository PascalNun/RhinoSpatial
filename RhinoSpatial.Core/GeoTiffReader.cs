using System;
using System.Linq;
using BitMiracle.LibTiff.Classic;

namespace RhinoSpatial.Core
{
    public static class GeoTiffReader
    {
        private const ushort GeographicTypeGeoKey = 2048;
        private const ushort ProjectedCSTypeGeoKey = 3072;

        public static GeoReferencedRasterInfo ReadImageInfo(string filePath)
        {
            using var tiff = Tiff.Open(filePath, "r");
            if (tiff is null)
            {
                throw new InvalidOperationException($"Failed to open GeoTIFF: {filePath}");
            }

            var width = tiff.GetField(TiffTag.IMAGEWIDTH)?[0].ToInt() ?? 0;
            var height = tiff.GetField(TiffTag.IMAGELENGTH)?[0].ToInt() ?? 0;
            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException("The GeoTIFF image dimensions could not be read.");
            }

            var srsName = ReadSrsName(tiff);
            if (string.IsNullOrWhiteSpace(srsName))
            {
                throw new InvalidOperationException("The GeoTIFF does not contain a readable EPSG code yet. The current RhinoSpatial GeoTIFF loader supports GeoTIFF files with embedded EPSG georeferencing.");
            }

            var boundingBox = ReadBoundingBox(tiff, width, height);
            return new GeoReferencedRasterInfo(
                filePath,
                srsName,
                width,
                height,
                boundingBox);
        }

        private static BoundingBox2D ReadBoundingBox(Tiff tiff, int width, int height)
        {
            var pixelScale = TryReadDoubleArray(tiff, TiffTag.GEOTIFF_MODELPIXELSCALETAG);
            var tiePoints = TryReadDoubleArray(tiff, TiffTag.GEOTIFF_MODELTIEPOINTTAG);

            if (pixelScale is { Length: >= 2 } && tiePoints is { Length: >= 6 })
            {
                var scaleX = Math.Abs(pixelScale[0]);
                var scaleY = Math.Abs(pixelScale[1]);
                if (scaleX <= 0.0 || scaleY <= 0.0)
                {
                    throw new InvalidOperationException("The GeoTIFF pixel scale is invalid.");
                }

                var rasterX = tiePoints[0];
                var rasterY = tiePoints[1];
                var modelX = tiePoints[3];
                var modelY = tiePoints[4];

                var minX = modelX - rasterX * scaleX;
                var maxY = modelY + rasterY * scaleY;
                var maxX = minX + width * scaleX;
                var minY = maxY - height * scaleY;
                return new BoundingBox2D(minX, minY, maxX, maxY);
            }

            var transform = TryReadDoubleArray(tiff, TiffTag.GEOTIFF_MODELTRANSFORMATIONTAG);
            if (transform is { Length: >= 16 })
            {
                var scaleX = transform[0];
                var shearX = transform[1];
                var shearY = transform[4];
                var scaleY = transform[5];
                var translateX = transform[3];
                var translateY = transform[7];

                if (Math.Abs(shearX) > 1e-9 || Math.Abs(shearY) > 1e-9)
                {
                    throw new InvalidOperationException("The GeoTIFF uses rotated or sheared georeferencing, which the current RhinoSpatial GeoTIFF loader does not support yet.");
                }

                if (Math.Abs(scaleX) <= 1e-9 || Math.Abs(scaleY) <= 1e-9)
                {
                    throw new InvalidOperationException("The GeoTIFF transformation matrix is invalid.");
                }

                var minX = translateX;
                var maxY = translateY;
                var maxX = minX + width * scaleX;
                var minY = maxY + height * scaleY;

                return new BoundingBox2D(
                    Math.Min(minX, maxX),
                    Math.Min(minY, maxY),
                    Math.Max(minX, maxX),
                    Math.Max(minY, maxY));
            }

            throw new InvalidOperationException("The GeoTIFF does not expose supported georeferencing tags. The current RhinoSpatial GeoTIFF loader supports north-up GeoTIFF files with ModelTiepoint/ModelPixelScale or simple affine transform tags.");
        }

        private static string ReadSrsName(Tiff tiff)
        {
            var geoKeys = TryReadUShortArray(tiff, TiffTag.GEOTIFF_GEOKEYDIRECTORYTAG);
            if (geoKeys is not { Length: >= 8 })
            {
                return string.Empty;
            }

            var keyCount = geoKeys[3];
            for (var keyIndex = 0; keyIndex < keyCount; keyIndex++)
            {
                var entryOffset = 4 + (keyIndex * 4);
                if (entryOffset + 3 >= geoKeys.Length)
                {
                    break;
                }

                var keyId = geoKeys[entryOffset];
                var tagLocation = geoKeys[entryOffset + 1];
                var valueOffset = geoKeys[entryOffset + 3];

                if (tagLocation != 0)
                {
                    continue;
                }

                if (keyId == ProjectedCSTypeGeoKey || keyId == GeographicTypeGeoKey)
                {
                    var normalizedSrs = $"EPSG:{valueOffset}";
                    return normalizedSrs;
                }
            }

            return string.Empty;
        }

        private static double[]? TryReadDoubleArray(Tiff tiff, TiffTag tag)
        {
            var field = tiff.GetField(tag);
            if (field is null || field.Length == 0)
            {
                return null;
            }

            for (var index = field.Length - 1; index >= 0; index--)
            {
                try
                {
                    var values = field[index].ToDoubleArray();
                    if (values is { Length: > 0 })
                    {
                        return values;
                    }
                }
                catch
                {
                }

                try
                {
                    return new[] { field[index].ToDouble() };
                }
                catch
                {
                }
            }

            return null;
        }

        private static ushort[]? TryReadUShortArray(Tiff tiff, TiffTag tag)
        {
            var field = tiff.GetField(tag);
            if (field is null || field.Length == 0)
            {
                return null;
            }

            for (var index = field.Length - 1; index >= 0; index--)
            {
                try
                {
                    var values = field[index].ToUShortArray();
                    if (values is { Length: > 0 })
                    {
                        return values;
                    }
                }
                catch
                {
                }

                try
                {
                    return new[] { field[index].ToUShort() };
                }
                catch
                {
                }
            }

            return null;
        }
    }
}
