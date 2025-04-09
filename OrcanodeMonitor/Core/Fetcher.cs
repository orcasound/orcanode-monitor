// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using System;
using System.Dynamic;
using System.Text.Json;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using System.Net.Sockets;
using System.Web;
using OrcanodeMonitor.Models;
using Microsoft.EntityFrameworkCore;
using OrcanodeMonitor.Data;
using Microsoft.IdentityModel.Tokens;
using Mono.TextTemplating;
using System.Net;
using OrcanodeMonitor.Api;

namespace OrcanodeMonitor.Core
{
    public class Fetcher
    {
        private static TimeZoneInfo _pacificTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        private static HttpClient _httpClient = new HttpClient();
        private static string _orcasoundProdSite = "live.orcasound.net";
        private static string _orcasoundFeedsUrlPath = "/api/json/feeds";
        private static string _dataplicityDevicesUrl = "https://apps.dataplicity.com/devices/";
        private static string _orcaHelloHydrophonesUrl = "https://aifororcasdetections2.azurewebsites.net/api/hydrophones";
        private static DateTime _unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        private static string _iftttServiceKey = Environment.GetEnvironmentVariable("IFTTT_SERVICE_KEY") ?? "<unknown>";
        private static string _defaultProdS3Bucket = "audio-orcasound-net";
        private static string _defaultDevS3Bucket = "dev-streaming-orcasound-net";
        public static string IftttServiceKey => _iftttServiceKey;

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
        private static Orcanode? FindOrcanodeByDataplicitySerial(List<Orcanode> nodes, string serial, out OrcanodeOnlineStatus connectionStatus)
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
        private static Orcanode FindOrCreateOrcanodeByDataplicitySerial(DbSet<Orcanode> nodeList, string serial, out OrcanodeOnlineStatus connectionStatus)
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

        private static Orcanode? FindOrcanodeByOrcasoundFeedId(List<Orcanode> nodes, string feedId)
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
                if ((node.OrcasoundName.IsNullOrEmpty()) && orcasoundName.Contains(node.DisplayName))
                {
                    node.OrcasoundName = orcasoundName;
                    return node;
                }
            }

