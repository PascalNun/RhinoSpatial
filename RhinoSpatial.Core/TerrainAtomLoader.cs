namespace RhinoSpatial.Core;

public record TerrainAtomResult(TerrainRasterData Raster, BoundingBox2D Bounds, string Status);

public sealed class TerrainAtomLoader
{
    private readonly AtomDownloadClient _client;
    public TerrainAtomLoader(HttpClient? http = null, string? cacheDirectory = null) => _client = new AtomDownloadClient(http, cacheDirectory, AtomDownloadKind.Terrain);

    public async Task<TerrainAtomResult?> TryLoadAsync(string source, SpatialContext2D context,
        Action<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var selection = await _client.TrySelectAsync(source, context, cancellationToken).ConfigureAwait(false);
        if (selection is null) return null;
        if (selection.Tiles.Count == 0) throw new InvalidOperationException("No terrain ATOM tiles intersect the selected Spatial Context.");
        var downloads = await _client.DownloadAsync(selection, progress, cancellationToken).ConfigureAwait(false);
        var rasters = new List<TerrainRasterData>();
        long samples = 0;
        foreach (var download in downloads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var files = SourceFilesLease.Open(download.FilePath, cancellationToken);
            foreach (var file in files.Files)
            {
                var extension = Path.GetExtension(file).ToLowerInvariant();
                if (extension is not (".tif" or ".tiff" or ".asc" or ".csv" or ".xyz")) continue;
                progress?.Invoke("Reading terrain " + Path.GetFileName(download.Tile.Url.LocalPath));
                var srs = download.Tile.NativeSrs;
                TerrainRasterData raster;
                if (extension is ".tif" or ".tiff")
                {
                    var metadata = GeoTiffReader.ReadImageInfo(file);
                    srs = metadata.SrsName;
                    raster = TerrainRasterReader.ReadRaster(file, Path.GetFileName(file), srs);
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(srs))
                        throw new InvalidDataException("Terrain text downloads require an unambiguous source CRS in the ATOM category metadata.");
                    var window = Lod2AtomFeedReader.TransformBounds(new AtomSpatialExtent(context.ResolvedSrs, context.RequestBoundingBox), srs);
                    raster = TerrainTextGridReader.ReadWindow(file, Path.GetFileName(file), srs, window, cancellationToken).Raster;
                }
                var bounds = Lod2AtomFeedReader.TransformBounds(new AtomSpatialExtent(context.ResolvedSrs, context.RequestBoundingBox), srs);
                raster = TerrainRasterMosaic.Crop(raster, bounds);
                samples += (long)raster.Width * raster.Height;
                if (samples > 8388608) throw new InvalidOperationException("Terrain ATOM windows exceed eight million source samples; select a smaller area.");
                rasters.Add(raster);
            }
        }
        if (rasters.Count == 0) throw new InvalidDataException("Selected terrain archives contained no readable terrain grids.");
        var mosaic = TerrainRasterMosaic.Merge(rasters, cancellationToken);
        var finalBounds = TerrainRasterMosaic.Bounds(mosaic);
        return new TerrainAtomResult(mosaic, finalBounds,
            $"ATOM terrain: {downloads.Count} selected tile(s), {downloads.Count(t => t.UsedCache)} cached, {mosaic.Width}x{mosaic.Height} samples, {mosaic.SrsName}. Tiles: {string.Join(", ", downloads.Select(t => Path.GetFileName(t.Tile.Url.LocalPath)))}.");
    }
}

public static class TerrainRasterMosaic
{
    private const int MaximumCells = 4194304;
    private static void Validate(TerrainRasterData raster)
    {
        if (raster.Width < 2 || raster.Height < 2 || raster.OffsetVectorX.X <= 0 || raster.OffsetVectorY.Y >= 0 ||
            Math.Abs(raster.OffsetVectorX.Y) > 1e-9 || Math.Abs(raster.OffsetVectorY.X) > 1e-9)
            throw new InvalidDataException("Terrain ATOM mosaics require regular north-up grids with at least 2x2 samples.");
    }
    public static BoundingBox2D Bounds(TerrainRasterData raster) => new(raster.Origin.X,
        raster.Origin.Y + (raster.Height - 1) * raster.OffsetVectorY.Y,
        raster.Origin.X + (raster.Width - 1) * raster.OffsetVectorX.X, raster.Origin.Y);

