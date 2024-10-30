﻿// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.IdentityModel.Tokens;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Net.Http;
using System.Text.Json;

namespace OrcanodeMonitor.Core
{
    public class MezmoFetcher
    {
        private static HttpClient _httpClient = new HttpClient();
        private static string _mezmoViewsUrl = "https://api.mezmo.com/v1/config/view";
        private static string _mezmoHostsUrl = "https://api.mezmo.com/v1/usage/hosts";
        private static string _mezmoLogUrl = "https://api.mezmo.com/v1/export";

        const int DEFAULT_MEZMO_LOG_SECONDS = 300;
        private static string _mezmoLogSeconds = Environment.GetEnvironmentVariable("MEZMO_LOG_SECONDS") ?? string.Empty;

        private static int MezmoLogSeconds
        {
            get
            {
                int seconds;
                bool success = int.TryParse(_mezmoLogSeconds, out seconds);
                return (success && seconds > 0) ? seconds : DEFAULT_MEZMO_LOG_SECONDS;
            }
        }

        /// <summary>
        /// Fetch string content from a Mezmo URL.
        /// </summary>
        /// <param name="url">URL to get content from</param>
        /// <returns>String content.  An empty string may mean empty content or an HTTP error.</returns>
        public async static Task<string> GetMezmoDataAsync(string url)
        {
            try
            {
                string? service_key = Environment.GetEnvironmentVariable("MEZMO_SERVICE_KEY");
                if (string.IsNullOrEmpty(service_key))
                {
                    Console.Error.WriteLine($"MEZMO_SERVICE_KEY not configured");
                    return string.Empty; // No content.
                }

                using (var request = new HttpRequestMessage
                {
                    RequestUri = new Uri(url),
                    Method = HttpMethod.Get,
                })
                {
                    request.Headers.Add("servicekey", service_key);
                    using HttpResponseMessage response = await _httpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                string msg = ex.ToString();
                Console.Error.WriteLine($"Exception in GetMezmoDataAsync: {msg}");
                return string.Empty; // No content.
            }
        }

        /// <summary>
        /// Fetch Mezmo recent Mezmo log entries for a given node.
        /// </summary>
        /// <param name="node">Node to fetch log entries for</param>
        /// <returns>Null on error, or a list of 0 or more JSON elements on success</returns>
        private async static Task<List<JsonElement>?> GetMezmoRecentLogAsync(Orcanode node)
        {
            try
            {
                int to = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                if (to < MezmoLogSeconds)
                {
                    logger.LogError("MezmoLogSeconds is greater than the current Unix time.");
                    return null;
                }
                int from = to - MezmoLogSeconds;
                string url = $"{_mezmoLogUrl}?from={from}&to={to}&hosts={node.S3NodeName}";
                string jsonString = await GetMezmoDataAsync(url);
                if (jsonString.IsNullOrEmpty())
                {
                    // Error.
                    return null;
                }

                // Mezmo does not return a legal JSON array, but instead a newline-separated
                // set of JSON objects.  Convert them to a JSON array now.
                string[] jsonObjects = jsonString.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                string jsonArray = "[" + string.Join(",", jsonObjects) + "]";

                JsonElement logArray = JsonSerializer.Deserialize<JsonElement>(jsonArray);
                if (logArray.ValueKind != JsonValueKind.Array)
                {
                    Console.Error.WriteLine($"Invalid logArray kind in GetMezmoRecentLogAsync: {logArray.ValueKind}");
                    return null;
                }

                List<JsonElement> result = new List<JsonElement>();
                foreach (var item in logArray.EnumerateArray())
                {
                    result.Add(item);
                }

                return result;
            }
            catch (Exception ex)
            {
                string msg = ex.ToString();
                Console.Error.WriteLine($"Exception in GetMezmoRecentLogAsync: {msg}");
                return null;
            }
        }

        /// <summary>
        /// Update Orcanode state by querying mezmo.com hosts usage.
        /// </summary>
        /// <param name="context">Database context to update</param>
        /// <param name="logger"></param>
        /// <returns></returns>
        public async static Task UpdateMezmoHostsAsync(OrcanodeMonitorContext context, ILogger logger)
        {
            try
            {
                string jsonArray = await GetMezmoDataAsync(_mezmoHostsUrl);
                if (jsonArray.IsNullOrEmpty())
                {
                    // Error so do nothing.
                    return;
                }

                var originalList = context.Orcanodes.ToList();

                // Create a list to track what nodes are no longer returned.
                var unfoundList = originalList.ToList();

                JsonElement hostsArray = JsonSerializer.Deserialize<JsonElement>(jsonArray);
                if (hostsArray.ValueKind != JsonValueKind.Array)
                {
                    logger.LogError($"Invalid hostsArray kind in UpdateMezmoHostsAsync: {hostsArray.ValueKind}");
                    return;
                }
                foreach (JsonElement view in hostsArray.EnumerateArray())
                {
                    if (!view.TryGetProperty("name", out var name))
                    {
                        logger.LogError($"Missing name in UpdateMezmoHostsAsync result");
                        continue;
                    }
                    if (name.ValueKind != JsonValueKind.String)
                    {
                        logger.LogError($"Invalid name kind in UpdateMezmoHostsAsync: {name.ValueKind}");
                        continue;
                    }
                    if (!view.TryGetProperty("current_total", out var currentTotal))
                    {
                        logger.LogError($"Missing current_total in UpdateMezmoHostsAsync result");
                        continue;
                    }
                    if (currentTotal.ValueKind != JsonValueKind.Number)
                    {
                        logger.LogError($"Invalid currentTotal kind in UpdateMezmoHostsAsync: {currentTotal.ValueKind}");
                        continue;
                    }

                    // Remove any matching nodes from the unfound list.
                    List<Orcanode> oldListNodes = unfoundList.FindAll(a => a.S3NodeName == name.ToString());
                    foreach (Orcanode oldListNode in oldListNodes)
                    {
                        unfoundList.Remove(oldListNode);
                    }

                    Orcanode? node = originalList.Find(a => a.S3NodeName == name.ToString());
                    if (node == null)
                    {
                        // TODO: create a node?
                        logger.LogWarning($"No node found for Mezmo name: {name}");
                        continue;
                    }
                    OrcanodeOnlineStatus oldStatus = node.MezmoStatus;

                    List<JsonElement>? log = await GetMezmoRecentLogAsync(node);
                    if (log == null)
                    {
                        // Mezmo error, so results are indeterminate.
                        logger.LogError($"Mezmo error for node: {name}");
                        continue;
                    }
                    node.MezmoLogSize = log.Count;

                    if (oldStatus == OrcanodeOnlineStatus.Absent)
                    {
                        // Save changes to make the node have an ID before we can
                        // possibly generate any events.
                        await context.SaveChangesAsync();
                    }

                    OrcanodeOnlineStatus newStatus = node.MezmoStatus;
                    if (newStatus != oldStatus)
                    {
                        AddMezmoStatusEvent(context, node);
                    }
                }

                // Mark any remaining unfound nodes as absent.
                foreach (var unfoundNode in unfoundList)
                {
                    Orcanode? oldNode = originalList.Find(a => a.S3NodeName == unfoundNode.S3NodeName);
                    if (oldNode != null)
                    {
                        logger.LogInformation($"Mezmo node no longer found: {unfoundNode.S3NodeName}");
                        oldNode.MezmoLogSize = 0;
                    }
                }

                MonitorState.GetFrom(context).LastUpdatedTimestampUtc = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in UpdateMezmoHostsAsync");
            }
        }

        /// <summary>
        /// Update Orcanode state by querying mezmo.com views.
        /// </summary>
        /// <param name="context">Database context to update</param>
        /// <param name="logger"></param>
        /// <returns></returns>
        public async static Task UpdateMezmoViewsAsync(OrcanodeMonitorContext context, ILogger logger)
        {
            try
            {
                string jsonArray = await GetMezmoDataAsync(_mezmoViewsUrl);
                if (jsonArray.IsNullOrEmpty())
                {
                    // Error so do nothing.
                    return;
                }

                var originalList = context.Orcanodes.ToList();

                // Create a list to track what nodes are no longer returned.
                var unfoundList = originalList.ToList();

                JsonElement viewsArray = JsonSerializer.Deserialize<JsonElement>(jsonArray);
                if (viewsArray.ValueKind != JsonValueKind.Array)
                {
                    logger.LogError($"Invalid viewsArray kind in UpdateMezmoViewsAsync: {viewsArray.ValueKind}");
                    return;
                }
                foreach (JsonElement view in viewsArray.EnumerateArray())
                {
                    if (!view.TryGetProperty("hosts", out var hostsArray))
                    {
                        logger.LogError($"Missing hosts in UpdateMezmoViewsAsync result");
                        continue;
                    }
                    if (hostsArray.ValueKind != JsonValueKind.Array)
                    {
                        logger.LogError($"Invalid hostsArray kind in UpdateMezmoViewsAsync: {hostsArray.ValueKind}");
                        continue;
                    }
                    if (!view.TryGetProperty("viewid", out var viewid))
                    {
                        logger.LogError($"Missing viewid in UpdateMezmoViewsAsync result");
                        continue;
                    }
                    if (viewid.ToString().IsNullOrEmpty())
                    {
                        logger.LogError($"Empty viewid in UpdateMezmoViewsAsync result");
                        continue;
                    }
                    foreach (JsonElement host in hostsArray.EnumerateArray())
                    {
                        if (host.ValueKind != JsonValueKind.String)
                        {
                            logger.LogError($"Invalid host kind in UpdateMezmoViewsAsync: {host.ValueKind}");
                            continue;
                        }

                        // Remove any matching nodes from the unfound list.
                        List<Orcanode> oldListNodes = unfoundList.FindAll(a => a.S3NodeName == host.ToString());
                        foreach (Orcanode oldListNode in oldListNodes)
                        {
                            unfoundList.Remove(oldListNode);
                        }

                        Orcanode? node = originalList.Find(a => a.S3NodeName == host.ToString());
                        if (node == null)
                        {
                            // TODO: create a node?
                            logger.LogWarning($"No node found for Mezmo name in UpdateMezmoViewsAsync: {host.ToString()}");
                            continue;
                        }
                        OrcanodeOnlineStatus oldStatus = node.MezmoStatus;
                        node.MezmoViewId = viewid.ToString();

                        if (oldStatus == OrcanodeOnlineStatus.Absent)
                        {
                            // Save changes to make the node have an ID before we can
                            // possibly generate any events.
                            await context.SaveChangesAsync();
                        }

                        OrcanodeOnlineStatus newStatus = node.MezmoStatus;
                        if (newStatus != oldStatus)
                        {
                            AddMezmoStatusEvent(context, node);
                        }
                    }
                }

                // Mark any remaining unfound nodes as absent.
                foreach (var unfoundNode in unfoundList)
                {
                    Orcanode? oldNode = originalList.Find(a => a.S3NodeName == unfoundNode.S3NodeName);
                    if (oldNode != null)
                    {
                        oldNode.MezmoViewId = string.Empty;
                    }
                }

                MonitorState.GetFrom(context).LastUpdatedTimestampUtc = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in UpdateMezmoViewsAsync");
            }
        }

        public async static Task UpdateMezmoDataAsync(OrcanodeMonitorContext context, ILogger logger)
        {
            await UpdateMezmoHostsAsync(context, logger);
            await UpdateMezmoViewsAsync(context, logger);
        }

        private static void AddMezmoStatusEvent(OrcanodeMonitorContext context, Orcanode node)
        {
            string value = node.MezmoStatus.ToString();
            Fetcher.AddOrcanodeEvent(context, node, "Mezmo logging", value);
        }
    }
}
