using System;
using System.Collections.Generic;
using Grasshopper.Kernel;

namespace RhinoSpatial
{
    internal static class WfsLayerInputResolver
    {
        public static bool IsConnectedDirectlyToWfsLayersOutput(IGH_Param? layerInputParameter)
        {
            if (layerInputParameter is null)
            {
                return false;
            }

            foreach (var sourceParameter in layerInputParameter.Sources)
            {
                var topLevelObject = sourceParameter.Attributes?.GetTopLevel?.DocObject;

                if (topLevelObject is WfsListLayersComponent)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool TryResolveBaseUrlFromLayerInput(IGH_Param? layerInputParameter, out string baseUrl)
        {
            baseUrl = string.Empty;

            if (layerInputParameter is null)
            {
                return false;
            }

            return TryResolveBaseUrlFromParam(layerInputParameter, new HashSet<Guid>(), out baseUrl);
        }

        private static bool TryResolveBaseUrlFromParam(IGH_Param parameter, HashSet<Guid> visitedParameterIds, out string baseUrl)
        {
            baseUrl = string.Empty;

            if (!visitedParameterIds.Add(parameter.InstanceGuid))
            {
                return false;
            }

            var topLevelObject = parameter.Attributes?.GetTopLevel?.DocObject;

            if (topLevelObject is WfsListLayersComponent listLayersComponent &&
                TryReadTextFromParameter(listLayersComponent.Params.Input[0], out baseUrl))
            {
                return true;
            }

            foreach (var sourceParameter in parameter.Sources)
            {
                if (TryResolveBaseUrlFromParam(sourceParameter, visitedParameterIds, out baseUrl))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TryReadTextFromParameter(IGH_Param parameter, out string value)
        {
            value = string.Empty;

            if (parameter.VolatileDataCount == 0)
            {
                return false;
            }

            var firstBranch = parameter.VolatileData.get_Branch(0);

            if (firstBranch is null || firstBranch.Count == 0)
            {
                return false;
            }

            var firstItem = firstBranch[0];

            if (firstItem is null)
            {
                return false;
            }

            value = firstItem.ToString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(value);
        }
    }
}
