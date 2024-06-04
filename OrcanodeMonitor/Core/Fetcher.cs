﻿// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using System;
using System.Dynamic;
using System.Text.Json;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Xml.Linq;

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
        private static HttpClient _httpClient = new HttpClient();
        private static string _orcasoundFeedsUrl = "https://live.orcasound.net/api/json/feeds";
        private static DateTime _unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Get the current list of Orcanodes from orcasound.net.
        /// </summary>
        /// <returns>EnumerateNodesResult object</returns>
        public async static Task<EnumerateNodesResult> EnumerateNodesAsync()
        {
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
        /// Convert a unix timestamp in integer form to a DateTime value.
        /// </summary>
        /// <param name="unixTimeStamp">Unix timestamp</param>
        /// <returns>DateTime value or null on failure</returns>
        public static DateTime? UnixTimeStampToDateTime(long unixTimeStamp)
        {
            // A Unix timestamp is a count of seconds past the Unix epoch.
            DateTime dateTime = _unixEpoch.AddSeconds(unixTimeStamp);
            return dateTime;
        }

        /// <summary>
        /// Convert a unix timestamp in string form to a DateTime value.
        /// </summary>
        /// <param name="unixTimeStampString">Unix timestamp string to parse</param>
        /// <returns>DateTime value or null on failure</returns>
        private static DateTime? UnixTimeStampStringToDateTime(string unixTimeStampString)
        {
            if (!long.TryParse(unixTimeStampString, out var unixTimeStamp))
            {
                return null;
            }

            return UnixTimeStampToDateTime(unixTimeStamp);
        }

        public static long DateTimeToUnixTimeStamp(DateTime dateTime)
        {
            long unixTime = (long)(dateTime - _unixEpoch).TotalSeconds;
            return unixTime;
        }

        /// <summary>
        /// Update the timestamps for a given Orcanode by querying files on S3.
        /// </summary>
        /// <param name="node">Orcanode to update</param>
        /// <returns></returns>
        public async static Task UpdateLatestTimestampAsync(Orcanode node)
        {
            string url = "https://" + node.Bucket + ".s3.amazonaws.com/" + node.NodeName + "/latest.txt";
            HttpResponseMessage response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            string content = await response.Content.ReadAsStringAsync();
            string unixTimestampString = content.TrimEnd();
            DateTime? latestRecorded = UnixTimeStampStringToDateTime(unixTimestampString);
            if (latestRecorded.HasValue)
            {
                node.LatestRecorded = latestRecorded;

                DateTimeOffset? offset = response.Content.Headers.LastModified;
                if (offset.HasValue)
                {
                    node.LatestUploaded = offset.Value.UtcDateTime;
                }
            }

            await UpdateManifestTimestampAsync(node, unixTimestampString);
        }

        /// <summary>
        /// Update the ManifestUpdated timestamp for a given Orcanode by querying S3.
        /// </summary>
        /// <param name="node">Orcanode to update</param>
        /// <param name="unixTimestampString">Value in the latest.txt file</param>
        /// <returns></returns>
        public async static Task UpdateManifestTimestampAsync(Orcanode node, string unixTimestampString)
        {
            string url = "https://" + node.Bucket + ".s3.amazonaws.com/" + node.NodeName + "/hls/" + unixTimestampString + "/live.m3u8";
            HttpResponseMessage response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            DateTimeOffset? offset = response.Content.Headers.LastModified;
            if (offset.HasValue)
            {
                node.ManifestUpdated = offset.Value.UtcDateTime;
            }
        }
    }
}