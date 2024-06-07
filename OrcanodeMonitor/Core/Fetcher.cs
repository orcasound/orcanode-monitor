// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using System;
using System.Dynamic;
using System.Text.Json;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using System.Net.Sockets;

namespace OrcanodeMonitor.Core
{
    public class EnumerateNodesResult
    {
        public List<Orcanode> NodeList { get; private set; }
        public bool Succeeded { get; set; }
        public DateTime Timestamp { get; private set; }

        public EnumerateNodesResult(DateTime timestamp)
        {
            NodeList = new List<Orcanode>();
            Succeeded = false;
            Timestamp = timestamp;
        }
    }
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

        /// <summary>
        /// Look for an Orcanode by Dataplicity name in a list and create one if not found.
        /// </summary>
        /// <param name="nodeList"></param>
        /// <param name="dataplicityName"></param>
        /// <returns></returns>
        private static Orcanode FindOrCreateOrcanode(List<Orcanode> nodeList, string dataplicityName)
        {
            foreach (Orcanode node in nodeList)
            {
                if (node.DataplicityName == dataplicityName)
                {
                    return node;
                }
                if ((node.DataplicityName == null) && dataplicityName.Contains(node.DisplayName))
                {
                    node.DataplicityName = dataplicityName;
                    return node;
                }
            }

            var newNode = new Orcanode(dataplicityName);
            nodeList.Add(newNode);
            return newNode;
        }

        public async static Task EnumerateDataplicityNodesAsync(EnumerateNodesResult result)
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
                    if (!device.TryGetProperty("name", out var name))
                    {
                        continue;
                    }
                    Orcanode node = FindOrCreateOrcanode(result.NodeList, name.ToString());
                    if (device.TryGetProperty("online", out var online))
                    {
                        node.DataplicityOnline = online.GetBoolean();
                    }
                    if (device.TryGetProperty("description", out var description))
                    {
                        node.DataplicityDescription = description.ToString();
                    }
                    if (device.TryGetProperty("serial", out var serial))
                    {
                        node.DataplicityId = serial.ToString();
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

                // TODO: how should we merge the Succeeded value?
                result.Succeeded = true;
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Get the current list of Orcanodes from orcasound.net and dataplicity.com.
        /// </summary>
        /// <returns>EnumerateNodesResult object</returns>
        public async static Task<EnumerateNodesResult> EnumerateOrcasoundNodesAsync()
        {
            // TODO: move this outside.
            var result = new EnumerateNodesResult(DateTime.Now);
            string json = "";
            try
            {
                json = await _httpClient.GetStringAsync(_orcasoundFeedsUrl);
                if (json == "")
                {
                    return result;
                }
                dynamic response = JsonSerializer.Deserialize<ExpandoObject>(json);
                if (response == null)
                {
                    return result;
                }
                JsonElement dataArray = response.data;
                if (dataArray.ValueKind != JsonValueKind.Array)
                {
                    return result;
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
                    if (!attributes.TryGetProperty("node_name", out var nodeName))
                    {
                        continue;
                    }
                    if (!attributes.TryGetProperty("bucket", out var bucket))
                    {
                        continue;
                    }
                    if (!attributes.TryGetProperty("slug", out var slug))
                    {
                        continue;
                    }
                    var node = new Orcanode(name.ToString(), nodeName.ToString(), bucket.ToString(), slug.ToString());
                    result.NodeList.Add(node);
                }
                result.Succeeded = true;
            }
            catch (Exception)
            {
            }
            return result;
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
        public async static Task UpdateLatestTimestampAsync(Orcanode node, DateTime responseTimestamp)
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
