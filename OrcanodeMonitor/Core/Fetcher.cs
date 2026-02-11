// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Mono.TextTemplating;
using OrcanodeMonitor.Api;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Dynamic;
using System.Net;
using System.Text.Json;

namespace OrcanodeMonitor.Core
{
    public class Fetcher
    {
        private static TimeZoneInfo _pacificTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        private static HttpClient _realHttpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        private static HttpClient _httpClient = _realHttpClient;
        public static HttpClient HttpClient => _httpClient;
        private static string _orcasoundProdSite = "live.orcasound.net";
        private static string _orcasoundFeedsUrlPath = "/api/json/feeds";
        private static DateTime _unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        private static string _iftttServiceKey = string.Empty;
        public static bool IsReadOnly = false;
        public static bool IsOffline = false;
        public static string IftttServiceKey => _iftttServiceKey;
        private static IConfiguration? _config = null;
        public static string? GetConfig(string key) => _config?[key] ?? null;
        public static void Initialize(IConfiguration config, HttpClient? httpClient, ILogger logger)
        {
            _config = config;
            OrcaHelloFetcher.Initialize(logger);
            MezmoFetcher.Initialize(httpClient);
            _iftttServiceKey = _config?["IFTTT_SERVICE_KEY"] ?? "<unknown>";
            if (httpClient != null)
            {
                _httpClient = httpClient;
            }
            else
            {
                _httpClient = _realHttpClient;
            }
        }

        public static void Uninitialize()
        {
            _httpClient = _realHttpClient;
            MezmoFetcher.Uninitialize();
        }

        public static IConfiguration? Configuration => _config;

