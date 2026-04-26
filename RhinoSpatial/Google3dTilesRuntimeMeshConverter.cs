using System;
using System.Collections.Generic;
using Rhino.Geometry;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    internal static class Google3dTilesRuntimeMeshConverter
    {
        private const double Wgs84SemiMajorAxis = 6378137.0;
        private const double Wgs84FirstEccentricitySquared = 6.69437999014e-3;
        private const double Wgs84SemiMinorAxis = 6356752.3142451793;
        private const double Wgs84SecondEccentricitySquared = 6.73949674228e-3;

        public static List<Mesh> ConvertBatch(
            Google3dTilesMeshBatchPayload payload,
            SpatialContext2D spatialContext,
            out int totalTriangleCount)
        {
            totalTriangleCount = 0;
            var convertedMeshes = new List<Mesh>();
            var offsetX = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.X;
            var offsetY = spatialContext.UseAbsoluteCoordinates ? 0.0 : spatialContext.PlacementOrigin.Y;
            var placedBounds = RhinoSpatialContextTools.CreatePlacedBoundingBox(
                spatialContext.PlacementBoundingBox,
                spatialContext.PlacementOrigin,
                spatialContext.UseAbsoluteCoordinates);

            var convertedMeshData = new List<(List<Point3d> Points, List<int> Indices, List<byte>? Colors)>(payload.Meshes.Count);
            var minimumHeight = double.PositiveInfinity;

            foreach (var sourceMesh in payload.Meshes)
            {
                if (sourceMesh.Positions.Count < 9 || sourceMesh.Indices.Count < 3)
                {
                    continue;
                }

                if (sourceMesh.Positions.Count % 3 != 0 || sourceMesh.Indices.Count % 3 != 0)
                {
                    continue;
                }

                var projectedVertices = new List<Point3d>(sourceMesh.Positions.Count / 3);
                var projectedMinimumX = double.PositiveInfinity;
                var projectedMinimumY = double.PositiveInfinity;
                var projectedMaximumX = double.NegativeInfinity;
                var projectedMaximumY = double.NegativeInfinity;
                var isValid = true;

                for (var vertexIndex = 0; vertexIndex < sourceMesh.Positions.Count; vertexIndex += 3)
                {
                    if (!TryConvertEcefToGeodetic(
                            sourceMesh.Positions[vertexIndex],
                            sourceMesh.Positions[vertexIndex + 1],
                            sourceMesh.Positions[vertexIndex + 2],
                            out var latitudeDegrees,
                            out var longitudeDegrees,
                            out var height))
                    {
                        isValid = false;
                        break;
                    }

                    if (!SpatialReferenceTransform.TryTransformXY(
                            "EPSG:4326",
                            spatialContext.ResolvedSrs,
                            longitudeDegrees,
                            latitudeDegrees,
                            out var projectedX,
                            out var projectedY))
                    {
                        isValid = false;
                        break;
                    }

                    projectedMinimumX = Math.Min(projectedMinimumX, projectedX);
                    projectedMinimumY = Math.Min(projectedMinimumY, projectedY);
                    projectedMaximumX = Math.Max(projectedMaximumX, projectedX);
                    projectedMaximumY = Math.Max(projectedMaximumY, projectedY);
                    minimumHeight = Math.Min(minimumHeight, height);

                    projectedVertices.Add(new Point3d(
                        projectedX - offsetX,
                        projectedY - offsetY,
                        height));
                }

                if (!isValid || projectedVertices.Count == 0)
                {
                    continue;
                }

                var projectedBounds = new BoundingBox2D(
                    projectedMinimumX - offsetX,
                    projectedMinimumY - offsetY,
                    projectedMaximumX - offsetX,
                    projectedMaximumY - offsetY);

                if (!RhinoSpatialContextTools.DoBoundingBoxesIntersect(placedBounds, projectedBounds))
                {
                    continue;
                }

                var sourceColors = sourceMesh.Colors.Count >= projectedVertices.Count * 3
                    ? sourceMesh.Colors
                    : null;

                convertedMeshData.Add((projectedVertices, sourceMesh.Indices, sourceColors));
            }

            if (double.IsPositiveInfinity(minimumHeight))
            {
                minimumHeight = 0.0;
            }

            foreach (var (points, indices, sourceColors) in convertedMeshData)
            {
                var mesh = new Mesh();
                var usedVertexMap = new Dictionary<int, int>();

                int GetOrCreateVertexIndex(int sourceVertexIndex)
                {
                    if (usedVertexMap.TryGetValue(sourceVertexIndex, out var existingIndex))
                    {
                        return existingIndex;
                    }

                    var point = points[sourceVertexIndex];
                    var newIndex = mesh.Vertices.Count;
                    mesh.Vertices.Add(
                        point.X,
                        point.Y,
                        spatialContext.UseAbsoluteCoordinates ? point.Z : point.Z - minimumHeight);

                    if (sourceColors is not null)
                    {
                        var colorOffset = sourceVertexIndex * 3;
                        mesh.VertexColors.Add(
                            sourceColors[colorOffset],
                            sourceColors[colorOffset + 1],
                            sourceColors[colorOffset + 2]);
                    }

                    usedVertexMap[sourceVertexIndex] = newIndex;
                    return newIndex;
                }

                for (var faceIndex = 0; faceIndex < indices.Count; faceIndex += 3)
                {
                    var a = indices[faceIndex];
                    var b = indices[faceIndex + 1];
                    var c = indices[faceIndex + 2];

                    if (a < 0 || b < 0 || c < 0)
                    {
                        continue;
                    }

                    if (a >= points.Count || b >= points.Count || c >= points.Count)
                    {
                        continue;
                    }

                    if (a == b || b == c || a == c)
                    {
                        continue;
                    }

                    if (!DoesTriangleIntersectBounds(points[a], points[b], points[c], placedBounds))
                    {
                        continue;
                    }

                    mesh.Faces.AddFace(
                        GetOrCreateVertexIndex(a),
                        GetOrCreateVertexIndex(b),
                        GetOrCreateVertexIndex(c));
                }

                if (mesh.Faces.Count == 0)
                {
                    continue;
                }

                if (mesh.VertexColors.Count == 0)
                {
                    mesh.Vertices.CombineIdentical(true, true);
                }
                mesh.Normals.ComputeNormals();
                mesh.Compact();

                totalTriangleCount += mesh.Faces.TriangleCount;
                convertedMeshes.Add(mesh);
            }

            return convertedMeshes;
        }

        private static bool DoesTriangleIntersectBounds(Point3d a, Point3d b, Point3d c, BoundingBox2D bounds)
        {
            var triangleBounds = new BoundingBox2D(
                Math.Min(a.X, Math.Min(b.X, c.X)),
                Math.Min(a.Y, Math.Min(b.Y, c.Y)),
                Math.Max(a.X, Math.Max(b.X, c.X)),
                Math.Max(a.Y, Math.Max(b.Y, c.Y)));

            return RhinoSpatialContextTools.DoBoundingBoxesIntersect(bounds, triangleBounds);
        }

        internal static bool TryConvertEcefToGeodetic(
            double x,
            double y,
            double z,
            out double latitudeDegrees,
            out double longitudeDegrees,
            out double height)
        {
            var p = Math.Sqrt((x * x) + (y * y));

            if (p < 1e-9 && Math.Abs(z) < 1e-9)
            {
                latitudeDegrees = 0.0;
                longitudeDegrees = 0.0;
                height = 0.0;
                return false;
            }

            var theta = Math.Atan2(z * Wgs84SemiMajorAxis, p * Wgs84SemiMinorAxis);
            var sinTheta = Math.Sin(theta);
            var cosTheta = Math.Cos(theta);
            var latitude = Math.Atan2(
                z + (Wgs84SecondEccentricitySquared * Wgs84SemiMinorAxis * sinTheta * sinTheta * sinTheta),
                p - (Wgs84FirstEccentricitySquared * Wgs84SemiMajorAxis * cosTheta * cosTheta * cosTheta));
            var longitude = Math.Atan2(y, x);

            var sinLatitude = Math.Sin(latitude);
            var radiusOfCurvature = Wgs84SemiMajorAxis / Math.Sqrt(1.0 - (Wgs84FirstEccentricitySquared * sinLatitude * sinLatitude));

            if (Math.Abs(Math.Cos(latitude)) > 1e-9)
            {
                height = (p / Math.Cos(latitude)) - radiusOfCurvature;
            }
            else
            {
                height = (z / Math.Sign(sinLatitude)) - (radiusOfCurvature * (1.0 - Wgs84FirstEccentricitySquared));
            }

            latitudeDegrees = latitude * (180.0 / Math.PI);
            longitudeDegrees = longitude * (180.0 / Math.PI);
            return true;
        }
    }
}
