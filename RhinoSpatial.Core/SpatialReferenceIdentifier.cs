using System.Globalization;
using System.Text.RegularExpressions;

namespace RhinoSpatial.Core;

/// <summary>Identifier syntax only; this does not imply that a CRS can be transformed.</summary>
internal static partial class SpatialReferenceIdentifier
{
    [GeneratedRegex(@"\Aurn:ogc:def:crs:EPSG:([0-9]+(?:\.[0-9]+)*)?:([0-9]+)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EpsgUrn();

    [GeneratedRegex(@"\Aurn:ogc:def:crs:OGC:([0-9]+(?:\.[0-9]+)*)?:CRS84\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Crs84Urn();

    [GeneratedRegex(@"\Aurn:adv:crs:ETRS89_UTM(32|33)(?:\*[A-Za-z0-9_:-]+)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AdvUrn();

    [GeneratedRegex(@"\A/def/crs/(EPSG|OGC)/([0-9]+(?:\.[0-9]+)*)/([A-Za-z0-9]+)\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DefinitionPath();

    public static string Normalize(string? identifier)
    {
        var text = identifier?.Trim() ?? string.Empty;
        // Canonical identifiers dominate coordinate loops; avoid URI/regex allocations for them.
        if (text.StartsWith("EPSG:", StringComparison.OrdinalIgnoreCase)) return Epsg(text.AsSpan(5), text);
        var urn = EpsgUrn().Match(text);
        if (urn.Success) return Epsg(urn.Groups[2].Value, text);
        var adv = AdvUrn().Match(text);
        // The vertical reference is deliberately not transformed or relabelled.
        if (adv.Success) return "EPSG:258" + adv.Groups[1].Value;
        if (IsCrs84(text)) return "EPSG:4326";
        if (TryOgcUrl(text, out var uri))
        {
            var path = DefinitionPath().Match(uri.AbsolutePath);
            if (path.Success && path.Groups[1].Value.Equals("EPSG", StringComparison.OrdinalIgnoreCase) && uri.Fragment.Length == 0)
                return Epsg(path.Groups[3].Value, text);
            if (uri.AbsolutePath.Equals("/gml/srs/epsg.xml", StringComparison.OrdinalIgnoreCase) && uri.Fragment.Length > 1)
                return Epsg(uri.Fragment.AsSpan(1), text);
        }
        return text.ToUpperInvariant();
    }

    public static bool IsCrs84(string? identifier)
    {
        var text = identifier?.Trim() ?? string.Empty;
        if (text.Equals("CRS84", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("CRS:84", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("OGC:CRS84", StringComparison.OrdinalIgnoreCase) || Crs84Urn().IsMatch(text)) return true;
        if (!TryOgcUrl(text, out var uri) || uri.Fragment.Length != 0) return false;
        var path = DefinitionPath().Match(uri.AbsolutePath);
        return path.Success && path.Groups[1].Value.Equals("OGC", StringComparison.OrdinalIgnoreCase) &&
            path.Groups[3].Value.Equals("CRS84", StringComparison.OrdinalIgnoreCase);
    }

    private static string Epsg(ReadOnlySpan<char> code, string original)
    {
        foreach (var c in code)
            if (c is < '0' or > '9') return original.ToUpperInvariant();
        if (!int.TryParse(code, NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value <= 0)
            return original.ToUpperInvariant();
        if (value == 900913) return "EPSG:3857";
        if (original.StartsWith("EPSG:", StringComparison.Ordinal) && code[0] != '0') return original;
        return "EPSG:" + value.ToString(CultureInfo.InvariantCulture);
    }

    private static bool TryOgcUrl(string text, out Uri uri)
    {
        uri = null!;
        return Uri.TryCreate(text, UriKind.Absolute, out uri!) &&
            uri.Scheme is "http" or "https" && uri.IsDefaultPort && uri.UserInfo.Length == 0 && uri.Query.Length == 0 &&
            (uri.Host.Equals("www.opengis.net", StringComparison.OrdinalIgnoreCase) || uri.Host.Equals("opengis.net", StringComparison.OrdinalIgnoreCase));
    }
}
