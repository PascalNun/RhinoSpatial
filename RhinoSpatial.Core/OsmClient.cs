using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace RhinoSpatial.Core
{
    public class OsmClient
    {
        private const string CacheSchemaVersion = "osm-v5";
        private static readonly string[] DefaultOverpassUrls =
        {
            "https://overpass-api.de/api/interpreter",
            "https://overpass.private.coffee/api/interpreter"
        };

        private static readonly TimeSpan CacheLifetime = TimeSpan.FromMinutes(5);
        private static readonly ConcurrentDictionary<string, CacheEntry> CacheEntriesByKey = new();

        private static readonly HttpClient SharedHttpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(60)
        };

        private sealed record CacheEntry(OsmDataSet DataSet, DateTimeOffset StoredAtUtc);
        private sealed record QueryBatch(OsmRequestOptions Options, string CategoryLabel, string Label);

        static OsmClient()
        {
            SharedHttpClient.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RhinoSpatial", "1.0"));
            SharedHttpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public async Task<OsmDataSet> LoadDataAsync(OsmRequestOptions options)
        {
            if (!options.IncludeBuildings &&
                !options.IncludeRoads &&
                !options.IncludeWater &&
                !options.IncludeGreen &&
                !options.IncludeRail)
            {
                return CreateEmptyDataSet();
            }

            var cacheKey = BuildCacheKey(options);
            if (TryGetCachedDataSet(cacheKey, allowStale: false, out var freshCachedDataSet))
            {
                return freshCachedDataSet;
            }

            var queryBatches = BuildQueryBatches(options);
            var combinedDataSet = CreateEmptyDataSet();
            string? statusNote = null;
            var failureMessages = new List<string>();
            var unavailableCategories = new List<string>();
            var cachedCategories = new List<string>();

            try
            {
                foreach (var queryBatch in queryBatches)
                {
                    var batchCacheKey = BuildCacheKey(queryBatch.Options);

                    try
                    {
                        var batchResult = await LoadBatchAsync(queryBatch, failureMessages);
                        combinedDataSet = MergeDataSets(combinedDataSet, batchResult.DataSet);
                        CacheEntriesByKey[batchCacheKey] = new CacheEntry(
                            batchResult.DataSet with
                            {
                                StatusNote = string.Empty,
                                UnavailableCategories = new List<string>(),
                                CachedCategories = new List<string>()
                            },
                            DateTimeOffset.UtcNow);

                        if (string.IsNullOrWhiteSpace(statusNote) && !string.IsNullOrWhiteSpace(batchResult.StatusNote))
                        {
                            statusNote = batchResult.StatusNote;
                        }
                    }
                    catch (Exception) when (
                        TryGetCachedDataSet(batchCacheKey, allowStale: true, out var cachedBatchDataSet) ||
                        TryGetCachedCategoryDataSet(cacheKey, queryBatch.CategoryLabel, out cachedBatchDataSet))
                    {
                        combinedDataSet = MergeDataSets(combinedDataSet, cachedBatchDataSet);
                        cachedCategories.Add(queryBatch.CategoryLabel);
                    }
                    catch (Exception)
                    {
                        unavailableCategories.Add(queryBatch.CategoryLabel);
                    }
                }

                if (combinedDataSet.Buildings.Count == 0 &&
                    combinedDataSet.Roads.Count == 0 &&
                    combinedDataSet.WaterAreas.Count == 0 &&
                    combinedDataSet.GreenAreas.Count == 0 &&
                    combinedDataSet.Rails.Count == 0)
                {
                    throw new InvalidOperationException(BuildFailureMessage(failureMessages));
                }

                if (unavailableCategories.Count > 0)
                {
                    var distinctUnavailableCategories = unavailableCategories
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var unavailableNote =
                        $"Some OSM context groups were temporarily unavailable and were skipped: {string.Join(", ", distinctUnavailableCategories)}.";
                    statusNote = string.IsNullOrWhiteSpace(statusNote)
                        ? unavailableNote
                        : $"{statusNote} {unavailableNote}";
                    unavailableCategories = distinctUnavailableCategories;
                }

                if (cachedCategories.Count > 0)
                {
                    cachedCategories = cachedCategories
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var cachedNote =
                        $"Using cached OSM data for: {string.Join(", ", cachedCategories)}.";
                    statusNote = string.IsNullOrWhiteSpace(statusNote)
                        ? cachedNote
                        : $"{statusNote} {cachedNote}";
                }

                var cleanCachedDataSet = combinedDataSet with
                {
                    StatusNote = string.Empty,
                    UnavailableCategories = new List<string>(),
                    CachedCategories = new List<string>()
                };
                CacheEntriesByKey[cacheKey] = new CacheEntry(cleanCachedDataSet, DateTimeOffset.UtcNow);

                if (!string.IsNullOrWhiteSpace(statusNote))
                {
                    return combinedDataSet with
                    {
                        StatusNote = statusNote,
                        UnavailableCategories = unavailableCategories,
                        CachedCategories = cachedCategories
                    };
                }

                return combinedDataSet with
                {
                    UnavailableCategories = unavailableCategories,
                    CachedCategories = cachedCategories
                };
            }
            catch (Exception) when (TryGetCachedDataSet(cacheKey, allowStale: true, out var staleCachedDataSet))
            {
                return staleCachedDataSet with
                {
                    StatusNote = "Using cached OSM result because the live OSM service was temporarily unavailable.",
                    CachedCategories = new List<string>()
                };
            }

            throw new InvalidOperationException(BuildFailureMessage(failureMessages));
        }

        private async Task<(OsmDataSet DataSet, string StatusNote)> LoadBatchAsync(QueryBatch queryBatch, List<string> failureMessages)
        {
            var query = BuildOverpassQuery(queryBatch.Options);
            var candidateBaseUrls = ResolveCandidateBaseUrls(queryBatch.Options.BaseUrl);

            foreach (var candidateBaseUrl in candidateBaseUrls)
            {
                var maxAttempts = ShouldRetryEndpoints(queryBatch.Options.BaseUrl) ? 2 : 1;

                for (var attempt = 0; attempt < maxAttempts; attempt++)
                {
                    try
                    {
                        var responseText = await PostQueryAsync(candidateBaseUrl, query);
                        var dataSet = ParseResponse(responseText, queryBatch.Options);
                        return (dataSet, BuildEndpointStatusNote(candidateBaseUrl, queryBatch.Options.BaseUrl));
                    }
                    catch (Exception ex)
                    {
                        failureMessages.Add($"{queryBatch.Label}: {ex.Message}");

                        if (attempt + 1 < maxAttempts && IsTransientFailure(ex))
                        {
                            await Task.Delay(TimeSpan.FromMilliseconds(250 * (attempt + 1)));
                            continue;
                        }

                        break;
                    }
                }
            }

            throw new InvalidOperationException(BuildFailureMessage(failureMessages));
        }

        private static async Task<string> PostQueryAsync(string baseUrl, string query)
        {
            var resolvedBaseUrl = string.IsNullOrWhiteSpace(baseUrl)
                ? DefaultOverpassUrls[0]
                : baseUrl.Trim();

            using var content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["data"] = query
            });

            using var response = await SharedHttpClient.PostAsync(resolvedBaseUrl, content);
            var responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"The OSM service returned {(int)response.StatusCode} {response.ReasonPhrase}. URL: {resolvedBaseUrl}");
            }

            return responseText;
        }

        private static string BuildOverpassQuery(OsmRequestOptions options)
        {
            var bbox = FormatOverpassBoundingBox(options.BoundingBox4326);
            var builder = new StringBuilder();

            builder.Append("[out:json][timeout:");
            builder.Append(Math.Max(10, options.TimeoutSeconds));
            builder.Append("];(");

            if (options.IncludeBuildings)
            {
                AppendAreaQuery(builder, "way", "\"building\"", bbox);
                AppendAreaQuery(builder, "relation", "\"building\"", bbox);
                AppendAreaQuery(builder, "way", "\"building:part\"", bbox);
                AppendAreaQuery(builder, "relation", "\"building:part\"", bbox);
            }

            if (options.IncludeRoads)
            {
                builder.Append("way[\"highway\"~\"^(motorway|trunk|primary|secondary|tertiary|motorway_link|trunk_link|primary_link|secondary_link|tertiary_link|residential|service|unclassified)$\"]");
                builder.Append(bbox);
                builder.Append(';');
            }

            if (options.IncludeWater)
            {
                AppendAreaQuery(builder, "way", "\"natural\"=\"water\"", bbox);
                AppendAreaQuery(builder, "relation", "\"natural\"=\"water\"", bbox);
                AppendAreaQuery(builder, "way", "\"water\"~\"^(river|canal|lake|pond|reservoir)$\"", bbox);
                AppendAreaQuery(builder, "relation", "\"water\"~\"^(river|canal|lake|pond|reservoir)$\"", bbox);
                AppendAreaQuery(builder, "way", "\"landuse\"=\"reservoir\"", bbox);
                AppendAreaQuery(builder, "relation", "\"landuse\"=\"reservoir\"", bbox);
                AppendAreaQuery(builder, "way", "\"waterway\"=\"riverbank\"", bbox);
                AppendAreaQuery(builder, "relation", "\"waterway\"=\"riverbank\"", bbox);
            }

            if (options.IncludeGreen)
            {
                AppendAreaQuery(builder, "way", "\"landuse\"~\"^(grass|meadow)$\"", bbox);
                AppendAreaQuery(builder, "relation", "\"landuse\"~\"^(grass|meadow)$\"", bbox);
                AppendAreaQuery(builder, "way", "\"leisure\"=\"park\"", bbox);
                AppendAreaQuery(builder, "relation", "\"leisure\"=\"park\"", bbox);
                AppendAreaQuery(builder, "way", "\"natural\"~\"^(wood|grassland)$\"", bbox);
                AppendAreaQuery(builder, "relation", "\"natural\"~\"^(wood|grassland)$\"", bbox);
            }

            if (options.IncludeRail)
            {
                builder.Append("way[\"railway\"~\"^(rail|light_rail|tram|subway|narrow_gauge)$\"]");
                builder.Append(bbox);
                builder.Append(';');
            }

            builder.Append(");out geom;");
            return builder.ToString();
        }

        private static void AppendAreaQuery(StringBuilder builder, string elementType, string filter, string bbox)
        {
            builder.Append(elementType);
            builder.Append('[');
            builder.Append(filter);
            builder.Append(']');
            builder.Append(bbox);
            builder.Append(';');
        }

        private static string FormatOverpassBoundingBox(BoundingBox2D boundingBox)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"({boundingBox.MinY},{boundingBox.MinX},{boundingBox.MaxY},{boundingBox.MaxX})");
        }

        private static OsmDataSet ParseResponse(string responseText, OsmRequestOptions options)
        {
            var trimmedResponse = responseText.TrimStart();
            if (!trimmedResponse.StartsWith("{", StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "The OSM service returned a non-JSON response. Try again in a moment.");
            }

            using var document = JsonDocument.Parse(responseText);

            if (!document.RootElement.TryGetProperty("elements", out var elementsElement) ||
                elementsElement.ValueKind != JsonValueKind.Array)
            {
                return CreateEmptyDataSet();
            }

            var buildings = new List<OsmAreaFeature>();
            var roads = new List<OsmLinearFeature>();
            var waterAreas = new List<OsmAreaFeature>();
            var greenAreas = new List<OsmAreaFeature>();
            var rails = new List<OsmLinearFeature>();

            foreach (var element in elementsElement.EnumerateArray())
            {
                var tags = ReadTags(element);
                if (tags.Count == 0)
                {
                    continue;
                }

                var type = element.TryGetProperty("type", out var typeElement)
                    ? typeElement.GetString() ?? string.Empty
                    : string.Empty;
                var id = element.TryGetProperty("id", out var idElement) && idElement.TryGetInt64(out var parsedId)
                    ? parsedId
                    : 0L;

                if (options.IncludeBuildings && IsBuilding(tags))
                {
                    var rings = ReadAreaRings(element, type);
                    if (rings.Count > 0)
                    {
                        buildings.Add(new OsmAreaFeature(id, rings, new List<LinearRing>(), tags));
                    }

                    continue;
                }

                if (options.IncludeRoads &&
                    tags.TryGetValue("highway", out var highwayValue) &&
                    IsRoad(highwayValue) &&
                    TryReadLineString(element, out var roadCenterLine))
                {
                    roads.Add(new OsmLinearFeature(id, roadCenterLine, tags));
                    continue;
                }

                if (options.IncludeWater)
                {
                    if (IsWaterArea(tags))
                    {
                        var areaGeometry = ReadAreaGeometry(element, type);
                        if (areaGeometry.OuterRings.Count > 0)
                        {
                            waterAreas.Add(new OsmAreaFeature(id, areaGeometry.OuterRings, areaGeometry.InnerRings, tags));
                            continue;
                        }
                    }

                }

                if (options.IncludeGreen && IsGreen(tags))
                {
                    var areaGeometry = ReadAreaGeometry(element, type);
                    if (areaGeometry.OuterRings.Count > 0)
                    {
                        greenAreas.Add(new OsmAreaFeature(id, areaGeometry.OuterRings, areaGeometry.InnerRings, tags));
                        continue;
                    }
                }

                if (options.IncludeRail &&
                    IsRail(tags) &&
                    TryReadLineString(element, out var railCenterLine))
                {
                    rails.Add(new OsmLinearFeature(id, railCenterLine, tags));
                }
            }

            return new OsmDataSet(buildings, roads, waterAreas, greenAreas, rails)
            {
                UnavailableCategories = new List<string>()
            };
        }

        private static OsmDataSet CreateEmptyDataSet()
        {
            return new OsmDataSet(
                new List<OsmAreaFeature>(),
                new List<OsmLinearFeature>(),
                new List<OsmAreaFeature>(),
                new List<OsmAreaFeature>(),
                new List<OsmLinearFeature>())
            {
                UnavailableCategories = new List<string>()
            };
        }

        private static List<QueryBatch> BuildQueryBatches(OsmRequestOptions options)
        {
            if (ShouldUseCombinedBatch(options))
            {
                return new List<QueryBatch>
                {
                    new(options, "Combined", "Combined")
                };
            }

            var batches = new List<QueryBatch>();

            if (options.IncludeBuildings)
            {
                AddCategoryBatches(
                    batches,
                    "Buildings",
                    options with
                    {
                        IncludeBuildings = true,
                        IncludeRoads = false,
                        IncludeWater = false,
                        IncludeGreen = false,
                        IncludeRail = false
                    });
            }

            if (options.IncludeRoads)
            {
                AddCategoryBatches(
                    batches,
                    "Roads",
                    options with
                    {
                        IncludeBuildings = false,
                        IncludeRoads = true,
                        IncludeWater = false,
                        IncludeGreen = false,
                        IncludeRail = false
                    });
            }

            if (options.IncludeWater)
            {
                AddCategoryBatches(
                    batches,
                    "Water",
                    options with
                    {
                        IncludeBuildings = false,
                        IncludeRoads = false,
                        IncludeWater = true,
                        IncludeGreen = false,
                        IncludeRail = false
                    });
            }

            if (options.IncludeGreen)
            {
                AddCategoryBatches(
                    batches,
                    "Green",
                    options with
                    {
                        IncludeBuildings = false,
                        IncludeRoads = false,
                        IncludeWater = false,
                        IncludeGreen = true,
                        IncludeRail = false
                    });
            }

            if (options.IncludeRail)
            {
                AddCategoryBatches(
                    batches,
                    "Rail",
                    options with
                    {
                        IncludeBuildings = false,
                        IncludeRoads = false,
                        IncludeWater = false,
                        IncludeGreen = false,
                        IncludeRail = true
                    });
            }

            return batches;
        }

        private static bool ShouldUseCombinedBatch(OsmRequestOptions options)
        {
            if (options.IncludeWater)
            {
                return false;
            }

            var requestedCategoryCount = CountRequestedCategories(options);
            if (requestedCategoryCount <= 1)
            {
                return false;
            }

            var width = Math.Max(0.0, options.BoundingBox4326.MaxX - options.BoundingBox4326.MinX);
            var height = Math.Max(0.0, options.BoundingBox4326.MaxY - options.BoundingBox4326.MinY);

            // Typical site-scale contexts are faster and usually stable as one
            // combined Overpass query. Larger areas still use the safer split
            // category batching and tiling path.
            return width <= 0.0045 && height <= 0.0045;
        }

        private static int CountRequestedCategories(OsmRequestOptions options)
        {
            var count = 0;

            if (options.IncludeBuildings)
            {
                count++;
            }

            if (options.IncludeRoads)
            {
                count++;
            }

            if (options.IncludeWater)
            {
                count++;
            }

            if (options.IncludeGreen)
            {
                count++;
            }

            if (options.IncludeRail)
            {
                count++;
            }

            return count;
        }

        private static void AddCategoryBatches(List<QueryBatch> target, string categoryLabel, OsmRequestOptions options)
        {
            var tileBoundingBoxes = BuildTileBoundingBoxes(categoryLabel, options.BoundingBox4326);
            if (tileBoundingBoxes.Count == 1)
            {
                target.Add(new QueryBatch(options, categoryLabel, categoryLabel));
                return;
            }

            for (var index = 0; index < tileBoundingBoxes.Count; index++)
            {
                target.Add(new QueryBatch(
                    options with { BoundingBox4326 = tileBoundingBoxes[index] },
                    categoryLabel,
                    $"{categoryLabel} tile {index + 1}/{tileBoundingBoxes.Count}"));
            }
        }

        private static List<BoundingBox2D> BuildTileBoundingBoxes(string categoryLabel, BoundingBox2D boundingBox)
        {
            var width = Math.Max(0.0, boundingBox.MaxX - boundingBox.MinX);
            var height = Math.Max(0.0, boundingBox.MaxY - boundingBox.MinY);
            var maxTileEdge = ResolveTileEdgeLimit(categoryLabel);
            if (width <= maxTileEdge && height <= maxTileEdge)
            {
                return new List<BoundingBox2D> { boundingBox };
            }

            var splitX = Math.Max(1, (int)Math.Ceiling(width / maxTileEdge));
            var splitY = Math.Max(1, (int)Math.Ceiling(height / maxTileEdge));
            splitX = Math.Min(splitX, 4);
            splitY = Math.Min(splitY, 4);

            var tileWidth = width / splitX;
            var tileHeight = height / splitY;
            var tiles = new List<BoundingBox2D>(splitX * splitY);

            for (var y = 0; y < splitY; y++)
            {
                for (var x = 0; x < splitX; x++)
                {
                    var minX = boundingBox.MinX + (tileWidth * x);
                    var minY = boundingBox.MinY + (tileHeight * y);
                    var maxX = x == splitX - 1 ? boundingBox.MaxX : minX + tileWidth;
                    var maxY = y == splitY - 1 ? boundingBox.MaxY : minY + tileHeight;
                    tiles.Add(new BoundingBox2D(minX, minY, maxX, maxY));
                }
            }

            return tiles;
        }

        private static double ResolveTileEdgeLimit(string categoryLabel)
        {
            return categoryLabel switch
            {
                "Green" => 0.0025,
                "Buildings" => 0.0045,
                "Roads" => 0.0045,
                "Water" => 0.0035,
                "Rail" => 0.0060,
                _ => 0.0060
            };
        }

        private static OsmDataSet MergeDataSets(OsmDataSet left, OsmDataSet right)
        {
            return new OsmDataSet(
                MergeById(left.Buildings, right.Buildings),
                MergeById(left.Roads, right.Roads),
                MergeById(left.WaterAreas, right.WaterAreas),
                MergeById(left.GreenAreas, right.GreenAreas),
                MergeById(left.Rails, right.Rails))
            {
                StatusNote = string.IsNullOrWhiteSpace(left.StatusNote)
                    ? right.StatusNote
                    : left.StatusNote
            };
        }

        private static List<TFeature> MergeById<TFeature>(IEnumerable<TFeature> left, IEnumerable<TFeature> right) where TFeature : class
        {
            var mergedById = new Dictionary<long, TFeature>();

            foreach (var feature in left.Concat(right))
            {
                if (feature is OsmAreaFeature areaFeature)
                {
                    mergedById[areaFeature.Id] = (TFeature)(object)areaFeature;
                    continue;
                }

                if (feature is OsmLinearFeature linearFeature)
                {
                    mergedById[linearFeature.Id] = (TFeature)(object)linearFeature;
                }
            }

            return mergedById.Values.ToList();
        }

        private static string BuildCacheKey(OsmRequestOptions options)
        {
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{CacheSchemaVersion}|{NormalizeBaseUrlForCache(options.BaseUrl)}|" +
                $"{options.BoundingBox4326.MinX:F6},{options.BoundingBox4326.MinY:F6},{options.BoundingBox4326.MaxX:F6},{options.BoundingBox4326.MaxY:F6}|" +
                $"b:{options.IncludeBuildings}|r:{options.IncludeRoads}|w:{options.IncludeWater}|g:{options.IncludeGreen}|rail:{options.IncludeRail}");
        }

        private static string NormalizeBaseUrlForCache(string? baseUrl)
        {
            return UsesBuiltInDefaultEndpoints(baseUrl)
                ? "default-overpass"
                : baseUrl!.Trim();
        }

        private static bool TryGetCachedDataSet(string cacheKey, bool allowStale, out OsmDataSet dataSet)
        {
            dataSet = default!;

            if (!CacheEntriesByKey.TryGetValue(cacheKey, out var entry))
            {
                return false;
            }

            if (!allowStale && DateTimeOffset.UtcNow - entry.StoredAtUtc > CacheLifetime)
            {
                return false;
            }

            dataSet = entry.DataSet;
            return true;
        }

        private static bool TryGetCachedCategoryDataSet(string cacheKey, string label, out OsmDataSet dataSet)
        {
            dataSet = default!;

            if (!TryGetCachedDataSet(cacheKey, allowStale: true, out var cachedDataSet))
            {
                return false;
            }

            dataSet = label switch
            {
                "Buildings" when cachedDataSet.Buildings.Count > 0 => CreateEmptyDataSet() with
                {
                    Buildings = cachedDataSet.Buildings
                },
                "Roads" when cachedDataSet.Roads.Count > 0 => CreateEmptyDataSet() with
                {
                    Roads = cachedDataSet.Roads
                },
                "Water" when cachedDataSet.WaterAreas.Count > 0 => CreateEmptyDataSet() with
                {
                    WaterAreas = cachedDataSet.WaterAreas
                },
                "Green" when cachedDataSet.GreenAreas.Count > 0 => CreateEmptyDataSet() with
                {
                    GreenAreas = cachedDataSet.GreenAreas
                },
                "Rail" when cachedDataSet.Rails.Count > 0 => CreateEmptyDataSet() with
                {
                    Rails = cachedDataSet.Rails
                },
                _ => default!
            };

            return dataSet is not null;
        }

        private static IReadOnlyList<string> ResolveCandidateBaseUrls(string? baseUrl)
        {
            if (UsesBuiltInDefaultEndpoints(baseUrl))
            {
                return DefaultOverpassUrls;
            }

            if (!string.IsNullOrWhiteSpace(baseUrl) &&
                Uri.TryCreate(baseUrl.Trim(), UriKind.Absolute, out var uri) &&
                (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)))
            {
                return new[] { baseUrl.Trim() };
            }

            return DefaultOverpassUrls;
        }

        private static bool ShouldRetryEndpoints(string? baseUrl)
        {
            return UsesBuiltInDefaultEndpoints(baseUrl);
        }

        private static bool IsTransientFailure(Exception ex)
        {
            return ex switch
            {
                TaskCanceledException => true,
                TimeoutException => true,
                HttpRequestException httpRequestException => IsTransientHttpFailure(httpRequestException.Message),
                _ => false
            };
        }

        private static bool IsTransientHttpFailure(string message)
        {
            return message.Contains(" 408 ", StringComparison.Ordinal) ||
                   message.Contains(" 429 ", StringComparison.Ordinal) ||
                   message.Contains(" 500 ", StringComparison.Ordinal) ||
                   message.Contains(" 502 ", StringComparison.Ordinal) ||
                   message.Contains(" 503 ", StringComparison.Ordinal) ||
                   message.Contains(" 504 ", StringComparison.Ordinal) ||
                   message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildEndpointStatusNote(string resolvedBaseUrl, string? requestedBaseUrl)
        {
            if (!UsesBuiltInDefaultEndpoints(requestedBaseUrl))
            {
                return string.Empty;
            }

            if (string.Equals(resolvedBaseUrl, DefaultOverpassUrls[0], StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            return TryGetHostLabel(resolvedBaseUrl, out var hostLabel)
                ? $"Loaded OSM context from fallback public OSM service: {hostLabel}."
                : "Loaded OSM context from a fallback public OSM service.";
        }

        private static bool TryGetHostLabel(string url, out string hostLabel)
        {
            hostLabel = string.Empty;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            hostLabel = uri.Host;
            return !string.IsNullOrWhiteSpace(hostLabel);
        }

        private static bool UsesBuiltInDefaultEndpoints(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return true;
            }

            var trimmedBaseUrl = baseUrl.Trim();
            foreach (var defaultOverpassUrl in DefaultOverpassUrls)
            {
                if (string.Equals(trimmedBaseUrl, defaultOverpassUrl, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string BuildFailureMessage(IReadOnlyList<string> failureMessages)
        {
            var lastMessage = failureMessages.Count > 0
                ? failureMessages[^1]
                : "The OSM service did not return data.";

            return
                "RhinoSpatial could not reach the public OSM service after retrying the built-in endpoints. " +
                $"Try again in a moment or use a smaller Spatial Context. Last error: {lastMessage}";
        }

        private static Dictionary<string, string?> ReadTags(JsonElement element)
        {
            var tags = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

            if (!element.TryGetProperty("tags", out var tagsElement) || tagsElement.ValueKind != JsonValueKind.Object)
            {
                return tags;
            }

            foreach (var property in tagsElement.EnumerateObject())
            {
                tags[property.Name] = property.Value.GetString();
            }

            return tags;
        }

        private static bool TryReadLineString(JsonElement element, out LineString lineString)
        {
            lineString = new LineString(new List<Coordinate2D>());

            if (!element.TryGetProperty("geometry", out var geometryElement) || geometryElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var points = ReadCoordinates(geometryElement, closeRing: false);
            if (points.Count < 2)
            {
                return false;
            }

            lineString = new LineString(points);
            return true;
        }

        private static (List<LinearRing> OuterRings, List<LinearRing> InnerRings) ReadAreaGeometry(JsonElement element, string type)
        {
            var outerRings = ReadAreaRings(element, type, includeInnerRings: false);
            var innerRings = ReadAreaRings(element, type, includeInnerRings: true);
            return (outerRings, innerRings);
        }

        private static List<LinearRing> ReadAreaRings(JsonElement element, string type, bool includeInnerRings = false)
        {
            var rings = new List<LinearRing>();

            if (string.Equals(type, "way", StringComparison.OrdinalIgnoreCase))
            {
                if (includeInnerRings)
                {
                    return rings;
                }

                if (element.TryGetProperty("geometry", out var geometryElement) && geometryElement.ValueKind == JsonValueKind.Array)
                {
                    var points = ReadCoordinates(geometryElement, closeRing: true);
                    if (points.Count >= 4)
                    {
                        rings.Add(new LinearRing(points));
                    }
                }

                return rings;
            }

            if (!string.Equals(type, "relation", StringComparison.OrdinalIgnoreCase) ||
                !element.TryGetProperty("members", out var membersElement) ||
                membersElement.ValueKind != JsonValueKind.Array)
            {
                return rings;
            }

            var relationSegments = new List<List<Coordinate2D>>();

            foreach (var member in membersElement.EnumerateArray())
            {
                if (!member.TryGetProperty("type", out var memberTypeElement) ||
                    !string.Equals(memberTypeElement.GetString(), "way", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var isInnerRing = member.TryGetProperty("role", out var roleElement) &&
                    string.Equals(roleElement.GetString(), "inner", StringComparison.OrdinalIgnoreCase);
                if (isInnerRing != includeInnerRings)
                {
                    continue;
                }

                if (!member.TryGetProperty("geometry", out var memberGeometry) || memberGeometry.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var points = ReadCoordinates(memberGeometry, closeRing: false);
                if (points.Count >= 2)
                {
                    relationSegments.Add(points);
                }
            }

            return AssembleRelationRings(relationSegments);
        }

        private static List<LinearRing> AssembleRelationRings(List<List<Coordinate2D>> segments)
        {
            var assembledRings = new List<LinearRing>();
            var remaining = new List<List<Coordinate2D>>(segments);

            while (remaining.Count > 0)
            {
                var current = new List<Coordinate2D>(remaining[0]);
                remaining.RemoveAt(0);

                var merged = true;
                while (merged)
                {
                    merged = false;

                    for (var index = 0; index < remaining.Count; index++)
                    {
                        if (!TryMergeRingSegment(current, remaining[index]))
                        {
                            continue;
                        }

                        remaining.RemoveAt(index);
                        merged = true;
                        break;
                    }
                }

                if (current.Count < 3)
                {
                    continue;
                }

                if (!CoordinatesMatch(current[0], current[^1]))
                {
                    current.Add(current[0]);
                }

                if (current.Count >= 4)
                {
                    assembledRings.Add(new LinearRing(current));
                }
            }

            return assembledRings;
        }

        private static bool TryMergeRingSegment(List<Coordinate2D> current, List<Coordinate2D> candidate)
        {
            if (CoordinatesMatch(current[^1], candidate[0]))
            {
                current.AddRange(candidate.GetRange(1, candidate.Count - 1));
                return true;
            }

            if (CoordinatesMatch(current[^1], candidate[^1]))
            {
                candidate.Reverse();
                current.AddRange(candidate.GetRange(1, candidate.Count - 1));
                return true;
            }

            if (CoordinatesMatch(current[0], candidate[^1]))
            {
                current.InsertRange(0, candidate.GetRange(0, candidate.Count - 1));
                return true;
            }

            if (CoordinatesMatch(current[0], candidate[0]))
            {
                candidate.Reverse();
                current.InsertRange(0, candidate.GetRange(0, candidate.Count - 1));
                return true;
            }

            return false;
        }

        private static bool CoordinatesMatch(Coordinate2D left, Coordinate2D right)
        {
            const double epsilon = 1e-8;
            return Math.Abs(left.X - right.X) <= epsilon &&
                   Math.Abs(left.Y - right.Y) <= epsilon;
        }

        private static List<Coordinate2D> ReadCoordinates(JsonElement geometryElement, bool closeRing)
        {
            var points = new List<Coordinate2D>();

            foreach (var coordinateElement in geometryElement.EnumerateArray())
            {
                if (!coordinateElement.TryGetProperty("lon", out var lonElement) ||
                    !coordinateElement.TryGetProperty("lat", out var latElement) ||
                    !lonElement.TryGetDouble(out var lon) ||
                    !latElement.TryGetDouble(out var lat))
                {
                    continue;
                }

                points.Add(new Coordinate2D(lon, lat));
            }

            if (closeRing && points.Count >= 3)
            {
                var first = points[0];
                var last = points[^1];
                if (Math.Abs(first.X - last.X) > 1e-9 || Math.Abs(first.Y - last.Y) > 1e-9)
                {
                    points.Add(first);
                }
            }

            return points;
        }

        private static bool IsBuilding(IReadOnlyDictionary<string, string?> tags)
        {
            return HasNonEmptyTag(tags, "building") || HasNonEmptyTag(tags, "building:part");
        }

        private static bool IsRoad(string? highwayValue)
        {
            if (string.IsNullOrWhiteSpace(highwayValue))
            {
                return false;
            }

            return highwayValue switch
            {
                "motorway" => true,
                "trunk" => true,
                "primary" => true,
                "secondary" => true,
                "tertiary" => true,
                "motorway_link" => true,
                "trunk_link" => true,
                "primary_link" => true,
                "secondary_link" => true,
                "tertiary_link" => true,
                "residential" => true,
                "service" => true,
                "unclassified" => true,
                _ => false
            };
        }

        private static bool IsWaterArea(IReadOnlyDictionary<string, string?> tags)
        {
            if (IsHiddenOrIntermittentWater(tags))
            {
                return false;
            }

            if (HasTagValue(tags, "natural", "water") ||
                HasTagValue(tags, "waterway", "riverbank"))
            {
                return true;
            }

            if (tags.TryGetValue("water", out var waterValue) && IsSupportedWaterValue(waterValue))
            {
                return true;
            }

            return HasTagValue(tags, "landuse", "reservoir");
        }

        private static bool IsHiddenOrIntermittentWater(IReadOnlyDictionary<string, string?> tags)
        {
            if (HasTagValue(tags, "intermittent", "yes") ||
                HasTagValue(tags, "seasonal", "yes") ||
                HasTagValue(tags, "covered", "yes") ||
                HasTagValue(tags, "location", "underground") ||
                HasTagValue(tags, "location", "underwater"))
            {
                return true;
            }

            if (tags.TryGetValue("tunnel", out var tunnelValue) && !string.IsNullOrWhiteSpace(tunnelValue) && !HasTagValue(tags, "tunnel", "no"))
            {
                return true;
            }

            return HasTagValue(tags, "waterway", "ditch") ||
                   HasTagValue(tags, "waterway", "drain") ||
                   HasTagValue(tags, "waterway", "culvert");
        }

        private static bool IsSupportedWaterValue(string? waterValue)
        {
            return waterValue switch
            {
                "river" => true,
                "canal" => true,
                "lake" => true,
                "pond" => true,
                "reservoir" => true,
                _ => false
            };
        }

        private static bool IsGreen(IReadOnlyDictionary<string, string?> tags)
        {
            if (tags.TryGetValue("landuse", out var landuseValue))
            {
                switch (landuseValue)
                {
                    case "grass":
                    case "meadow":
                        return true;
                }
            }

            if (HasTagValue(tags, "leisure", "park"))
            {
                return true;
            }

            if (tags.TryGetValue("natural", out var naturalValue))
            {
                switch (naturalValue)
                {
                    case "wood":
                    case "grassland":
                        return true;
                }
            }

            return false;
        }

        private static bool IsRail(IReadOnlyDictionary<string, string?> tags)
        {
            if (!tags.TryGetValue("railway", out var railwayValue) || string.IsNullOrWhiteSpace(railwayValue))
            {
                return false;
            }

            if (HasTagValue(tags, "location", "underground") ||
                HasTagValue(tags, "location", "underwater") ||
                HasTagValue(tags, "covered", "yes"))
            {
                return false;
            }

            if (tags.TryGetValue("tunnel", out var tunnelValue) &&
                !string.IsNullOrWhiteSpace(tunnelValue) &&
                !HasTagValue(tags, "tunnel", "no"))
            {
                return false;
            }

            return railwayValue switch
            {
                "rail" => true,
                "light_rail" => true,
                "tram" => true,
                "narrow_gauge" => true,
                _ => false
            };
        }

        private static bool HasTagValue(IReadOnlyDictionary<string, string?> tags, string key, string expectedValue)
        {
            return tags.TryGetValue(key, out var rawValue) &&
                   string.Equals(rawValue, expectedValue, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasNonEmptyTag(IReadOnlyDictionary<string, string?> tags, string key)
        {
            return tags.TryGetValue(key, out var rawValue) && !string.IsNullOrWhiteSpace(rawValue);
        }
    }
}
