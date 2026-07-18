using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RhinoSpatial.Core;

namespace RhinoSpatial
{
    internal sealed class Google3dTilesDirectLoadResult
    {
        public List<Google3dTilesDisplayPrimitive> Primitives { get; init; } = new();

        public string Status { get; init; } = string.Empty;

        public string Attribution { get; init; } = string.Empty;

        public static Google3dTilesDirectLoadResult Failed(string status)
        {
            return new Google3dTilesDirectLoadResult
            {
                Status = status
            };
        }
    }

    internal static class Google3dTilesDirectLoader
    {
        private const int MaxVisitedTiles = 12000;
        private const int MaxExternalTilesets = 120;
        private const int MaxContentTiles = 12;
        private const int MaxSelectedContentTiles = 96;
        private const int MaxQueueSize = 20000;
        private const double MinimumTargetGeometricError = 0.75;
        private const double MaximumTargetGeometricError = 20.0;
        private const double TargetErrorStudyAreaDivisor = 400.0;

        private static readonly HttpClient HttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        public static async Task<Google3dTilesDirectLoadResult> LoadAsync(
            string apiKey,
            BoundingBox2D boundingBox4326,
            SpatialContext2D spatialContext,
            Action<string>? reportProgress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return Google3dTilesDirectLoadResult.Failed("Google 3D Tiles viewer needs a user-managed Google Maps API key.");
            }

            var rootUri = BuildGoogleTilesUri("/v1/3dtiles/root.json", apiKey, null, null);
            reportProgress?.Invoke("Google 3D Tiles viewer is requesting the root tileset from Google...");
            using var rootDocument = await LoadJsonDocumentAsync(rootUri, cancellationToken).ConfigureAwait(false);
            reportProgress?.Invoke("Google 3D Tiles root tileset received. Traversing intersecting tile nodes...");
            var rootElement = rootDocument.RootElement;
            var session = TryGetSession(rootUri) ?? TryGetSessionFromJson(rootElement);

            var rootTile = TryGetProperty(rootElement, "root", out var root)
                ? root.Clone()
                : rootElement.Clone();
            var targetGeometricError = CalculateTargetGeometricError(boundingBox4326);
            var queue = new Queue<TileVisit>();
            queue.Enqueue(new TileVisit(rootTile, rootUri, IdentityMatrix(), 0, null, "REPLACE"));

            var rootNodes = new List<Google3dTilesRefinementNode>();
            var visitedTiles = 0;
            var loadedExternalTilesets = 0;
            var nextNodeId = 0;
            var traversalLimitReached = false;
            var externalTilesetFailureCount = 0;
            var lastTraversalError = string.Empty;
            var maximumVisitedDepth = 0;
            var finiteGeometricErrorCount = 0;
            var missingGeometricErrorCount = 0;
            var minimumGeometricError = double.PositiveInfinity;
            var maximumGeometricError = 0.0;
            var targetStopCount = 0;
            var evaluatedTraversalDepth = -1;
            var traversalStoppedAtSelectionBudget = false;

