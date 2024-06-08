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

        private static Orcanode FindOrCreateOrcanodeByDataplicitySerial(List<Orcanode> nodeList, string serial)
        {
            foreach (Orcanode node in nodeList)
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
        private static Orcanode FindOrCreateOrcanodeByOrcasoundName(List<Orcanode> nodeList, string orcasoundName)
        {
            foreach (Orcanode node in nodeList)
            {
                if (node.OrcasoundName == orcasoundName)
                {
                    return node;
                }
                if ((node.OrcasoundName == null) && orcasoundName.Contains(node.DisplayName))
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
        /// <returns></returns>
        public async static Task UpdateOrcaHelloDataAsync()
        {
            foreach (Orcanode node in State.Nodes)
            {
                await UpdateOrcaHelloDataAsync(node);
            }
        }

        public async static Task UpdateOrcaHelloDataAsync(Orcanode node)
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
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Update Orcanode state by querying dataplicity.com.
        /// </summary>
        /// <returns></returns>
        public async static Task UpdateDataplicityDataAsync()
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
                    Orcanode node = FindOrCreateOrcanodeByDataplicitySerial(State.Nodes, serial.ToString());
                    if (device.TryGetProperty("name", out var name))
                    {
                        string dataplicityName = name.ToString();
                        node.DataplicityName = dataplicityName;
                        if (node.DisplayName == null)
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
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Update the current list of Orcanodes using data from orcasound.net.
        /// </summary>
        /// <returns></returns>
        public async static Task UpdateOrcasoundDataAsync()
        {
            string json = "";
            try
            {
                json = await _httpClient.GetStringAsync(_orcasoundFeedsUrl);
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
                    Orcanode node = FindOrCreateOrcanodeByOrcasoundName(State.Nodes, name.ToString());
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

                foreach (Orcanode node in State.Nodes)
                {
                    if (node.S3Bucket != null)
                    {
                        await Fetcher.UpdateS3DataAsync(node, DateTime.UtcNow);
                    }
                }
            }
            catch (Exception)
            {
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
            if (utcDateTime == null)
            {
                return null;
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
        /// <param name="node">Orcanode to update</param>
        /// <param name="responseTimestamp">Timestamp at which the result was returned</param>
        /// <returns></returns>
        public async static Task UpdateS3DataAsync(Orcanode node, DateTime responseTimestamp)
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

            await UpdateManifestTimestampAsync(node, unixTimestampString, responseTimestamp);
        }

        /// <summary>
        /// Update the ManifestUpdated timestamp for a given Orcanode by querying S3.
        /// </summary>
        /// <param name="node">Orcanode to update</param>
        /// <param name="unixTimestampString">Value in the latest.txt file</param>
        /// <param name="responseTimestamp">Timestamp at which the result was returned</param>
        /// <returns></returns>
        public async static Task UpdateManifestTimestampAsync(Orcanode node, string unixTimestampString, DateTime responseTimestamp)
        {
            string url = "https://" + node.S3Bucket + ".s3.amazonaws.com/" + node.S3NodeName + "/hls/" + unixTimestampString + "/live.m3u8";
            HttpResponseMessage response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            DateTimeOffset? offset = response.Content.Headers.LastModified;
            if (offset.HasValue)
            {
                node.ManifestUpdatedUtc = offset.Value.UtcDateTime;
            }

            node.LastCheckedUtc = responseTimestamp.ToUniversalTime();
        }
    }
}
