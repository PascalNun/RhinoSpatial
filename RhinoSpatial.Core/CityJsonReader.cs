using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace RhinoSpatial.Core
{
    public record CityJsonSourceMetadata(string SrsName, BoundingBox2D? BoundingBox);

    public static class CityJsonReader
    {
        public static CityJsonSourceMetadata ReadSourceMetadata(string jsonText)
        {
            using var document = JsonDocument.Parse(jsonText);
            var root = document.RootElement;
            var vertices = ReadVertices(root);
            var srsName = ReadSrsName(root);
            return new CityJsonSourceMetadata(srsName, CalculateBounds(vertices));
        }

        public static List<Lod2Building> ReadBuildings(string jsonText, string sourceLayerName, BoundingBox2D? filterBounds = null)
        {
            using var document = JsonDocument.Parse(jsonText);
            var root = document.RootElement;
            var vertices = ReadVertices(root);
            var buildings = new List<Lod2Building>();

            if (!root.TryGetProperty("CityObjects", out var cityObjects) ||
                cityObjects.ValueKind != JsonValueKind.Object)
            {
                return buildings;
            }

            foreach (var cityObject in cityObjects.EnumerateObject())
            {
                var value = cityObject.Value;
                if (!IsBuildingObject(value))
                {
                    continue;
                }

                var surfaces = new List<SurfacePolygon3D>();
                if (value.TryGetProperty("geometry", out var geometries) &&
                    geometries.ValueKind == JsonValueKind.Array)
                {
                    foreach (var geometry in geometries.EnumerateArray())
                    {
                        if (geometry.TryGetProperty("boundaries", out var boundaries))
                        {
                            CollectSurfaces(boundaries, vertices, surfaces);
                        }
                    }
                }

                if (surfaces.Count == 0)
                {
                    continue;
                }

                var filteredSurfaces = filterBounds is null
                    ? surfaces
                    : surfaces.Where(surface => SurfaceIntersectsBounds(surface, filterBounds)).ToList();
                if (filteredSurfaces.Count == 0)
                {
                    continue;
                }

                buildings.Add(new Lod2Building(
                    cityObject.Name,
                    sourceLayerName,
                    filteredSurfaces,
                    new Dictionary<string, string?>
                    {
                        ["source_format"] = "CityJSON"
                    }));
            }

            return buildings;
        }

        private static bool IsBuildingObject(JsonElement cityObject)
        {
            if (!cityObject.TryGetProperty("type", out var typeElement) ||
                typeElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            var type = typeElement.GetString() ?? string.Empty;
            return type.Contains("Building", StringComparison.OrdinalIgnoreCase);
        }

        private static List<Coordinate3D> ReadVertices(JsonElement root)
        {
            var scale = new[] { 1.0, 1.0, 1.0 };
            var translate = new[] { 0.0, 0.0, 0.0 };

            if (root.TryGetProperty("transform", out var transform))
            {
                if (transform.TryGetProperty("scale", out var scaleElement))
                {
                    ReadTriple(scaleElement, scale);
                }

                if (transform.TryGetProperty("translate", out var translateElement))
                {
                    ReadTriple(translateElement, translate);
                }
            }

            var vertices = new List<Coordinate3D>();
            if (!root.TryGetProperty("vertices", out var verticesElement) ||
                verticesElement.ValueKind != JsonValueKind.Array)
            {
                return vertices;
            }

            foreach (var vertex in verticesElement.EnumerateArray())
            {
                if (vertex.ValueKind != JsonValueKind.Array || vertex.GetArrayLength() < 3)
                {
                    continue;
                }

                vertices.Add(new Coordinate3D(
                    vertex[0].GetDouble() * scale[0] + translate[0],
                    vertex[1].GetDouble() * scale[1] + translate[1],
                    vertex[2].GetDouble() * scale[2] + translate[2]));
            }

            return vertices;
        }

        private static void ReadTriple(JsonElement arrayElement, double[] target)
        {
            if (arrayElement.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            for (var index = 0; index < Math.Min(3, arrayElement.GetArrayLength()); index++)
            {
                target[index] = arrayElement[index].GetDouble();
            }
        }

        private static string ReadSrsName(JsonElement root)
        {
            if (!root.TryGetProperty("metadata", out var metadata) ||
                metadata.ValueKind != JsonValueKind.Object ||
                !metadata.TryGetProperty("referenceSystem", out var referenceSystem) ||
                referenceSystem.ValueKind != JsonValueKind.String)
            {
                return string.Empty;
            }

            var value = referenceSystem.GetString() ?? string.Empty;
            var lastSlash = value.LastIndexOf('/');
            var epsgText = lastSlash >= 0 ? value[(lastSlash + 1)..] : value;
            return int.TryParse(epsgText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var epsg)
                ? $"EPSG:{epsg}"
                : string.Empty;
        }

        private static BoundingBox2D? CalculateBounds(IReadOnlyList<Coordinate3D> vertices)
        {
            if (vertices.Count == 0)
            {
                return null;
            }

            return new BoundingBox2D(
                vertices.Min(point => point.X),
                vertices.Min(point => point.Y),
                vertices.Max(point => point.X),
                vertices.Max(point => point.Y));
        }

        private static void CollectSurfaces(
            JsonElement element,
            IReadOnlyList<Coordinate3D> vertices,
            List<SurfacePolygon3D> surfaces)
        {
            if (TryReadSurface(element, vertices, out var surface))
            {
                surfaces.Add(surface);
                return;
            }

            if (element.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var child in element.EnumerateArray())
            {
                CollectSurfaces(child, vertices, surfaces);
            }
        }

        private static bool TryReadSurface(
            JsonElement element,
            IReadOnlyList<Coordinate3D> vertices,
            out SurfacePolygon3D surface)
        {
            surface = new SurfacePolygon3D(new List<Coordinate3D>(), new List<List<Coordinate3D>>());
            if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() == 0)
            {
                return false;
            }

            var firstRing = element[0];
            if (!IsIndexRing(firstRing))
            {
                return false;
            }

            var rings = new List<List<Coordinate3D>>();
            foreach (var ringElement in element.EnumerateArray())
            {
                var ring = ReadRing(ringElement, vertices);
                if (ring.Count >= 3)
                {
                    rings.Add(ring);
                }
            }

            if (rings.Count == 0)
            {
                return false;
            }

            surface = new SurfacePolygon3D(
                rings[0],
                rings.Skip(1).ToList());
            return true;
        }

        private static bool IsIndexRing(JsonElement element)
        {
            return element.ValueKind == JsonValueKind.Array &&
                   element.GetArrayLength() >= 3 &&
                   element.EnumerateArray().All(item => item.ValueKind == JsonValueKind.Number);
        }

        private static List<Coordinate3D> ReadRing(JsonElement element, IReadOnlyList<Coordinate3D> vertices)
        {
            var ring = new List<Coordinate3D>();
            if (!IsIndexRing(element))
            {
                return ring;
            }

            foreach (var indexElement in element.EnumerateArray())
            {
                var index = indexElement.GetInt32();
                if (index >= 0 && index < vertices.Count)
                {
                    ring.Add(vertices[index]);
                }
            }

            return ring;
        }

        private static bool SurfaceIntersectsBounds(SurfacePolygon3D surface, BoundingBox2D filterBounds)
        {
            if (surface.OuterPoints.Count == 0)
            {
                return false;
            }

            var bounds = new BoundingBox2D(
                surface.OuterPoints.Min(point => point.X),
                surface.OuterPoints.Min(point => point.Y),
                surface.OuterPoints.Max(point => point.X),
                surface.OuterPoints.Max(point => point.Y));
            return bounds.MinX <= filterBounds.MaxX &&
                   bounds.MaxX >= filterBounds.MinX &&
                   bounds.MinY <= filterBounds.MaxY &&
                   bounds.MaxY >= filterBounds.MinY;
        }
    }
}
