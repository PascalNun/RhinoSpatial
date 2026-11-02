using System;
using BitMiracle.LibTiff.Classic;

namespace RhinoSpatial.Core
{
    public static class TerrainRasterReader
    {
        private const long MaxDecodedElevationBytes = 256L * 1024 * 1024;
        private const int MaxDecodeBufferBytes = 32 * 1024 * 1024;
        private readonly record struct DecodeLayout(bool Tiled, int Width, int Height, int BufferSize);

        public static TerrainRasterData ReadRaster(string filePath, string coverageId, string srsName) =>
            ReadRaster(filePath, coverageId, srsName, CancellationToken.None);

        public static TerrainRasterData ReadRaster(string filePath, string coverageId, string srsName, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            TiffWarningSuppression.EnsureInstalled();
            using var tiff = Tiff.Open(filePath, "r");
            if (tiff is null)
            {
                throw new InvalidOperationException($"Failed to open GeoTIFF: {filePath}");
            }

            var width = tiff.GetField(TiffTag.IMAGEWIDTH)[0].ToInt();
            var height = tiff.GetField(TiffTag.IMAGELENGTH)[0].ToInt();
            var samplesPerPixel = tiff.GetFieldDefaulted(TiffTag.SAMPLESPERPIXEL)[0].ToInt();
            var bitsPerSample = tiff.GetFieldDefaulted(TiffTag.BITSPERSAMPLE)[0].ToInt();
            var sampleFormatField = tiff.GetField(TiffTag.SAMPLEFORMAT);
            var sampleFormat = sampleFormatField is null || sampleFormatField.Length == 0
                ? SampleFormat.UINT
                : (SampleFormat)sampleFormatField[0].ToInt();

            if (samplesPerPixel != 1)
            {
                throw new InvalidOperationException("Terrain raster must have a single sample per pixel.");
            }

            if (width <= 0 || height <= 0)
                throw new InvalidDataException("Invalid terrain raster dimensions.");
            var sampleCount = (long)width * height;
            if (sampleCount > MaxDecodedElevationBytes / sizeof(float))
                throw new InvalidDataException("Terrain raster exceeds the 256 MiB elevation memory budget. Crop the source raster or request a smaller WCS/COG subset.");
            if (!((bitsPerSample is 8 or 16 or 32 && sampleFormat is SampleFormat.UINT or SampleFormat.INT) ||
                  (bitsPerSample is 32 or 64 && sampleFormat == SampleFormat.IEEEFP)))
                throw new InvalidDataException($"Unsupported terrain raster sample format: {bitsPerSample}-bit {sampleFormat}.");
            var layout = ReadDecodeLayout(tiff, width, bitsPerSample / 8);
            var noData = ReadNoDataValue(tiff);

            GeoReferencedRasterInfo? geo = null;
            try { geo = GeoTiffReader.ReadImageInfo(tiff, filePath, new FileInfo(filePath).Length, srsName, true); }
            catch (InvalidDataException) when (tiff.GetField(TiffTag.GEOTIFF_MODELPIXELSCALETAG) is null && tiff.GetField(TiffTag.GEOTIFF_MODELTRANSFORMATIONTAG) is null) { }

            var elevations = new float[(int)sampleCount];
            var buffer = new byte[layout.BufferSize];
            if (layout.Tiled)
            {
                ReadTiledRaster(tiff, width, height, bitsPerSample, sampleFormat, layout, buffer, elevations, token);
            }
            else
            {
                ReadScanlineRaster(tiff, width, height, bitsPerSample, sampleFormat, buffer, elevations, token);
            }

            var origin = geo?.GeoTransform?.At(0.5, 0.5) ?? new Coordinate2D(0, 0);
            var offsetX = geo?.GeoTransform is { } gx ? new Coordinate2D(gx.A, gx.D) : new Coordinate2D(1, 0);
            var offsetY = geo?.GeoTransform is { } gy ? new Coordinate2D(gy.B, gy.E) : new Coordinate2D(0, -1);

            token.ThrowIfCancellationRequested();
            return new TerrainRasterData(
                coverageId,
                geo?.SrsName ?? srsName,
                width,
                height,
                origin,
                offsetX,
                offsetY,
                noData,
                elevations)
            { GeoTransform = geo?.GeoTransform };
        }

        private static DecodeLayout ReadDecodeLayout(Tiff tiff, int width, int bytesPerSample)
        {
            var tiled = tiff.IsTiled();
            var bufferWidth = tiled ? tiff.GetField(TiffTag.TILEWIDTH)[0].ToInt() : width;
            var bufferHeight = tiled ? tiff.GetField(TiffTag.TILELENGTH)[0].ToInt() : 1;
            if (bufferWidth <= 0 || bufferHeight <= 0)
                throw new InvalidDataException("Invalid terrain raster tile dimensions.");
            var samples = (long)bufferWidth * bufferHeight;
            if (samples > MaxDecodeBufferBytes / bytesPerSample)
                throw new InvalidDataException("Terrain raster row or tile exceeds the 32 MiB decode buffer budget.");
            var size = tiled ? tiff.TileSize() : tiff.ScanlineSize();
            if (size != samples * bytesPerSample)
                throw new InvalidDataException("Terrain raster decoded row or tile size disagrees with its sample layout.");
            return new DecodeLayout(tiled, bufferWidth, bufferHeight, size);
        }

        private static void ReadScanlineRaster(
            Tiff tiff,
            int width,
            int height,
            int bitsPerSample,
            SampleFormat sampleFormat,
            byte[] buffer,
            float[] elevations,
            CancellationToken token)
        {
            for (var row = 0; row < height; row++)
            {
                token.ThrowIfCancellationRequested();
                if (!tiff.ReadScanline(buffer, row))
                    throw new InvalidDataException($"Incomplete terrain raster: failed to decode row {row + 1}. No terrain was returned.");

                for (var col = 0; col < width; col++)
                {
                    if (col % 4096 == 0) token.ThrowIfCancellationRequested();
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
            DecodeLayout layout,
            byte[] buffer,
            float[] elevations,
            CancellationToken token)
        {
            var tileWidth = layout.Width;
            var tileHeight = layout.Height;
            var tileSize = layout.BufferSize;

            for (var tileY = 0; tileY < height; tileY += tileHeight)
            {
                for (var tileX = 0; tileX < width; tileX += tileWidth)
                {
                    token.ThrowIfCancellationRequested();
                    var tile = tiff.ComputeTile(tileX, tileY, 0, 0);
                    var bytesRead = tiff.ReadEncodedTile(tile, buffer, 0, tileSize);
                    if (bytesRead != tileSize)
                    {
                        throw new InvalidDataException($"Incomplete terrain raster: tile {tile} decoded {bytesRead} of {tileSize} bytes. No terrain was returned.");
                    }

                    var readWidth = Math.Min(tileWidth, width - tileX);
                    var readHeight = Math.Min(tileHeight, height - tileY);
                    for (var localY = 0; localY < readHeight; localY++)
                    {
                        token.ThrowIfCancellationRequested();
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

        internal static float ReadSample(byte[] buffer, int sampleIndex, int bitsPerSample, SampleFormat sampleFormat)
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

        internal static double? ReadNoDataValue(Tiff tiff)
        {
            var noDataField = tiff.GetField(TiffTag.GDAL_NODATA);
            if (noDataField is null || noDataField.Length == 0)
            {
                return null;
            }

            // Unknown ASCII TIFF tags expose a count followed by the value.
            var text = noDataField[^1].ToString().TrimEnd('\0');
            if (double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            throw new InvalidDataException("Invalid GeoTIFF NoData value.");
        }
    }
}
