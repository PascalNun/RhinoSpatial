using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Rhino.Geometry;
using SharpGLTF.Schema2;

namespace RhinoSpatial
{
    internal sealed class Google3dTilesDecodedTile
    {
        public List<Google3dTilesDecodedPrimitive> Primitives { get; init; } = new();

        public bool UsesDracoCompression { get; init; }

        public bool RequiresDracoCompression { get; init; }

        public int SkippedPrimitiveCount { get; init; }

        public string Copyright { get; init; } = string.Empty;

        public string LastError { get; init; } = string.Empty;
    }

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
        public static Google3dTilesDecodedTile Decode(byte[] glbBytes)
        {
            var jsonText = TryReadGlbJsonText(glbBytes);
            var usesDracoCompression = jsonText.Contains("KHR_draco_mesh_compression", StringComparison.Ordinal);
            var requiresDracoCompression = jsonText.Contains("\"extensionsRequired\"", StringComparison.Ordinal) &&
                jsonText.Contains("KHR_draco_mesh_compression", StringComparison.Ordinal);

            using var stream = new MemoryStream(glbBytes, writable: false);
            var model = ModelRoot.ReadGLB(stream);
            var decodedPrimitives = new List<Google3dTilesDecodedPrimitive>();
            var skippedPrimitiveCount = 0;

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
                            skippedPrimitiveCount++;
                            continue;
                        }

                        var positions = positionAccessor.AsVector3Array().ToArray();
                        if (positions.Length < 3)
                        {
                            skippedPrimitiveCount++;
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
                            skippedPrimitiveCount++;
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

            return new Google3dTilesDecodedTile
            {
                Primitives = decodedPrimitives,
                UsesDracoCompression = usesDracoCompression,
                RequiresDracoCompression = requiresDracoCompression,
                SkippedPrimitiveCount = skippedPrimitiveCount,
                Copyright = model.Asset?.Copyright ?? string.Empty
            };
        }

        private static string TryReadGlbJsonText(byte[] glbBytes)
        {
            const uint glbMagic = 0x46546C67;
            const uint jsonChunkType = 0x4E4F534A;

            if (glbBytes.Length < 20 ||
                BitConverter.ToUInt32(glbBytes, 0) != glbMagic)
            {
                return string.Empty;
            }

            var offset = 12;
            while (offset + 8 <= glbBytes.Length)
            {
                var chunkLength = checked((int)BitConverter.ToUInt32(glbBytes, offset));
                var chunkType = BitConverter.ToUInt32(glbBytes, offset + 4);
                offset += 8;

                if (chunkLength < 0 || offset + chunkLength > glbBytes.Length)
                {
                    return string.Empty;
                }

                if (chunkType == jsonChunkType)
                {
                    return Encoding.UTF8.GetString(glbBytes, offset, chunkLength);
                }

                offset += chunkLength;
            }

            return string.Empty;
        }
    }
}
