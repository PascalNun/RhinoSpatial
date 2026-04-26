using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Rhino.Geometry;
using SharpGLTF.Schema2;

namespace RhinoSpatial
{
    internal sealed class Google3dTilesDecodedPrimitive
    {
        public string Name { get; init; } = string.Empty;

        public List<Point3d> EcefVertices { get; init; } = new();

        public List<Point2f> TextureCoordinates { get; init; } = new();

        public List<int> TriangleIndices { get; init; } = new();

        public byte[]? BaseColorTextureBytes { get; init; }

        public string? BaseColorTextureMimeType { get; init; }

        public bool IsUnlit { get; init; }
    }

    internal static class Google3dTilesGlbDecoder
    {
        public static List<Google3dTilesDecodedPrimitive> Decode(byte[] glbBytes)
        {
            using var stream = new MemoryStream(glbBytes, writable: false);
            var model = ModelRoot.ReadGLB(stream);
            var decodedPrimitives = new List<Google3dTilesDecodedPrimitive>();

            foreach (var logicalMesh in model.LogicalMeshes)
            {
                var meshNodes = model.LogicalNodes
                    .Where(node => node.Mesh == logicalMesh)
                    .ToArray();

                if (meshNodes.Length == 0)
                {
                    meshNodes = Array.Empty<Node>();
                }

                foreach (var meshNode in meshNodes.DefaultIfEmpty())
                {
                    var worldMatrix = meshNode?.WorldMatrix ?? Matrix4x4.Identity;

                    foreach (var primitive in logicalMesh.Primitives)
                    {
                        var positionAccessor = primitive.GetVertexAccessor("POSITION");
                        if (positionAccessor is null)
                        {
                            continue;
                        }

                        var positions = positionAccessor.AsVector3Array().ToArray();
                        if (positions.Length < 3)
                        {
                            continue;
                        }

                        var textureAccessor = primitive.GetVertexAccessor("TEXCOORD_0");
                        var textureCoordinates = textureAccessor is null
                            ? null
                            : textureAccessor.AsVector2Array().ToArray();

                        var triangleIndices = new List<int>();
                        foreach (var (a, b, c) in primitive.GetTriangleIndices())
                        {
                            triangleIndices.Add(a);
                            triangleIndices.Add(b);
                            triangleIndices.Add(c);
                        }

                        if (triangleIndices.Count == 0)
                        {
                            continue;
                        }

                        var transformedVertices = new List<Point3d>(positions.Length);
                        foreach (var position in positions)
                        {
                            var transformed = Vector3.Transform(position, worldMatrix);
                            transformedVertices.Add(new Point3d(transformed.X, transformed.Y, transformed.Z));
                        }

                        var mappedTextureCoordinates = new List<Point2f>(positions.Length);
                        for (var vertexIndex = 0; vertexIndex < positions.Length; vertexIndex++)
                        {
                            if (textureCoordinates is not null && vertexIndex < textureCoordinates.Length)
                            {
                                mappedTextureCoordinates.Add(new Point2f(
                                    textureCoordinates[vertexIndex].X,
                                    textureCoordinates[vertexIndex].Y));
                            }
                            else
                            {
                                mappedTextureCoordinates.Add(new Point2f(0f, 0f));
                            }
                        }

                        var texture = primitive.Material?.FindChannel("BaseColor")?.Texture;
                        var image = texture?.PrimaryImage;
                        var imageBytes = image is null ? null : image.Content.Content.ToArray();
                        string? mimeType = null;

                        if (image is not null)
                        {
                            var content = image.Content;
                            if (content.IsJpg)
                            {
                                mimeType = "image/jpeg";
                            }
                            else if (content.IsPng)
                            {
                                mimeType = "image/png";
                            }
                        }

                        decodedPrimitives.Add(new Google3dTilesDecodedPrimitive
                        {
                            Name = string.IsNullOrWhiteSpace(logicalMesh.Name) ? "Google 3D Tile" : logicalMesh.Name,
                            EcefVertices = transformedVertices,
                            TextureCoordinates = mappedTextureCoordinates,
                            TriangleIndices = triangleIndices,
                            BaseColorTextureBytes = imageBytes,
                            BaseColorTextureMimeType = mimeType,
                            IsUnlit = primitive.Material?.Unlit ?? false
                        });
                    }
                }
            }

            return decodedPrimitives;
        }
    }
}
