﻿// Copyright (c) Orcanode Monitor contributors
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

namespace OrcanodeMonitor.Core
{
    public class Fetcher
    {
        private static TimeZoneInfo _pacificTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        private static HttpClient _httpClient = new HttpClient();
        private static string _orcasoundFeedsUrl = "https://live.orcasound.net/api/json/feeds";
        private static string _dataplicityDevicesUrl = "https://apps.dataplicity.com/devices/";
        private static DateTime _unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

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

        private static Orcanode FindOrCreateOrcanodeByDataplicitySerial(DbSet<Orcanode> nodeList, string serial)
        {
            List<Orcanode> nodes = nodeList.ToList();
            foreach (Orcanode node in nodes)
            {
                if (node.DataplicitySerial == serial)
                {
                    return node;
                }
            }

            var newNode = new Orcanode();
            newNode.DataplicitySerial = serial;

            nodeList.Add(newNode);
            return newNode;
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

            var newNode = new Orcanode();
            newNode.OrcasoundName = orcasoundName;
            newNode.DisplayName = Orcanode.OrcasoundNameToDisplayName(orcasoundName);

            nodeList.Add(newNode);
            return newNode;
        }

        /// <summary>
        /// Update the list of Orcanodes using data from OrcaHello.
        /// OrcaHello does not currently allow enumerating nodes.
        /// </summary>
        /// <param name="context">Database context to update</param>
        /// <returns></returns>
        public async static Task UpdateOrcaHelloDataAsync(OrcanodeMonitorContext context)
        {
            List<Orcanode> nodes = await context.Orcanodes.ToListAsync();
            foreach (Orcanode node in nodes)
            {
                await UpdateOrcaHelloDataAsync(context, node);
            }
        }

        public async static Task UpdateOrcaHelloDataAsync(OrcanodeMonitorContext context, Orcanode node)
        {
            string? name = HttpUtility.UrlEncode(node.OrcaHelloName);
            if (name == null)
            {
                return;
            }
            string url = "https://aifororcasdetections.azurewebsites.net/api/detections?Page=1&SortBy=timestamp&SortOrder=desc&Timeframe=all&Location=" + name + "&RecordsPerPage=1";
            string json = "";
            try
            {
                json = await _httpClient.GetStringAsync(url);
                if (json == "")
                {
                    return;
                }
                dynamic dataArray = JsonSerializer.Deserialize<JsonElement>(json);
                if (dataArray.ValueKind != JsonValueKind.Array)
                {
                    return;
                }
                foreach (JsonElement detection in dataArray.EnumerateArray())
                {
                    if (!detection.TryGetProperty("timestamp", out var timestampElement))
                    {
                        return;
                    }
                    if (!DateTime.TryParseExact(timestampElement.ToString(), "yyyy-MM-ddTHH:mm:ss.ffffffZ", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime timestamp))
                    {
                        return;
                    }
                    if (timestamp <= node.LastOrcaHelloDetectionTimestamp)
                    {
                        // No new detections.
                        return;
                    }

                    // Parse other properties.
                    if (!detection.TryGetProperty("confidence", out var confidenceElement))
                    {
                        return;
                    }
                    if (!confidenceElement.TryGetDouble(out double confidence))
                    {
                        return;
                    }
                    if (!detection.TryGetProperty("comments", out var comments))
                    {
                        return;
                    }
                    if (!detection.TryGetProperty("found", out var found))
                    {
                        return;
                    }

                    node.LastOrcaHelloDetectionTimestamp = timestamp;
                    node.LastOrcaHelloDetectionFound = found.ToString() == "yes";
                    node.LastOrcaHelloDetectionComments = comments.ToString();
                    node.LastOrcaHelloDetectionConfidence = (int)(confidence + 0.5);
                }

                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
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
                    Orcanode node = FindOrCreateOrcanodeByDataplicitySerial(context.Orcanodes, serial.ToString());
                    if (device.TryGetProperty("name", out var name))
                    {
                        string dataplicityName = name.ToString();
                        node.DataplicityName = dataplicityName;
                        if (node.DisplayName.IsNullOrEmpty())
                        {
                            node.DisplayName = Orcanode.DataplicityNameToDisplayName(dataplicityName);
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
                }

                MonitorState.GetFrom(context).LastUpdatedTimestampUtc = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
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
                foreach (JsonElement feed in dataArray.EnumerateArray())
                {
                    if (!feed.TryGetProperty("attributes", out JsonElement attributes))
                    {
                        continue;
                    }
                    if (!attributes.TryGetProperty("name", out var name))
                    {
                        continue;
                    }
                    Orcanode node = FindOrCreateOrcanodeByOrcasoundName(context.Orcanodes, name.ToString());
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
                }

                List<Orcanode> nodes = await context.Orcanodes.ToListAsync();
                foreach (Orcanode node in nodes)
                {
                    if (!node.S3Bucket.IsNullOrEmpty())
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

        private static void AddOrcanodeStreamStatusEvent(OrcanodeMonitorContext context, Orcanode node)
        {
            string value = (node.OrcasoundOnlineStatus == OrcanodeOnlineStatus.Online) ? "up" : "OFFLINE";
            var orcanodeEvent = new OrcanodeEvent(node, "stream status", value, DateTime.UtcNow);
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
            OrcanodeOnlineStatus oldStatus = node.OrcasoundOnlineStatus;

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

            OrcanodeOnlineStatus newStatus = node.OrcasoundOnlineStatus;
            if (newStatus != oldStatus)
            {
                AddOrcanodeStreamStatusEvent(context, node);
            }
        }
    }
}
