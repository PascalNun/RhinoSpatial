namespace RhinoSpatial.Core
{
    public class WmsRequestOptions
    {
        public string BaseUrl { get; set; } = string.Empty;

        public string? GetMapBaseUrl { get; set; }

        public string LayerName { get; set; } = string.Empty;

        public BoundingBox2D BoundingBox { get; set; } = new(0, 0, 0, 0);

        public string SrsName { get; set; } = string.Empty;

        public int Width { get; set; } = 2048;

        public int Height { get; set; } = 2048;

        public string Version { get; set; } = "1.3.0";

        public string Format { get; set; } = "image/png";

        public bool Transparent { get; set; } = true;
    }
}
