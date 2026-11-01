using System.Globalization;
using RhinoSpatial.Core;

namespace RhinoSpatial.Sandbox;

internal static class SourceCompatibilityProbe
{
    public static async Task RunAsync(string[] args)
    {
        if(args.Length < 3) throw new ArgumentException("Usage: compatibility-probe discover|reference|vector|buildings|terrain <url> [srs bbox]");
        var mode=args[1]; var url=args[2];
        if(mode=="discover")
        {
            var collections=await new OgcApiFeaturesClient().TryDiscoverAsync(url) ?? throw new Exception("Not an OGC API.");
            Console.WriteLine($"Collections: {collections.Count}");
            foreach(var c in collections.Take(12)) Console.WriteLine($"{c.Id} | {c.Title} | {c.ItemsUrl} | {c.Wgs84Bounds}");
        }
        else if(mode=="reference")
        {
            var metadata=await new RemoteReferenceMetadataReader().TryReadAsync(url) ?? throw new Exception("No remote metadata.");
            Console.WriteLine(metadata);
        }
        else if(mode=="terrain")
        {
            if(args.Length<5) throw new Exception("Terrain probe requires source SRS and minX,minY,maxX,maxY.");
            var values=args[4].Split(',').Select(x=>double.Parse(x,CultureInfo.InvariantCulture)).ToArray();
            var context=Lod2AtomChecks.Context(args[3],new BoundingBox2D(values[0],values[1],values[2],values[3]));
            var result=await new TerrainAtomLoader().TryLoadAsync(url,context,Console.WriteLine) ?? throw new Exception("Not an ATOM feed.");
            var mesh=TerrainMeshBuilder.Build(result.Raster,result.Bounds,context,512,0);
            var finite=result.Raster.Elevations.Where(x=>!TerrainMeshBuilder.IsNoData(result.Raster,x)).ToArray();
            Console.WriteLine(result.Status);
            Console.WriteLine($"Bounds: {result.Bounds}; mesh: {mesh.Vertices.Count} vertices, {mesh.Faces.Count} faces; heights {finite.Min()}..{finite.Max()}");
        }
        else
        {
            var kind=mode=="buildings"?AtomDownloadKind.Buildings:AtomDownloadKind.Vector;
            var result=await new SourceDownloadCache(kind).DownloadAsync(new Lod2AtomLink(new Uri(url),mode,"",null));
            Console.WriteLine($"Validated {(result.UsedCache?"cached":"downloaded")} payload: {new FileInfo(result.FilePath).Length} bytes");
            using var files=SourceFilesLease.Open(result.FilePath);
            foreach(var file in files.Files)
            {
                var extension=Path.GetExtension(file).ToLowerInvariant();
                if(kind==AtomDownloadKind.Buildings && extension is ".cityjson" or ".json")
                {
                    var text=File.ReadAllText(file); var metadata=CityJsonReader.ReadSourceMetadata(text);
                    Console.WriteLine($"CityJSON: {metadata.SrsName}, bounds {metadata.BoundingBox}, buildings {CityJsonReader.ReadBuildings(text,"probe").Count}");
                }
                else if(kind==AtomDownloadKind.Vector && extension is ".geojson" or ".json")
                {
                    var text=File.ReadAllText(file);
                    Console.WriteLine($"GeoJSON: {GeoJsonReader.ReadFeatures(text,"probe").Count} features, {GeoJsonReader.TryReadSourceSrs(text)??"EPSG:4326"}");
                }
                else if(kind==AtomDownloadKind.Vector && extension==".shp")
                {
                    var metadata=ShapefileFeatureReader.ReadSourceMetadata(file);
                    if(string.IsNullOrWhiteSpace(metadata.SourceSrs) || metadata.BoundingBox is null) throw new Exception("Shapefile metadata unresolved.");
                    var context=Lod2AtomChecks.Context(metadata.SourceSrs,metadata.BoundingBox);
                    var read=ShapefileFeatureReader.ReadFeatures(file,"probe",context,3);
                    Console.WriteLine($"Shapefile: {metadata.SourceSrs}, {read.Features.Count} bounded sample features, bounds {metadata.BoundingBox}");
                }
            }
        }
    }
}
