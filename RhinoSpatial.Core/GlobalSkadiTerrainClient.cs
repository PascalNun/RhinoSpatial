using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace RhinoSpatial.Core
{
    public sealed class GlobalSkadiTerrainClient
    {
        private const int TileSampleSize = 3601;
        private const short NoDataValue = -32768;
        private const int DefaultMaxSamples = 512;

        private static readonly HttpClient SharedHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        private static readonly ConcurrentDictionary<TileKey, Task<HgtTile?>> TileCache = new();
        private static readonly string TerrainTileCacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RhinoSpatial",
            "TerrainCache",
            "Skadi");

        static GlobalSkadiTerrainClient()
        {
            SharedHttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RhinoSpatial", "1.0"));
            SharedHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        }

        public async Task<TerrainRasterData> LoadAsync(
            BoundingBox2D boundingBox4326,
            int maxSamples = DefaultMaxSamples)
        {
            var safeBoundingBox = NormalizeBoundingBox(boundingBox4326);
            var dimensions = ResolveSampleDimensions(safeBoundingBox, maxSamples);
            var elevations = new float[dimensions.Width * dimensions.Height];

            for (var y = 0; y < dimensions.Height; y++)
            {
                var latitude = dimensions.Height == 1
                    ? (safeBoundingBox.MinY + safeBoundingBox.MaxY) * 0.5
                    : safeBoundingBox.MaxY - ((safeBoundingBox.MaxY - safeBoundingBox.MinY) * y / (dimensions.Height - 1));

                for (var x = 0; x < dimensions.Width; x++)
                {
                    var longitude = dimensions.Width == 1
                        ? (safeBoundingBox.MinX + safeBoundingBox.MaxX) * 0.5
                        : safeBoundingBox.MinX + ((safeBoundingBox.MaxX - safeBoundingBox.MinX) * x / (dimensions.Width - 1));

                    elevations[(y * dimensions.Width) + x] = await SampleElevationAsync(latitude, longitude).ConfigureAwait(false);
                }
            }

            return new TerrainRasterData(
                "Mapzen Skadi HGT",
                "EPSG:4326",
                dimensions.Width,
                dimensions.Height,
                new Coordinate2D(safeBoundingBox.MinX, safeBoundingBox.MaxY),
                new Coordinate2D((safeBoundingBox.MaxX - safeBoundingBox.MinX) / Math.Max(1, dimensions.Width - 1), 0.0),
                new Coordinate2D(0.0, -((safeBoundingBox.MaxY - safeBoundingBox.MinY) / Math.Max(1, dimensions.Height - 1))),
                NoDataValue,
                elevations);
        }

        private static async Task<float> SampleElevationAsync(double latitude, double longitude)
        {
            var tileKey = TileKey.From(latitude, longitude);
            var tile = await TileCache.GetOrAdd(tileKey, LoadTileAsync).ConfigureAwait(false);
            if (tile is null)
            {
                return NoDataValue;
            }

            return tile.Sample(latitude, longitude);
        }

        private static async Task<HgtTile?> LoadTileAsync(TileKey tileKey)
        {
            Directory.CreateDirectory(TerrainTileCacheDirectory);
            var localPath = Path.Combine(TerrainTileCacheDirectory, tileKey.FileName);
            if (!File.Exists(localPath))
            {
                var requestUrl = $"https://s3.amazonaws.com/elevation-tiles-prod/skadi/{tileKey.LatitudeBand}/{tileKey.FileName}.gz";
                using var response = await SharedHttpClient.GetAsync(requestUrl).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                await using var responseStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                await using var gzipStream = new GZipStream(responseStream, CompressionMode.Decompress);
                await using var fileStream = File.Create(localPath);
                await gzipStream.CopyToAsync(fileStream).ConfigureAwait(false);
            }

            var bytes = await File.ReadAllBytesAsync(localPath).ConfigureAwait(false);
            if (bytes.Length != TileSampleSize * TileSampleSize * 2)
            {
                return null;
            }

            return new HgtTile(tileKey, bytes);
        }

        private static BoundingBox2D NormalizeBoundingBox(BoundingBox2D boundingBox)
        {
            var minX = Math.Clamp(Math.Min(boundingBox.MinX, boundingBox.MaxX), -180.0, 180.0);
            var maxX = Math.Clamp(Math.Max(boundingBox.MinX, boundingBox.MaxX), -180.0, 180.0);
            var minY = Math.Clamp(Math.Min(boundingBox.MinY, boundingBox.MaxY), -90.0, 90.0);
            var maxY = Math.Clamp(Math.Max(boundingBox.MinY, boundingBox.MaxY), -90.0, 90.0);

            if (maxX <= minX)
            {
                maxX = Math.Min(180.0, minX + 0.001);
            }

            if (maxY <= minY)
            {
                maxY = Math.Min(90.0, minY + 0.001);
            }

            return new BoundingBox2D(minX, minY, maxX, maxY);
        }

        private static (int Width, int Height) ResolveSampleDimensions(BoundingBox2D boundingBox, int maxSamples)
        {
            var safeMaxSamples = Math.Clamp(maxSamples, 32, 1024);
            var midLatitude = (boundingBox.MinY + boundingBox.MaxY) * 0.5 * (Math.PI / 180.0);
            var width = Math.Max(0.000001, (boundingBox.MaxX - boundingBox.MinX) * Math.Max(0.05, Math.Cos(midLatitude)));
            var height = Math.Max(0.000001, boundingBox.MaxY - boundingBox.MinY);

            if (width >= height)
            {
                return (safeMaxSamples, Math.Max(32, (int)Math.Round(safeMaxSamples * (height / width))));
            }

            return (Math.Max(32, (int)Math.Round(safeMaxSamples * (width / height))), safeMaxSamples);
        }

        private readonly record struct TileKey(int Latitude, int Longitude)
        {
            public string LatitudeBand => FormatLatitude(Latitude);

            public string FileName => $"{FormatLatitude(Latitude)}{FormatLongitude(Longitude)}.hgt";

            public static TileKey From(double latitude, double longitude)
            {
                var safeLatitude = Math.Clamp(latitude, -89.999999, 89.999999);
                var safeLongitude = Math.Clamp(longitude, -179.999999, 179.999999);
                return new TileKey((int)Math.Floor(safeLatitude), (int)Math.Floor(safeLongitude));
            }

            private static string FormatLatitude(int latitude)
            {
                var prefix = latitude < 0 ? "S" : "N";
                return prefix + Math.Abs(latitude).ToString("00", CultureInfo.InvariantCulture);
            }

            private static string FormatLongitude(int longitude)
            {
                var prefix = longitude < 0 ? "W" : "E";
                return prefix + Math.Abs(longitude).ToString("000", CultureInfo.InvariantCulture);
            }
        }

        private sealed class HgtTile
        {
            private readonly TileKey _tileKey;
            private readonly byte[] _bytes;

            public HgtTile(TileKey tileKey, byte[] bytes)
            {
                _tileKey = tileKey;
                _bytes = bytes;
            }

            public float Sample(double latitude, double longitude)
            {
                var x = Math.Clamp((longitude - _tileKey.Longitude) * (TileSampleSize - 1), 0.0, TileSampleSize - 1);
                var y = Math.Clamp((_tileKey.Latitude + 1.0 - latitude) * (TileSampleSize - 1), 0.0, TileSampleSize - 1);
                var x0 = (int)Math.Floor(x);
                var y0 = (int)Math.Floor(y);
                var x1 = Math.Min(TileSampleSize - 1, x0 + 1);
                var y1 = Math.Min(TileSampleSize - 1, y0 + 1);
                var tx = x - x0;
                var ty = y - y0;

                var z00 = ReadSample(x0, y0);
                var z10 = ReadSample(x1, y0);
                var z01 = ReadSample(x0, y1);
                var z11 = ReadSample(x1, y1);
                if (z00 == NoDataValue || z10 == NoDataValue || z01 == NoDataValue || z11 == NoDataValue)
                {
                    return NoDataValue;
                }

                var north = Lerp(z00, z10, tx);
                var south = Lerp(z01, z11, tx);
                return (float)Lerp(north, south, ty);
            }

            private short ReadSample(int x, int y)
            {
                var index = ((y * TileSampleSize) + x) * 2;
                return (short)((_bytes[index] << 8) | _bytes[index + 1]);
            }

            private static double Lerp(double a, double b, double amount)
            {
                return a + ((b - a) * amount);
            }
        }
    }
}
