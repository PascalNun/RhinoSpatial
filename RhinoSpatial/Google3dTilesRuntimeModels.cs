using System.Collections.Generic;

namespace RhinoSpatial
{
    internal sealed class Google3dTilesRuntimeStatusPayload
    {
        public string OwnerId { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;
    }

    internal sealed class Google3dTilesTileBatchPayload
    {
        public string OwnerId { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public List<Google3dTilesTileDescriptorPayload> Tiles { get; set; } = new();
    }

    internal sealed class Google3dTilesTileDescriptorPayload
    {
        public string Url { get; set; } = string.Empty;

        public List<double> Transform { get; set; } = new();
    }
}
