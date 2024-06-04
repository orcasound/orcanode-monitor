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

        public EnumerateNodesResult()
        {
            NodeList = new List<Orcanode>();
            Succeeded = false;
        }
    }
    public class Fetcher
    {
        private static HttpClient httpClient = new HttpClient();
        private static string url = "https://live.orcasound.net/api/json/feeds";
        private static DateTime unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// Get the current list of Orcanodes from orcasound.net.
        /// </summary>
        /// <returns>EnumerateNodesResult object</returns>
        public async static Task<EnumerateNodesResult> EnumerateNodesAsync()
        {
            var result = new EnumerateNodesResult();
            string json = "";
            try
            {
                json = await httpClient.GetStringAsync(url);
            }
            catch (Exception)
            {
            }
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
                var node = new Orcanode(name.ToString(), nodeName.ToString(), bucket.ToString());
                result.NodeList.Add(node);
            }
            result.Succeeded = true;
            return result;
        }

        /// <summary>
        /// Convert a unix timestamp in string form to a DateTime value.
        /// </summary>
        /// <param name="unixTimeStampString">Unix timestamp string to parse</param>
        /// <returns>DateTime value or null on failure</returns>
        private static DateTime? UnixTimeStampToDateTime(string unixTimeStampString)
        {
            if (!double.TryParse(unixTimeStampString, out var unixTimeStampDouble))
            {
                return null;
            }

            // A Unix timestamp is a count of seconds past the Unix epoch.
            DateTime dateTime = unixEpoch.AddSeconds(unixTimeStampDouble);
            return dateTime;
        }

        /// <summary>
        /// Update the timestamps for a given Orcanode by querying files on S3.
        /// </summary>
        /// <param name="node">Orcanode to update</param>
        /// <returns></returns>
        public async static Task UpdateLatestTimestampAsync(Orcanode node)
        {
            string url = "https://" + node.Bucket + ".s3.amazonaws.com/" + node.NodeName + "/latest.txt";
            HttpResponseMessage response = await httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                return;
            }

            string content = await response.Content.ReadAsStringAsync();
            DateTime? latestRecorded = UnixTimeStampToDateTime(content);
            node.LatestRecorded = UnixTimeStampToDateTime(content);

            DateTimeOffset? offset = response.Content.Headers.LastModified;
            if (offset.HasValue)
            {
                node.LatestUploaded = offset.Value.UtcDateTime;
            }
        }
    }
}
