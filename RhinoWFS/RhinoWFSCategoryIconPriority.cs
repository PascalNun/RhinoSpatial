using Grasshopper;
using Grasshopper.Kernel;

namespace RhinoWFS
{
    public class RhinoWFSCategoryIconPriority : GH_AssemblyPriority
    {
        public override GH_LoadingInstruction PriorityLoad()
        {
            var categoryIcon = IconLoader.Load("RhinoWFS.Resources.categoryWfs.png");

            if (categoryIcon is not null)
            {
                Instances.ComponentServer.AddCategoryIcon("RhinoWFS", categoryIcon);
            }

            return GH_LoadingInstruction.Proceed;
        }
    }
}
