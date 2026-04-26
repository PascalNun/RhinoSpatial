using System;
using System.Collections.Generic;
using Rhino.Display;
using Rhino.Geometry;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    public sealed class Google3dTilesReferenceSession
    {
        public sealed class DisplayPrimitive
        {
            public Mesh Mesh { get; init; } = null!;

            public DisplayMaterial? Material { get; init; }

            public string TextureFilePath { get; init; } = string.Empty;

            public string SourceUrl { get; init; } = string.Empty;

            public bool IsClippedFallback { get; init; }
        }

        public Guid OwnerId { get; set; }

        public string ApiKey { get; set; } = string.Empty;

        public SpatialContext2D SpatialContext { get; set; } = null!;

        public BoundingBox2D BoundingBox4326 { get; set; } = new(0.0, 0.0, 0.0, 0.0);

        public string Status { get; set; } = string.Empty;

        public Curve? AreaFrame { get; set; }

        public List<DisplayPrimitive> DecodedPrimitives { get; set; } = new();

        public int RuntimeTriangleCount { get; set; }

        public string LastDirectLoadSignature { get; set; } = string.Empty;

        public bool DirectLoadInProgress { get; set; }

        public DateTime DirectLoadStartedUtc { get; set; } = DateTime.MinValue;

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
