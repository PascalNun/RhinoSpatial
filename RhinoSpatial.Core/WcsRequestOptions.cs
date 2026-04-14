namespace RhinoSpatial.Core
{
    public class WcsRequestOptions
    {
        public string BaseUrl { get; set; } = string.Empty;

        public string? GetCoverageBaseUrl { get; set; }

        public string? DescribeCoverageBaseUrl { get; set; }

        public string CoverageId { get; set; } = string.Empty;

        public BoundingBox2D BoundingBox { get; set; } = new(0, 0, 0, 0);

        public string SrsName { get; set; } = string.Empty;

        public string Version { get; set; } = "2.0.1";

        public string Format { get; set; } = "image/tiff";

        public string? AxisXLabel { get; set; }

        public string? AxisYLabel { get; set; }
    }
}
