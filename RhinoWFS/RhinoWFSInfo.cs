using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace RhinoWFS
{
    public class RhinoWFSInfo : GH_AssemblyInfo
    {
        public override string Name => "RhinoWFS";

        public override Bitmap? Icon => null;

        public override string Description => "Grasshopper components for listing WFS layers and loading WFS data.";

        public override Guid Id => new Guid("1b7de011-9623-49c8-b867-3a2116c9549f");

        public override string AuthorName => "RhinoWFS";

        public override string AuthorContact => string.Empty;

        public override string AssemblyVersion => GetType().Assembly.GetName().Version?.ToString() ?? "0.1.0";
    }
}
