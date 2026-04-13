namespace WfsCore
{
    public class WfsRequestOptions
    {
        public string BaseUrl { get; set; } = string.Empty;

        public string? GetFeatureBaseUrl { get; set; }

        public string TypeName { get; set; } = string.Empty;

        public int MaxFeatures { get; set; }

        public string Version { get; set; } = "1.1.0";

        public string SrsName { get; set; } = string.Empty;

        public string OutputFormat { get; set; } = "application/json";

        public BoundingBox2D? BoundingBox { get; set; }
    }
}
