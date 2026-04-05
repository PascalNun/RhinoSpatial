using System.Collections.Generic;

namespace WfsCore
{
    public record Coordinate2D(double X, double Y);

    public record BoundingBox2D(
        double MinX,
        double MinY,
        double MaxX,
        double MaxY
    );

    public record LinearRing(List<Coordinate2D> Points);

    public record LineString(List<Coordinate2D> Points);

    public record WfsGeometry
    {
        public string Type { get; init; } = string.Empty;

        public List<LinearRing> OuterRings { get; init; } = new();

        public List<LineString> LineStrings { get; init; } = new();

        public List<Coordinate2D> Points { get; init; } = new();
    }

    public record WfsLayerInfo(
        string Name,
        string Title,
        string DefaultSrs,
        List<string> OtherSrs,
        BoundingBox2D? Wgs84BoundingBox
    );

    public record WfsFeature(
        string Id,
        string SourceLayerName,
        WfsGeometry Geometry,
        Dictionary<string, string?> Attributes
    );
}
