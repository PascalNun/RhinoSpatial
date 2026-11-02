using System.Net;
using RhinoSpatial.Core;

namespace RhinoSpatial.Sandbox;

internal static class ContextStateChecks
{
    public static async Task RunAsync()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var updates = new List<SpatialContextHelperHost.SelectionEventArgs>();
        var count = 0;
        void Require(bool passed, string name)
        {
            if (!passed) throw new InvalidOperationException(name);
            count++;
            Console.WriteLine("PASS " + name);
        }
        void Changed(object? sender, SpatialContextHelperHost.SelectionEventArgs e) { lock (updates) updates.Add(e); }
        SpatialContextHelperHost.SelectionChanged += Changed;
        SpatialContextHelperHost.RegisterSession(a);
        SpatialContextHelperHost.RegisterSession(b);
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var url = SpatialContextHelperHost.EnsureStarted() + "api/selection?";
        async Task<HttpStatusCode> Send(string query)
        {
            using var response = await client.GetAsync(url + query);
            await response.Content.ReadAsStringAsync();
            return response.StatusCode;
        }
        try
        {
            Require(await Send($"session={a:N}&bbox25833=0,0,128,128") == HttpStatusCode.OK, "First map session accepts a selection");
            Require(await Send($"session={b:N}&bbox25833=1000,1000,1020,1020") == HttpStatusCode.OK, "Second map session accepts a different selection");
            Require(updates.Count == 2 && updates[0].Session == a && updates[0].Selection.BoundingBox25833 == "0,0,128,128" &&
                updates[1].Session == b && updates[1].Selection.BoundingBox25833 == "1000,1000,1020,1020", "Selection notifications retain their own session and immutable area");
            Require(await Send($"session={a:N}") == HttpStatusCode.OK && !updates[^1].Selection.HasSelection && updates[^1].Session == a,
                "Clearing a rectangle is delivered to its own map session");
            var before = updates.Count;
            Require(await Send("bbox25833=0,0,1,1") == HttpStatusCode.Gone && updates.Count == before, "Unscoped old map tabs cannot change a study");
            Require(await Send($"session={Guid.NewGuid():N}&bbox25833=0,0,1,1") == HttpStatusCode.Gone && updates.Count == before, "Unknown map session is rejected");
            SpatialContextHelperHost.RemoveSession(a);
            Require(await Send($"session={a:N}&bbox25833=0,0,1,1") == HttpStatusCode.Gone && updates.Count == before, "Removed component's old tab cannot publish a selection");
            Require(await Send($"session={b:N}&bbox25833=2000,2000,2020,2020") == HttpStatusCode.OK && updates.Count == before + 1 && updates[^1].Session == b,
                "Removing one session leaves the other usable");
            foreach (var srs in new[] { "EPSG:25832", "EPSG:25833", "EPSG:27700", "EPSG:28992", "EPSG:7415", "EPSG:3857", "EPSG:32632", "EPSG:32755", "urn:adv:crs:ETRS89_UTM33*DE_DHHN2016_NH" })
                Require(SpatialReferenceTransform.TryGetMetresPerUnit(srs, out var unit) && unit == 1, srs + " declares metre project units");
            foreach (var srs in new[] { "EPSG:4326", "EPSG:4258", "EPSG:4283", "EPSG:7423", "EPSG:7844", "EPSG:9994326", "" })
                Require(!SpatialReferenceTransform.TryGetMetresPerUnit(srs, out _), srs + " is not treated as a linear metre CRS");
            Console.WriteLine($"Context state checks passed: {count}.");
        }
        finally
        {
            SpatialContextHelperHost.RemoveSession(a);
            SpatialContextHelperHost.RemoveSession(b);
            SpatialContextHelperHost.SelectionChanged -= Changed;
        }
    }
}
