using System;
using System.Dynamic;
using System.Text.Json;
using System.Net.Http;
using System.Text.Json.Nodes;

namespace OrcanodeMonitor.Core
{
    public class FetchNodesResult
    {
        public List<Orcanode> NodeList { get; private set; }
        public bool Succeeded { get; set; }

        public FetchNodesResult()
        {
            NodeList = new List<Orcanode>();
            Succeeded = false;
        }
    }
    public class Fetcher
    {
        private static HttpClient httpClient = new HttpClient();
        private static string url = "https://live.orcasound.net/api/json/feeds";

        public async static Task<FetchNodesResult> FetchNodesAsync()
        {
            var result = new FetchNodesResult();
            string json = await httpClient.GetStringAsync(url);
            if (json == null)
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
                if (!attributes.TryGetProperty("node_name", out var node_name))
                {
                    continue;
                }
                if (!attributes.TryGetProperty("bucket", out var bucket))
                {
                    continue;
                }
                var node = new Orcanode(name.ToString(), node_name.ToString(), bucket.ToString());
                result.NodeList.Add(node);
            }
            result.Succeeded = true;
            return result;
        }
    }
}
