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

    public record WfsGeometry(
        string Type,
        List<LinearRing> OuterRings
    );

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
