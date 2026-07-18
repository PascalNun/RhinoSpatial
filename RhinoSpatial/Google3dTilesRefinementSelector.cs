using System;
using System.Collections.Generic;
using System.Linq;

namespace RhinoSpatial
{
    internal sealed class Google3dTilesRefinementNode
    {
        public int Id { get; init; }

        public Google3dTilesRefinementNode? Parent { get; init; }

        public List<Google3dTilesRefinementNode> Children { get; } = new();

        public List<Google3dTilesTileDescriptor> Contents { get; } = new();

        public List<double> Transform { get; init; } = new();

        public int Depth { get; init; }

        public string Refinement { get; init; } = "REPLACE";

        public double GeometricError { get; init; } = double.PositiveInfinity;
    }

    internal sealed class Google3dTilesRefinementSelection
    {
        public List<Google3dTilesRefinementNode> Nodes { get; init; } = new();

        public bool BudgetLimited { get; init; }

        public bool CoverageIncomplete { get; init; }
    }

    internal static class Google3dTilesRefinementSelector
    {
        public static Google3dTilesRefinementSelection Select(
            IReadOnlyCollection<Google3dTilesRefinementNode> roots,
            double targetGeometricError,
            int maxContentCount)
        {
            var frontier = new HashSet<Google3dTilesRefinementNode>();
            var coverageComplete = true;
            foreach (var root in roots)
            {
                var initialNodes = new List<Google3dTilesRefinementNode>();
                coverageComplete &= TryCollectNearestContentNodes(root, initialNodes);
                frontier.UnionWith(initialNodes);
            }

            var expandedNodes = new HashSet<Google3dTilesRefinementNode>();
            var budgetLimited = CountContents(frontier) > maxContentCount;

            while (frontier.Count > 0)
            {
                var candidates = frontier
                    .Where(node => !expandedNodes.Contains(node) &&
                                   node.Children.Count > 0 &&
                                   NormalizeGeometricError(node.GeometricError) > targetGeometricError)
                    .OrderByDescending(node => NormalizeGeometricError(node.GeometricError))
                    .ThenBy(static node => node.Depth)
                    .ToList();
                if (candidates.Count == 0)
                {
                    break;
                }

                var refined = false;
                foreach (var candidate in candidates)
                {
                    if (!TryCollectReplacementNodes(candidate, out var replacements) || replacements.Count == 0)
                    {
                        expandedNodes.Add(candidate);
                        continue;
                    }

                    var additive = string.Equals(candidate.Refinement, "ADD", StringComparison.OrdinalIgnoreCase);
                    var nextContentCount = CountContents(frontier) + CountContents(replacements);
                    if (!additive)
                    {
                        nextContentCount -= candidate.Contents.Count;
                    }

                    if (nextContentCount > maxContentCount)
                    {
                        budgetLimited = true;
                        expandedNodes.Add(candidate);
                        continue;
                    }

                    if (!additive)
                    {
                        frontier.Remove(candidate);
                    }
                    else
                    {
                        expandedNodes.Add(candidate);
                    }

                    frontier.UnionWith(replacements);
                    refined = true;
                    break;
                }

                if (!refined)
                {
                    break;
                }
            }

            return new Google3dTilesRefinementSelection
            {
                Nodes = frontier.OrderBy(static node => node.Id).ToList(),
                BudgetLimited = budgetLimited,
                CoverageIncomplete = !coverageComplete
            };
        }

        public static IEnumerable<Google3dTilesRefinementNode> EnumerateNodes(
            IEnumerable<Google3dTilesRefinementNode> roots)
        {
            var pending = new Stack<Google3dTilesRefinementNode>(roots.Reverse());
            while (pending.Count > 0)
            {
                var node = pending.Pop();
                yield return node;
                for (var childIndex = node.Children.Count - 1; childIndex >= 0; childIndex--)
                {
                    pending.Push(node.Children[childIndex]);
                }
            }
        }

        public static Google3dTilesRefinementNode? FindFallbackAncestor(
            Google3dTilesRefinementNode node,
            IReadOnlySet<Google3dTilesRefinementNode> activeNodes)
        {
            var ancestor = node.Parent;
            while (ancestor is not null)
            {
                if (ancestor.Contents.Count > 0)
                {
                    return activeNodes.Contains(ancestor) ? null : ancestor;
                }

                ancestor = ancestor.Parent;
            }

            return null;
        }

        public static bool IsDescendantOrSelf(
            Google3dTilesRefinementNode node,
            Google3dTilesRefinementNode ancestor)
        {
            Google3dTilesRefinementNode? current = node;
            while (current is not null)
            {
                if (ReferenceEquals(current, ancestor))
                {
                    return true;
                }

                current = current.Parent;
            }

            return false;
        }

        private static bool TryCollectReplacementNodes(
            Google3dTilesRefinementNode node,
            out List<Google3dTilesRefinementNode> replacements)
        {
            replacements = new List<Google3dTilesRefinementNode>();
            if (node.Children.Count == 0)
            {
                return false;
            }

            foreach (var child in node.Children)
            {
                var branchNodes = new List<Google3dTilesRefinementNode>();
                if (!TryCollectNearestContentNodes(child, branchNodes))
                {
                    replacements.Clear();
                    return false;
                }

                replacements.AddRange(branchNodes);
            }

            return replacements.Count > 0;
        }

        private static bool TryCollectNearestContentNodes(
            Google3dTilesRefinementNode node,
            ICollection<Google3dTilesRefinementNode> result)
        {
            if (node.Contents.Count > 0)
            {
                result.Add(node);
                return true;
            }

            if (node.Children.Count == 0)
            {
                return false;
            }

            var complete = true;
            foreach (var child in node.Children)
            {
                complete &= TryCollectNearestContentNodes(child, result);
            }

            return complete;
        }

        private static int CountContents(IEnumerable<Google3dTilesRefinementNode> nodes)
        {
            return nodes.Sum(static node => node.Contents.Count);
        }

        private static double NormalizeGeometricError(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? double.MaxValue
                : Math.Max(0.0, value);
        }
    }
}
