using System.IO;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Render;

namespace RhinoSpatial
{
    internal static class RhinoSpatialRasterDisplayTools
    {
        public static GH_Material? CreateGrasshopperMaterial(string imageFilePath)
        {
            var rhinoMaterial = CreateRhinoMaterial(imageFilePath);
            if (rhinoMaterial is null)
            {
                return null;
            }

            if (RhinoDoc.ActiveDoc is not null)
            {
                var renderMaterial = RenderMaterial.CreateBasicMaterial(rhinoMaterial, RhinoDoc.ActiveDoc);
                return new GH_Material(renderMaterial);
            }

            return new GH_Material(new DisplayMaterial(rhinoMaterial));
        }

        public static Material? CreateRhinoMaterial(string imageFilePath)
        {
            if (string.IsNullOrWhiteSpace(imageFilePath) || !File.Exists(imageFilePath))
            {
                return null;
            }

            var material = new Material
            {
                DiffuseColor = System.Drawing.Color.White,
                Transparency = 0.0,
                DisableLighting = true
            };
            material.SetBitmapTexture(imageFilePath);
            return material;
        }

        public static DisplayMaterial? CreateDisplayMaterial(string imageFilePath)
        {
            var rhinoMaterial = CreateRhinoMaterial(imageFilePath);
            if (rhinoMaterial is null)
            {
                return null;
            }

            return new DisplayMaterial(rhinoMaterial)
            {
                IsTwoSided = true
            };
        }
    }
}