            while (queue.Count > 0 &&
                   visitedTiles < MaxVisitedTiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nextTraversalDepth = queue.Peek().Depth;
                if (nextTraversalDepth > evaluatedTraversalDepth)
                {
                    evaluatedTraversalDepth = nextTraversalDepth;
                    if (rootNodes.Count > 0)
                    {
                        var interimSelection = Google3dTilesRefinementSelector.Select(
                            rootNodes,
                            targetGeometricError,
                            MaxSelectedContentTiles);
                        if (interimSelection.BudgetLimited &&
                            !interimSelection.CoverageIncomplete &&
                            interimSelection.Nodes.Count > 0)
                        {
                            traversalStoppedAtSelectionBudget = true;
                            queue.Clear();
                            break;
                        }
                    }
                }

                var visit = queue.Dequeue();
                visitedTiles++;

                var tileTransform = CombineMatrices(
                    visit.Transform,
                    TryReadTransform(visit.Tile, out var localTransform) ? localTransform : IdentityMatrix());

                if (!TileIntersectsStudyArea(visit.Tile, boundingBox4326, tileTransform))
                {
                    continue;
                }

                var refinement = TryReadRefinement(visit.Tile) ?? visit.InheritedRefinement;
                var node = new Google3dTilesRefinementNode
                {
                    Id = ++nextNodeId,
                    Parent = visit.Parent,
                    Transform = tileTransform,
                    Depth = visit.Depth,
                    Refinement = refinement,
                    GeometricError = ScaleGeometricError(TryReadGeometricError(visit.Tile), tileTransform)
                };
                maximumVisitedDepth = Math.Max(maximumVisitedDepth, visit.Depth);
                if (double.IsNaN(node.GeometricError) || double.IsInfinity(node.GeometricError))
                {
                    missingGeometricErrorCount++;
                }
                else
                {
                    finiteGeometricErrorCount++;
                    minimumGeometricError = Math.Min(minimumGeometricError, node.GeometricError);
                    maximumGeometricError = Math.Max(maximumGeometricError, node.GeometricError);
                }

                if (visit.Parent is null)
                {
                    rootNodes.Add(node);
                }
                else
                {
                    visit.Parent.Children.Add(node);
                }

                var contentReferences = ReadContentReferences(visit.Tile)
                    .Select((contentReference, contentIndex) => new
                    {
                        ContentReference = contentReference,
                        ContentIndex = contentIndex,
                        ContentUri = BuildContentUri(contentReference.Uri, visit.BaseUri, apiKey, session)
                    })
                    .Where(reference => ContentIntersectsStudyArea(
                        reference.ContentReference.Content,
                        boundingBox4326,
                        tileTransform))
                    .ToList();

                foreach (var contentReference in contentReferences)
                {
                    if (contentReference.ContentUri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    node.Contents.Add(new Google3dTilesTileDescriptor
                    {
                        Key = $"{node.Id}:{contentReference.ContentIndex}",
                        Url = contentReference.ContentUri.ToString(),
                        Transform = tileTransform,
                        Depth = visit.Depth,
                        GeometricError = node.GeometricError
                    });
                }

                var shouldRefineNode = node.Contents.Count == 0 ||
                                       double.IsNaN(node.GeometricError) ||
                                       double.IsInfinity(node.GeometricError) ||
                                       node.GeometricError > targetGeometricError;
                if (!shouldRefineNode)
                {
                    targetStopCount++;
                    continue;
                }

                foreach (var contentReference in contentReferences)
                {
                    if (!contentReference.ContentUri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (loadedExternalTilesets >= MaxExternalTilesets)
                    {
                        traversalLimitReached = true;
                        continue;
                    }

                    loadedExternalTilesets++;
                    try
                    {
                        reportProgress?.Invoke($"Google 3D Tiles viewer is resolving child tileset {loadedExternalTilesets}...");
                        using var externalDocument = await LoadJsonDocumentAsync(contentReference.ContentUri, cancellationToken).ConfigureAwait(false);
                        var externalRootElement = externalDocument.RootElement;
                        session ??= TryGetSessionFromJson(externalRootElement);
                        if (TryGetProperty(externalRootElement, "root", out var externalRoot))
                        {
                            var externalTransform = CombineMatrices(
                                tileTransform,
                                TryReadTransform(externalRoot, out var externalLocalTransform)
                                    ? externalLocalTransform
                                    : IdentityMatrix());
                            if (TileIntersectsStudyArea(externalRoot, boundingBox4326, externalTransform) &&
                                queue.Count < MaxQueueSize)
                            {
                                queue.Enqueue(new TileVisit(externalRoot.Clone(), contentReference.ContentUri, tileTransform, visit.Depth + 1, node, refinement));
                            }
                            else if (queue.Count >= MaxQueueSize)
                            {
                                traversalLimitReached = true;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception exception)
                    {
                        externalTilesetFailureCount++;
                        lastTraversalError = exception.Message;
                    }
                }

                if (TryGetProperty(visit.Tile, "children", out var children) &&
                    children.ValueKind == JsonValueKind.Array)
                {
                    foreach (var child in children.EnumerateArray())
                    {
                        if (queue.Count >= MaxQueueSize)
                        {
                            traversalLimitReached = true;
                            break;
                        }

                        var childTransform = CombineMatrices(
                            tileTransform,
                            TryReadTransform(child, out var childLocalTransform)
                                ? childLocalTransform
                                : IdentityMatrix());
                        if (!TileIntersectsStudyArea(child, boundingBox4326, childTransform))
                        {
                            continue;
                        }

                        queue.Enqueue(new TileVisit(child.Clone(), visit.BaseUri, tileTransform, visit.Depth + 1, node, refinement));
                    }
                }
            }

            if (queue.Count > 0)
            {
                traversalLimitReached = true;
            }

            var totalCandidateCount = Google3dTilesRefinementSelector
                .EnumerateNodes(rootNodes)
                .Sum(static node => node.Contents.Count);
            if (totalCandidateCount == 0)
            {
                var traversalNote = BuildTraversalNote(
                    traversalLimitReached,
                    externalTilesetFailureCount,
                    lastTraversalError,
                    visitedTiles,
                    loadedExternalTilesets,
                    maximumVisitedDepth,
                    finiteGeometricErrorCount,
                    missingGeometricErrorCount,
                    minimumGeometricError,
                    maximumGeometricError,
                    targetStopCount,
                    targetGeometricError,
                    traversalStoppedAtSelectionBudget);
                return Google3dTilesDirectLoadResult.Failed(
                    $"Google 3D Tiles viewer did not find tile content intersecting the selected area after visiting {visitedTiles} tile node(s).{traversalNote}");
            }

            var selection = Google3dTilesRefinementSelector.Select(
                rootNodes,
                targetGeometricError,
                MaxSelectedContentTiles);
            var activeNodes = new HashSet<Google3dTilesRefinementNode>(selection.Nodes);
            var decodedPrimitives = new List<Google3dTilesDisplayPrimitive>();
            var aggregateContentResult = new Google3dTilesContentLoadResult();
            var attemptedDescriptorKeys = new HashSet<string>(StringComparer.Ordinal);
            var fallbackPromotionCount = 0;
            var unresolvedNodeIds = new HashSet<int>();

            while (activeNodes.Count > 0)
            {
                var descriptorsToDecode = activeNodes
                    .SelectMany(static node => node.Contents)
                    .Where(descriptor => attemptedDescriptorKeys.Add(descriptor.Key))
                    .ToList();

                foreach (var descriptorBatch in descriptorsToDecode.Chunk(MaxContentTiles))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var batch = descriptorBatch.ToList();
                    reportProgress?.Invoke($"Google 3D Tiles viewer is decoding {attemptedDescriptorKeys.Count} selected tile content URL(s)...");
                    var contentResult = await Google3dTilesTileContentLoader
                        .LoadDisplayPrimitivesAsync(batch, spatialContext, cancellationToken)
                        .ConfigureAwait(false);
                    aggregateContentResult = CombineContentResults(aggregateContentResult, contentResult);
                    decodedPrimitives.AddRange(contentResult.Primitives);
                }

                var successfulKeys = decodedPrimitives
                    .Where(static primitive => primitive.Mesh.Faces.Count > 0)
                    .Select(static primitive => primitive.SourceKey)
                    .ToHashSet(StringComparer.Ordinal);
                var failedNodes = activeNodes
                    .Where(node => node.Contents.Count == 0 || node.Contents.All(descriptor => !successfulKeys.Contains(descriptor.Key)))
                    .OrderByDescending(static node => node.Depth)
                    .ToList();
                if (failedNodes.Count == 0)
                {
                    break;
                }

                var promotedAnyBranch = false;
                foreach (var failedNode in failedNodes)
                {
                    if (!activeNodes.Contains(failedNode))
                    {
                        continue;
                    }

                    var fallbackAncestor = Google3dTilesRefinementSelector.FindFallbackAncestor(failedNode, activeNodes);
                    if (fallbackAncestor is null)
                    {
                        unresolvedNodeIds.Add(failedNode.Id);
                        continue;
                    }

                    activeNodes.RemoveWhere(node => Google3dTilesRefinementSelector.IsDescendantOrSelf(node, fallbackAncestor));
                    activeNodes.Add(fallbackAncestor);
                    fallbackPromotionCount++;
                    promotedAnyBranch = true;
                }

                if (!promotedAnyBranch)
                {
                    break;
                }
            }

            var activeKeys = activeNodes
                .SelectMany(static node => node.Contents)
                .Select(static descriptor => descriptor.Key)
                .ToHashSet(StringComparer.Ordinal);
            var primitives = decodedPrimitives
                .Where(primitive => activeKeys.Contains(primitive.SourceKey))
                .ToList();
            var triangleCount = primitives.Sum(static primitive => primitive.Mesh.Faces.TriangleCount);
            var outputBounds = CalculatePrimitiveBounds(primitives);
            var selectedDescriptorCount = activeNodes.Sum(static node => node.Contents.Count);
            var traversalNoteForStatus = BuildTraversalNote(
                traversalLimitReached,
                externalTilesetFailureCount,
                lastTraversalError,
                visitedTiles,
                loadedExternalTilesets,
                maximumVisitedDepth,
                finiteGeometricErrorCount,
                missingGeometricErrorCount,
                minimumGeometricError,
                maximumGeometricError,
                targetStopCount,
                targetGeometricError,
                traversalStoppedAtSelectionBudget);

            return new Google3dTilesDirectLoadResult
            {
                Primitives = primitives,
                Attribution = FormatAttribution(contentResult: aggregateContentResult),
                Status = primitives.Count > 0
                    ? BuildSuccessStatus(primitives.Count, triangleCount, attemptedDescriptorKeys.Count, selectedDescriptorCount, totalCandidateCount, fallbackPromotionCount, unresolvedNodeIds.Count, targetGeometricError, selection.BudgetLimited, selection.CoverageIncomplete, aggregateContentResult, outputBounds, traversalNoteForStatus)
                    : BuildEmptyContentStatus(attemptedDescriptorKeys.Count, totalCandidateCount, aggregateContentResult, fallbackPromotionCount, traversalNoteForStatus)
            };
        }

        private static string BuildSuccessStatus(
            int primitiveCount,
            int triangleCount,
            int attemptedDescriptorCount,
            int selectedDescriptorCount,
            int totalCandidateCount,
            int fallbackPromotionCount,
            int unresolvedBranchCount,
            double targetGeometricError,
            bool budgetLimited,
            bool coverageIncomplete,
            Google3dTilesContentLoadResult contentResult,
            Rhino.Geometry.BoundingBox? outputBounds,
            string traversalNote)
        {
            var fallbackNote = fallbackPromotionCount > 0
                ? $" Parent fallback: promoted {fallbackPromotionCount} failed refined branch(es) to the nearest usable coarser ancestor."
                : string.Empty;
            var unresolvedNote = unresolvedBranchCount > 0
                ? $" {unresolvedBranchCount} selected branch(es) had no usable content or parent fallback."
                : string.Empty;
            var budgetNote = budgetLimited
                ? $" Refinement stopped at the {MaxSelectedContentTiles}-content coverage budget."
                : string.Empty;
            var coverageNote = coverageIncomplete
                ? " One or more intersecting source branches did not expose usable content at any traversed level."
                : string.Empty;
            var attributionNote = contentResult.Copyrights.Count > 0
                ? $" Attribution: {string.Join("; ", contentResult.Copyrights)}."
                : " Attribution metadata was not present in the decoded content.";
            var validityNote = contentResult.InvalidMeshCount > 0 || contentResult.DegenerateTriangleCount > 0
                ? $" Dropped {contentResult.InvalidMeshCount} invalid mesh(es) and {contentResult.DegenerateTriangleCount} degenerate triangle(s)."
                : string.Empty;
            var rejectedNote = contentResult.TotalDecodedTriangleCount > 0
                ? $" Triangle filtering: kept {triangleCount} / {contentResult.TotalDecodedTriangleCount}, rejected {contentResult.RejectedOutOfBoundsTriangleCount} out-of-bounds, {contentResult.RejectedOversizedTriangleCount} oversized, {contentResult.DegenerateTriangleCount} degenerate."
                : string.Empty;
            var decodeNote = contentResult.DecodeFailureCount > 0 || contentResult.EmptyTileCount > 0 || contentResult.EmptyPrimitiveCount > 0
                ? $" Decode diagnostics: attempted {contentResult.AttemptedTileCount} tile(s), {contentResult.DecodeFailureCount} failed, {contentResult.EmptyTileCount} empty tile(s), {contentResult.EmptyPrimitiveCount} empty primitive(s)."
                : $" Decode diagnostics: attempted {contentResult.AttemptedTileCount} tile(s), decoded {contentResult.DecodedPrimitiveCount} primitive(s).";
            var projectionNote = FormatProjectionModeNote(contentResult);
            var boundsNote = outputBounds is not null && outputBounds.Value.IsValid
                ? $" Output local bounds: {FormatBoundingBox(outputBounds.Value)}."
                : " Output local bounds: none.";
            var baselineNote = contentResult.UsedSharedElevationBaseline
                ? contentResult.EstablishedElevationBaseline
                    ? $" Local elevation baseline: {FormatNumber(contentResult.AppliedElevationBaseline)} m, established from usable Google mesh vertices."
                    : $" Local elevation baseline: {FormatNumber(contentResult.AppliedElevationBaseline)} m, reused from the shared Spatial Context."
                : " Local elevation baseline: absolute coordinates; no localization applied.";

            return $"Google 3D Tiles viewer selected a refinement frontier of {selectedDescriptorCount} tile content URL(s) at target geometric error {FormatNumber(targetGeometricError)} m, attempted {attemptedDescriptorCount} URL(s) including fallbacks, and decoded {primitiveCount} preview mesh(es) ({totalCandidateCount} intersecting candidate URL(s)), {triangleCount} triangles. Vertical correction: EGM96 geoid grid.{baselineNote}{decodeNote}{projectionNote}{rejectedNote}{boundsNote}{budgetNote}{coverageNote}{fallbackNote}{unresolvedNote}{validityNote}{attributionNote}{traversalNote}";
        }

        private static Google3dTilesContentLoadResult CombineContentResults(
            Google3dTilesContentLoadResult left,
            Google3dTilesContentLoadResult right)
        {
            var primitives = new List<Google3dTilesDisplayPrimitive>(left.Primitives.Count + right.Primitives.Count);
            primitives.AddRange(left.Primitives);
            primitives.AddRange(right.Primitives);

            return new Google3dTilesContentLoadResult
            {
                Primitives = primitives,
                AttemptedTileCount = left.AttemptedTileCount + right.AttemptedTileCount,
                DecodeFailureCount = left.DecodeFailureCount + right.DecodeFailureCount,
                DecodedPrimitiveCount = left.DecodedPrimitiveCount + right.DecodedPrimitiveCount,
                EmptyPrimitiveCount = left.EmptyPrimitiveCount + right.EmptyPrimitiveCount,
                EmptyTileCount = left.EmptyTileCount + right.EmptyTileCount,
                DracoCompressedTileCount = left.DracoCompressedTileCount + right.DracoCompressedTileCount,
                DracoRequiredTileCount = left.DracoRequiredTileCount + right.DracoRequiredTileCount,
                SkippedDecodedPrimitiveCount = left.SkippedDecodedPrimitiveCount + right.SkippedDecodedPrimitiveCount,
                TotalDecodedTriangleCount = left.TotalDecodedTriangleCount + right.TotalDecodedTriangleCount,
                RejectedOutOfBoundsTriangleCount = left.RejectedOutOfBoundsTriangleCount + right.RejectedOutOfBoundsTriangleCount,
                RejectedOversizedTriangleCount = left.RejectedOversizedTriangleCount + right.RejectedOversizedTriangleCount,
                DegenerateTriangleCount = left.DegenerateTriangleCount + right.DegenerateTriangleCount,
                InvalidMeshCount = left.InvalidMeshCount + right.InvalidMeshCount,
                TileTransformProjectionCount = left.TileTransformProjectionCount + right.TileTransformProjectionCount,
                YUpProjectionCount = left.YUpProjectionCount + right.YUpProjectionCount,
                InverseYUpProjectionCount = left.InverseYUpProjectionCount + right.InverseYUpProjectionCount,
                RawEcefProjectionCount = left.RawEcefProjectionCount + right.RawEcefProjectionCount,
                ClosestProjectedCenterDistance = Math.Min(left.ClosestProjectedCenterDistance, right.ClosestProjectedCenterDistance),
                ClosestTileOriginDistance = Math.Min(left.ClosestTileOriginDistance, right.ClosestTileOriginDistance),
                MinimumProjectedBoundsDiagonal = Math.Min(left.MinimumProjectedBoundsDiagonal, right.MinimumProjectedBoundsDiagonal),
                AppliedElevationBaseline = right.UsedSharedElevationBaseline
                    ? right.AppliedElevationBaseline
                    : left.AppliedElevationBaseline,
                UsedSharedElevationBaseline = left.UsedSharedElevationBaseline || right.UsedSharedElevationBaseline,
                EstablishedElevationBaseline = left.EstablishedElevationBaseline || right.EstablishedElevationBaseline,
                Copyrights = left.Copyrights
                    .Concat(right.Copyrights)
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(static value => value, StringComparer.Ordinal)
                    .ToList(),
                LastError = string.IsNullOrWhiteSpace(right.LastError) ? left.LastError : right.LastError
            };
        }

        private static Rhino.Geometry.BoundingBox? CalculatePrimitiveBounds(IReadOnlyList<Google3dTilesDisplayPrimitive> primitives)
        {
            Rhino.Geometry.BoundingBox? bounds = null;
            foreach (var primitive in primitives)
            {
                var primitiveBounds = primitive.Mesh.GetBoundingBox(true);
                if (!primitiveBounds.IsValid)
                {
                    continue;
                }

                if (bounds is null || !bounds.Value.IsValid)
                {
                    bounds = primitiveBounds;
                    continue;
                }

                var expanded = bounds.Value;
                expanded.Union(primitiveBounds);
                bounds = expanded;
            }

            return bounds;
        }

        private static double CalculateTargetGeometricError(BoundingBox2D studyArea4326)
        {
            var middleLatitudeRadians = ((studyArea4326.MinY + studyArea4326.MaxY) * 0.5) * (Math.PI / 180.0);
            var widthMeters = Math.Abs(studyArea4326.MaxX - studyArea4326.MinX) * 111_320.0 * Math.Max(0.01, Math.Cos(middleLatitudeRadians));
            var heightMeters = Math.Abs(studyArea4326.MaxY - studyArea4326.MinY) * 110_574.0;
            var diagonalMeters = Math.Sqrt((widthMeters * widthMeters) + (heightMeters * heightMeters));
            return Math.Clamp(
                diagonalMeters / TargetErrorStudyAreaDivisor,
                MinimumTargetGeometricError,
                MaximumTargetGeometricError);
        }

        private static double ScaleGeometricError(double geometricError, IReadOnlyList<double> transform)
        {
            if (double.IsInfinity(geometricError) || double.IsNaN(geometricError))
            {
                return geometricError;
            }

            return Math.Max(0.0, geometricError) * GetMaximumScale(transform);
        }

        private static string BuildTraversalNote(
            bool limitReached,
            int externalFailureCount,
            string lastError,
            int visitedTileCount,
            int externalTilesetCount,
            int maximumDepth,
            int finiteGeometricErrorCount,
            int missingGeometricErrorCount,
            double minimumGeometricError,
            double maximumGeometricError,
            int targetStopCount,
            double targetGeometricError,
            bool stoppedAtSelectionBudget)
        {
            var limitNote = limitReached
                ? " Tileset traversal reached a safety limit; the status may represent incomplete source coverage."
                : string.Empty;
            var failureNote = externalFailureCount > 0
                ? $" {externalFailureCount} external tileset request(s) failed."
                : string.Empty;
            var errorNote = externalFailureCount > 0 && !string.IsNullOrWhiteSpace(lastError)
                ? $" Last tileset error: {lastError}"
                : string.Empty;
            var geometricErrorRange = finiteGeometricErrorCount == 0
                ? "none"
                : $"{FormatNumber(minimumGeometricError)}..{FormatNumber(maximumGeometricError)} m";
            var budgetNote = stoppedAtSelectionBudget
                ? $" Traversal stopped at a balanced depth before exceeding the {MaxSelectedContentTiles}-content preview budget."
                : string.Empty;
            var diagnostics = $" Traversal diagnostics: visited {visitedTileCount} node(s), loaded {externalTilesetCount} external tileset(s), maximum depth {maximumDepth}, stopped {targetStopCount} branch(es) at target {FormatNumber(targetGeometricError)} m; geometric errors {geometricErrorRange} ({missingGeometricErrorCount} missing/non-finite).";
            return limitNote + failureNote + errorNote + budgetNote + diagnostics;
        }

        private static string FormatAttribution(Google3dTilesContentLoadResult contentResult)
        {
            return contentResult.Copyrights.Count == 0
                ? string.Empty
                : string.Join("; ", contentResult.Copyrights);
        }

        private static string BuildEmptyContentStatus(
            int decodedDescriptorCount,
            int totalCandidateCount,
            Google3dTilesContentLoadResult contentResult,
            int fallbackPromotionCount,
            string traversalNote)
        {
            if (contentResult.DecodeFailureCount > 0 && contentResult.DecodeFailureCount == contentResult.AttemptedTileCount)
            {
                var suffix = string.IsNullOrWhiteSpace(contentResult.LastError)
                    ? string.Empty
                    : $" Last decode error: {contentResult.LastError}";
                return $"Google 3D Tiles viewer found {totalCandidateCount} candidate tile content URL(s), but all {contentResult.DecodeFailureCount} attempted GLB decode(s) failed.{suffix}{traversalNote}";
            }

            var dracoNote = contentResult.DracoCompressedTileCount > 0
                ? $" {contentResult.DracoCompressedTileCount} attempted GLB(s) use Draco compression ({contentResult.DracoRequiredTileCount} required)."
                : string.Empty;
            var skippedNote = contentResult.SkippedDecodedPrimitiveCount > 0
                ? $" Skipped {contentResult.SkippedDecodedPrimitiveCount} primitive(s) without directly readable POSITION/index data."
                : string.Empty;
            var rejectedNote = contentResult.TotalDecodedTriangleCount > 0
                ? $" Rejected {contentResult.RejectedOversizedTriangleCount} oversized and {contentResult.RejectedOutOfBoundsTriangleCount} out-of-context triangle(s) from {contentResult.TotalDecodedTriangleCount} decoded triangle(s)."
                : string.Empty;
            var fallbackNote = fallbackPromotionCount > 0
                ? $" Tried {fallbackPromotionCount} coarser parent fallback branch(es)."
                : string.Empty;
            var validityNote = contentResult.InvalidMeshCount > 0 || contentResult.DegenerateTriangleCount > 0
                ? $" Dropped {contentResult.InvalidMeshCount} invalid mesh(es) and {contentResult.DegenerateTriangleCount} degenerate triangle(s)."
                : string.Empty;
            var errorNote = string.IsNullOrWhiteSpace(contentResult.LastError)
                ? string.Empty
                : $" Last decode/build error: {contentResult.LastError}";
            var projectionNote = FormatProjectionModeNote(contentResult);

            return $"Google 3D Tiles viewer found {totalCandidateCount} candidate tile content URL(s), attempted {contentResult.AttemptedTileCount} GLB load(s) from {decodedDescriptorCount} selected candidate(s), decoded {contentResult.DecodedPrimitiveCount} primitive(s), but produced no usable preview mesh faces.{dracoNote}{skippedNote}{projectionNote}{rejectedNote}{fallbackNote}{validityNote}{errorNote}{traversalNote}";
        }

        private static string FormatProjectionModeNote(Google3dTilesContentLoadResult contentResult)
        {
            var total = contentResult.TileTransformProjectionCount +
                        contentResult.YUpProjectionCount +
                        contentResult.InverseYUpProjectionCount +
                        contentResult.RawEcefProjectionCount;
            return total == 0
                ? string.Empty
                : $" Projection frames: tile {contentResult.TileTransformProjectionCount}, Y-up {contentResult.YUpProjectionCount}, inverse Y-up {contentResult.InverseYUpProjectionCount}, raw ECEF {contentResult.RawEcefProjectionCount}.{FormatProjectionDistanceNote(contentResult)}";
        }

        private static string FormatProjectionDistanceNote(Google3dTilesContentLoadResult contentResult)
        {
            if (double.IsInfinity(contentResult.ClosestProjectedCenterDistance) &&
                double.IsInfinity(contentResult.ClosestTileOriginDistance))
            {
                return string.Empty;
            }

            var contentCenter = double.IsInfinity(contentResult.ClosestProjectedCenterDistance)
                ? "unavailable"
                : $"{FormatNumber(contentResult.ClosestProjectedCenterDistance)} m";
            var tileOrigin = double.IsInfinity(contentResult.ClosestTileOriginDistance)
                ? "unavailable"
                : $"{FormatNumber(contentResult.ClosestTileOriginDistance)} m";
            var boundsDiagonal = double.IsInfinity(contentResult.MinimumProjectedBoundsDiagonal)
                ? "unavailable"
                : $"{FormatNumber(contentResult.MinimumProjectedBoundsDiagonal)} m";
            return $" Nearest projected content center: {contentCenter}; nearest cumulative tile origin: {tileOrigin}; smallest projected content diagonal: {boundsDiagonal}.";
        }

        private static async Task<JsonDocument> LoadJsonDocumentAsync(Uri uri, CancellationToken cancellationToken)
        {
            using var response = await HttpClient
                .GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException(
                    $"Google 3D Tiles request failed ({(int)response.StatusCode}) for {uri.AbsolutePath}: {TrimStatusBody(body)}");
            }

            return JsonDocument.Parse(body);
        }

        private static Uri BuildContentUri(string contentUriText, Uri baseUri, string apiKey, string? session)
        {
            var inheritedQuery = ParseQuery(baseUri.Query);
            if (contentUriText.StartsWith("/v1/3dtiles/", StringComparison.OrdinalIgnoreCase))
            {
                return BuildGoogleTilesUri(
                    contentUriText,
                    apiKey,
                    session ?? TryGetSession(baseUri),
                    inheritedQuery);
            }

            var uri = Uri.TryCreate(contentUriText, UriKind.Absolute, out var absoluteUri)
                ? absoluteUri
                : new Uri(baseUri, contentUriText);

            if (!string.Equals(uri.Host, "tile.googleapis.com", StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }

            return BuildGoogleTilesUri(
                uri.PathAndQuery,
                apiKey,
                session ?? TryGetSession(uri) ?? TryGetSession(baseUri),
                inheritedQuery);
        }

        private static Uri BuildGoogleTilesUri(
            string pathAndQuery,
            string apiKey,
            string? session,
            IReadOnlyDictionary<string, string>? inheritedQuery)
        {
            var builder = new UriBuilder(
                pathAndQuery.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? new Uri(pathAndQuery)
                    : new Uri(new Uri("https://tile.googleapis.com"), pathAndQuery));
            var query = ParseQuery(builder.Query);

            if (inheritedQuery is not null)
            {
                foreach (var entry in inheritedQuery)
                {
                    if (!query.ContainsKey(entry.Key))
                    {
                        query[entry.Key] = entry.Value;
                    }
                }
            }

            if (!query.ContainsKey("key"))
            {
                query["key"] = apiKey;
            }

            if (!string.IsNullOrWhiteSpace(session) &&
                !query.ContainsKey("session") &&
                builder.Path.StartsWith("/v1/3dtiles/datasets/", StringComparison.OrdinalIgnoreCase))
            {
                query["session"] = session!;
            }

            builder.Query = string.Join(
                "&",
                query.Select(entry => $"{Uri.EscapeDataString(entry.Key)}={Uri.EscapeDataString(entry.Value)}"));
            return builder.Uri;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var trimmed = query.TrimStart('?');
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return result;
            }

            foreach (var part in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var keyValue = part.Split('=', 2);
                var key = Uri.UnescapeDataString(keyValue[0]);
                var value = keyValue.Length == 2 ? Uri.UnescapeDataString(keyValue[1]) : string.Empty;
                if (!string.IsNullOrWhiteSpace(key))
                {
                    result[key] = value;
                }
            }

            return result;
        }

        private static IEnumerable<TileContentReference> ReadContentReferences(JsonElement tile)
        {
            if (TryGetProperty(tile, "content", out var content) &&
                TryReadUri(content, out var uri))
            {
                yield return new TileContentReference(uri, content.Clone());
            }

            if (!TryGetProperty(tile, "contents", out var contents) ||
                contents.ValueKind != JsonValueKind.Array)
            {
                yield break;
            }

            foreach (var entry in contents.EnumerateArray())
            {
                if (TryReadUri(entry, out var contentUri))
                {
                    yield return new TileContentReference(contentUri, entry.Clone());
                }
            }
        }

        private static bool ContentIntersectsStudyArea(
            JsonElement content,
            BoundingBox2D studyArea4326,
            IReadOnlyList<double> tileTransform)
        {
            return !TryGetProperty(content, "boundingVolume", out var boundingVolume) ||
                   BoundingVolumeIntersectsStudyArea(boundingVolume, studyArea4326, tileTransform);
        }

        private static bool TryReadUri(JsonElement content, out string uri)
        {
            uri = string.Empty;
            foreach (var propertyName in new[] { "uri", "url" })
            {
                if (TryGetProperty(content, propertyName, out var property) &&
                    property.ValueKind == JsonValueKind.String)
                {
                    uri = property.GetString() ?? string.Empty;
                    return !string.IsNullOrWhiteSpace(uri);
                }
            }

            return false;
        }

        private static bool TileIntersectsStudyArea(JsonElement tile, BoundingBox2D studyArea4326, IReadOnlyList<double> tileTransform)
        {
            return !TryGetProperty(tile, "boundingVolume", out var boundingVolume) ||
                   BoundingVolumeIntersectsStudyArea(boundingVolume, studyArea4326, tileTransform);
        }

        private static bool BoundingVolumeIntersectsStudyArea(
            JsonElement boundingVolume,
            BoundingBox2D studyArea4326,
            IReadOnlyList<double> tileTransform)
        {
            if (TryGetProperty(boundingVolume, "region", out var region) &&
                region.ValueKind == JsonValueKind.Array)
            {
                var values = region.EnumerateArray()
                    .Select(static value => value.GetDouble())
                    .ToArray();
                if (values.Length >= 4)
                {
                    return GeographicBoundsIntersect(
                        RadiansToDegrees(values[0]),
                        RadiansToDegrees(values[1]),
                        RadiansToDegrees(values[2]),
                        RadiansToDegrees(values[3]),
                        studyArea4326);
                }
            }

            if (TryGetProperty(boundingVolume, "sphere", out var sphere) &&
                sphere.ValueKind == JsonValueKind.Array)
            {
                var values = sphere.EnumerateArray()
                    .Select(static value => value.GetDouble())
                    .ToArray();
                if (values.Length >= 4)
                {
                    var center = ApplyMatrixToPoint(values[0], values[1], values[2], tileTransform);
                    if (!Google3dTilesCoordinateConverter.TryConvertEcefToGeodetic(
                        center.X,
                        center.Y,
                        center.Z,
                        out var latitude,
                        out var longitude,
                        out _))
                    {
                        return true;
                    }

                    var radiusMeters = Math.Abs(values[3]) * GetMaximumScale(tileTransform) * 1.02;
                    var latitudeRadius = Math.Max(radiusMeters / 110_574.0, 0.000001);
                    var longitudeScale = 111_320.0 * Math.Max(0.01, Math.Cos(latitude * (Math.PI / 180.0)));
                    var longitudeRadius = Math.Min(180.0, Math.Max(radiusMeters / longitudeScale, 0.000001));
                    return GeographicBoundsIntersect(
                        longitude - longitudeRadius,
                        latitude - latitudeRadius,
                        longitude + longitudeRadius,
                        latitude + latitudeRadius,
                        studyArea4326);
                }
            }

            if (TryGetProperty(boundingVolume, "box", out var box) &&
                box.ValueKind == JsonValueKind.Array)
            {
                var values = box.EnumerateArray()
                    .Select(static value => value.GetDouble())
                    .ToArray();
                if (values.Length >= 12)
                {
                    var longitudes = new List<double>(8);
                    var minimumLatitude = double.PositiveInfinity;
                    var maximumLatitude = double.NegativeInfinity;
                    var transformedCenter = ApplyMatrixToPoint(
                        values[0],
                        values[1],
                        values[2],
                        tileTransform);
                    var maximumCornerDistance = 0.0;
                    foreach (var signX in new[] { -1.0, 1.0 })
                    {
                        foreach (var signY in new[] { -1.0, 1.0 })
                        {
                            foreach (var signZ in new[] { -1.0, 1.0 })
                            {
                                var corner = ApplyMatrixToPoint(
                                    values[0] + (signX * values[3]) + (signY * values[6]) + (signZ * values[9]),
                                    values[1] + (signX * values[4]) + (signY * values[7]) + (signZ * values[10]),
                                    values[2] + (signX * values[5]) + (signY * values[8]) + (signZ * values[11]),
                                    tileTransform);
                                if (!Google3dTilesCoordinateConverter.TryConvertEcefToGeodetic(
                                    corner.X,
                                    corner.Y,
                                    corner.Z,
                                    out var latitude,
                                    out var longitude,
                                    out _))
                                {
                                    return true;
                                }

                                longitudes.Add(longitude);
                                minimumLatitude = Math.Min(minimumLatitude, latitude);
                                maximumLatitude = Math.Max(maximumLatitude, latitude);
                                maximumCornerDistance = Math.Max(
                                    maximumCornerDistance,
                                    VectorLength(
                                        corner.X - transformedCenter.X,
                                        corner.Y - transformedCenter.Y,
                                        corner.Z - transformedCenter.Z));
                            }
                        }
                    }

                    if (VectorLength(
                            transformedCenter.X,
                            transformedCenter.Y,
                            transformedCenter.Z) <= maximumCornerDistance)
                    {
                        return true;
                    }

                    if (!Google3dTilesGeographicBounds.TryCreateMinimalLongitudeArc(
                            longitudes,
                            out var minimumLongitude,
                            out var maximumLongitude))
                    {
                        return true;
                    }

                    const double cornerPaddingDegrees = 0.000001;
                    return GeographicBoundsIntersect(
                        minimumLongitude - cornerPaddingDegrees,
                        minimumLatitude - cornerPaddingDegrees,
                        maximumLongitude + cornerPaddingDegrees,
                        maximumLatitude + cornerPaddingDegrees,
                        studyArea4326);
                }
            }

            return true;
        }

        private static bool GeographicBoundsIntersect(
            double west,
            double south,
            double east,
            double north,
            BoundingBox2D studyArea4326)
        {
            if (south > studyArea4326.MaxY || north < studyArea4326.MinY)
            {
                return false;
            }

            return Google3dTilesGeographicBounds.LongitudeRangesIntersect(
                west,
                east,
                studyArea4326.MinX,
                studyArea4326.MaxX);
        }

        private static (double X, double Y, double Z) ApplyMatrixToPoint(
            double x,
            double y,
            double z,
            IReadOnlyList<double> matrixValues)
        {
            if (matrixValues.Count != 16)
            {
                return (x, y, z);
            }

            var transformedX = (matrixValues[0] * x) + (matrixValues[4] * y) + (matrixValues[8] * z) + matrixValues[12];
            var transformedY = (matrixValues[1] * x) + (matrixValues[5] * y) + (matrixValues[9] * z) + matrixValues[13];
            var transformedZ = (matrixValues[2] * x) + (matrixValues[6] * y) + (matrixValues[10] * z) + matrixValues[14];
            var transformedW = (matrixValues[3] * x) + (matrixValues[7] * y) + (matrixValues[11] * z) + matrixValues[15];

            if (Math.Abs(transformedW) > 1e-9 && Math.Abs(transformedW - 1.0) > 1e-9)
            {
                transformedX /= transformedW;
                transformedY /= transformedW;
                transformedZ /= transformedW;
            }

            return (transformedX, transformedY, transformedZ);
        }

        private static double VectorLength(double x, double y, double z)
        {
            return Math.Sqrt((x * x) + (y * y) + (z * z));
        }

        private static double GetMaximumScale(IReadOnlyList<double> transform)
        {
            if (transform.Count != 16)
            {
                return 1.0;
            }

            var scaleX = VectorLength(transform[0], transform[1], transform[2]);
            var scaleY = VectorLength(transform[4], transform[5], transform[6]);
            var scaleZ = VectorLength(transform[8], transform[9], transform[10]);
            return Math.Max(scaleX, Math.Max(scaleY, scaleZ));
        }

        private static bool TryReadTransform(JsonElement tile, out List<double> transform)
        {
            transform = new List<double>();
            if (!TryGetProperty(tile, "transform", out var transformElement) ||
                transformElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            transform = transformElement.EnumerateArray()
                .Select(static value => value.GetDouble())
                .ToList();
            return transform.Count == 16;
        }

        private static double TryReadGeometricError(JsonElement tile)
        {
            if (TryGetProperty(tile, "geometricError", out var geometricError) &&
                geometricError.ValueKind == JsonValueKind.Number &&
                geometricError.TryGetDouble(out var value))
            {
                return value;
            }

            return double.PositiveInfinity;
        }

        private static string? TryReadRefinement(JsonElement tile)
        {
            if (!TryGetProperty(tile, "refine", out var refinement) ||
                refinement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var value = refinement.GetString();
            return string.Equals(value, "ADD", StringComparison.OrdinalIgnoreCase)
                ? "ADD"
                : string.Equals(value, "REPLACE", StringComparison.OrdinalIgnoreCase)
                    ? "REPLACE"
                    : null;
        }

        private static List<double> IdentityMatrix()
        {
            return new List<double>
            {
                1.0, 0.0, 0.0, 0.0,
                0.0, 1.0, 0.0, 0.0,
                0.0, 0.0, 1.0, 0.0,
                0.0, 0.0, 0.0, 1.0
            };
        }

        private static List<double> CombineMatrices(IReadOnlyList<double> parent, IReadOnlyList<double> local)
        {
            if (parent.Count != 16)
            {
                return local.ToList();
            }

            if (local.Count != 16)
            {
                return parent.ToList();
            }

            var result = new double[16];
            for (var column = 0; column < 4; column++)
            {
                for (var row = 0; row < 4; row++)
                {
                    var sum = 0.0;
                    for (var k = 0; k < 4; k++)
                    {
                        sum += parent[(k * 4) + row] * local[(column * 4) + k];
                    }

                    result[(column * 4) + row] = sum;
                }
            }

            return result.ToList();
        }

        private static bool TryGetProperty(JsonElement element, string propertyName, out JsonElement value)
        {
            if (element.ValueKind == JsonValueKind.Object &&
                element.TryGetProperty(propertyName, out value))
            {
                return true;
            }

            value = default;
            return false;
        }

        private static string? TryGetSession(Uri uri)
        {
            var query = ParseQuery(uri.Query);
            return query.TryGetValue("session", out var session) ? session : null;
        }

        private static string? TryGetSessionFromJson(JsonElement root)
        {
            foreach (var propertyName in new[] { "session", "sessionId" })
            {
                if (TryGetProperty(root, propertyName, out var sessionProperty) &&
                    sessionProperty.ValueKind == JsonValueKind.String)
                {
                    var session = sessionProperty.GetString();
                    if (!string.IsNullOrWhiteSpace(session))
                    {
                        return session;
                    }
                }
            }

            return null;
        }

        private static double RadiansToDegrees(double value)
        {
            return value * (180.0 / Math.PI);
        }

        private static string TrimStatusBody(string body)
        {
            var trimmed = body.Trim();
            if (trimmed.Length <= 220)
            {
                return trimmed;
            }

            return trimmed[..220] + "...";
        }

        private static string FormatBoundingBox(Rhino.Geometry.BoundingBox bounds)
        {
            return $"X {FormatNumber(bounds.Min.X)}..{FormatNumber(bounds.Max.X)}, " +
                   $"Y {FormatNumber(bounds.Min.Y)}..{FormatNumber(bounds.Max.Y)}, " +
                   $"Z {FormatNumber(bounds.Min.Z)}..{FormatNumber(bounds.Max.Z)}";
        }

        private static string FormatNumber(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private sealed record TileContentReference(string Uri, JsonElement Content);

        private sealed record TileVisit(
            JsonElement Tile,
            Uri BaseUri,
            List<double> Transform,
            int Depth,
            Google3dTilesRefinementNode? Parent,
            string InheritedRefinement);
    }
}
