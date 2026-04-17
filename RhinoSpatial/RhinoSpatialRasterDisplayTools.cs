using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Grasshopper.Kernel.Types;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Render;
using Rhino.Geometry;

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

        public static Guid BakeTexturedMesh(
            RhinoDoc doc,
            Mesh previewMesh,
            string imageFilePath,
            ObjectAttributes sourceAttributes)
        {
            if (previewMesh is null || string.IsNullOrWhiteSpace(imageFilePath) || !File.Exists(imageFilePath))
            {
                return Guid.Empty;
            }

            var meshToBake = previewMesh.DuplicateMesh();
            var attributes = sourceAttributes.Duplicate();
            RenderMaterial? renderMaterial = null;

            if (TryAssignBakeMaterial(doc, imageFilePath, attributes, out renderMaterial) && renderMaterial is not null)
            {
                attributes.RenderMaterial = renderMaterial;
            }

            var objectId = doc.Objects.AddMesh(meshToBake, attributes);
            if (objectId != Guid.Empty && renderMaterial is not null)
            {
                doc.Objects.ModifyRenderMaterial(objectId, renderMaterial);
            }

            return objectId;
        }

        private static bool TryAssignBakeMaterial(
            RhinoDoc doc,
            string imageFilePath,
            ObjectAttributes attributes,
            out RenderMaterial? renderMaterial)
        {
            renderMaterial = null;

            var materialIndex = FindOrCreateRhinoMaterial(doc, imageFilePath);
            if (materialIndex < 0)
            {
                return false;
            }

            attributes.MaterialIndex = materialIndex;
            attributes.MaterialSource = ObjectMaterialSource.MaterialFromObject;
            renderMaterial = FindOrCreateRenderMaterial(doc, doc.Materials[materialIndex], imageFilePath);
            return true;
        }

        private static int FindOrCreateRhinoMaterial(RhinoDoc doc, string imageFilePath)
        {
            var normalizedFilePath = NormalizeFilePath(imageFilePath);
            if (string.IsNullOrWhiteSpace(normalizedFilePath))
            {
                return -1;
            }

            for (var materialIndex = 0; materialIndex < doc.Materials.Count; materialIndex++)
            {
                var existingMaterial = doc.Materials[materialIndex];
                if (existingMaterial is null)
                {
                    continue;
                }

                if (MaterialUsesImage(existingMaterial, normalizedFilePath))
                {
                    return materialIndex;
                }
            }

            var material = CreateRhinoMaterial(normalizedFilePath);
            if (material is null)
            {
                return -1;
            }

            material.Name = BuildRasterMaterialName(normalizedFilePath);
            material.CommitChanges();
            return doc.Materials.Add(material);
        }

        private static RenderMaterial? FindOrCreateRenderMaterial(RhinoDoc doc, Material material, string imageFilePath)
        {
            var materialName = BuildRasterMaterialName(imageFilePath);
            foreach (var existingRenderMaterial in doc.RenderMaterials)
            {
                if (existingRenderMaterial is not null &&
                    string.Equals(existingRenderMaterial.Name, materialName, StringComparison.Ordinal))
                {
                    return existingRenderMaterial;
                }
            }

            var renderMaterial = RenderMaterial.CreateBasicMaterial(material, doc);
            if (renderMaterial is null)
            {
                return null;
            }

            renderMaterial.Name = materialName;
            doc.RenderMaterials.Add(renderMaterial);
            return renderMaterial;
        }

        private static bool MaterialUsesImage(Material material, string normalizedFilePath)
        {
            var texture = material.GetBitmapTexture();
            if (texture is null || string.IsNullOrWhiteSpace(texture.FileName))
            {
                return false;
            }

            return string.Equals(NormalizeFilePath(texture.FileName), normalizedFilePath, StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildRasterMaterialName(string imageFilePath)
        {
            var normalizedFilePath = NormalizeFilePath(imageFilePath);
            using var sha1 = SHA1.Create();
            var hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(normalizedFilePath));
            var hash = Convert.ToHexString(hashBytes)[..10];
            return $"RhinoSpatial Raster {Path.GetFileNameWithoutExtension(normalizedFilePath)} {hash}";
        }

        private static string NormalizeFilePath(string imageFilePath)
        {
            return string.IsNullOrWhiteSpace(imageFilePath)
                ? string.Empty
                : Path.GetFullPath(imageFilePath.Trim());
        }
    }
}
