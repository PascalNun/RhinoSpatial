using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace RhinoSpatial.Core
{
    public readonly record struct SpatialHeightConversionResult(
        double HeightMeters,
        double AppliedOffsetMeters,
        string Method
    );

    public static class SpatialVerticalDatumTransform
    {
        private const string Egm96ResourceName = "RhinoSpatial.Core.Resources.egm96-15.pgm";
        private static readonly Lazy<PgmGeoidGrid?> GlobalGeoidGrid = new(LoadEmbeddedEgm96Grid);

        public static SpatialHeightConversionResult ConvertWgs84EllipsoidHeightToSpatialHeight(
            double latitudeDegrees,
            double longitudeDegrees,
            double ellipsoidHeightMeters)
        {
            if (!TryEstimateWgs84GeoidUndulationMeters(
                    latitudeDegrees,
                    longitudeDegrees,
                    out var geoidUndulationMeters,
                    out var method))
            {
                return new SpatialHeightConversionResult(
                    ellipsoidHeightMeters,
                    0.0,
                    "WGS84 ellipsoid height");
            }

            return new SpatialHeightConversionResult(
                ellipsoidHeightMeters - geoidUndulationMeters,
                geoidUndulationMeters,
                method);
        }

        public static bool TryEstimateWgs84GeoidUndulationMeters(
            double latitudeDegrees,
            double longitudeDegrees,
            out double geoidUndulationMeters,
            out string method)
        {
            if (!IsFinite(latitudeDegrees) || !IsFinite(longitudeDegrees))
            {
                geoidUndulationMeters = 0.0;
                method = string.Empty;
                return false;
            }

            var globalGeoidGrid = GlobalGeoidGrid.Value;
            if (globalGeoidGrid is not null &&
                globalGeoidGrid.TryGetUndulationMeters(latitudeDegrees, longitudeDegrees, out geoidUndulationMeters))
            {
                method = "EGM96 15-minute global geoid grid";
                return true;
            }

            geoidUndulationMeters = 0.0;
            method = string.Empty;
            return false;
        }

        private static PgmGeoidGrid? LoadEmbeddedEgm96Grid()
        {
            try
            {
                var assembly = typeof(SpatialVerticalDatumTransform).GetTypeInfo().Assembly;
                using var stream = assembly.GetManifestResourceStream(Egm96ResourceName);
                return stream is null ? null : PgmGeoidGrid.Read(stream);
            }
            catch
            {
                return null;
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }

    internal sealed class PgmGeoidGrid
    {
        private readonly int _width;
        private readonly int _height;
        private readonly double _offset;
        private readonly double _scale;
        private readonly ushort[] _samples;

        private PgmGeoidGrid(
            int width,
            int height,
            double offset,
            double scale,
            ushort[] samples)
        {
            _width = width;
            _height = height;
            _offset = offset;
            _scale = scale;
            _samples = samples;
        }

        public static PgmGeoidGrid Read(Stream stream)
        {
            var comments = new List<string>();
            var magic = ReadToken(stream, comments);
            if (!string.Equals(magic, "P5", StringComparison.Ordinal))
            {
                throw new InvalidDataException("Expected a binary PGM geoid grid.");
            }

            var width = int.Parse(ReadToken(stream, comments), CultureInfo.InvariantCulture);
            var height = int.Parse(ReadToken(stream, comments), CultureInfo.InvariantCulture);
            var maxValue = int.Parse(ReadToken(stream, comments), CultureInfo.InvariantCulture);
            if (width <= 0 || height <= 1 || maxValue != 65535)
            {
                throw new InvalidDataException("Unsupported PGM geoid grid dimensions or sample depth.");
            }

            var offset = ParseHeaderDouble(comments, "Offset", 0.0);
            var scale = ParseHeaderDouble(comments, "Scale", 1.0);
            var samples = new ushort[checked(width * height)];
            for (var index = 0; index < samples.Length; index++)
            {
                var high = stream.ReadByte();
                var low = stream.ReadByte();
                if (high < 0 || low < 0)
                {
                    throw new EndOfStreamException("The PGM geoid grid ended before all samples were read.");
                }

                samples[index] = (ushort)((high << 8) | low);
            }

            return new PgmGeoidGrid(width, height, offset, scale, samples);
        }

        public bool TryGetUndulationMeters(double latitudeDegrees, double longitudeDegrees, out double undulationMeters)
        {
            undulationMeters = 0.0;
            if (!IsFinite(latitudeDegrees) || !IsFinite(longitudeDegrees))
            {
                return false;
            }

            var latitude = Math.Clamp(latitudeDegrees, -90.0, 90.0);
            var longitude = NormalizeLongitude(longitudeDegrees);
            var longitudeStep = 360.0 / _width;
            var latitudeStep = 180.0 / (_height - 1);

            var x = longitude / longitudeStep;
            var x0 = (int)Math.Floor(x);
            var tx = x - x0;
            x0 = Mod(x0, _width);
            var x1 = Mod(x0 + 1, _width);

            var y = (90.0 - latitude) / latitudeStep;
            var y0 = (int)Math.Floor(y);
            var ty = y - y0;
            y0 = Math.Clamp(y0, 0, _height - 1);
            var y1 = Math.Clamp(y0 + 1, 0, _height - 1);
            if (y0 == y1)
            {
                ty = 0.0;
            }

            var northWest = ReadSampleMeters(x0, y0);
            var northEast = ReadSampleMeters(x1, y0);
            var southWest = ReadSampleMeters(x0, y1);
            var southEast = ReadSampleMeters(x1, y1);
            var north = Lerp(northWest, northEast, tx);
            var south = Lerp(southWest, southEast, tx);
            undulationMeters = Lerp(north, south, ty);
            return true;
        }

        private double ReadSampleMeters(int x, int y)
        {
            return _offset + (_scale * _samples[(y * _width) + x]);
        }

        private static string ReadToken(Stream stream, List<string> comments)
        {
            var bytes = new List<byte>();

            while (true)
            {
                var value = stream.ReadByte();
                if (value < 0)
                {
                    throw new EndOfStreamException("Unexpected end of PGM header.");
                }

                var character = (char)value;
                if (character == '#')
                {
                    comments.Add(ReadComment(stream));
                    continue;
                }

                if (char.IsWhiteSpace(character))
                {
                    continue;
                }

                bytes.Add((byte)value);
                break;
            }

            while (true)
            {
                var value = stream.ReadByte();
                if (value < 0)
                {
                    break;
                }

                var character = (char)value;
                if (char.IsWhiteSpace(character))
                {
                    break;
                }

                if (character == '#')
                {
                    comments.Add(ReadComment(stream));
                    break;
                }

                bytes.Add((byte)value);
            }

            return System.Text.Encoding.ASCII.GetString(bytes.ToArray());
        }

        private static string ReadComment(Stream stream)
        {
            var bytes = new List<byte>();
            while (true)
            {
                var value = stream.ReadByte();
                if (value < 0 || value == '\n' || value == '\r')
                {
                    break;
                }

                bytes.Add((byte)value);
            }

            return System.Text.Encoding.ASCII.GetString(bytes.ToArray()).Trim();
        }

        private static double ParseHeaderDouble(IEnumerable<string> comments, string key, double fallback)
        {
            foreach (var comment in comments)
            {
                if (!comment.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var valueText = comment[(key.Length + 1)..].Trim();
                if (double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    return value;
                }
            }

            return fallback;
        }

        private static double NormalizeLongitude(double longitudeDegrees)
        {
            var longitude = longitudeDegrees % 360.0;
            return longitude < 0.0 ? longitude + 360.0 : longitude;
        }

        private static int Mod(int value, int modulus)
        {
            var result = value % modulus;
            return result < 0 ? result + modulus : result;
        }

        private static double Lerp(double a, double b, double amount)
        {
            return a + ((b - a) * amount);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