    public static TerrainRasterData Crop(TerrainRasterData raster, BoundingBox2D window)
    {
        Validate(raster);
        var x0 = Math.Clamp((int)Math.Floor((window.MinX - raster.Origin.X) / raster.OffsetVectorX.X), 0, raster.Width - 1);
        var x1 = Math.Clamp((int)Math.Ceiling((window.MaxX - raster.Origin.X) / raster.OffsetVectorX.X), 0, raster.Width - 1);
        var y0 = Math.Clamp((int)Math.Floor((raster.Origin.Y - window.MaxY) / -raster.OffsetVectorY.Y), 0, raster.Height - 1);
        var y1 = Math.Clamp((int)Math.Ceiling((raster.Origin.Y - window.MinY) / -raster.OffsetVectorY.Y), 0, raster.Height - 1);
        var width = x1 - x0 + 1; var height = y1 - y0 + 1;
        if (width < 2 || height < 2 || (long)width * height > MaximumCells)
            throw new InvalidDataException("Terrain window has fewer than 2x2 or more than four million samples; adjust the selected area.");
        var samples = new float[width * height];
        for (var row = 0; row < height; row++) Array.Copy(raster.Elevations, (row + y0) * raster.Width + x0, samples, row * width, width);
        return raster with { Width = width, Height = height, Origin = new Coordinate2D(raster.Origin.X + x0 * raster.OffsetVectorX.X, raster.Origin.Y + y0 * raster.OffsetVectorY.Y), Elevations = samples };
    }

    public static TerrainRasterData Merge(IReadOnlyList<TerrainRasterData> rasters, CancellationToken cancellationToken = default)
    {
        if (rasters.Count == 0) throw new ArgumentException("No terrain rasters.");
        var first = rasters[0];
        foreach (var raster in rasters)
        {
            Validate(raster);
            if (SpatialReferenceTransform.NormalizeSrs(raster.SrsName) != SpatialReferenceTransform.NormalizeSrs(first.SrsName) ||
                Math.Abs(raster.OffsetVectorX.X - first.OffsetVectorX.X) > 1e-6 || Math.Abs(raster.OffsetVectorY.Y - first.OffsetVectorY.Y) > 1e-6)
                throw new InvalidDataException("Terrain tiles have different coordinate systems or grid spacing. They cannot be mosaicked without resampling.");
        }
        var boxes = rasters.Select(Bounds).ToList();
        var minX = boxes.Min(b => b.MinX); var maxY = boxes.Max(b => b.MaxY);
        var dx = first.OffsetVectorX.X; var dy = -first.OffsetVectorY.Y;
        int Index(double value)
        {
            var rounded = Math.Round(value);
            if (!double.IsFinite(value) || Math.Abs(value - rounded) > 1e-5 || rounded < 0 || rounded > int.MaxValue)
                throw new InvalidDataException("Terrain tile origins are not aligned to one regular grid.");
            return (int)rounded;
        }
        var width = checked(Index((boxes.Max(b => b.MaxX) - minX) / dx) + 1);
        var height = checked(Index((maxY - boxes.Min(b => b.MinY)) / dy) + 1);
        if ((long)width * height > MaximumCells) throw new InvalidDataException("Terrain mosaic exceeds four million cells; select a smaller area.");
        var values = Enumerable.Repeat(float.NaN, width * height).ToArray();
        foreach (var raster in rasters)
        {
            var x0 = Index((raster.Origin.X - minX) / dx); var y0 = Index((maxY - raster.Origin.Y) / dy);
            for (var row = 0; row < raster.Height; row++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                for (var column = 0; column < raster.Width; column++)
                {
                    var value = raster.Elevations[row * raster.Width + column];
                    if (TerrainMeshBuilder.IsNoData(raster, value)) continue;
                    var index = (row + y0) * width + column + x0;
                    if (float.IsFinite(values[index]) && Math.Abs(values[index] - value) > 0.01f)
                        throw new InvalidDataException("Overlapping terrain tiles disagree on elevation; no artificial seam was created.");
                    values[index] = value;
                }
            }
        }
        return first with { CoverageId = "ATOM terrain mosaic", Width = width, Height = height,
            Origin = new Coordinate2D(minX, maxY), NoDataValue = double.NaN, Elevations = values };
    }
}
