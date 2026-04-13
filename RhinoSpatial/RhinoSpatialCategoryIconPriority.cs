using Grasshopper;
using Grasshopper.Kernel;

namespace RhinoSpatial
{
    public class RhinoSpatialCategoryIconPriority : GH_AssemblyPriority
    {
        public override GH_LoadingInstruction PriorityLoad()
        {
            var categoryIcon = IconLoader.Load("RhinoSpatial.Resources.Category.png");

            if (categoryIcon is not null)
            {
                Instances.ComponentServer.AddCategoryIcon("RhinoSpatial", categoryIcon);
            }

            return GH_LoadingInstruction.Proceed;
        }
    }
}
