using System;
using System.Collections.Generic;
using Rhino.Display;
using Rhino.Geometry;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    internal sealed class Google3dTilesReferenceSession
    {
        internal sealed class DisplayPrimitive
        {
            public Mesh Mesh { get; init; } = null!;

            public DisplayMaterial? Material { get; init; }

            public string SourceUrl { get; init; } = string.Empty;
        }

        public Guid OwnerId { get; set; }

        public string ApiKey { get; set; } = string.Empty;

        public SpatialContext2D SpatialContext { get; set; } = null!;

        public BoundingBox2D BoundingBox4326 { get; set; } = new(0.0, 0.0, 0.0, 0.0);

        public string Status { get; set; } = string.Empty;

        public Curve? AreaFrame { get; set; }

        public List<Mesh> RuntimeMeshes { get; set; } = new();

        public List<DisplayPrimitive> DecodedPrimitives { get; set; } = new();

        public DateTime LastRuntimeUpdateUtc { get; set; } = DateTime.MinValue;

        public int RuntimeTriangleCount { get; set; }

        public string LastTileBatchSignature { get; set; } = string.Empty;

        public bool TileBatchInProgress { get; set; }

        public DisplayMaterial TileMaterial { get; init; } = new(System.Drawing.Color.FromArgb(220, 174, 174, 174))
        {
            IsTwoSided = true,
            Shine = 0.2
        };

        public System.Drawing.Color FrameColor { get; init; } = System.Drawing.Color.FromArgb(255, 255, 214, 10);

        public bool IsUsable =>
            !string.IsNullOrWhiteSpace(ApiKey) &&
            AreaFrame is not null;
    }
}
