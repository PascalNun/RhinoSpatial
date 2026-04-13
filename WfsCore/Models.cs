using System.Collections.Generic;

namespace WfsCore
{
    public record Coordinate2D(double X, double Y);

    public record Coordinate3D(double X, double Y, double Z);

    public record BoundingBox2D(
        double MinX,
        double MinY,
        double MaxX,
        double MaxY
    );

    public record LinearRing(List<Coordinate2D> Points);

    public record SurfaceRing3D(List<Coordinate3D> Points);

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

    public record WfsCapabilitiesInfo(
        List<WfsLayerInfo> Layers,
        string GetFeatureUrl,
        string ServiceVersion
    );

    public record WfsFeatureResponse(
        string ResponseText,
        WfsRequestOptions AppliedOptions
    );

    public record SpatialContext2D(
        string ResolvedSrs,
        BoundingBox2D RequestBoundingBox,
        BoundingBox2D PlacementBoundingBox,
        BoundingBox2D? Wgs84BoundingBox,
        Coordinate2D PlacementOrigin,
        bool UseAbsoluteCoordinates,
        Dictionary<string, BoundingBox2D> BoundingBoxesBySrs
    );

    public record WmsImageResult(
        string RequestUrl,
        string LocalFilePath,
        string ContentType
    );

    public record WmsCapabilitiesInfo(
        List<WmsLayerInfo> Layers,
        string GetMapUrl,
        string ServiceVersion,
        int? MaxWidth,
        int? MaxHeight
    );

    public record WmsLayerInfo(
        string Name,
        string Title,
        List<string> SupportedSrs,
        BoundingBox2D? Wgs84BoundingBox
    );

    public record Lod2Building(
        string Id,
        string SourceLayerName,
        List<SurfaceRing3D> Surfaces,
        Dictionary<string, string?> Attributes
    );

    public record WfsFeature(
        string Id,
        string SourceLayerName,
        WfsGeometry Geometry,
        Dictionary<string, string?> Attributes
    );
}
