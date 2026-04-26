using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Rhino;
using Rhino.Geometry;

namespace RhinoSpatial
{
    internal static class Google3dTilesReferenceManager
    {
        private static readonly object SyncRoot = new();
        private static readonly Dictionary<Guid, Google3dTilesReferenceSession> Sessions = new();
        private static readonly Dictionary<Guid, WeakReference<Google3dTilesViewerComponent>> Components = new();
        private static readonly TimeSpan RuntimeStaleAfter = TimeSpan.FromSeconds(10);

        public static void RegisterComponent(Google3dTilesViewerComponent component)
        {
            lock (SyncRoot)
            {
                Components[component.InstanceGuid] = new WeakReference<Google3dTilesViewerComponent>(component);
            }
        }

        public static void UnregisterComponent(Guid ownerId)
        {
            lock (SyncRoot)
            {
                Components.Remove(ownerId);
            }
        }

        public static void SetSession(Google3dTilesReferenceSession session)
        {
            lock (SyncRoot)
            {
                PruneStaleRuntimeMeshesUnsafe();

                if (Sessions.TryGetValue(session.OwnerId, out var existingSession))
                {
                    session.Status = string.IsNullOrWhiteSpace(existingSession.Status)
                        ? session.Status
                        : existingSession.Status;
                    session.RuntimeMeshes = existingSession.RuntimeMeshes;
                    session.DecodedPrimitives = existingSession.DecodedPrimitives;
                    session.RuntimeTriangleCount = existingSession.RuntimeTriangleCount;
                    session.LastRuntimeUpdateUtc = existingSession.LastRuntimeUpdateUtc;
                }

                Sessions[session.OwnerId] = session;
            }

            SyncComponentPreview(session.OwnerId);
            RhinoDoc.ActiveDoc?.Views.Redraw();
        }

        public static void RemoveSession(Guid ownerId)
        {
            lock (SyncRoot)
            {
                if (!Sessions.Remove(ownerId))
                {
                    return;
                }
            }

            SyncComponentPreview(ownerId, clearOnly: true);
            RhinoDoc.ActiveDoc?.Views.Redraw();
        }

        public static IReadOnlyList<Google3dTilesReferenceSession> GetSessions()
        {
            lock (SyncRoot)
            {
                PruneStaleRuntimeMeshesUnsafe();
                return Sessions.Values.ToList();
            }
        }

        public static bool TryGetSession(Guid ownerId, out Google3dTilesReferenceSession? session)
        {
            lock (SyncRoot)
            {
                PruneStaleRuntimeMeshesUnsafe();
                return Sessions.TryGetValue(ownerId, out session);
            }
        }

        public static bool UpdateRuntimeStatus(Guid ownerId, string statusMessage)
        {
            lock (SyncRoot)
            {
                PruneStaleRuntimeMeshesUnsafe();

                if (!Sessions.TryGetValue(ownerId, out var session))
                {
                    return false;
                }

                session.Status = statusMessage;
                session.LastRuntimeUpdateUtc = DateTime.UtcNow;
            }

            RhinoDoc.ActiveDoc?.Views.Redraw();
            return true;
        }

