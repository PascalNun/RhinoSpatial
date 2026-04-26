using System.Collections.Generic;

namespace RhinoSpatial
{
    internal sealed class Google3dTilesTileDescriptor
    {
        public string Url { get; set; } = string.Empty;

        public List<double> Transform { get; set; } = new();
    }
}
