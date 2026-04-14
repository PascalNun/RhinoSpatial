namespace RhinoSpatial.Core
{
    public record OsmRequestOptions
    {
        public string BaseUrl { get; init; } = string.Empty;

        public BoundingBox2D BoundingBox4326 { get; init; } = new(0.0, 0.0, 0.0, 0.0);

        public bool IncludeBuildings { get; init; } = true;

        public bool IncludeRoads { get; init; } = true;

        public bool IncludeWater { get; init; }

        public bool IncludeGreen { get; init; }

        public bool IncludeRail { get; init; }

        public int TimeoutSeconds { get; init; } = 40;
    }
}