        public static async Task<(bool Success, string StatusMessage)> ApplyTileBatchAsync(Guid ownerId, Google3dTilesTileBatchPayload payload)
        {
            Google3dTilesReferenceSession? session;
            string signature;

            lock (SyncRoot)
            {
                PruneStaleRuntimeMeshesUnsafe();

                if (!Sessions.TryGetValue(ownerId, out session))
                {
                    return (false, "No active Google 3D Tiles viewer session was found for this tile batch.");
                }

                signature = BuildTileBatchSignature(payload);
                if (string.Equals(session.LastTileBatchSignature, signature, StringComparison.Ordinal))
                {
                    return (true, session.Status);
                }

                if (session.TileBatchInProgress)
                {
                    return (true, session.Status);
                }

                session.TileBatchInProgress = true;
            }

            List<Google3dTilesReferenceSession.DisplayPrimitive> decodedPrimitives;

            try
            {
                decodedPrimitives = await Google3dTilesTileContentLoader
                    .LoadDisplayPrimitivesAsync(payload.Tiles, session!.SpatialContext)
                    .ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                var errorMessage = $"Google 3D Tiles could not decode the loaded tile content in Rhino: {exception.Message}";
                lock (SyncRoot)
                {
                    if (Sessions.TryGetValue(ownerId, out var currentSession))
                    {
                        currentSession.TileBatchInProgress = false;
                    }
                }
                UpdateRuntimeStatus(ownerId, errorMessage);
                return (false, errorMessage);
            }

            lock (SyncRoot)
            {
                if (!Sessions.TryGetValue(ownerId, out session))
                {
                    return (false, "The Google 3D Tiles viewer session changed before Rhino could apply the decoded tiles.");
                }

                session.DecodedPrimitives = decodedPrimitives;
                session.LastRuntimeUpdateUtc = DateTime.UtcNow;
                session.RuntimeMeshes = new List<Mesh>();
                session.LastTileBatchSignature = signature;
                session.TileBatchInProgress = false;
                session.RuntimeTriangleCount = decodedPrimitives.Sum(static primitive => primitive.Mesh.Faces.TriangleCount);
                session.Status = decodedPrimitives.Count > 0
                    ? $"Google 3D Tiles reference active in Rhino: {decodedPrimitives.Count} decoded tile meshes, {session.RuntimeTriangleCount} triangles."
                    : string.IsNullOrWhiteSpace(payload.Status)
                        ? "Google 3D Tiles runtime loaded tiles, but Rhino did not find any decoded tile geometry intersecting the selected area."
                        : payload.Status;

                var statusMessage = session.Status;
                SyncComponentPreview(ownerId);
                RhinoDoc.ActiveDoc?.Views.Redraw();
                return (true, statusMessage);
            }
        }

        private static void PruneStaleRuntimeMeshesUnsafe()
        {
            var utcNow = DateTime.UtcNow;

            foreach (var session in Sessions.Values)
            {
                if (session.RuntimeMeshes.Count == 0)
                {
                    continue;
                }

                if (utcNow - session.LastRuntimeUpdateUtc <= RuntimeStaleAfter)
                {
                    continue;
                }

                session.RuntimeMeshes = new List<Mesh>();
                session.RuntimeTriangleCount = 0;
                session.Status = "Google 3D Tiles runtime is not currently streaming. Open the runtime window again to refresh the transient Rhino viewport viewer layer.";
            }
        }

        private static string BuildTileBatchSignature(Google3dTilesTileBatchPayload payload)
        {
            return string.Join(
                "|",
                payload.Tiles
                    .Where(static tile => tile is not null && !string.IsNullOrWhiteSpace(tile.Url))
                    .OrderBy(static tile => tile.Url, StringComparer.Ordinal)
                    .Select(static tile =>
                        $"{tile.Url}#{string.Join(",", tile.Transform.Select(static value => value.ToString("G17", System.Globalization.CultureInfo.InvariantCulture)))}"));
        }

        private static void SyncComponentPreview(Guid ownerId, bool clearOnly = false)
        {
            RhinoApp.InvokeOnUiThread((Action)(() =>
            {
                Google3dTilesViewerComponent? component = null;

                lock (SyncRoot)
                {
                    if (Components.TryGetValue(ownerId, out var componentReference) &&
                        componentReference.TryGetTarget(out var registeredComponent))
                    {
                        component = registeredComponent;
                    }
                }

                if (component is null)
                {
                    return;
                }

                if (clearOnly)
                {
                    component.ClearLivePreviewState();
                    component.ExpirePreview(true);
                    return;
                }

                Google3dTilesReferenceSession? session;
                lock (SyncRoot)
                {
                    Sessions.TryGetValue(ownerId, out session);
                }

                if (session is null)
                {
                    component.ClearLivePreviewState();
                }
                else
                {
                    component.ApplyLivePreviewState(session);
                }

                component.ExpirePreview(true);
            }));
        }
    }
}
