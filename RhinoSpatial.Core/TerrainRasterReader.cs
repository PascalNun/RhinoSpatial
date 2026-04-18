using System;
using BitMiracle.LibTiff.Classic;

namespace RhinoSpatial.Core
{
    public static class TerrainRasterReader
    {
        public static TerrainRasterData ReadRaster(string filePath, string coverageId, string srsName)
        {
            TiffWarningSuppression.EnsureInstalled();
            using var tiff = Tiff.Open(filePath, "r");
            if (tiff is null)
            {
                throw new InvalidOperationException($"Failed to open GeoTIFF: {filePath}");
            }

            var width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            var height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            var samplesPerPixel = tiff.GetField(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
            var bitsPerSample = tiff.GetField(TiffTag.BITSPERSAMPLE)[0].ToInt();

            if (samplesPerPixel != 1)
            {
                throw new InvalidOperationException("Terrain raster must have a single sample per pixel.");
            }

            var elevations = new float[width * height];
            var scanlineSize = tiff.ScanlineSize();
            var buffer = new byte[scanlineSize];

            for (var row = 0; row < height; row++)
            {
                tiff.ReadScanline(buffer, row);

                if (bitsPerSample == 32)
                {
                    for (var col = 0; col < width; col++)
                    {
                        var offset = col * 4;
                        elevations[row * width + col] = BitConverter.ToSingle(buffer, offset);
                    }
                }
                else if (bitsPerSample == 16)
                {
                    for (var col = 0; col < width; col++)
                    {
                        var offset = col * 2;
                        var value = BitConverter.ToInt16(buffer, offset);
                        elevations[row * width + col] = value;
                    }
                }
                else
                {
                    throw new InvalidOperationException($"Unsupported terrain raster bit depth: {bitsPerSample}.");
                }
            }

            var origin = new Coordinate2D(0, 0);
            var offsetX = new Coordinate2D(1, 0);
            var offsetY = new Coordinate2D(0, -1);
            var noData = ReadNoDataValue(tiff);

            return new TerrainRasterData(
                coverageId,
                srsName,
                width,
                height,
                origin,
                offsetX,
                offsetY,
                noData,
                elevations);
        }

        private static double? ReadNoDataValue(Tiff tiff)
        {
            var noDataField = tiff.GetField(TiffTag.GDAL_NODATA);
            if (noDataField is null || noDataField.Length == 0)
            {
                return null;
            }

            var text = noDataField[0].ToString();
            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return null;
        }
    }
}
