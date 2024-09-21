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
using Azure.Core;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using OrcanodeMonitor.Api;
using Newtonsoft.Json.Linq;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.HttpResults;
using System.Net.Mail;

namespace OrcanodeMonitor.Core
{
    public class Fetcher
    {
        private static TimeZoneInfo _pacificTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        private static HttpClient _httpClient = new HttpClient();
        private static string _orcasoundFeedsUrl = "https://live.orcasound.net/api/json/feeds";
        private static string _dataplicityDevicesUrl = "https://apps.dataplicity.com/devices/";
        private static string _orcaHelloHydrophonesUrl = "https://aifororcasdetections2.azurewebsites.net/api/hydrophones";
        private static DateTime _unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        private static string _iftttServiceKey = Environment.GetEnvironmentVariable("IFTTT_SERVICE_KEY") ?? "<unknown>";
        private static string _defaultS3Bucket = "streaming-orcasound-net";
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
        /// <param name="nodeList">Database table to look in and potentially update</param>
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
                ID = Guid.NewGuid().ToString()
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
            newNode.partitionvalue = "1";
            return newNode;
        }

        private static Orcanode? FindOrcanodeByOrcasoundFeedId(DbSet<Orcanode> nodeList, string feedId)
        {
            List<Orcanode> nodes = nodeList.ToList();
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
        /// <param name="nodeList">Orcanode list to look in</param>
        /// <param name="orcasoundName">Name to look for</param>
        /// <returns>Node found</returns>
        private static Orcanode? FindOrcanodeByOrcasoundName(List<Orcanode> nodeList, string orcasoundName)
        {
            List<Orcanode> nodes = nodeList.ToList();
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
        /// Look for an Orcanode by Orcasound name in a list and create one if not found.
        /// </summary>
        /// <param name="nodeList">Orcanode list to look in</param>
        /// <param name="orcasoundName">Name to look for</param>
        /// <returns>Node found or created</returns>
        private static Orcanode FindOrCreateOrcanodeByOrcasoundName(DbSet<Orcanode> nodeList, string orcasoundName)
        {
            List<Orcanode> nodes = nodeList.ToList();
            foreach (Orcanode node in nodes)
            {
                if (node.OrcasoundName == orcasoundName)
                {
                    return node;
                }
                if ((node.OrcasoundName.IsNullOrEmpty()) && orcasoundName.Contains(node.DisplayName))
                {
                    node.OrcasoundName = orcasoundName;
                    return node;
                }
            }

            Orcanode newNode = CreateOrcanode(nodeList);
            newNode.OrcasoundName = orcasoundName;
            newNode.partitionvalue = "1";
            return newNode;
        }

        /// <summary>
        /// Update the list of Orcanodes using data from OrcaHello.
        /// </summary>
        /// <param name="context">Database context to update</param>
        /// <returns></returns>
        public async static Task UpdateOrcaHelloDataAsync(OrcanodeMonitorContext context)
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
                    return;
                }
                JsonElement hydrophoneArray = response.hydrophones;
                if (hydrophoneArray.ValueKind != JsonValueKind.Array)
                {
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
                        continue;
                    }

                    // "name" holds the display name.
                    if (!hydrophone.TryGetProperty("name", out var name))
                    {
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
                    if (!unfoundNode.OrcasoundName.IsNullOrEmpty())
                    {
                        oldNode = FindOrcanodeByOrcasoundName(foundList, unfoundNode.OrcasoundName);
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
                string msg = ex.ToString();
            }
        }


        /// <summary>
        /// Update Orcanode state by querying dataplicity.com.
        /// </summary>
        /// <param name="context">Database context to update</param>
        /// <returns></returns>
        public async static Task UpdateDataplicityDataAsync(OrcanodeMonitorContext context)
        {
            try
            {
                string? orcasound_dataplicity_token = Environment.GetEnvironmentVariable("ORCASOUND_DATAPLICITY_TOKEN");
                if (orcasound_dataplicity_token == null)
                {
                    return;
                }

                string jsonArray;
                using (var request = new HttpRequestMessage
                {
                    RequestUri = new Uri(_dataplicityDevicesUrl),
                    Method = HttpMethod.Get,
                })
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Token", orcasound_dataplicity_token);
                    HttpResponseMessage response = await _httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    jsonArray = await response.Content.ReadAsStringAsync();
                }

                var foundList = context.Orcanodes.ToList();

                // Create a list to track what nodes are no longer returned.
                var unfoundList = foundList.ToList();

                dynamic deviceArray = JsonSerializer.Deserialize<JsonElement>(jsonArray);
                if (deviceArray.ValueKind != JsonValueKind.Array)
                {
                    return;
                }
                foreach (JsonElement device in deviceArray.EnumerateArray())
                {
                    if (!device.TryGetProperty("serial", out var serial))
                    {
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

                        if (node.S3Bucket.IsNullOrEmpty())
                        {
                            node.S3Bucket = _defaultS3Bucket;
                        }

                        if (node.S3NodeName.IsNullOrEmpty())
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
                    var oldNode = FindOrcanodeByDataplicitySerial(foundList, unfoundNode.DataplicitySerial, out OrcanodeOnlineStatus unfoundNodeStatus);
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
                string msg = ex.ToString();
            }
        }

        /// <summary>
        /// Update the current list of Orcanodes using data from orcasound.net.
        /// </summary>
        /// <param name="context">Database context to update</param>
        /// <returns></returns>
        public async static Task UpdateOrcasoundDataAsync(OrcanodeMonitorContext context)
        {
            try
            {
                string json = await _httpClient.GetStringAsync(_orcasoundFeedsUrl);
                if (json == "")
                {
                    return;
                }
                dynamic response = JsonSerializer.Deserialize<ExpandoObject>(json);
                if (response == null)
                {
                    return;
                }
                JsonElement dataArray = response.data;
                if (dataArray.ValueKind != JsonValueKind.Array)
                {
                    return;
                }

                // Create a list to track what nodes are no longer returned.
                var unfoundList = context.Orcanodes!=null ?context.Orcanodes.ToList():null;

                foreach (JsonElement feed in dataArray.EnumerateArray())
                {
                    if (!feed.TryGetProperty("id", out var feedId))
                    {
                        continue;
                    }
                    if (!feed.TryGetProperty("attributes", out JsonElement attributes))
                    {
                        continue;
                    }
                    if (!attributes.TryGetProperty("name", out var name))
                    {
                        continue;
                    }
                    string orcasoundName = name.ToString();
                    if (!attributes.TryGetProperty("dataplicity_id", out var dataplicity_id))
                    {
                        continue;
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
                    node = FindOrcanodeByOrcasoundFeedId(context.Orcanodes, feedId.ToString());
                    if (node == null)
                    {
                        // We didn't used to store the feed ID, only the name, so try again by name.
                        node = FindOrcanodeByOrcasoundName(context.Orcanodes.ToList(), orcasoundName);
                    }

                    // See if we can find a node by dataplicity ID, so that if a node
                    // shows up in dataplicity first and Orcasite later, we don't create a
                    // duplicate entry.
                    Orcanode? dataplicityNode = null;
                    if (!dataplicitySerial.IsNullOrEmpty())
                    {
                        dataplicityNode = FindOrcanodeByDataplicitySerial(context.Orcanodes.ToList(), dataplicitySerial, out OrcanodeOnlineStatus oldStatus);
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
                        node.partitionvalue = "1";
                    }

                    if (!dataplicitySerial.IsNullOrEmpty())
                    {
                        if (!node.DataplicitySerial.IsNullOrEmpty() && dataplicitySerial != node.DataplicitySerial)
                        {
                            // TODO: The orcasound entry for the node changed its dataplicity_id.
                        }
                        node.DataplicitySerial = dataplicitySerial;
                    }

                    if (node.OrcasoundFeedId.IsNullOrEmpty())
                    {
                        node.OrcasoundFeedId = feedId.ToString();
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
                   /* if (attributes.TryGetProperty("bucket_region", out var bucketregion))
                    {
                        node.S3BucketRegion = bucketregion.ToString();
                    }
                    if (attributes.TryGetProperty("location_point", out var locationpoint))
                    {
                        node.LocationPoint = locationpoint.ToString();
                    }
                    if (attributes.TryGetProperty("intro_html", out var introhtml))
                    {
                        node.Introhtml = introhtml.ToString();
                    }
                    if (attributes.TryGetProperty("image_url", out var imageurl))
                    {
                        node.ImageURL = imageurl.ToString();
                    }
                    if (attributes.TryGetProperty("cloudfront_url", out var cloudfronturl))
                    {
                        node.CloudFrontURL = cloudfronturl.ToString();
                    }
                    if (attributes.TryGetProperty("lat_lng", out var geocoordinates))
                    {
                        node.Geocoordinates = geocoordinates.ToString();
                    } */

                }

                // Mark any remaining unfound nodes as absent.
                foreach (var unfoundNode in unfoundList)
                {
                    var oldNode = FindOrcanodeByOrcasoundFeedId(context.Orcanodes, unfoundNode.OrcasoundFeedId);
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
                string msg = ex.ToString();
            }
        }

        public async static Task UpdateS3DataAsync(OrcanodeMonitorContext context)
        {
            try
            {
                List<Orcanode> nodes = await context.Orcanodes.ToListAsync();
                foreach (Orcanode node in nodes)
                {
                    if (!node.S3NodeName.IsNullOrEmpty())
                    {
                        await Fetcher.UpdateS3DataAsync(context, node);
                    }
                }

                MonitorState.GetFrom(context).LastUpdatedTimestampUtc = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                string msg = ex.ToString();
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

        /// <summary>
        /// Update the timestamps for a given Orcanode by querying files on S3.
        /// </summary>
        /// <param name="context">Database context</param>
        /// <param name="node">Orcanode to update</param>
        /// <returns></returns>
        public async static Task UpdateS3DataAsync(OrcanodeMonitorContext context, Orcanode node)
        {
            string url = "https://" + node.S3Bucket + ".s3.amazonaws.com/" + node.S3NodeName + "/latest.txt";
            HttpResponseMessage response = await _httpClient.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                // Absent.
                node.LatestRecordedUtc = null;
                return;
            }
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            string content = await response.Content.ReadAsStringAsync();
            string unixTimestampString = content.TrimEnd();
            DateTime? latestRecorded = UnixTimeStampStringToDateTimeUtc(unixTimestampString);
            if (latestRecorded.HasValue)
            {
                node.LatestRecordedUtc = latestRecorded.HasValue ? latestRecorded.Value.ToUniversalTime() : null;

                DateTimeOffset? offset = response.Content.Headers.LastModified;
                if (offset.HasValue)
                {
                    node.LatestUploadedUtc = offset.Value.UtcDateTime;
                }
            }

            await UpdateManifestTimestampAsync(context, node, unixTimestampString);
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

        private static void AddDataplicityConnectionStatusEvent(OrcanodeMonitorContext context, Orcanode node)
        {
            string value = (node.DataplicityConnectionStatus == OrcanodeOnlineStatus.Online) ? "up" : "OFFLINE";
            var orcanodeEvent = new OrcanodeEvent(node, "dataplicity connection", value, DateTime.UtcNow,DateTime.UtcNow.Year,Guid.NewGuid());
            context.OrcanodeEvents.Add(orcanodeEvent);
        }

        private static void AddDataplicityAgentUpgradeStatusChangeEvent(OrcanodeMonitorContext context, Orcanode node)
        {
            string value = node.DataplicityUpgradeStatus.ToString();
            var orcanodeEvent = new OrcanodeEvent(node, "agent upgrade status", value, DateTime.UtcNow, DateTime.UtcNow.Year, Guid.NewGuid());
            context.OrcanodeEvents.Add(orcanodeEvent);
        }

        private static void AddDiskCapacityChangeEvent(OrcanodeMonitorContext context, Orcanode node)
        {
            string value = string.Format("{0}G", node.DiskCapacityInGigs);
            var orcanodeEvent = new OrcanodeEvent(node, "SD card size", value, DateTime.UtcNow, DateTime.UtcNow.Year, Guid.NewGuid());
            context.OrcanodeEvents.Add(orcanodeEvent);
        }

        private static void AddHydrophoneStreamStatusEvent(OrcanodeMonitorContext context, Orcanode node)
        {
            string value = node.OrcasoundOnlineStatusString;
            var orcanodeEvent = new OrcanodeEvent(node, "hydrophone stream", value, DateTime.UtcNow, DateTime.UtcNow.Year, Guid.NewGuid());
            context.OrcanodeEvents.Add(orcanodeEvent);
        }

        /// <summary>
        /// Update the ManifestUpdated timestamp for a given Orcanode by querying S3.
        /// </summary>
        /// <param name="context">Database context</param>
        /// <param name="node">Orcanode to update</param>
        /// <param name="unixTimestampString">Value in the latest.txt file</param>
        /// <returns></returns>
        public async static Task UpdateManifestTimestampAsync(OrcanodeMonitorContext context, Orcanode node, string unixTimestampString)
        {
            OrcanodeOnlineStatus oldStatus = node.S3StreamStatus;

            string url = "https://" + node.S3Bucket + ".s3.amazonaws.com/" + node.S3NodeName + "/hls/" + unixTimestampString + "/live.m3u8";
            HttpResponseMessage response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            DateTimeOffset? offset = response.Content.Headers.LastModified;
            if (!offset.HasValue)
            {
                node.LastCheckedUtc = DateTime.UtcNow;
                return;
            }

            node.ManifestUpdatedUtc = offset.Value.UtcDateTime;
            node.LastCheckedUtc = DateTime.UtcNow;

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
            try
            {
                using Stream stream = await _httpClient.GetStreamAsync(newUri);
                double stdDev = await FfmpegCoreAnalyzer.AnalyzeAudioStreamAsync(stream);
                node.AudioStandardDeviation = stdDev;
            } catch (Exception ex)
            {
                // We couldn't fetch the stream audio so could not update the
                // audio standard deviation. Just ignore this for now.
                var msg = ex.ToString();
            }

            OrcanodeOnlineStatus newStatus = node.S3StreamStatus;
            if (newStatus != oldStatus)
            {
                AddHydrophoneStreamStatusEvent(context, node);
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