            return null;
        }

        /// <summary>
        /// Update the list of Orcanodes using data from OrcaHello.
        /// </summary>
        /// <param name="context">Database context to update</param>
        /// <param name="logger"></param>
        /// <returns></returns>
        public async static Task UpdateOrcaHelloDataAsync(OrcanodeMonitorContext context, ILogger logger)
        {
            try
            {
                string json = await _httpClient.GetStringAsync(_orcaHelloHydrophonesUrl);
                if (json.IsNullOrEmpty())
                {
                    return;
                }
                dynamic response = JsonSerializer.Deserialize<ExpandoObject>(json);
                if (response == null)
                {
                    logger.LogError("UpdateOrcaHelloDataAsync couldn't deserialize JSON");
                    return;
                }
                JsonElement hydrophoneArray = response.hydrophones;
                if (hydrophoneArray.ValueKind != JsonValueKind.Array)
                {
                    logger.LogError($"Invalid hydrophoneArray kind in UpdateOrcaHelloDataAsync: {hydrophoneArray.ValueKind}");
                    return;
                }

                // Get a snapshot to use during the loop to avoid multiple queries.
                var foundList = context.Orcanodes.ToList();

                // Create a list to track what nodes are no longer returned.
                var unfoundList = foundList.ToList();

                foreach (JsonElement hydrophone in hydrophoneArray.EnumerateArray())
                {
                    // "id" holds the OrcaHello id which is also the S3NodeName.
                    if (!hydrophone.TryGetProperty("id", out var hydrophoneId))
                    {
                        logger.LogError("No id inUpdateOrcaHelloDataAsync result");
                        continue;
                    }

                    // "name" holds the display name.
                    if (!hydrophone.TryGetProperty("name", out var name))
                    {
                        logger.LogError("No name in UpdateOrcaHelloDataAsync result");
                        continue;
                    }

                    // Remove the returned node from the unfound list.
                    Orcanode? oldListNode = unfoundList.Find(a => a.OrcaHelloId == hydrophoneId.ToString());
                    if (oldListNode == null)
                    {
                        oldListNode = unfoundList.Find(a => a.S3NodeName == hydrophoneId.ToString());
                    }
                    if (oldListNode == null)
                    {
                        oldListNode = unfoundList.Find(a => a.OrcasoundName == name.ToString());
                    }
                    if (oldListNode != null)
                    {
                        unfoundList.Remove(oldListNode);
                    }

                    // TODO: we should have a unique id, independent of S3.
                    Orcanode? node = FindOrcanodeByOrcasoundName(foundList, name.ToString());
                    if (node == null)
                    {
                        node = CreateOrcanode(context.Orcanodes);
                        node.OrcasoundName = name.ToString();
                        node.S3NodeName = hydrophoneId.ToString();
                    }

                    node.OrcaHelloId = hydrophoneId.ToString();
                }

                // Mark any remaining unfound nodes as absent.
                foreach (var unfoundNode in unfoundList)
                {
                    Orcanode? oldNode = null;
                    if (!unfoundNode.OrcasoundFeedId.IsNullOrEmpty())
                    {
                        oldNode = FindOrcanodeByOrcasoundFeedId(foundList, unfoundNode.OrcasoundFeedId);
                    }
                    else if (!unfoundNode.DataplicitySerial.IsNullOrEmpty())
                    {
                        oldNode = FindOrcanodeByDataplicitySerial(foundList, unfoundNode.DataplicitySerial, out OrcanodeOnlineStatus connectionStatus);
                    }
                    if (oldNode != null)
                    {
                        oldNode.OrcaHelloId = String.Empty;
                    }
                }

                MonitorState.GetFrom(context).LastUpdatedTimestampUtc = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in UpdateOrcaHelloDataAsync");
            }
        }

        public async static Task<string> GetDataplicityDataAsync(string serial, ILogger logger)
        {
            try
            {
                string? orcasound_dataplicity_token = Environment.GetEnvironmentVariable("ORCASOUND_DATAPLICITY_TOKEN");
                if (orcasound_dataplicity_token == null)
                {
                    logger.LogError("ORCASOUND_DATAPLICITY_TOKEN not found");
                    return string.Empty;
                }

                string url = _dataplicityDevicesUrl;
                if (!serial.IsNullOrEmpty())
                {
                    url += serial + "/";
                }

                using (var request = new HttpRequestMessage
                {
                    RequestUri = new Uri(url),
                    Method = HttpMethod.Get,
                })
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Token", orcasound_dataplicity_token);
                    using HttpResponseMessage response = await _httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in GetDataplicityDataAsync");
                return string.Empty;
            }
        }

        /// <summary>
        /// Update Orcanode state by querying dataplicity.com.
        /// </summary>
        /// <param name="context">Database context to update</param>
        /// <param name="logger"></param>
        /// <returns></returns>
        public async static Task UpdateDataplicityDataAsync(OrcanodeMonitorContext context, ILogger logger)
        {
            try
            {
                string jsonArray = await GetDataplicityDataAsync(string.Empty, logger);
                if (jsonArray.IsNullOrEmpty())
                {
                    // Indeterminate result, so don't update anything.
                    return;
                }

                var originalList = context.Orcanodes.ToList();

                // Create a list to track what nodes are no longer returned.
                var unfoundList = originalList.ToList();

                dynamic deviceArray = JsonSerializer.Deserialize<JsonElement>(jsonArray);
                if (deviceArray.ValueKind != JsonValueKind.Array)
                {
                    logger.LogError($"Invalid deviceArray kind in UpdateDataplicityDataAsync: {deviceArray.ValueKind}");
                    return;
                }
                foreach (JsonElement device in deviceArray.EnumerateArray())
                {
                    if (!device.TryGetProperty("serial", out var serial))
                    {
                        logger.LogError($"Missing serial in UpdateDataplicityDataAsync result");
                        continue;
                    }
                    if (serial.ToString().IsNullOrEmpty())
                    {
                        logger.LogError($"Empty serial in UpdateDataplicityDataAsync result");
                        continue;
                    }

                    // Remove the found node from the unfound list.
                    Orcanode? oldListNode = unfoundList.Find(a => a.DataplicitySerial == serial.ToString());
                    if (oldListNode != null)
                    {
                        unfoundList.Remove(oldListNode);
                    }

                    Orcanode node = FindOrCreateOrcanodeByDataplicitySerial(context.Orcanodes, serial.ToString(), out OrcanodeOnlineStatus oldStatus);
                    OrcanodeUpgradeStatus oldAgentUpgradeStatus = node.DataplicityUpgradeStatus;
                    long oldDiskCapacityInGigs = node.DiskCapacityInGigs;

                    if (device.TryGetProperty("name", out var name))
                    {
                        string dataplicityName = name.ToString();
                        node.DataplicityName = dataplicityName;

                        if (node.S3Bucket.IsNullOrEmpty() || (node.OrcasoundStatus == OrcanodeOnlineStatus.Absent))
                        {
                            node.S3Bucket = dataplicityName.ToLower().StartsWith("dev") ? _defaultDevS3Bucket : _defaultProdS3Bucket;
                        }

                        if (node.S3NodeName.IsNullOrEmpty() || (node.OrcasoundStatus == OrcanodeOnlineStatus.Absent))
                        {
                            // Fill in a non-authoritative default S3 node name.
                            // Orcasound is authoritative here since our default is
                            // just derived from the name, but there might be no
                            // relation.  We use this to see if an S3 stream exists
                            // even if Orcasound doesn't know about it.
                            node.S3NodeName = Orcanode.DataplicityNameToS3Name(dataplicityName);
                        }
                    }
                    if (device.TryGetProperty("online", out var online))
                    {
                        node.DataplicityOnline = online.GetBoolean();
                    }
                    if (device.TryGetProperty("description", out var description))
                    {
                        node.DataplicityDescription = description.ToString();
                    }
                    if (device.TryGetProperty("agent_version", out var agentVersion))
                    {
                        node.AgentVersion = agentVersion.ToString();
                    }
                    if (device.TryGetProperty("disk_capacity", out var diskCapacity))
                    {
                        node.DiskCapacity = diskCapacity.GetInt64();
                    }
                    if (device.TryGetProperty("disk_used", out var diskUsed))
                    {
                        node.DiskUsed = diskUsed.GetInt64();
                    }
                    if (device.TryGetProperty("upgrade_available", out var upgradeAvailable))
                    {
                        node.DataplicityUpgradeAvailable = upgradeAvailable.GetBoolean();
                    }
                    if (oldStatus == OrcanodeOnlineStatus.Absent)
                    {
                        // Save changes to make the node have an ID before we can
                        // possibly generate any events.
                        await context.SaveChangesAsync();
                    }

                    // Trigger any event changes.
                    OrcanodeOnlineStatus newStatus = node.DataplicityConnectionStatus;
                    if (newStatus != oldStatus)
                    {
                        AddDataplicityConnectionStatusEvent(context, node);
                    }
                    if (oldStatus != OrcanodeOnlineStatus.Absent)
                    {
                        if (oldAgentUpgradeStatus != node.DataplicityUpgradeStatus)
                        {
                            AddDataplicityAgentUpgradeStatusChangeEvent(context, node);
                        }
                        if (oldDiskCapacityInGigs != node.DiskCapacityInGigs)
                        {
                            AddDiskCapacityChangeEvent(context, node);
                        }
                    }
                }

                // Mark any remaining unfound nodes as absent.
                foreach (var unfoundNode in unfoundList)
                {
                    var oldNode = FindOrcanodeByDataplicitySerial(originalList, unfoundNode.DataplicitySerial, out OrcanodeOnlineStatus unfoundNodeStatus);
                    if (oldNode != null)
                    {
                        oldNode.DataplicityOnline = null;
                    }
                }

                MonitorState.GetFrom(context).LastUpdatedTimestampUtc = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in UpdateDataplicityDataAsync");
            }
        }

        /// <summary>
        /// Get Orcasound data
        /// </summary>
        /// <param name="context"></param>
        /// <param name="site"></param>
        /// <param name="logger"></param>
        /// <returns>null on error, or JsonElement on success</returns>
        private async static Task<JsonElement?> GetOrcasoundDataAsync(OrcanodeMonitorContext context, string site, ILogger logger)
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
                logger.LogError(ex, "Exception in GetOrcasoundDataAsync");
                return null;
            }
        }

        private static void UpdateOrcasoundNode(JsonElement feed, List<Orcanode> foundList, List<Orcanode> unfoundList, OrcanodeMonitorContext context, string site, ILogger logger)
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

        private async static Task UpdateOrcasoundSiteDataAsync(OrcanodeMonitorContext context, string site, List<Orcanode> foundList, List<Orcanode> unfoundList, ILogger logger)
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
        /// Update the current list of Orcanodes using data from orcasound.net.
        /// </summary>
        /// <param name="context">Database context to update</param>
        /// <param name="logger"></param>
        /// <returns></returns>
        public async static Task UpdateOrcasoundDataAsync(OrcanodeMonitorContext context, ILogger logger)
        {
            try
            {
                var foundList = context.Orcanodes.ToList();

                // Create a list to track what nodes are no longer returned.
                var unfoundList = foundList.ToList();

                await UpdateOrcasoundSiteDataAsync(context, _orcasoundProdSite, foundList, unfoundList, logger);

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
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in UpdateOrcasoundDataAsync");
            }
        }

        public async static Task UpdateS3DataAsync(OrcanodeMonitorContext context, ILogger logger)
        {
            try
            {
                List<Orcanode> nodes = context.Orcanodes.ToList();
                foreach (Orcanode node in nodes)
                {
                    if (!node.S3NodeName.IsNullOrEmpty())
                    {
                        await Fetcher.UpdateS3DataAsync(context, node, logger);
                    }
                }

                MonitorState.GetFrom(context).LastUpdatedTimestampUtc = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in UpdateS3DataAsync");
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
        /// <param name="logger"></param>
        /// <returns></returns>
        public async static Task UpdateS3DataAsync(OrcanodeMonitorContext context, Orcanode node, ILogger logger)
        {
            TimestampResult? result = await GetLatestS3TimestampAsync(node, true, logger);
            if (result == null)
            {
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
        public static List<OrcanodeEvent> GetEvents(OrcanodeMonitorContext context, int limit)
        {
            List<OrcanodeEvent> orcanodeEvents = context.OrcanodeEvents.OrderByDescending(e => e.DateTimeUtc).Take(limit).ToList();
            return orcanodeEvents;
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
        /// Get recent events for a node
        /// </summary>
        /// <param name="context"></param>
        /// <param name="id">ID of node to get events for</param>
        /// <param name="since">Time to get events since</param>
        /// <param name="logger"></param>
        /// <returns>null on error, or list of events on success</returns>
        public static List<OrcanodeEvent>? GetRecentEventsForNode(OrcanodeMonitorContext context, string id, DateTime since, ILogger logger)
        {
            try
            {
                List<OrcanodeEvent> events = context.OrcanodeEvents.Where(e => e.OrcanodeId == id).OrderByDescending(e => e.DateTimeUtc).ToList();
                List<OrcanodeEvent> orcanodeEvents = events.Where(e => e.DateTimeUtc >= since).ToList();

                // Add one older event per type we can filter on.
                orcanodeEvents = AddOlderEvent(orcanodeEvents, events, since, OrcanodeEventTypes.HydrophoneStream);
                orcanodeEvents = AddOlderEvent(orcanodeEvents, events, since, OrcanodeEventTypes.DataplicityConnection);
                orcanodeEvents = AddOlderEvent(orcanodeEvents, events, since, OrcanodeEventTypes.MezmoLogging);

                return orcanodeEvents;
            } catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to fetch events for node {id}");
                return null;
            }
        }

        public static void AddOrcanodeEvent(OrcanodeMonitorContext context, Orcanode node, string type, string value, string? url = null)
        {
            var orcanodeEvent = new OrcanodeEvent(node, type, value, DateTime.UtcNow, url);
            context.OrcanodeEvents.Add(orcanodeEvent);
        }

        private static void AddDataplicityConnectionStatusEvent(OrcanodeMonitorContext context, Orcanode node)
        {
            string value = node.DataplicityConnectionStatus.ToString();
            AddOrcanodeEvent(context, node, OrcanodeEventTypes.DataplicityConnection, value);
        }

        private static void AddDataplicityAgentUpgradeStatusChangeEvent(OrcanodeMonitorContext context, Orcanode node)
        {
            string value = node.DataplicityUpgradeStatus.ToString();
            AddOrcanodeEvent(context, node, OrcanodeEventTypes.AgentUpgradeStatus, value);
        }

        private static void AddDiskCapacityChangeEvent(OrcanodeMonitorContext context, Orcanode node)
        {
            string value = string.Format("{0}G", node.DiskCapacityInGigs);
            AddOrcanodeEvent(context, node, OrcanodeEventTypes.SDCardSize, value);
        }

        private static void AddHydrophoneStreamStatusEvent(OrcanodeMonitorContext context, Orcanode node, string? url)
        {
            string value = node.OrcasoundOnlineStatusString;
            AddOrcanodeEvent(context, node, OrcanodeEventTypes.HydrophoneStream, value, url);
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
                logger.LogError(ex, "Exception in UpdateManifestTimestampAsync");
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
        public async static Task UpdateManifestTimestampAsync(OrcanodeMonitorContext context, Orcanode node, string unixTimestampString, ILogger logger)
        {
            OrcanodeOnlineStatus oldStatus = node.S3StreamStatus;

            FrequencyInfo? frequencyInfo = await GetLatestAudioSampleAsync(node, unixTimestampString, true, logger);
            if (frequencyInfo != null)
            {
                node.AudioStreamStatus = frequencyInfo.Status;
                node.DecibelLevel = frequencyInfo.GetMaxNonHumDecibels();
            }
            node.AudioStandardDeviation = 0.0;

            OrcanodeOnlineStatus newStatus = node.S3StreamStatus;
            if (newStatus != oldStatus)
            {
                AddHydrophoneStreamStatusEvent(context, node, frequencyInfo?.AudioSampleUrl);
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
    }
}
