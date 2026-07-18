using System;
using System.IO.Compression;
using System.Threading.Tasks;
using RhinoSpatial.Core;

namespace RhinoSpatial.Sandbox
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length > 0 && string.Equals(args[0], "probe", StringComparison.OrdinalIgnoreCase))
            {
                await RunProbeAsync(args);
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "wfsfeature", StringComparison.OrdinalIgnoreCase))
            {
                await RunWfsFeatureProbeAsync(args);
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "wmsimage", StringComparison.OrdinalIgnoreCase))
            {
                await RunWmsImageProbeAsync(args);
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "wcscoverage", StringComparison.OrdinalIgnoreCase))
            {
                await RunWcsCoverageProbeAsync(args);
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "ogc-request-check", StringComparison.OrdinalIgnoreCase))
            {
                RunOgcRequestChecks();
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "tiles-selection-check", StringComparison.OrdinalIgnoreCase))
            {
                RunGoogleTilesSelectionChecks();
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "reference-source-check", StringComparison.OrdinalIgnoreCase))
            {
                RunReferenceSourceChecks();
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "lod2file", StringComparison.OrdinalIgnoreCase))
            {
                RunLod2File(args);
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "geotiff", StringComparison.OrdinalIgnoreCase))
            {
                RunGeoTiff(args);
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "terrainfile", StringComparison.OrdinalIgnoreCase))
            {
                RunTerrainFile(args);
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "shapefile", StringComparison.OrdinalIgnoreCase))
            {
                RunShapefile(args);
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "geojson", StringComparison.OrdinalIgnoreCase))
            {
                RunGeoJson(args);
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "ogcapi", StringComparison.OrdinalIgnoreCase))
            {
                await RunOgcApiFeaturesAsync(args);
                return;
            }

            if (args.Length > 0 && string.Equals(args[0], "lod2", StringComparison.OrdinalIgnoreCase))
            {
                await RunLod2Async(args);
                return;
            }

            var requestOptions = CreateRequestOptions(args);

            var client = new WfsClient();

            try
            {
                Console.WriteLine("Loading WFS features...");
                Console.WriteLine($"SRS: {requestOptions.SrsName}");
                Console.WriteLine($"Selected area active: {requestOptions.BoundingBox is not null}");
                Console.WriteLine(WfsClient.BuildGetFeatureRequestUrl(requestOptions));
                Console.WriteLine();

                var features = await client.LoadFeaturesAsync(requestOptions);

                Console.WriteLine("Parsed features:");
                Console.WriteLine();

                for (int i = 0; i < features.Count; i++)
                {
                    var feature = features[i];
                    var firstOuterRing = GetFirstOuterRing(feature);

                    Console.WriteLine($"Feature {i + 1}");
                    Console.WriteLine($"Feature id: {feature.Id}");
                    Console.WriteLine($"Title: {GetAttributeValue(feature, "titel")}");
                    Console.WriteLine($"Project number: {GetAttributeValue(feature, "projekt_nr")}");
                    Console.WriteLine($"Status: {GetAttributeValue(feature, "status")}");
                    Console.WriteLine($"Geometry type: {feature.Geometry.Type}");
                    Console.WriteLine($"Outer ring count: {feature.Geometry.OuterRings.Count}");
                    Console.WriteLine($"First outer ring point count: {firstOuterRing?.Points.Count ?? 0}");
                    Console.WriteLine($"First ring is closed: {firstOuterRing is not null && GeometryUtilities.IsClosedRing(firstOuterRing.Points)}");
                    Console.WriteLine($"Attribute count: {feature.Attributes.Count}");
                    Console.WriteLine();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while loading or parsing the response:");
                Console.WriteLine(ex.Message);
            }
        }

        private static async Task RunProbeAsync(string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Usage: probe <wfs|wms|wcs> <GetCapabilities URL>");
                return;
            }

            var kind = args[1].Trim().ToLowerInvariant();
            var url = args[2];

            switch (kind)
            {
                case "wfs":
                    await ProbeSafeAsync("WFS", url, () => ProbeWfsAsync(url));
                    break;
                case "wms":
                    await ProbeSafeAsync("WMS", url, () => ProbeWmsAsync(url));
                    break;
                case "wcs":
                    await ProbeSafeAsync("WCS", url, () => ProbeWcsAsync(url));
                    break;
                default:
                    Console.WriteLine("Unknown probe type. Use one of: wfs, wms, wcs.");
                    break;
            }
        }

        private static async Task RunWfsFeatureProbeAsync(string[] args)
        {
            if (args.Length < 5 || !TryParseBoundingBox(args[4], out var boundingBox) || boundingBox is null)
            {
                Console.WriteLine("Usage: wfsfeature <GetCapabilities URL> <layer> <srs> <minX,minY,maxX,maxY> [maxFeatures]");
                return;
            }

            var maxFeatures = args.Length > 5 && int.TryParse(args[5], out var parsedMaxFeatures)
                ? Math.Clamp(parsedMaxFeatures, 1, 1000)
                : 25;
            var startedAt = DateTime.UtcNow;
            var client = new WfsClient();
            var capabilities = await client.LoadCapabilitiesAsync(args[1]);
            var layer = capabilities.Layers.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, args[2], StringComparison.OrdinalIgnoreCase));
            if (layer is null)
            {
                throw new InvalidOperationException($"WFS layer '{args[2]}' was not advertised by the service.");
            }

            var result = await client.LoadFeaturesWithStatusAsync(new WfsRequestOptions
            {
                BaseUrl = args[1],
                GetFeatureBaseUrl = capabilities.GetFeatureUrl,
                TypeName = layer.Name,
                MaxFeatures = maxFeatures,
                Version = string.IsNullOrWhiteSpace(capabilities.ServiceVersion) ? "2.0.0" : capabilities.ServiceVersion,
                SrsName = args[3],
                OutputFormat = "application/json",
                BoundingBox = boundingBox
            });

            Console.WriteLine("WFS feature request probe");
            Console.WriteLine($"URL: {args[1]}");
            Console.WriteLine($"Operation URL: {capabilities.GetFeatureUrl}");
            Console.WriteLine($"Layer: {layer.Name}");
            Console.WriteLine($"SRS: {args[3]}");
            Console.WriteLine($"Bounds: {FormatBounds(boundingBox)}");
            Console.WriteLine($"Feature count: {result.Features.Count}");
            Console.WriteLine($"Geometry items: {CountGeometryItems(result.Features)}");
            Console.WriteLine($"Status: {result.StatusNote}");
            Console.WriteLine($"Elapsed: {(DateTime.UtcNow - startedAt).TotalSeconds:0.###} s");
        }

        private static async Task RunWmsImageProbeAsync(string[] args)
        {
            if (args.Length < 5 || !TryParseBoundingBox(args[4], out var boundingBox) || boundingBox is null)
            {
                Console.WriteLine("Usage: wmsimage <GetCapabilities URL> <layer> <srs> <minX,minY,maxX,maxY> [width] [height]");
                return;
            }

            var width = args.Length > 5 && int.TryParse(args[5], out var parsedWidth)
                ? Math.Clamp(parsedWidth, 16, 2048)
                : 512;
            var height = args.Length > 6 && int.TryParse(args[6], out var parsedHeight)
                ? Math.Clamp(parsedHeight, 16, 2048)
                : 512;
            var startedAt = DateTime.UtcNow;
            var client = new WmsClient();
            var capabilities = await client.LoadCapabilitiesAsync(args[1]);
            var layer = capabilities.Layers.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, args[2], StringComparison.OrdinalIgnoreCase));
            if (layer is null)
            {
                throw new InvalidOperationException($"WMS layer '{args[2]}' was not advertised by the service.");
            }

            var result = await client.DownloadImageAsync(new WmsRequestOptions
            {
                BaseUrl = args[1],
                GetMapBaseUrl = capabilities.GetMapUrl,
                LayerName = layer.Name,
                BoundingBox = boundingBox,
                SrsName = args[3],
                Width = width,
                Height = height,
                Version = string.IsNullOrWhiteSpace(capabilities.ServiceVersion) ? "1.3.0" : capabilities.ServiceVersion,
                Format = "image/png",
                Transparent = true
            });

            Console.WriteLine("WMS image request probe");
            Console.WriteLine($"URL: {args[1]}");
            Console.WriteLine($"Operation URL: {capabilities.GetMapUrl}");
            Console.WriteLine($"Layer: {layer.Name}");
            Console.WriteLine($"SRS: {args[3]}");
            Console.WriteLine($"Bounds: {FormatBounds(boundingBox)}");
            Console.WriteLine($"Content type: {result.ContentType}");
            Console.WriteLine($"File size: {new FileInfo(result.LocalFilePath).Length} bytes");
            Console.WriteLine($"Used cache: {result.UsedCachedFile}");
            Console.WriteLine($"Elapsed: {(DateTime.UtcNow - startedAt).TotalSeconds:0.###} s");
        }

        private static async Task RunWcsCoverageProbeAsync(string[] args)
        {
            if (args.Length < 4 || !TryParseBoundingBox(args[3], out var boundingBox) || boundingBox is null)
            {
                Console.WriteLine("Usage: wcscoverage <GetCapabilities URL> <coverage> <minX,minY,maxX,maxY> [srs]");
                return;
            }

            var startedAt = DateTime.UtcNow;
            var client = new WcsClient();
            var capabilities = await client.LoadCapabilitiesAsync(args[1]);
            var coverage = capabilities.Coverages.FirstOrDefault(candidate =>
                string.Equals(candidate.CoverageId, args[2], StringComparison.OrdinalIgnoreCase));
            if (coverage is null)
            {
                throw new InvalidOperationException($"WCS coverage '{args[2]}' was not advertised by the service.");
            }

            var descriptionOptions = new WcsRequestOptions
            {
                BaseUrl = args[1],
                DescribeCoverageBaseUrl = capabilities.DescribeCoverageUrl,
                CoverageId = coverage.CoverageId,
                Version = string.IsNullOrWhiteSpace(capabilities.ServiceVersion) ? "2.0.1" : capabilities.ServiceVersion
            };
            var description = await client.LoadCoverageDescriptionAsync(descriptionOptions);
            var requestedSrs = args.Length > 4 ? args[4] : description.NativeSrs;
            var result = await client.DownloadCoverageAsync(new WcsRequestOptions
            {
                BaseUrl = args[1],
                GetCoverageBaseUrl = capabilities.GetCoverageUrl,
                DescribeCoverageBaseUrl = capabilities.DescribeCoverageUrl,
                CoverageId = coverage.CoverageId,
                BoundingBox = boundingBox,
                SrsName = requestedSrs,
                Version = descriptionOptions.Version,
                Format = "image/tiff",
                AxisXLabel = description.AxisXLabel,
                AxisYLabel = description.AxisYLabel
            });

            var validElevations = result.Raster.Elevations.Count(static elevation =>
                !float.IsNaN(elevation) && !float.IsInfinity(elevation));
            Console.WriteLine("WCS coverage request probe");
            Console.WriteLine($"URL: {args[1]}");
            Console.WriteLine($"Coverage: {coverage.CoverageId}");
            Console.WriteLine($"Native SRS: {description.NativeSrs}");
            Console.WriteLine($"Native coverage bounds: {FormatBounds(description.NativeBoundingBox)}");
            if (ReferenceSourceMetadataReader.TryTransformBoundsToWgs84(
                    description.NativeSrs,
                    description.NativeBoundingBox,
                    out var referenceBounds))
            {
                Console.WriteLine($"Reference bounds EPSG:4326: {FormatBounds(referenceBounds)}");
            }
            Console.WriteLine($"Request SRS: {requestedSrs}");
            Console.WriteLine($"Axes: {description.AxisXLabel}, {description.AxisYLabel}");
            Console.WriteLine($"Bounds: {FormatBounds(boundingBox)}");
            Console.WriteLine($"Raster size: {result.Raster.Width} x {result.Raster.Height}");
            Console.WriteLine($"Valid elevations: {validElevations}");
            Console.WriteLine($"File size: {new FileInfo(result.LocalFilePath).Length} bytes");
            Console.WriteLine($"Used cache: {result.UsedCachedFile}");
            Console.WriteLine($"Elapsed: {(DateTime.UtcNow - startedAt).TotalSeconds:0.###} s");
        }

        private static int CountGeometryItems(IEnumerable<WfsFeature> features)
        {
            return features.Sum(feature =>
                feature.Geometry.OuterRings.Count +
                feature.Geometry.LineStrings.Count +
                feature.Geometry.Points.Count);
        }

        private static void RunOgcRequestChecks()
        {
            var geographicBounds = new BoundingBox2D(11.61, 52.11, 11.63, 52.13);
            var wfs4258Url = WfsClient.BuildGetFeatureRequestUrl(new WfsRequestOptions
            {
                BaseUrl = "https://example.invalid/wfs",
                TypeName = "test:buildings",
                Version = "2.0.0",
                SrsName = "EPSG:4258",
                BoundingBox = geographicBounds
            });
            Require(
                wfs4258Url.Contains("BBOX=52.11,11.61,52.13,11.63,EPSG%3A4258", StringComparison.Ordinal),
                "WFS 2.0 EPSG:4258 requests should use latitude/longitude authority axis order.");

            var secureWfsUrl = WfsClient.BuildGetFeatureRequestUrl(new WfsRequestOptions
            {
                BaseUrl = "https://example.invalid/wfs",
                GetFeatureBaseUrl = "http://example.invalid/wfs/operation",
                TypeName = "test:features"
            });
            Require(
                secureWfsUrl.StartsWith("https://example.invalid/wfs/operation?", StringComparison.Ordinal),
                "An HTTPS capabilities entry point should upgrade a same-host HTTP operation URL before use.");

            var wfs4326Url = WfsClient.BuildGetFeatureRequestUrl(new WfsRequestOptions
            {
                BaseUrl = "https://example.invalid/wfs",
                TypeName = "test:features",
                Version = "2.0.0",
                SrsName = "EPSG:4326",
                BoundingBox = geographicBounds
            });
            Require(
                wfs4326Url.Contains("BBOX=11.61,52.11,11.63,52.13,EPSG%3A4326", StringComparison.Ordinal),
                "WFS EPSG:4326 should retain the established XY compatibility order.");

            var wms130Url = WmsClient.BuildGetMapRequestUrl(new WmsRequestOptions
            {
                BaseUrl = "https://example.invalid/wms",
                LayerName = "test",
                Version = "1.3.0",
                SrsName = "EPSG:4326",
                BoundingBox = geographicBounds,
                Width = 256,
                Height = 256
            });
            Require(
                wms130Url.Contains("BBOX=52.11,11.61,52.13,11.63", StringComparison.Ordinal),
                "WMS 1.3 EPSG:4326 requests should use latitude/longitude authority axis order.");

            var secureWmsUrl = WmsClient.BuildGetMapRequestUrl(new WmsRequestOptions
            {
                BaseUrl = "https://example.invalid/wms",
                GetMapBaseUrl = "http://example.invalid/wms/operation",
                LayerName = "test",
                SrsName = "EPSG:3857",
                BoundingBox = new BoundingBox2D(0.0, 0.0, 1.0, 1.0)
            });
            Require(
                secureWmsUrl.StartsWith("https://example.invalid/wms/operation?", StringComparison.Ordinal),
                "WMS should upgrade a same-host HTTP operation URL from an HTTPS entry point.");

            var secureWcsUrl = WcsClient.BuildGetCoverageRequestUrl(new WcsRequestOptions
            {
                BaseUrl = "https://example.invalid/wcs",
                GetCoverageBaseUrl = "http://example.invalid/wcs/operation",
                CoverageId = "test",
                SrsName = "EPSG:3857",
                BoundingBox = new BoundingBox2D(0.0, 0.0, 1.0, 1.0)
            });
            Require(
                secureWcsUrl.StartsWith("https://example.invalid/wcs/operation?", StringComparison.Ordinal),
                "WCS should upgrade a same-host HTTP operation URL from an HTTPS entry point.");

            var wms111Url = WmsClient.BuildGetMapRequestUrl(new WmsRequestOptions
            {
                BaseUrl = "https://example.invalid/wms",
                LayerName = "test",
                Version = "1.1.1",
                SrsName = "EPSG:4326",
                BoundingBox = geographicBounds,
                Width = 256,
                Height = 256
            });
            Require(
                wms111Url.Contains("BBOX=11.61,52.11,11.63,52.13", StringComparison.Ordinal),
                "WMS 1.1.1 EPSG:4326 requests should keep longitude/latitude order.");

            var gmlFeatures = GmlReader.ReadFeatures(
                """
                <wfs:FeatureCollection xmlns:wfs="http://www.opengis.net/wfs/2.0" xmlns:gml="http://www.opengis.net/gml/3.2" xmlns:test="https://example.invalid/test">
                  <wfs:member>
                    <test:feature gml:id="point-1">
                      <test:shape><gml:Point srsName="EPSG:4258"><gml:pos>52.11 11.61</gml:pos></gml:Point></test:shape>
                    </test:feature>
                  </wfs:member>
                </wfs:FeatureCollection>
                """,
                "test:feature");
            Require(
                gmlFeatures.Count == 1 &&
                gmlFeatures[0].Geometry.Points.Count == 1 &&
                Math.Abs(gmlFeatures[0].Geometry.Points[0].X - 11.61) < 1e-9 &&
                Math.Abs(gmlFeatures[0].Geometry.Points[0].Y - 52.11) < 1e-9,
                "EPSG:4258 GML response coordinates should normalize to XY longitude/latitude order.");

            var lod2Buildings = Lod2GmlReader.ReadBuildings(
                """
                <core:CityModel xmlns:core="http://www.opengis.net/citygml/2.0" xmlns:bldg="http://www.opengis.net/citygml/building/2.0" xmlns:gml="http://www.opengis.net/gml/3.2">
                  <core:cityObjectMember>
                    <bldg:Building gml:id="building-1">
                      <bldg:lod2MultiSurface>
                        <gml:MultiSurface srsName="EPSG:4258">
                          <gml:surfaceMember>
                            <gml:Polygon srsDimension="3">
                              <gml:exterior><gml:LinearRing><gml:posList>52.11 11.61 50 52.11 11.62 50 52.12 11.62 50 52.12 11.61 50 52.11 11.61 50</gml:posList></gml:LinearRing></gml:exterior>
                            </gml:Polygon>
                          </gml:surfaceMember>
                        </gml:MultiSurface>
                      </bldg:lod2MultiSurface>
                    </bldg:Building>
                  </core:cityObjectMember>
                </core:CityModel>
                """,
                "test:buildings");
            Require(
                lod2Buildings.Count == 1 &&
                lod2Buildings[0].Surfaces.Count == 1 &&
                lod2Buildings[0].Surfaces[0].OuterPoints.Count == 5 &&
                Math.Abs(lod2Buildings[0].Surfaces[0].OuterPoints[0].Z - 50.0) < 1e-9,
                "LoD2 position lists should inherit srsDimension=3 from enclosing GML geometry.");

            Console.WriteLine("OGC request axis-order checks passed.");
        }

        private static void RunGoogleTilesSelectionChecks()
        {
            var replacementRoot = CreateTileNode(1, null, 100.0, "REPLACE", true);
            for (var index = 0; index < 4; index++)
            {
                AddTileChild(replacementRoot, 2 + index, 10.0, "REPLACE", true);
            }

            var refined = Google3dTilesRefinementSelector.Select(new[] { replacementRoot }, 20.0, 8);
            Require(refined.Nodes.Count == 4, "REPLACE refinement should select all four children.");
            Require(!refined.Nodes.Contains(replacementRoot), "REPLACE refinement must not retain the parent with its children.");

            var budgeted = Google3dTilesRefinementSelector.Select(new[] { replacementRoot }, 20.0, 3);
            Require(budgeted.Nodes.Count == 1 && budgeted.Nodes[0] == replacementRoot, "Coverage budget should retain the complete parent branch.");
            Require(budgeted.BudgetLimited, "Coverage budget should be reported as limiting refinement.");

            var incompleteRoot = CreateTileNode(10, null, 100.0, "REPLACE", true);
            AddTileChild(incompleteRoot, 11, 10.0, "REPLACE", true);
            AddTileChild(incompleteRoot, 12, 10.0, "REPLACE", false);
            var incomplete = Google3dTilesRefinementSelector.Select(new[] { incompleteRoot }, 20.0, 8);
            Require(incomplete.Nodes.Count == 1 && incomplete.Nodes[0] == incompleteRoot, "An incomplete child frontier should retain its parent.");

            var contentlessRoot = CreateTileNode(13, null, 100.0, "REPLACE", false);
            AddTileChild(contentlessRoot, 14, 10.0, "REPLACE", true);
            AddTileChild(contentlessRoot, 15, 10.0, "REPLACE", false);
            var partialSource = Google3dTilesRefinementSelector.Select(new[] { contentlessRoot }, 20.0, 8);
            Require(partialSource.CoverageIncomplete, "A source branch with no usable content should be reported as incomplete.");

            var additiveRoot = CreateTileNode(20, null, 100.0, "ADD", true);
            AddTileChild(additiveRoot, 21, 10.0, "ADD", true);
            AddTileChild(additiveRoot, 22, 10.0, "ADD", true);
            var additive = Google3dTilesRefinementSelector.Select(new[] { additiveRoot }, 20.0, 8);
            Require(additive.Nodes.Count == 3 && additive.Nodes.Contains(additiveRoot), "ADD refinement should retain the parent and add its children.");

            var balancedRoot = CreateTileNode(30, null, 100.0, "REPLACE", true);
            var balancedBranches = new List<Google3dTilesRefinementNode>();
            for (var branchIndex = 0; branchIndex < 4; branchIndex++)
            {
                var branch = AddTileChild(balancedRoot, 31 + branchIndex, 40.0, "REPLACE", true);
                balancedBranches.Add(branch);
                AddTileChild(branch, 40 + (branchIndex * 2), 5.0, "REPLACE", true);
                AddTileChild(branch, 41 + (branchIndex * 2), 5.0, "REPLACE", true);
            }

            var balanced = Google3dTilesRefinementSelector.Select(new[] { balancedRoot }, 10.0, 6);
            Require(balanced.Nodes.Count == 6 && balanced.BudgetLimited, "Budgeted refinement should use the available frontier budget.");
            foreach (var branch in balancedBranches)
            {
                Require(
                    balanced.Nodes.Any(node => Google3dTilesRefinementSelector.IsDescendantOrSelf(node, branch)),
                    "Budgeted refinement must retain coverage for every top-level branch.");
            }

            foreach (var selectedNode in balanced.Nodes.Where(node => node.Refinement == "REPLACE"))
            {
                Require(
                    !balanced.Nodes.Any(other => other != selectedNode && Google3dTilesRefinementSelector.IsDescendantOrSelf(other, selectedNode)),
                    "A REPLACE frontier must not contain both a selected node and its descendant.");
            }

            var activeChildren = refined.Nodes.ToHashSet();
            var fallback = Google3dTilesRefinementSelector.FindFallbackAncestor(refined.Nodes[0], activeChildren);
            Require(fallback == replacementRoot, "A failed refined branch should promote its nearest content-bearing parent.");
            Require(Google3dTilesRefinementSelector.IsDescendantOrSelf(refined.Nodes[0], replacementRoot), "Fallback ancestry should recognize selected descendants.");

            Require(
                Google3dTilesGeographicBounds.LongitudeRangesIntersect(10.0, 20.0, 15.0, 16.0),
                "Ordinary overlapping longitude ranges should intersect.");
            Require(
                !Google3dTilesGeographicBounds.LongitudeRangesIntersect(10.0, 20.0, 30.0, 40.0),
                "Ordinary disjoint longitude ranges should not intersect.");
            Require(
                Google3dTilesGeographicBounds.TryCreateMinimalLongitudeArc(
                    new[] { 179.5, -179.7, 178.9, -179.2 },
                    out var wrappedWest,
                    out var wrappedEast),
                "Antimeridian corner longitudes should produce a circular arc.");
            Require(
                wrappedWest > wrappedEast,
                "The minimal antimeridian arc should retain its wrapped representation.");
            Require(
                Google3dTilesGeographicBounds.LongitudeRangesIntersect(
                    wrappedWest,
                    wrappedEast,
                    179.7,
                    -179.8),
                "Wrapped tile and study ranges should intersect around the antimeridian.");
            Require(
                !Google3dTilesGeographicBounds.LongitudeRangesIntersect(
                    wrappedWest,
                    wrappedEast,
                    13.0,
                    14.0),
                "An antimeridian tile must not be treated as intersecting a distant European range.");
            Require(
                Google3dTilesGeographicBounds.LongitudeRangesIntersect(174.0, 184.0, -179.0, -178.0),
                "Longitude ranges extending past 180 degrees should wrap correctly.");

            var candidateElevations = new[] { -500.0 }
                .Concat(Enumerable.Range(0, 100).Select(index => 100.0 + index))
                .Concat(new[] { double.NaN, double.PositiveInfinity });
            Require(
                Google3dTilesElevationBaseline.TryResolveCandidate(candidateElevations, out var googleCandidateBaseline),
                "Usable Google mesh elevations should produce a local baseline candidate.");
            Require(
                Math.Abs(googleCandidateBaseline - 101.0) < 1e-9,
                "The Google baseline candidate should use a robust lower percentile instead of one low outlier.");

            var googleFirstContext = CreateBaselineTestContext(1000.0);
            SpatialElevationBaselineCache.Remove(googleFirstContext);
            var googleFirstBaseline = SpatialElevationBaselineCache.ResolveOrStore(
                googleFirstContext,
                googleCandidateBaseline,
                out var googleEstablishedBaseline);
            var lod2AfterGoogleBaseline = SpatialElevationBaselineCache.ResolveOrStore(
                googleFirstContext,
                95.0,
                out var lod2ReplacedGoogleBaseline);
            Require(googleEstablishedBaseline, "Google should establish the shared baseline when it solves first.");
            Require(!lod2ReplacedGoogleBaseline, "LoD2 should reuse rather than replace a Google-established baseline.");
            Require(
                Math.Abs(googleFirstBaseline - lod2AfterGoogleBaseline) < 1e-9,
                "Google-first and subsequent LoD2 placement should use the same baseline.");

            var lod2FirstContext = CreateBaselineTestContext(2000.0);
            SpatialElevationBaselineCache.Remove(lod2FirstContext);
            var lod2FirstBaseline = SpatialElevationBaselineCache.ResolveOrStore(
                lod2FirstContext,
                95.0,
                out var lod2EstablishedBaseline);
            var googleAfterLod2Baseline = SpatialElevationBaselineCache.ResolveOrStore(
                lod2FirstContext,
                googleCandidateBaseline,
                out var googleReplacedLod2Baseline);
            Require(lod2EstablishedBaseline, "LoD2 should establish the shared baseline when it solves first.");
            Require(!googleReplacedLod2Baseline, "Google should reuse rather than replace a LoD2-established baseline.");
            Require(
                Math.Abs(lod2FirstBaseline - googleAfterLod2Baseline) < 1e-9,
                "LoD2-first and subsequent Google placement should use the same baseline.");

            var concurrentContext = CreateBaselineTestContext(3000.0);
            SpatialElevationBaselineCache.Remove(concurrentContext);
            var concurrentBaselines = new double[2];
            var concurrentStoredCandidates = new bool[2];
            Parallel.Invoke(
                () => concurrentBaselines[0] = SpatialElevationBaselineCache.ResolveOrStore(
                    concurrentContext,
                    googleCandidateBaseline,
                    out concurrentStoredCandidates[0]),
                () => concurrentBaselines[1] = SpatialElevationBaselineCache.ResolveOrStore(
                    concurrentContext,
                    95.0,
                    out concurrentStoredCandidates[1]));
            Require(
                Math.Abs(concurrentBaselines[0] - concurrentBaselines[1]) < 1e-9,
                "Concurrent Google and LoD2 solves should resolve to one shared baseline.");
            Require(
                concurrentStoredCandidates.Count(static stored => stored) == 1,
                "Exactly one concurrent component should establish the shared baseline.");

            SpatialElevationBaselineCache.Remove(googleFirstContext);
            SpatialElevationBaselineCache.Remove(lod2FirstContext);
            SpatialElevationBaselineCache.Remove(concurrentContext);

            Console.WriteLine("Google 3D Tiles refinement, geographic bounds, and elevation baseline checks passed.");
        }

        private static SpatialContext2D CreateBaselineTestContext(double minX)
        {
            var bounds = new BoundingBox2D(minX, 5000.0, minX + 100.0, 5100.0);
            return new SpatialContext2D(
                "EPSG:25832",
                bounds,
                bounds,
                null,
                new Coordinate2D(bounds.MinX, bounds.MinY),
                false,
                new Dictionary<string, BoundingBox2D>(StringComparer.OrdinalIgnoreCase)
                {
                    ["EPSG:25832"] = bounds
                });
        }

        private static void RunReferenceSourceChecks()
        {
            var testDirectory = Path.Combine(
                Path.GetTempPath(),
                $"rhino-spatial-reference-{Guid.NewGuid():N}");
            Directory.CreateDirectory(testDirectory);
            try
            {
                var geoJsonPath = Path.Combine(testDirectory, "area.geojson");
                File.WriteAllText(
                    geoJsonPath,
                    """
                    {
                      "type": "FeatureCollection",
                      "bbox": [8.5, 49.9, 8.8, 50.2],
                      "features": []
                    }
                    """);
                var geoJson = ReferenceSourceMetadataReader.Read(geoJsonPath);
                Require(geoJson.SourceKind == "GeoJSON", "GeoJSON should resolve as a reference source.");
                Require(geoJson.SrsName == "EPSG:4326", "GeoJSON without a legacy CRS should default to WGS84.");
                Require(geoJson.Wgs84BoundingBox is not null, "GeoJSON bbox metadata should produce a map extent.");

                var cityJsonPath = Path.Combine(testDirectory, "building.cityjson");
                File.WriteAllText(
                    cityJsonPath,
                    """
                    {
                      "type": "CityJSON",
                      "version": "2.0",
                      "metadata": { "referenceSystem": "https://www.opengis.net/def/crs/EPSG/0/4326" },
                      "CityObjects": {},
                      "vertices": [[8.6, 50.0, 0], [8.7, 50.1, 10]]
                    }
                    """);
                var cityJson = ReferenceSourceMetadataReader.Read(cityJsonPath);
                Require(cityJson.SourceKind == "CityJSON", "CityJSON should resolve as a reference source.");
                Require(cityJson.Wgs84BoundingBox is not null, "CityJSON vertices should produce a map extent.");

                var terrainPath = Path.Combine(testDirectory, "terrain.asc");
                File.WriteAllText(
                    terrainPath,
                    """
                    ncols 2
                    nrows 2
                    xllcorner 8.5
                    yllcorner 49.9
                    cellsize 0.1
                    NODATA_value -9999
                    1 2
                    3 4
                    """);
                var terrain = ReferenceSourceMetadataReader.Read(terrainPath, "EPSG:4326");
                Require(terrain.SourceKind == "Esri ASCII Grid", "ASCII terrain should resolve as a reference source.");
                Require(terrain.Wgs84BoundingBox is not null, "ASCII terrain with an SRS hint should produce a map extent.");

                var zipPath = Path.Combine(testDirectory, "reference.zip");
                using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
                {
                    var validEntry = archive.CreateEntry("area.geojson");
                    using (var writer = new StreamWriter(validEntry.Open()))
                    {
                        writer.Write(
                            """
                            {
                              "type": "FeatureCollection",
                              "bbox": [8.5, 49.9, 8.8, 50.2],
                              "features": []
                            }
                            """);
                    }

                    var invalidEntry = archive.CreateEntry("stale.json");
                    using var invalidWriter = new StreamWriter(invalidEntry.Open());
                    invalidWriter.Write("not valid JSON");
                }

                var zipped = ReferenceSourceMetadataReader.Read(zipPath);
                Require(zipped.SourceKind == "ZIP archive", "A supported ZIP should combine its usable metadata entries.");
                Require(zipped.SourceItemCount == 1, "Malformed ZIP entries should not hide usable reference metadata.");
                Require(zipped.Wgs84BoundingBox is not null, "ZIP metadata should produce a map extent.");

                var folder = ReferenceSourceMetadataReader.Read(testDirectory, "EPSG:4326");
                Require(folder.SourceKind == "folder", "A folder should combine supported reference sources.");
                Require(folder.SourceItemCount == 4, "The reference folder should report all usable source items, including ZIP contents.");

                var mixedDirectory = Path.Combine(testDirectory, "mixed-srs");
                Directory.CreateDirectory(mixedDirectory);
                File.Copy(geoJsonPath, Path.Combine(mixedDirectory, "area.geojson"));
                File.WriteAllText(
                    Path.Combine(mixedDirectory, "unknown-srs.asc"),
                    """
                    ncols 2
                    nrows 2
                    xllcorner 500000
                    yllcorner 5500000
                    cellsize 1
                    NODATA_value -9999
                    1 2
                    3 4
                    """);
                var mixedFolder = ReferenceSourceMetadataReader.Read(mixedDirectory);
                Require(mixedFolder.SrsName == "EPSG:4326", "A known folder SRS should remain usable when another file has no SRS.");
                Require(
                    mixedFolder.NativeBoundingBox is not null && mixedFolder.NativeBoundingBox.MaxX < 10.0,
                    "Unknown-CRS native bounds must not be merged into a known folder coordinate space.");

                Require(
                    ReferenceSourceMetadataReader.TryTransformBoundsToWgs84(
                        "EPSG:3857",
                        new BoundingBox2D(0.0, 0.0, 111319.4908, 111325.1429),
                        out var transformedBounds),
                    "Supported projected reference bounds should transform to WGS84.");
                Require(
                    Math.Abs(transformedBounds.MaxX - 1.0) < 0.001 &&
                    Math.Abs(transformedBounds.MaxY - 1.0) < 0.001,
                    "Web Mercator reference bounds should transform to the expected longitude and latitude.");

                Require(
                    SpatialReferenceTransform.TryTransformXY(
                        "EPSG:27700",
                        "EPSG:4326",
                        530000.0,
                        180000.0,
                        out var londonLongitude,
                        out var londonLatitude),
                    "British National Grid reference metadata should transform to WGS84.");
                Require(
                    Math.Abs(londonLongitude - -0.128) < 0.01 &&
                    Math.Abs(londonLatitude - 51.504) < 0.01,
                    "British National Grid reference metadata should resolve near central London.");

                Require(
                    SpatialReferenceTransform.TryTransformXY(
                        "EPSG:28992",
                        "EPSG:4326",
                        155000.0,
                        463000.0,
                        out var amersfoortLongitude,
                        out var amersfoortLatitude),
                    "Dutch RD New reference metadata should transform to WGS84.");
                Require(
                    Math.Abs(amersfoortLongitude - 5.387) < 0.01 &&
                    Math.Abs(amersfoortLatitude - 52.155) < 0.01,
                    "The Dutch RD New reference point should resolve near Amersfoort.");
                Require(
                    SpatialReferenceTransform.TryTransformXY(
                        "EPSG:7415",
                        "EPSG:4326",
                        155000.0,
                        463000.0,
                        out _,
                        out _),
                    "Dutch RD New + NAP compound CityJSON metadata should use the supported horizontal transform.");

                Console.WriteLine("Reference source metadata checks passed.");
            }
            finally
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        private static Google3dTilesRefinementNode CreateTileNode(
            int id,
            Google3dTilesRefinementNode? parent,
            double geometricError,
            string refinement,
            bool hasContent)
        {
            var node = new Google3dTilesRefinementNode
            {
                Id = id,
                Parent = parent,
                Depth = parent is null ? 0 : parent.Depth + 1,
                GeometricError = geometricError,
                Refinement = refinement
            };
            if (hasContent)
            {
                node.Contents.Add(new Google3dTilesTileDescriptor
                {
                    Key = id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Url = $"https://example.invalid/{id}.glb"
                });
            }

            return node;
        }

        private static Google3dTilesRefinementNode AddTileChild(
            Google3dTilesRefinementNode parent,
            int id,
            double geometricError,
            string refinement,
            bool hasContent)
        {
            var child = CreateTileNode(id, parent, geometricError, refinement, hasContent);
            parent.Children.Add(child);
            return child;
        }

        private static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static async Task ProbeSafeAsync(string kind, string url, Func<Task> probe)
        {
            try
            {
                await probe();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{kind} capabilities probe failed");
                Console.WriteLine($"URL: {url}");
                Console.WriteLine(ex.Message);
            }
        }

        private static async Task ProbeWfsAsync(string url)
        {
            var startedAt = DateTime.UtcNow;
            var capabilities = await new WfsClient().LoadCapabilitiesAsync(url);
            Console.WriteLine("WFS capabilities probe");
            Console.WriteLine($"URL: {url}");
            Console.WriteLine($"Version: {capabilities.ServiceVersion}");
            Console.WriteLine($"Layer count: {capabilities.Layers.Count}");
            Console.WriteLine($"GetFeature URL: {capabilities.GetFeatureUrl}");
            PrintLayers(capabilities.Layers.Take(12));
            Console.WriteLine($"Elapsed: {(DateTime.UtcNow - startedAt).TotalSeconds:0.###} s");
        }

        private static async Task ProbeWmsAsync(string url)
        {
            var startedAt = DateTime.UtcNow;
            var capabilities = await new WmsClient().LoadCapabilitiesAsync(url);
            Console.WriteLine("WMS capabilities probe");
            Console.WriteLine($"URL: {url}");
            Console.WriteLine($"Version: {capabilities.ServiceVersion}");
            Console.WriteLine($"Layer count: {capabilities.Layers.Count}");
            Console.WriteLine($"GetMap URL: {capabilities.GetMapUrl}");
            Console.WriteLine($"Max size: {capabilities.MaxWidth?.ToString() ?? "?"} x {capabilities.MaxHeight?.ToString() ?? "?"}");
            PrintLayers(capabilities.Layers.Take(12));
            Console.WriteLine($"Elapsed: {(DateTime.UtcNow - startedAt).TotalSeconds:0.###} s");
        }

        private static async Task ProbeWcsAsync(string url)
        {
            var startedAt = DateTime.UtcNow;
            var capabilities = await new WcsClient().LoadCapabilitiesAsync(url);
            Console.WriteLine("WCS capabilities probe");
            Console.WriteLine($"URL: {url}");
            Console.WriteLine($"Version: {capabilities.ServiceVersion}");
            Console.WriteLine($"Coverage count: {capabilities.Coverages.Count}");
            Console.WriteLine($"GetCoverage URL: {capabilities.GetCoverageUrl}");
            Console.WriteLine($"DescribeCoverage URL: {capabilities.DescribeCoverageUrl}");
            foreach (var coverage in capabilities.Coverages.Take(12))
            {
                Console.WriteLine($"- {coverage.CoverageId} | {coverage.Title} | bbox {FormatBounds(coverage.Wgs84BoundingBox)}");
            }
            Console.WriteLine($"Elapsed: {(DateTime.UtcNow - startedAt).TotalSeconds:0.###} s");
        }

        private static void PrintLayers(IEnumerable<WfsLayerInfo> layers)
        {
            foreach (var layer in layers)
            {
                Console.WriteLine($"- {layer.Name} | {layer.Title} | default {layer.DefaultSrs} | bbox {FormatBounds(layer.Wgs84BoundingBox)}");
            }
        }

        private static void PrintLayers(IEnumerable<WmsLayerInfo> layers)
        {
            foreach (var layer in layers)
            {
                var srsPreview = layer.SupportedSrs.Count == 0
                    ? "?"
                    : string.Join(",", layer.SupportedSrs.Take(4));
                Console.WriteLine($"- {layer.Name} | {layer.Title} | srs {srsPreview} | bbox {FormatBounds(layer.Wgs84BoundingBox)}");
            }
        }

        private static string FormatBounds(BoundingBox2D? bounds)
        {
            return bounds is null
                ? "?"
                : string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0:0.####},{1:0.####},{2:0.####},{3:0.####}",
                    bounds.MinX,
                    bounds.MinY,
                    bounds.MaxX,
                    bounds.MaxY);
        }

        private static void RunLod2File(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: lod2file <path> [minX,minY,maxX,maxY]");
                return;
            }

            var filePath = args[1];
            var filterBounds = args.Length > 2 && TryParseBoundingBox(args[2], out var parsedBounds)
                ? parsedBounds
                : null;
            var metadataStartedAt = DateTime.UtcNow;
            var text = System.IO.File.ReadAllText(filePath);
            var isCityJson = filePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                             filePath.EndsWith(".cityjson", StringComparison.OrdinalIgnoreCase);
            var metadata = isCityJson
                ? ConvertCityJsonMetadata(CityJsonReader.ReadSourceMetadata(text))
                : ReadCityGmlSourceMetadata(filePath);
            var metadataElapsed = DateTime.UtcNow - metadataStartedAt;

            var startedAt = DateTime.UtcNow;
            var buildings = isCityJson
                ? CityJsonReader.ReadBuildings(text, System.IO.Path.GetFileNameWithoutExtension(filePath), filterBounds)
                : Lod2GmlReader.ReadBuildings(text, System.IO.Path.GetFileNameWithoutExtension(filePath), filterBounds);
            var elapsed = DateTime.UtcNow - startedAt;

            Console.WriteLine($"File: {filePath}");
            Console.WriteLine($"Source format: {(isCityJson ? "CityJSON" : "CityGML/GML")}");
            Console.WriteLine($"Source SRS: {metadata.SrsName}");
            Console.WriteLine($"File bounds: {metadata.BoundingBox}");
            Console.WriteLine($"Filter active: {filterBounds is not null}");
            Console.WriteLine($"Parsed buildings: {buildings.Count}");
            Console.WriteLine($"Parsed surfaces: {buildings.Sum(building => building.Surfaces.Count)}");
            Console.WriteLine($"Metadata elapsed: {metadataElapsed.TotalSeconds:0.###} s");
            Console.WriteLine($"Parse elapsed: {elapsed.TotalSeconds:0.###} s");
        }

        private static CityGmlSourceMetadata ReadCityGmlSourceMetadata(string filePath)
        {
            using var stream = System.IO.File.OpenRead(filePath);
            return Lod2GmlReader.ReadSourceMetadata(stream);
        }

        private static CityGmlSourceMetadata ConvertCityJsonMetadata(CityJsonSourceMetadata metadata)
        {
            return new CityGmlSourceMetadata(metadata.SrsName, metadata.BoundingBox);
        }

        private static void RunGeoTiff(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: geotiff <path>");
                return;
            }

            var startedAt = DateTime.UtcNow;
            var info = GeoTiffReader.ReadImageInfo(args[1]);
            var elapsed = DateTime.UtcNow - startedAt;

            Console.WriteLine($"File: {args[1]}");
            Console.WriteLine($"SRS: {info.SrsName}");
            Console.WriteLine($"Size: {info.Width} x {info.Height}");
            Console.WriteLine($"Bounds: {FormatBounds(info.BoundingBox)}");
            Console.WriteLine($"File size: {info.FileSizeBytes} bytes");
            Console.WriteLine($"Elapsed: {elapsed.TotalSeconds:0.###} s");
        }

        private static void RunTerrainFile(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: terrainfile <path> [sourceSrs]");
                return;
            }

            var startedAt = DateTime.UtcNow;
            var extension = System.IO.Path.GetExtension(args[1]);
            TerrainRasterData raster;
            BoundingBox2D bounds;
            string sourceSrs;
            string formatLabel;
            if (extension.Equals(".tif", StringComparison.OrdinalIgnoreCase) ||
                extension.Equals(".tiff", StringComparison.OrdinalIgnoreCase))
            {
                var info = GeoTiffReader.ReadImageInfo(args[1]);
                raster = TerrainRasterReader.ReadRaster(args[1], System.IO.Path.GetFileNameWithoutExtension(args[1]), info.SrsName);
                bounds = info.BoundingBox;
                sourceSrs = info.SrsName;
                formatLabel = "GeoTIFF DEM";
            }
            else
            {
                sourceSrs = args.Length > 2 ? args[2] : "EPSG:4326";
                var readResult = TerrainTextGridReader.ReadRaster(
                    args[1],
                    System.IO.Path.GetFileNameWithoutExtension(args[1]),
                    sourceSrs);
                raster = readResult.Raster;
                bounds = readResult.BoundingBox;
                formatLabel = readResult.FormatLabel;
            }

            var elapsed = DateTime.UtcNow - startedAt;
            var validCount = 0;
            var minElevation = double.PositiveInfinity;
            var maxElevation = double.NegativeInfinity;

            foreach (var elevation in raster.Elevations)
            {
                if (float.IsNaN(elevation) ||
                    float.IsInfinity(elevation) ||
                    (raster.NoDataValue.HasValue &&
                     !double.IsNaN(raster.NoDataValue.Value) &&
                     Math.Abs(elevation - raster.NoDataValue.Value) < 1e-3))
                {
                    continue;
                }

                validCount++;
                minElevation = Math.Min(minElevation, elevation);
                maxElevation = Math.Max(maxElevation, elevation);
            }

            Console.WriteLine($"File: {args[1]}");
            Console.WriteLine($"Format: {formatLabel}");
            Console.WriteLine($"SRS: {sourceSrs}");
            Console.WriteLine($"Size: {raster.Width} x {raster.Height}");
            Console.WriteLine($"Bounds: {FormatBounds(bounds)}");
            Console.WriteLine($"Valid elevations: {validCount}");
            Console.WriteLine($"Elevation range: {(double.IsInfinity(minElevation) ? "?" : minElevation.ToString("0.###"))}..{(double.IsInfinity(maxElevation) ? "?" : maxElevation.ToString("0.###"))}");
            Console.WriteLine($"NoData: {raster.NoDataValue?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "?"}");
            Console.WriteLine($"Elapsed: {elapsed.TotalSeconds:0.###} s");
        }

        private static void RunShapefile(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: shapefile <path>");
                return;
            }

            var startedAt = DateTime.UtcNow;
            var shapefilePath = args[1];
            var sourceSrs = ResolveSandboxShapefileSrs(shapefilePath);
            var bounds = ResolveSandboxShapefileBounds(shapefilePath);
            var spatialContext = new SpatialContext2D(
                sourceSrs,
                bounds,
                bounds,
                sourceSrs == "EPSG:4326" ? bounds : null,
                new Coordinate2D(bounds.MinX, bounds.MinY),
                false,
                new Dictionary<string, BoundingBox2D>
                {
                    [sourceSrs] = bounds
                });
            if (sourceSrs == "EPSG:4326")
            {
                spatialContext.BoundingBoxesBySrs["EPSG:7423"] = bounds;
            }

            var result = ShapefileFeatureReader.ReadFeatures(
                shapefilePath,
                System.IO.Path.GetFileNameWithoutExtension(shapefilePath),
                spatialContext,
                0);
            var elapsed = DateTime.UtcNow - startedAt;

            Console.WriteLine($"File: {shapefilePath}");
            Console.WriteLine($"Source SRS: {result.SourceSrs}");
            Console.WriteLine($"Source bounds: {FormatBounds(result.SourceBoundingBox)}");
            Console.WriteLine($"Scanned features: {result.SourceFeatureCount}");
            Console.WriteLine($"Parsed features: {result.Features.Count}");
            Console.WriteLine($"Skipped outside context: {result.SkippedOutsideContextCount}");
            Console.WriteLine($"Failed features: {result.FailedFeatureCount}");
            Console.WriteLine($"Elapsed: {elapsed.TotalSeconds:0.###} s");
        }

        private static void RunGeoJson(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: geojson <path> [sourceSrs]");
                return;
            }

            var startedAt = DateTime.UtcNow;
            var geoJsonPath = args[1];
            var geoJson = System.IO.File.ReadAllText(geoJsonPath);
            var sourceSrs = args.Length > 2
                ? args[2]
                : GeoJsonReader.TryReadSourceSrs(geoJson) ?? "EPSG:4326";
            var features = GeoJsonReader.ReadFeatures(
                geoJson,
                System.IO.Path.GetFileNameWithoutExtension(geoJsonPath));
            var geometryItemCount = features.Sum(feature =>
                feature.Geometry.OuterRings.Count +
                feature.Geometry.LineStrings.Count +
                feature.Geometry.Points.Count);
            var elapsed = DateTime.UtcNow - startedAt;

            Console.WriteLine($"File: {geoJsonPath}");
            Console.WriteLine($"Source SRS: {sourceSrs}");
            Console.WriteLine($"Feature count: {features.Count}");
            Console.WriteLine($"Geometry items: {geometryItemCount}");
            Console.WriteLine($"Elapsed: {elapsed.TotalSeconds:0.###} s");
        }

        private static async Task RunOgcApiFeaturesAsync(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Usage: ogcapi <collection-items-url> [minX,minY,maxX,maxY] [maxFeatures]");
                return;
            }

            var bbox = args.Length > 2 && TryParseBoundingBox(args[2], out var parsedBounds)
                ? parsedBounds
                : null;
            var maxFeatures = args.Length > 3 && int.TryParse(args[3], out var parsedMaxFeatures)
                ? parsedMaxFeatures
                : 0;
            var startedAt = DateTime.UtcNow;
            var client = new OgcApiFeaturesClient();
            var features = await client.LoadFeaturesAsync(args[1], "ogcapi", bbox, maxFeatures);
            var elapsed = DateTime.UtcNow - startedAt;

            Console.WriteLine("OGC API Features probe");
            Console.WriteLine($"URL: {args[1]}");
            Console.WriteLine($"Feature count: {features.Count}");
            Console.WriteLine($"Geometry items: {features.Sum(feature => feature.Geometry.OuterRings.Count + feature.Geometry.LineStrings.Count + feature.Geometry.Points.Count)}");
            Console.WriteLine($"Elapsed: {elapsed.TotalSeconds:0.###} s");
        }

        private static string ResolveSandboxShapefileSrs(string shapefilePath)
        {
            var prjPath = System.IO.Path.ChangeExtension(shapefilePath, ".prj");
            if (!System.IO.File.Exists(prjPath))
            {
                return "EPSG:4326";
            }

            var text = System.IO.File.ReadAllText(prjPath);
            if (text.Contains("25832", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25832";
            }

            if (text.Contains("25833", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:25833";
            }

            if (text.Contains("3857", StringComparison.OrdinalIgnoreCase))
            {
                return "EPSG:3857";
            }

            return "EPSG:4326";
        }

        private static BoundingBox2D ResolveSandboxShapefileBounds(string shapefilePath)
        {
            var sourceSrs = ResolveSandboxShapefileSrs(shapefilePath);
            return sourceSrs == "EPSG:4326"
                ? new BoundingBox2D(-180.0, -90.0, 180.0, 90.0)
                : new BoundingBox2D(double.MinValue / 4.0, double.MinValue / 4.0, double.MaxValue / 4.0, double.MaxValue / 4.0);
        }

        private static bool TryParseBoundingBox(string text, out BoundingBox2D? boundingBox)
        {
            boundingBox = null;
            var parts = text.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length != 4 ||
                !double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minX) ||
                !double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var minY) ||
                !double.TryParse(parts[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var maxX) ||
                !double.TryParse(parts[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var maxY))
            {
                return false;
            }

            boundingBox = new BoundingBox2D(minX, minY, maxX, maxY);
            return true;
        }

        private static async Task RunLod2Async(string[] args)
        {
            if (args.Length > 1 && args.Length < 6)
            {
                Console.WriteLine("Usage: lod2 <GetCapabilities URL> <layer> <max-features> <srs> <minX,minY,maxX,maxY>");
                return;
            }

            var requestOptions = CreateLod2RequestOptions(args);
            var client = new WfsClient();

            try
            {
                Console.WriteLine("Loading LoD2 buildings...");
                Console.WriteLine($"SRS: {requestOptions.SrsName}");
                Console.WriteLine(WfsClient.BuildGetFeatureRequestUrl(requestOptions));
                Console.WriteLine();

                var featureResponse = await client.LoadFeatureResponseAsync(requestOptions);
                var buildings = Lod2GmlReader.ReadBuildings(featureResponse.ResponseText, requestOptions.TypeName);

                Console.WriteLine("Parsed LoD2 buildings:");
                Console.WriteLine();

                for (int i = 0; i < buildings.Count; i++)
                {
                    var building = buildings[i];
                    Console.WriteLine($"Building {i + 1}");
                    Console.WriteLine($"Id: {building.Id}");
                    Console.WriteLine($"Surface count: {building.Surfaces.Count}");
                    Console.WriteLine($"First surface outer point count: {(building.Surfaces.Count > 0 ? building.Surfaces[0].OuterPoints.Count : 0)}");
                    Console.WriteLine($"First surface inner ring count: {(building.Surfaces.Count > 0 ? building.Surfaces[0].InnerRings.Count : 0)}");

                    if (building.Attributes.TryGetValue("HeightAboveGround", out var height))
                    {
                        Console.WriteLine($"HeightAboveGround: {height}");
                    }

                    Console.WriteLine();
                }

                if (buildings.Count == 0)
                {
                    Console.WriteLine("No LoD2 buildings were parsed.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error while loading or parsing the LoD2 response:");
                Console.WriteLine(ex.Message);
            }
        }

        private static WfsRequestOptions CreateRequestOptions(string[] args)
        {
            var options = new WfsRequestOptions
            {
                BaseUrl = "https://planas.frankfurt.de/wfs/bebauungsplaene_rv_flaechennutzung",
                TypeName = "n_bplan_rv",
                SrsName = string.Empty,
                MaxFeatures = 5
            };

            if (args.Length > 0)
            {
                options.BaseUrl = args[0];
            }

            if (args.Length > 1)
            {
                options.TypeName = args[1];
            }

            if (args.Length > 2 && int.TryParse(args[2], out var maxFeatures))
            {
                options.MaxFeatures = maxFeatures;
            }

            if (args.Length > 3)
            {
                options.SrsName = args[3];
            }

            return options;
        }

        private static WfsRequestOptions CreateLod2RequestOptions(string[] args)
        {
            var options = new WfsRequestOptions
            {
                BaseUrl = "https://www.geoportal.hessen.de/mapbender/php/wfs.php?FEATURETYPE_ID=5589&INSPIRE=1&REQUEST=GetCapabilities&SERVICE=WFS&VERSION=2.0.0",
                TypeName = "bu-core3d:Building",
                SrsName = "EPSG:7423",
                MaxFeatures = 1,
                Version = "2.0.0",
                OutputFormat = "application/gml+xml; version=3.2"
            };

            if (args.Length > 1)
            {
                options.BaseUrl = args[1];
            }

            if (args.Length > 2)
            {
                options.TypeName = args[2];
            }

            if (args.Length > 3 && int.TryParse(args[3], out var maxFeatures))
            {
                options.MaxFeatures = maxFeatures;
            }

            if (args.Length > 4)
            {
                options.SrsName = args[4];
            }

            if (args.Length > 5 && TryParseBoundingBox(args[5], out var boundingBox))
            {
                options.BoundingBox = boundingBox;
            }

            return options;
        }

        private static LinearRing? GetFirstOuterRing(WfsFeature feature)
        {
            return feature.Geometry.OuterRings.Count > 0
                ? feature.Geometry.OuterRings[0]
                : null;
        }

        private static string GetAttributeValue(WfsFeature feature, string key)
        {
            return feature.Attributes.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
                ? value
                : "(not available)";
        }
    }
}
