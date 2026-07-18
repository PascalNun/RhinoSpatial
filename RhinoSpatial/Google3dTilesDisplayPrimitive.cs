using Rhino.Display;
using Rhino.Geometry;

namespace RhinoSpatial
{
    public sealed class Google3dTilesDisplayPrimitive
    {
        public Mesh Mesh { get; init; } = null!;

        public DisplayMaterial? Material { get; init; }

        public string TextureFilePath { get; init; } = string.Empty;

        public string SourceUrl { get; init; } = string.Empty;

        public string SourceKey { get; init; } = string.Empty;
    }
}
