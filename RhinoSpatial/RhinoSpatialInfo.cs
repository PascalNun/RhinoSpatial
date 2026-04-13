using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace RhinoSpatial
{
    public class RhinoSpatialInfo : GH_AssemblyInfo
    {
        public override string Name => "RhinoSpatial";

        public override Bitmap? Icon => null;

        public override string Description => "RhinoSpatial — a study-oriented geospatial toolkit for Rhino and Grasshopper.";

        public override Guid Id => new Guid("1b7de011-9623-49c8-b867-3a2116c9549f");

        public override string AuthorName => "Pascal Nun";

        public override string AuthorContact => "https://github.com/PascalNun/RhinoSpatial";

        public override string AssemblyVersion => GetType().Assembly.GetName().Version?.ToString() ?? "0.1.1";
    }
}
