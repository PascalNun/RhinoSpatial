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
            var sampleFormatField = tiff.GetField(TiffTag.SAMPLEFORMAT);
            var sampleFormat = sampleFormatField is null || sampleFormatField.Length == 0
                ? SampleFormat.UINT
                : (SampleFormat)sampleFormatField[0].ToInt();

            if (samplesPerPixel != 1)
            {
                throw new InvalidOperationException("Terrain raster must have a single sample per pixel.");
            }

            var elevations = new float[width * height];
            if (tiff.IsTiled())
            {
                ReadTiledRaster(tiff, width, height, bitsPerSample, sampleFormat, elevations);
            }
            else
            {
                ReadScanlineRaster(tiff, width, height, bitsPerSample, sampleFormat, elevations);
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

        private static void ReadScanlineRaster(
            Tiff tiff,
            int width,
            int height,
            int bitsPerSample,
            SampleFormat sampleFormat,
            float[] elevations)
        {
            var scanlineSize = tiff.ScanlineSize();
            var buffer = new byte[scanlineSize];

            for (var row = 0; row < height; row++)
            {
                tiff.ReadScanline(buffer, row);

                for (var col = 0; col < width; col++)
                {
                    elevations[row * width + col] = ReadSample(buffer, col, bitsPerSample, sampleFormat);
                }
            }
        }

        private static void ReadTiledRaster(
            Tiff tiff,
            int width,
            int height,
            int bitsPerSample,
            SampleFormat sampleFormat,
            float[] elevations)
        {
            var tileWidth = tiff.GetField(TiffTag.TILEWIDTH)[0].ToInt();
            var tileHeight = tiff.GetField(TiffTag.TILELENGTH)[0].ToInt();
            var tileSize = tiff.TileSize();
            var buffer = new byte[tileSize];

            for (var tileY = 0; tileY < height; tileY += tileHeight)
            {
                for (var tileX = 0; tileX < width; tileX += tileWidth)
                {
                    var tile = tiff.ComputeTile(tileX, tileY, 0, 0);
                    var bytesRead = tiff.ReadEncodedTile(tile, buffer, 0, tileSize);
                    if (bytesRead < 0)
                    {
                        throw new InvalidOperationException("Failed to read tiled terrain raster data.");
                    }

                    var readWidth = Math.Min(tileWidth, width - tileX);
                    var readHeight = Math.Min(tileHeight, height - tileY);
                    for (var localY = 0; localY < readHeight; localY++)
                    {
                        var rowOffset = localY * tileWidth;
                        var targetRow = (tileY + localY) * width;
                        for (var localX = 0; localX < readWidth; localX++)
                        {
                            elevations[targetRow + tileX + localX] = ReadSample(
                                buffer,
                                rowOffset + localX,
                                bitsPerSample,
                                sampleFormat);
                        }
                    }
                }
            }
        }

        private static float ReadSample(byte[] buffer, int sampleIndex, int bitsPerSample, SampleFormat sampleFormat)
        {
            if (bitsPerSample == 64 && sampleFormat == SampleFormat.IEEEFP)
            {
                return (float)BitConverter.ToDouble(buffer, sampleIndex * 8);
            }

            if (bitsPerSample == 32 && sampleFormat == SampleFormat.IEEEFP)
            {
                return BitConverter.ToSingle(buffer, sampleIndex * 4);
            }

            if (bitsPerSample == 32)
            {
                var offset = sampleIndex * 4;
                return sampleFormat == SampleFormat.UINT
                    ? BitConverter.ToUInt32(buffer, offset)
                    : BitConverter.ToInt32(buffer, offset);
            }

            if (bitsPerSample == 16)
            {
                var offset = sampleIndex * 2;
                return sampleFormat == SampleFormat.UINT
                    ? BitConverter.ToUInt16(buffer, offset)
                    : BitConverter.ToInt16(buffer, offset);
            }

            if (bitsPerSample == 8)
            {
                return sampleFormat == SampleFormat.INT
                    ? unchecked((sbyte)buffer[sampleIndex])
                    : buffer[sampleIndex];
            }

            throw new InvalidOperationException($"Unsupported terrain raster sample format: {bitsPerSample}-bit {sampleFormat}.");
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
