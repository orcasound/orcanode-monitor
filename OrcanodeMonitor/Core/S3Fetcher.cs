// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using OrcanodeMonitor.Models;
using System.Net.Http;
using static OrcanodeMonitor.Core.Fetcher;

namespace OrcanodeMonitor.Core
{
    public class S3Fetcher
    {
        private static Dictionary<string, List<string>> _s3FoldersCache = new Dictionary<string, List<string>>();

        /// <summary>
        /// Get the list of folders (representing .ts segment start times) for
        /// a given location ID from the public S3 bucket.
        /// </summary>
        /// <param name="locationIdString">Location ID to look in</param>
        /// <returns>List of folder names representing HLS start times</returns>
        public static async Task<List<string>> GetPublicS3FoldersAsync(string locationIdString)
        {
            // First try using a cached value.
            if (_s3FoldersCache.TryGetValue(locationIdString, out var cachedFolders))
            {
                return cachedFolders;
            }

            var config = new AmazonS3Config
            {
                RegionEndpoint = RegionEndpoint.USWest2
            };

            var client = new AmazonS3Client(new Amazon.Runtime.AnonymousAWSCredentials(), config);
            var allFolders = new List<string>();
            string continuationToken = null;

            do
            {
                var request = new ListObjectsV2Request
                {
                    BucketName = "audio-orcasound-net",
                    Prefix = locationIdString + "/hls/",
                    Delimiter = "/", // Group by folders
                    ContinuationToken = continuationToken
                };

                var response = await client.ListObjectsV2Async(request);

                var folderNames = response.CommonPrefixes
                    .Select(prefix => prefix.TrimEnd('/').Split('/').Last())
                    .ToList();

                allFolders.AddRange(folderNames);

                continuationToken = response.IsTruncated ?? false
                    ? response.NextContinuationToken
                    : null;
            } while (continuationToken != null);

            _s3FoldersCache[locationIdString] = allFolders;
            return allFolders;
        }

        /// <summary>
        /// Get the S3 timestamp that covers a specified time.
        /// </summary>
        /// <param name="node">Orcanode to check</param>
        /// <param name="dateTime">Time to cover</param>
        /// <param name="logger">Logger</param>
        /// <returns></returns>
        public async static Task<TimestampResult?> GetS3TimestampAsync(Orcanode node, DateTime dateTime, ILogger logger)
        {
            // Convert dateTime to Unix time (seconds since 1970-01-01).
            var originalDateTimeOffset = new DateTimeOffset(dateTime);
            long originalUnixTimeSeconds = originalDateTimeOffset.ToUnixTimeSeconds();

            List<string> folders = await GetPublicS3FoldersAsync(node.S3NodeName);

            // Find the most recent folder older than originalUnixTimeSeconds.
            long folderTimeSeconds = 0;
            string unixTimestampString = string.Empty;
            foreach (var folderName in folders)
            {
                if (long.TryParse(folderName, out long unixTime))
                {
                    if (unixTime <= originalUnixTimeSeconds && unixTime > folderTimeSeconds)
                    {
                        folderTimeSeconds = unixTime;
                        unixTimestampString = folderName;
                    }
                }
            }
            if (folderTimeSeconds == 0)
            {
                // No folder found.
                return null;
            }

            var offset = DateTimeOffset.FromUnixTimeSeconds(folderTimeSeconds);
            var result = new TimestampResult(unixTimestampString, offset);
            return result;
        }

        /// <summary>
        /// Get the audio sample for a given time.
        /// </summary>
        /// <param name="node">Orcanode to get audio from</param>
        /// <param name="unixTimestampString">Folder time, being the last restart time</param>
        /// <param name="dateTime">Time to get audio for</param>
        /// <param name="logger">Logger</param>
        /// <returns></returns>
        public async static Task<FrequencyInfo?> GetAudioSampleAsync(Orcanode node, string unixTimestampString, DateTime dateTime, ILogger logger)
        {
            // Compute the desired index.
            if (!long.TryParse(unixTimestampString, out long unixFolderSeconds))
            {
                return null;
            }
            long desiredUnixSeconds = new DateTimeOffset(dateTime).ToUnixTimeSeconds();
            long index = (desiredUnixSeconds - unixFolderSeconds) / 10;
            string url = $"https://{node.S3Bucket}.s3.amazonaws.com/{node.S3NodeName}/hls/{unixTimestampString}/live{index:000}.ts";
            return await GetExactAudioSampleAsync(node, new Uri(url), logger);
        }
    }
}
