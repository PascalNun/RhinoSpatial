using System.Collections.Generic;

namespace RhinoSpatial.Core
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

    public record WcsCoverageInfo(
        string CoverageId,
        string Title,
        BoundingBox2D? Wgs84BoundingBox
    );

    public record WcsCapabilitiesInfo(
        List<WcsCoverageInfo> Coverages,
        string GetCoverageUrl,
        string DescribeCoverageUrl,
        string ServiceVersion
    );

    public record WcsCoverageDescription(
        string CoverageId,
        string NativeSrs,
        BoundingBox2D NativeBoundingBox,
        Coordinate2D Origin,
        Coordinate2D OffsetVectorX,
        Coordinate2D OffsetVectorY,
        string AxisXLabel,
        string AxisYLabel,
        double? NoDataValue
    );

    public record TerrainRasterData(
        string CoverageId,
        string SrsName,
        int Width,
        int Height,
        Coordinate2D Origin,
        Coordinate2D OffsetVectorX,
        Coordinate2D OffsetVectorY,
        double? NoDataValue,
        float[] Elevations
    );

    public record WcsCoverageResult(
        string RequestUrl,
        string LocalFilePath,
        string ContentType,
        TerrainRasterData Raster
    );

    public record GeoReferencedRasterInfo(
        string LocalFilePath,
        string SrsName,
        int Width,
        int Height,
        BoundingBox2D BoundingBox,
        long FileSizeBytes
    );

    public record OsmAreaFeature(
        long Id,
        List<LinearRing> OuterRings,
        List<LinearRing> InnerRings,
        Dictionary<string, string?> Tags
    );

    public record OsmLinearFeature(
        long Id,
        LineString CenterLine,
        Dictionary<string, string?> Tags
    );

    public record OsmDataSet(
        List<OsmAreaFeature> Buildings,
        List<OsmLinearFeature> Roads,
        List<OsmAreaFeature> WaterAreas,
        List<OsmAreaFeature> GreenAreas,
        List<OsmLinearFeature> Rails
    )
    {
        public string StatusNote { get; init; } = string.Empty;

        public List<string> UnavailableCategories { get; init; } = new();

        public List<string> CachedCategories { get; init; } = new();
    }

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
