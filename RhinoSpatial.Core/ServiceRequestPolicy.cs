namespace RhinoSpatial.Core;

/// <summary>Per-operation limits for the older WMS, WCS and Overpass clients.</summary>
public sealed record ServiceRequestPolicy
{
    public int MaximumMetadataBytes { get; init; } = 64 * 1024 * 1024;
    public int MaximumImageBytes { get; init; } = 64 * 1024 * 1024;
    public int MaximumCoverageBytes { get; init; } = 256 * 1024 * 1024;
    public int MaximumOsmBytes { get; init; } = 32 * 1024 * 1024;
    public int MaximumImagePixels { get; init; } = 16777216;
    public int MaximumOsmElements { get; init; } = 500000;
    public int MaximumOsmCoordinates { get; init; } = 2000000;
    public TimeSpan OperationTimeout { get; init; } = TimeSpan.FromMinutes(2);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(60);

    internal void Validate()
    {
        if (MaximumMetadataBytes <= 0 || MaximumImageBytes <= 0 || MaximumCoverageBytes <= 0 ||
            MaximumOsmBytes <= 0 || MaximumImagePixels <= 0 || MaximumOsmElements <= 0 || MaximumOsmCoordinates <= 0 ||
            OperationTimeout <= TimeSpan.Zero || OperationTimeout.TotalMilliseconds > int.MaxValue ||
            RequestTimeout <= TimeSpan.Zero || RequestTimeout.TotalMilliseconds > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(ServiceRequestPolicy), "Service limits and deadlines must be positive and finite.");
    }
}

/// <summary>A deliberate resource limit; retrying another endpoint must not bypass it.</summary>
public sealed class ServiceLimitException : InvalidOperationException
{
    public ServiceLimitException(string message) : base(message) { }
}