        /// <summary>
        /// Test for a match between a human-readable name at Orcasound, and
        /// a device name at Duplicity.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="dataplicityName"></param>
        /// <returns></returns>
        private static bool NameMatch(string name, string dataplicityName)
        {
            // Look for the result inside the Dataplicity name.
            if (dataplicityName.Contains(name))
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Find a node using the serial number value at Dataplicity.
        /// </summary>
        /// <param name="nodes">Database table to look in and potentially update</param>
        /// <param name="serial">Dataplicity serial number to look for</param>
        /// <param name="connectionStatus">Dataplicity connection status</param>
        /// <returns></returns>
        public static Orcanode? FindOrcanodeByDataplicitySerial(List<Orcanode> nodes, string serial, out OrcanodeOnlineStatus connectionStatus)
        {
            foreach (Orcanode node in nodes)
            {
                if (node.DataplicitySerial == serial)
                {
                    connectionStatus = node.DataplicityConnectionStatus;
                    return node;
                }
            }

            connectionStatus = OrcanodeOnlineStatus.Absent;
            return null;
        }

        /// <summary>
        /// Create a node.
        /// </summary>
        /// <param name="nodeList">Database table to update</param>
        /// <returns></returns>
        private static Orcanode CreateOrcanode(DbSet<Orcanode> nodeList)
        {
            var newNode = new Orcanode()
            {
                ID = Guid.NewGuid().ToString(),
                PartitionValue = 1
            };

            nodeList.Add(newNode);
            return newNode;
        }

        /// <summary>
        /// Find or create a node using the serial number value at Dataplicity.
        /// </summary>
        /// <param name="nodeList">Database table to look in and potentially update</param>
        /// <param name="serial">Dataplicity serial number to look for</param>
        /// <param name="connectionStatus">Returns the dataplicity connection status</param>
        /// <returns></returns>
        public static Orcanode FindOrCreateOrcanodeByDataplicitySerial(DbSet<Orcanode> nodeList, string serial, out OrcanodeOnlineStatus connectionStatus)
        {
            Orcanode? node = FindOrcanodeByDataplicitySerial(nodeList.ToList(), serial, out connectionStatus);
            if (node != null)
            {
                return node;
            }

            connectionStatus = OrcanodeOnlineStatus.Absent;
            Orcanode newNode = CreateOrcanode(nodeList);
            newNode.DataplicitySerial = serial;
            return newNode;
        }

        public static Orcanode? FindOrcanodeByOrcasoundFeedId(List<Orcanode> nodes, string feedId)
        {
            foreach (Orcanode node in nodes)
            {
                if (node.OrcasoundFeedId == feedId)
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>
        /// Look for an Orcanode by Orcasound name in a list.
        /// </summary>
        /// <param name="nodes">Orcanode list to look in</param>
        /// <param name="orcasoundName">Name to look for</param>
        /// <returns>Node found</returns>
        private static Orcanode? FindOrcanodeByOrcasoundName(List<Orcanode> nodes, string orcasoundName)
        {
            foreach (Orcanode node in nodes)
            {
                if (node.OrcasoundName == orcasoundName)
                {
                    return node;
                }

                // See if we can match a node name derived from dataplicity.
                if (node.OrcasoundName.IsNullOrEmpty() && orcasoundName.Contains(node.DisplayName))
                {
                    node.OrcasoundName = orcasoundName;
                    return node;
                }
            }

            return null;
        }

        /// <summary>
        /// Get Orcasound data
        /// </summary>
        /// <param name="context">Database context</param>
        /// <param name="site">Orcasound site to query</param>
        /// <param name="logger">Logger</param>
        /// <returns>null on error, or JsonElement on success</returns>
        private async static Task<JsonElement?> GetOrcasoundDataAsync(IOrcanodeMonitorContext context, string site, ILogger logger)
        {
            string url = "https://" + site + _orcasoundFeedsUrlPath;
            try
            {
                string json = await _httpClient.GetStringAsync(url);
                if (json.IsNullOrEmpty())
                {
                    // Error.
                    return null;
                }
                dynamic response = JsonSerializer.Deserialize<ExpandoObject>(json);
                if (response == null)
                {
                    // Error.
                    logger.LogError("Couldn't deserialize JSON in GetOrcasoundDataAsync");
                    return null;
                }
                JsonElement dataArray = response.data;
                if (dataArray.ValueKind != JsonValueKind.Array)
                {
                    // Error.
                    logger.LogError($"Invalid dataArray kind in GetOrcasoundDataAsync: {dataArray.ValueKind}");
                    return null;
                }
                return dataArray;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in GetOrcasoundDataAsync: {ex.Message}");
                return null;
            }
        }

        private static void UpdateOrcasoundNode(JsonElement feed, List<Orcanode> foundList, List<Orcanode> unfoundList, IOrcanodeMonitorContext context, string site, ILogger logger)
        {
            if (!feed.TryGetProperty("id", out var feedId))
            {
                logger.LogError($"Missing id in UpdateOrcasoundNode");
                return;
            }
            if (!feed.TryGetProperty("attributes", out JsonElement attributes))
            {
                logger.LogError($"Missing attributes in UpdateOrcasoundNode");
                return;
            }
            if (!attributes.TryGetProperty("name", out var name))
            {
                logger.LogError($"Missing name in UpdateOrcasoundNode");
                return;
            }
            string orcasoundName = name.ToString();
            if (!attributes.TryGetProperty("dataplicity_id", out var dataplicity_id))
            {
                logger.LogError($"Missing dataplicity_id in UpdateOrcasoundNode");
                return;
            }
            bool hidden = false;
            if (attributes.TryGetProperty("hidden", out var hiddenElement))
            {
                hidden = hiddenElement.GetBoolean();
            }
            string dataplicitySerial = dataplicity_id.ToString();

            // Remove the found node from the unfound list.
            Orcanode? oldListNode = unfoundList.Find(a => a.OrcasoundFeedId == feedId.ToString());
            if (oldListNode != null)
            {
                unfoundList.Remove(oldListNode);
            }

            Orcanode? node = null;
            node = FindOrcanodeByOrcasoundFeedId(foundList, feedId.ToString());
            if (node == null)
            {
                // We didn't used to store the feed ID, only the name, so try again by name.
                Orcanode? possibleNode = FindOrcanodeByOrcasoundName(foundList, orcasoundName);
                if ((possibleNode != null) && possibleNode.OrcasoundFeedId.IsNullOrEmpty())
                {
                    node = possibleNode;
                    oldListNode = unfoundList.Find(a => a.OrcasoundName == orcasoundName);
                    if (oldListNode != null)
                    {
                        unfoundList.Remove(oldListNode);
                    }
                }
            }

            // See if we can find a node by dataplicity ID, so that if a node
            // shows up in dataplicity first and Orcasite later, we don't create a
            // duplicate entry.
            Orcanode? dataplicityNode = null;
            if (!dataplicitySerial.IsNullOrEmpty())
            {
                dataplicityNode = FindOrcanodeByDataplicitySerial(foundList, dataplicitySerial, out OrcanodeOnlineStatus oldStatus);
                if (dataplicityNode != null)
                {
                    if (node == null)
                    {
                        node = dataplicityNode;
                    }
                    else if (node != dataplicityNode)
                    {
                        // We have duplicate nodes to merge. In theory we shouldn't have any
                        // node state for the dataplicity-only node. (TODO: verify this)
                        logger.LogWarning($"Merging duplicate nodes for {node.DataplicitySerial}");
                        node.DataplicityDescription = dataplicityNode.DataplicityDescription;
                        node.DataplicityName = dataplicityNode.DataplicityName;
                        node.DataplicityOnline = dataplicityNode.DataplicityOnline;
                        node.AgentVersion = dataplicityNode.AgentVersion;
                        node.DiskCapacity = dataplicityNode.DiskCapacity;
                        node.DiskUsed = dataplicityNode.DiskUsed;
                        node.DataplicityUpgradeAvailable = dataplicityNode.DataplicityUpgradeAvailable;
                        context.Orcanodes.Remove(dataplicityNode);
                    }
                }
            }

            if (node == null)
            {
                node = CreateOrcanode(context.Orcanodes);
                node.OrcasoundName = name.ToString();
            }

            if (!dataplicitySerial.IsNullOrEmpty())
            {
                if (!node.DataplicitySerial.IsNullOrEmpty() && dataplicitySerial != node.DataplicitySerial)
                {
                    // TODO: The orcasound entry for the node changed its dataplicity_id.
                    logger.LogWarning($"dataplicity_id changed for {node.DisplayName} from {node.DataplicitySerial} to {dataplicitySerial}");
                }
                node.DataplicitySerial = dataplicitySerial;
            }

            if (node.OrcasoundFeedId.IsNullOrEmpty())
            {
                node.OrcasoundFeedId = feedId.ToString();
            }
            if (!site.IsNullOrEmpty())
            {
                node.OrcasoundHost = site;
            }
            if (orcasoundName != node.OrcasoundName)
            {
                // We just detected a name change.
                node.OrcasoundName = orcasoundName;
            }
            if (attributes.TryGetProperty("node_name", out var nodeName))
            {
                node.S3NodeName = nodeName.ToString();
            }
            if (attributes.TryGetProperty("bucket", out var bucket))
            {
                node.S3Bucket = bucket.ToString();
            }
            if (attributes.TryGetProperty("slug", out var slug))
            {
                node.OrcasoundSlug = slug.ToString();
            }
            if (attributes.TryGetProperty("visible", out var visible))
            {
                node.OrcasoundVisible = visible.GetBoolean();
            }
        }

        private async static Task UpdateOrcasoundDataAsync(IOrcanodeMonitorContext context, string site, List<Orcanode> foundList, List<Orcanode> unfoundList, ILogger logger)
        {
            JsonElement? dataArray = await GetOrcasoundDataAsync(context, site, logger);
            if (dataArray.HasValue)
            {
                foreach (JsonElement feed in dataArray.Value.EnumerateArray())
                {
                    UpdateOrcasoundNode(feed, foundList, unfoundList, context, site, logger);
                }
            }
        }

        /// <summary>
        /// Saves changes to the database if the application is not in read-only mode.
        /// </summary>
        /// <param name="context">The database context to save changes for.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public static async Task SaveChangesAsync(IOrcanodeMonitorContext context)
        {
            if (!IsReadOnly)
            {
                await context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Update the current list of Orcanodes using data from orcasound.net.
        /// </summary>
        /// <param name="context">Database context to update</param>
        /// <param name="logger">Logger</param>
        /// <returns></returns>
        public async static Task UpdateOrcasoundDataAsync(IOrcanodeMonitorContext context, ILogger logger)
        {
            try
            {
                var foundList = await context.Orcanodes.ToListAsync();

                // Create a list to track what nodes are no longer returned.
                var unfoundList = foundList.ToList();

                await UpdateOrcasoundDataAsync(context, _orcasoundProdSite, foundList, unfoundList, logger);

                // Mark any remaining unfound nodes as absent.
                foreach (var unfoundNode in unfoundList)
                {
                    var oldNode = FindOrcanodeByOrcasoundFeedId(foundList, unfoundNode.OrcasoundFeedId);
                    if (oldNode != null)
                    {
                        oldNode.OrcasoundFeedId = String.Empty;
                    }
                }

                MonitorState.GetFrom(context).LastUpdatedTimestampUtc = DateTime.UtcNow;
                await SaveChangesAsync(context);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in UpdateOrcasoundDataAsync: {ex.Message}");
            }
        }

        public async static Task UpdateS3DataAsync(IOrcanodeMonitorContext context, ILogger logger)
        {
            try
            {
                List<Orcanode> nodes = await context.Orcanodes.ToListAsync();
                foreach (Orcanode node in nodes)
                {
                    if (!node.S3NodeName.IsNullOrEmpty())
                    {
                        await Fetcher.UpdateS3DataAsync(context, node, logger);
                    }
                }

                MonitorState.GetFrom(context).LastUpdatedTimestampUtc = DateTime.UtcNow;
                await SaveChangesAsync(context);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in UpdateS3DataAsync: {ex.Message}");
            }
        }

        /// <summary>
        /// Convert a unix timestamp in integer form to a DateTime value in UTC.
        /// </summary>
        /// <param name="unixTimeStamp">Unix timestamp</param>
        /// <returns>DateTime value or null on failure</returns>
        public static DateTime? UnixTimeStampToDateTimeUtc(long unixTimeStamp)
        {
            // A Unix timestamp is a count of seconds past the Unix epoch.
            DateTime dateTime = _unixEpoch.AddSeconds(unixTimeStamp);
            return dateTime;
        }

        /// <summary>
        /// Convert a unix timestamp in integer form to a DateTime value in local time.
        /// </summary>
        /// <param name="unixTimeStamp">Unix timestamp</param>
        /// <returns>DateTime value or null on failure</returns>
        public static DateTime? UnixTimeStampToDateTimeLocal(long unixTimeStamp)
        {
            return UtcToLocalDateTime(UnixTimeStampToDateTimeUtc(unixTimeStamp));
        }

        public static DateTime? UtcToLocalDateTime(DateTime? utcDateTime)
        {
            if (!utcDateTime.HasValue)
            {
                return null;
            }
            if (utcDateTime.Value.Kind == DateTimeKind.Unspecified)
            {
                utcDateTime = DateTime.SpecifyKind(utcDateTime.Value, DateTimeKind.Utc);
            }
            DateTime localDateTime = TimeZoneInfo.ConvertTime(utcDateTime.Value, _pacificTimeZone);
            return localDateTime;
        }

        /// <summary>
        /// Convert a unix timestamp in string form to a DateTime value in UTC.
        /// </summary>
        /// <param name="unixTimeStampString">Unix timestamp string to parse</param>
        /// <returns>DateTime value or null on failure</returns>
        private static DateTime? UnixTimeStampStringToDateTimeUtc(string unixTimeStampString)
        {
            if (!long.TryParse(unixTimeStampString, out var unixTimeStamp))
            {
                return null;
            }

            return UnixTimeStampToDateTimeUtc(unixTimeStamp);
        }

        public static long DateTimeToUnixTimeStamp(DateTime dateTime)
        {
            DateTime utcTime = dateTime.ToUniversalTime();
            long unixTime = (long)(utcTime - _unixEpoch).TotalSeconds;
            return unixTime;
        }

        public class TimestampResult
        {
            public string UnixTimestampString { get; }
            public DateTimeOffset? Offset { get; }
            public TimestampResult(string unixTimestampString, DateTimeOffset? offset)
            {
                UnixTimestampString = unixTimestampString;
                Offset = offset;
            }
        }

        public async static Task<TimestampResult?> GetLatestS3TimestampAsync(Orcanode node, bool updateNode, ILogger logger)
        {
            string url = "https://" + node.S3Bucket + ".s3.amazonaws.com/" + node.S3NodeName + "/latest.txt";
            using HttpResponseMessage response = await _httpClient.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogError($"{node.S3NodeName} not found on S3");

                // Absent.
                if (updateNode)
                {
                    node.LatestRecordedUtc = null;
                }
                return null;
            }
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                logger.LogError($"{node.S3NodeName} got access denied on S3");

                // Access denied.
                if (updateNode)
                {
                    node.LatestRecordedUtc = DateTime.MinValue;
                }
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError($"{node.S3NodeName} got status {response.StatusCode} on S3");

                return null;
            }

            string content = await response.Content.ReadAsStringAsync();
            string unixTimestampString = content.TrimEnd();
            var result = new TimestampResult(unixTimestampString, response.Content.Headers.LastModified);
            return result;
        }

        /// <summary>
        /// Update the timestamps for a given Orcanode by querying files on S3.
        /// </summary>
        /// <param name="context">Database context</param>
        /// <param name="node">Orcanode to update</param>
        /// <param name="logger">Logger</param>
        /// <returns></returns>
        public async static Task UpdateS3DataAsync(IOrcanodeMonitorContext context, Orcanode node, ILogger logger)
        {
            OrcanodeOnlineStatus oldStatus = node.S3StreamStatus;
            TimestampResult? result = await GetLatestS3TimestampAsync(node, true, logger);
            if (result == null)
            {
                OrcanodeOnlineStatus newStatus = node.S3StreamStatus;
                if (newStatus != oldStatus)
                {
                    // Log event if it just went absent. Other events will be logged
                    // inside the call to UpdateManifestTimestampAsync() below.
                    AddHydrophoneStreamStatusEvent(context, logger, node, null);
                }
                return;
            }
            string unixTimestampString = result.UnixTimestampString;
            DateTime? latestRecorded = UnixTimeStampStringToDateTimeUtc(unixTimestampString);
            if (latestRecorded.HasValue)
            {
                node.LatestRecordedUtc = latestRecorded?.ToUniversalTime();

                DateTimeOffset? offset = result.Offset;
                if (offset.HasValue)
                {
                    node.LatestUploadedUtc = offset.Value.UtcDateTime;
                }
            }

            await UpdateManifestTimestampAsync(context, node, unixTimestampString, logger);
        }

        /// <summary>
        /// Get a list of the most recent events in order from most to least recent,
        /// up to a maximum of 'limit' events.
        /// </summary>
        /// <param name="context">Database context</param>
        /// <param name="limit">Maximum number of events to return</param>
        /// <returns>List of events</returns>
        public static async Task<List<OrcanodeEvent>> GetEventsAsync(IOrcanodeMonitorContext context, int limit)
        {
            List<OrcanodeEvent> orcanodeEvents = await context.OrcanodeEvents.OrderByDescending(e => e.DateTimeUtc).Take(limit).ToListAsync();
            return orcanodeEvents;
        }

        /// <summary>
        /// Get recent events.
        /// </summary>
        /// <param name="context">Database context</param>
        /// <param name="since">Time to get events since</param>
        /// <param name="logger">Logger</param>
        /// <returns>null on error, or list of events on success</returns>
        public static async Task<List<OrcanodeEvent>?> GetRecentEventsAsync(IOrcanodeMonitorContext context, DateTime since, ILogger logger)
        {
            try
            {
                List<OrcanodeEvent> events = await context.OrcanodeEvents.Where(e => e.DateTimeUtc >= since).OrderByDescending(e => e.DateTimeUtc).ToListAsync();
                return events;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to fetch recent events");
                return null;
            }
        }

        private static List<OrcanodeEvent> AddOlderEvent(List<OrcanodeEvent> orcanodeEvents, List<OrcanodeEvent> events, DateTime since, string type)
        {
            OrcanodeEvent? olderEvent = events.Where(e => (e.DateTimeUtc < since) && (e.Type == type)).FirstOrDefault();
            if (olderEvent == null)
            {
                return orcanodeEvents;
            }
            else
            {
                return orcanodeEvents.Append(olderEvent).OrderByDescending(e => e.DateTimeUtc).ToList();
            }
        }

        /// <summary>
        /// Get recent events for a node.
        /// </summary>
        /// <param name="context">database context</param>
        /// <param name="id">ID of node to get events for</param>
        /// <param name="since">UTC time to get events since</param>
        /// <param name="logger">Logger</param>
        /// <returns>null on error, or list of events on success</returns>
        public static async Task<List<OrcanodeEvent>?> GetRecentEventsForNodeAsync(IOrcanodeMonitorContext context, string id, DateTime since, ILogger logger)
        {
            try
            {
                List<OrcanodeEvent> events = await context.OrcanodeEvents.Where(e => e.OrcanodeId == id).OrderByDescending(e => e.DateTimeUtc).ToListAsync();
                List<OrcanodeEvent> orcanodeEvents = events.Where(e => e.DateTimeUtc >= since).ToList();

                // Add one older event per type we can filter on.
                orcanodeEvents = AddOlderEvent(orcanodeEvents, events, since, OrcanodeEventTypes.HydrophoneStream);
                orcanodeEvents = AddOlderEvent(orcanodeEvents, events, since, OrcanodeEventTypes.DataplicityConnection);
                orcanodeEvents = AddOlderEvent(orcanodeEvents, events, since, OrcanodeEventTypes.MezmoLogging);

                return orcanodeEvents;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to fetch events for node {id}");
                return null;
            }
        }

        /// <summary>
        /// Get recent detections.
        /// </summary>
        /// <param name="logger">Logger</param>
        /// <returns>null on error, or list of detections on success</returns>
        public static async Task<List<Detection>?> GetRecentDetectionsAsync(ILogger logger)
        {
            string site = _orcasoundProdSite;
            string url = $"https://{site}/api/json/detections?page%5Blimit%5D=500&page%5Boffset%5D=0&fields%5Bdetection%5D=id%2Cplaylist_timestamp%2Cplayer_offset%2Ctimestamp%2Cdescription%2Csource%2Ccategory%2Cfeed_id";

            try
            {
                string jsonString = await _httpClient.GetStringAsync(url);
                if (jsonString.IsNullOrEmpty())
                {
                    // Error.
                    return null;
                }

                var response = JsonSerializer.Deserialize<DetectionResponse>(
                    jsonString,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                if (response?.Data == null)
                {
                    return null;
                }

                List<Detection> detections =
                    response.Data.Select(d => new Detection
                    {
                        ID = d.Id,
                        NodeID = d.Attributes?.FeedId ?? string.Empty,
                        Timestamp = d.Attributes?.Timestamp ?? default,
                        Source = d.Attributes?.Source ?? string.Empty,
                        Description = d.Attributes?.Description ?? string.Empty,
                        Category = d.Attributes?.Category ?? string.Empty
                    }).ToList();

                return detections;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in GetRecentDetectionsForNodeAsync: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get recent detections for a node.
        /// </summary>
        /// <param name="feedId">Orcasound feed ID of node to get detections for</param>
        /// <param name="logger">Logger</param>
        /// <returns>null on error, or list of detections on success</returns>
        public static async Task<List<Detection>?> GetRecentDetectionsForNodeAsync(string feedId, ILogger logger)
        {
            string site = _orcasoundProdSite;
            string url = $"https://{site}/api/json/detections?page%5Blimit%5D=500&page%5Boffset%5D=0&fields%5Bdetection%5D=id%2Cplaylist_timestamp%2Cplayer_offset%2Ctimestamp%2Cdescription%2Csource%2Ccategory%2Cfeed_id&filter[feed_id]={feedId}";

            try
            {
                string jsonString = await _httpClient.GetStringAsync(url);
                if (jsonString.IsNullOrEmpty())
                {
                    // Error.
                    return null;
                }

                var response = JsonSerializer.Deserialize<DetectionResponse>(
                    jsonString,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                if (response?.Data == null)
                {
                    return null;
                }

                List<Detection> detections =
                    response.Data.Select(d => new Detection
                    {
                        ID = d.Id,
                        NodeID = d.Attributes?.FeedId ?? string.Empty,
                        Timestamp = d.Attributes?.Timestamp ?? default,
                        Source = d.Attributes?.Source ?? string.Empty,
                        Description = d.Attributes?.Description ?? string.Empty,
                        Category = d.Attributes?.Category ?? string.Empty
                    }).ToList();

                return detections;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in GetRecentDetectionsForNodeAsync: {ex.Message}");
                return null;
            }
        }

        public static void AddOrcanodeEvent(IOrcanodeMonitorContext context, ILogger logger, Orcanode node, string type, string value, string? url = null)
        {
            logger.LogInformation($"Orcanode event: {node.DisplayName} {type} {value}");
            var orcanodeEvent = new OrcanodeEvent(node, type, value, DateTime.UtcNow, url);
            context.OrcanodeEvents.Add(orcanodeEvent);
        }

        private static void AddHydrophoneStreamStatusEvent(IOrcanodeMonitorContext context, ILogger logger, Orcanode node, string? url)
        {
            string value = node.OrcasoundOnlineStatusString;
            AddOrcanodeEvent(context, logger, node, OrcanodeEventTypes.HydrophoneStream, value, url);
        }

        public async static Task<FrequencyInfo?> GetLatestAudioSampleAsync(Orcanode node, string unixTimestampString, bool updateNode, ILogger logger)
        {
            string url = "https://" + node.S3Bucket + ".s3.amazonaws.com/" + node.S3NodeName + "/hls/" + unixTimestampString + "/live.m3u8";
            using HttpResponseMessage response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                if (updateNode)
                {
                    // Set the manifest updated time to be old, since it would be the timestamp of the
                    // previous manifest file. The actual value doesn't matter, just that it's older than
                    // MaxUploadDelay ago so the stream will appear as offline.
                    node.ManifestUpdatedUtc = DateTime.MinValue;
                    node.LastCheckedUtc = DateTime.UtcNow;
                }
                return null;
            }

            DateTimeOffset? offset = response.Content.Headers.LastModified;
            if (!offset.HasValue)
            {
                if (updateNode)
                {
                    node.LastCheckedUtc = DateTime.UtcNow;
                }
                return null;
            }

            if (updateNode)
            {
                node.ManifestUpdatedUtc = offset.Value.UtcDateTime;
                node.LastCheckedUtc = DateTime.UtcNow;
            }

            // Download manifest.
            Uri baseUri = new Uri(url);
            string manifestContent = await _httpClient.GetStringAsync(url);

            // Get a recent filename in the manifest.
            // Sometimes the manifest is updated before the file is actually available,
            // so we try to get the penultimate .ts file in the manifest.
            string[] lines = manifestContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            int lineNumber = lines.Count();
            lineNumber = (lineNumber > 3) ? lineNumber - 3 : lineNumber - 1;
            string lastLine = lines[lineNumber];
            Uri newUri = new Uri(baseUri, lastLine);
            return await GetExactAudioSampleAsync(node, newUri, logger);
        }

        /// <summary>
        /// Retrieves the last modified date of a resource via HTTP HEAD request.
        /// </summary>
        /// <param name="uri">The URI of the resource to check.</param>
        /// <returns>The last modified date in UTC, or null if not available.</returns>
        public async static Task<DateTime?> GetLastModifiedAsync(Uri uri)
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, uri);
            using var headResponse = await _httpClient.SendAsync(headRequest);
            DateTime? lastModified = headResponse.Content.Headers.LastModified?.UtcDateTime;
            return lastModified;
        }

        public async static Task<FrequencyInfo?> GetExactAudioSampleAsync(Orcanode node, Uri uri, ILogger logger)
        {
            OrcanodeOnlineStatus oldStatus = node.S3StreamStatus;

            try
            {
                using Stream stream = await _httpClient.GetStreamAsync(uri);
#if true
                FrequencyInfo frequencyInfo = await FfmpegCoreAnalyzer.AnalyzeAudioStreamAsync(stream, oldStatus);
#else
                FrequencyInfo frequencyInfo = await FfmpegCoreAnalyzer.AnalyzeFileAsync("output-2channels.wav", oldStatus);
#endif
                frequencyInfo.AudioSampleUrl = uri.AbsoluteUri;
                return frequencyInfo;
            }
            catch (Exception ex)
            {
                // We couldn't fetch the stream audio so could not update the
                // audio standard deviation. Just ignore this for now.
                logger.LogError(ex, $"Exception in UpdateManifestTimestampAsync: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Update the ManifestUpdated timestamp for a given Orcanode by querying S3.
        /// </summary>
        /// <param name="context">Database context</param>
        /// <param name="node">Orcanode to update</param>
        /// <param name="unixTimestampString">Value in the latest.txt file</param>
        /// <param name="logger">Logger</param>
        /// <returns></returns>
        public async static Task UpdateManifestTimestampAsync(IOrcanodeMonitorContext context, Orcanode node, string unixTimestampString, ILogger logger)
        {
            OrcanodeOnlineStatus oldStatus = node.S3StreamStatus;

            FrequencyInfo? frequencyInfo = await GetLatestAudioSampleAsync(node, unixTimestampString, true, logger);
            if (frequencyInfo != null)
            {
                node.AudioStreamStatus = frequencyInfo.Status;

                // Compute an exponential weighted moving average of the non-hum decibel level.
                double newValue = frequencyInfo.GetAverageNonHumDecibels();
                if (node.DecibelLevel == null || node.DecibelLevel == double.NegativeInfinity)
                {
                    node.DecibelLevel = newValue;
                }
                else
                {
                    // Let it be a moving average across a day.
                    double alpha = 1.0 / PeriodicTasks.PollsPerDay;
                    node.DecibelLevel = (alpha * newValue) + ((1 - alpha) * node.DecibelLevel);
                }

                // Do the same for the hum decibel level.
                newValue = frequencyInfo.GetAverageHumDecibels();
                if (node.HumDecibelLevel == null || node.HumDecibelLevel == double.NegativeInfinity)
                {
                    node.HumDecibelLevel = newValue;
                }
                else
                {
                    // Let it be a moving average across a day.
                    double alpha = 1.0 / PeriodicTasks.PollsPerDay;
                    node.HumDecibelLevel = (alpha * newValue) + ((1 - alpha) * node.HumDecibelLevel);
                }
            }
            node.AudioStandardDeviation = 0.0;

            OrcanodeOnlineStatus newStatus = node.S3StreamStatus;
            if (newStatus != oldStatus)
            {
                AddHydrophoneStreamStatusEvent(context, logger, node, frequencyInfo?.AudioSampleUrl);
            }
        }

        /// <summary>
        /// Check whether a request includes the correct IFTTT-Service-Key value.
        /// </summary>
        /// <param name="request">HTTP request received</param>
        /// <returns>null on success, ObjectResult if failed</returns>
        public static ErrorResponse? CheckIftttServiceKey(HttpRequest request)
        {
            if (request.Headers.TryGetValue("IFTTT-Service-Key", out var values) &&
                values.Any())
            {
                string value = values.First() ?? String.Empty;
                if (value == Fetcher.IftttServiceKey)
                {
                    return null;
                }
            }
            string errorMessage = "Unauthorized access";
            var errorResponse = new ErrorResponse
            {
                Errors = new List<ErrorItem>
                {
                    new ErrorItem { Message = errorMessage }
                }
            };
            return errorResponse;
        }

        /// <summary>
        /// Get the number of OrcaHello detections for a given location in the past week.
        /// </summary>
        /// <param name="orcanode">Node to check</param>
        /// <returns>Count of AI detections in the past week</returns>
        public static async Task<long> GetOrcaHelloDetectionCountAsync(Orcanode orcanode)
        {
            try
            {
                string location = Uri.EscapeDataString(orcanode.OrcaHelloDisplayName);
                var uri = new Uri($"https://aifororcasdetections.azurewebsites.net/api/detections?Timeframe=1w&Location={location}&RecordsPerPage=1");

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await _httpClient.SendAsync(request);

                // Try to get the custom header.
                if (!response.Headers.TryGetValues("totalnumberrecords", out var values))
                {
                    return 0;
                }

                string headerValue = values?.FirstOrDefault() ?? string.Empty;
                if (!long.TryParse(headerValue, out long totalRecords))
                {
                    return 0;
                }

                return totalRecords;
            }
            catch (Exception)
            {
                return 0;
            }
        }
    }
}
