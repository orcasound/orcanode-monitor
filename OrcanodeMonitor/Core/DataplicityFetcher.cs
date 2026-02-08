// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Text.Json;

namespace OrcanodeMonitor.Core
{
    public class DataplicityFetcher
    {
        private static string _dataplicityDevicesUrl = "https://apps.dataplicity.com/devices/";
        private static string _defaultProdS3Bucket = "audio-orcasound-net";
        private static string _defaultDevS3Bucket = "dev-streaming-orcasound-net";

        public async static Task<string> GetDataplicityDataAsync(string serial, ILogger logger)
        {
            try
            {
                string? orcasound_dataplicity_token = Fetcher.GetConfig("ORCASOUND_DATAPLICITY_TOKEN");
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
                    using HttpResponseMessage response = await Fetcher.HttpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in GetDataplicityDataAsync: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Reboot a hydrophone node.
        /// </summary>
        /// <param name="node">Node to reboot</param>
        /// <param name="logger">Logger object</param>
        /// <returns>true on success, false on failure</returns>
        public async static Task<bool> RebootDataplicityDeviceAsync(Orcanode node, ILogger logger)
        {
            try
            {
                string deviceJson = await GetDataplicityDataAsync(node.DataplicitySerial, logger);
                if (deviceJson.IsNullOrEmpty())
                {
                    logger.LogWarning($"Node {node.DisplayName} needs a reboot, but couldn't get its Dataplicity data.");
                    return false;
                }

                // Parse out the reboot URL from the device JSON.
                var device = JsonSerializer.Deserialize<JsonElement>(deviceJson);
                if (device.ValueKind != JsonValueKind.Object)
                {
                    logger.LogError($"Invalid device kind in RebootDataplicityDeviceAsync: {device.ValueKind}");
                    return false;
                }
                if (!device.TryGetProperty("reboot_url", out JsonElement rebootUrl))
                {
                    logger.LogError($"Missing reboot_url in RebootDataplicityDeviceAsync result");
                    return false;
                }
                if (rebootUrl.ValueKind != JsonValueKind.String)
                {
                    logger.LogError($"Invalid reboot_url kind in RebootDataplicityDeviceAsync: {rebootUrl.ValueKind}");
                    return false;
                }
                string rebootUrlString = rebootUrl.ToString();
                if (rebootUrlString.IsNullOrEmpty())
                {
                    logger.LogError($"Empty reboot_url in RebootDataplicityDeviceAsync result");
                    return false;
                }

                // Validate URL to avoid Server-Side Request Forgery.
                if (!Uri.TryCreate(rebootUrlString, UriKind.Absolute, out var rebootUri))
                {
                    logger.LogError("Invalid reboot_url format in RebootDataplicityDeviceAsync");
                    return false;
                }
                var host = rebootUri.IdnHost;
                bool hostAllowed =
                    host.Equals("dataplicity.com", StringComparison.OrdinalIgnoreCase) ||
                    host.EndsWith(".dataplicity.com", StringComparison.OrdinalIgnoreCase);
                if (!rebootUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) || !hostAllowed)
                {
                    logger.LogError($"Blocked non-Dataplicity reboot_url: {rebootUri}");
                    return false;
                }
                if (!rebootUri.IsDefaultPort && rebootUri.Port != 443)
                {
                    logger.LogError($"Blocked non-standard port in reboot_url: {rebootUri}");
                    return false;
                }

                // Get the dataplicity auth token.
                string? orcasound_dataplicity_token = Fetcher.GetConfig("ORCASOUND_DATAPLICITY_TOKEN");
                if (orcasound_dataplicity_token == null)
                {
                    logger.LogError("ORCASOUND_DATAPLICITY_TOKEN not found");
                    return false;
                }

                using (var request = new HttpRequestMessage
                {
                    RequestUri = new Uri(rebootUrlString),
                    Method = HttpMethod.Post,
                })
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Token", orcasound_dataplicity_token);
                    using HttpResponseMessage response = await Fetcher.HttpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in RebootDataplicityDeviceAsync: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Update Orcanode state by querying dataplicity.com.
        /// </summary>
        /// <param name="context">Database context to update</param>
        /// <param name="logger"></param>
        /// <returns></returns>
        public async static Task UpdateDataplicityDataAsync(IOrcanodeMonitorContext context, ILogger logger)
        {
            try
            {
                string jsonArray = await GetDataplicityDataAsync(string.Empty, logger);
                if (jsonArray.IsNullOrEmpty())
                {
                    // Indeterminate result, so don't update anything.
                    return;
                }

                List<Orcanode> originalList = await context.Orcanodes.ToListAsync();

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

                    Orcanode node = Fetcher.FindOrCreateOrcanodeByDataplicitySerial(context.Orcanodes, serial.ToString(), out OrcanodeOnlineStatus oldStatus);
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
                        await Fetcher.SaveChangesAsync(context);
                    }

                    // Trigger any event changes.
                    OrcanodeOnlineStatus newStatus = node.DataplicityConnectionStatus;
                    if (newStatus != oldStatus)
                    {
                        AddDataplicityConnectionStatusEvent(context, node, logger);
                    }
                    if (oldStatus != OrcanodeOnlineStatus.Absent)
                    {
                        if (oldAgentUpgradeStatus != node.DataplicityUpgradeStatus)
                        {
                            AddDataplicityAgentUpgradeStatusChangeEvent(context, node, logger);
                        }
                        if (oldDiskCapacityInGigs != node.DiskCapacityInGigs)
                        {
                            AddDiskCapacityChangeEvent(context, node, logger);
                        }
                    }
                }

                // Mark any remaining unfound nodes as absent.
                foreach (var unfoundNode in unfoundList)
                {
                    var oldNode = Fetcher.FindOrcanodeByDataplicitySerial(originalList, unfoundNode.DataplicitySerial, out OrcanodeOnlineStatus unfoundNodeStatus);
                    if (oldNode != null)
                    {
                        oldNode.DataplicityOnline = null;
                    }
                }

                MonitorState.GetFrom(context).LastUpdatedTimestampUtc = DateTime.UtcNow;
                await Fetcher.SaveChangesAsync(context);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in UpdateDataplicityDataAsync: {ex.Message}");
            }
        }

        /// <summary>
        /// Check for any nodes that need a reboot to fix a container restart issue.
        /// </summary>
        /// <param name="context">Database context to update</param>
        /// <param name="logger">Logger</param>
        /// <returns></returns>
        public async static Task CheckForRebootsNeededAsync(IOrcanodeMonitorContext context, ILogger logger)
        {
            var originalList = await context.Orcanodes.ToListAsync();
            foreach (Orcanode? node in originalList)
            {
                if (!node.NeedsRebootForContainerRestart)
                {
                    continue;
                }
                if (Fetcher.IsReadOnly)
                {
                    logger.LogInformation($"Node {node.DisplayName} needs a reboot, but we are in read-only mode.");
                    continue;
                }
                if (node.DataplicitySerial.IsNullOrEmpty())
                {
                    logger.LogWarning($"Node {node.DisplayName} needs a reboot, but has no Dataplicity serial.");
                    continue;
                }
                bool success = await RebootDataplicityDeviceAsync(node, logger);
                if (success)
                {
                    logger.LogInformation($"Node {node.DisplayName} rebooted successfully.");
                }
                else
                {
                    logger.LogWarning($"Node {node.DisplayName} needs a reboot, but the reboot request failed.");
                }

                // Wait a bit to avoid hammering the Dataplicity API.
                await Task.Delay(2000);
            }
        }

        private static void AddDataplicityConnectionStatusEvent(IOrcanodeMonitorContext context, Orcanode node, ILogger logger)
        {
            string value = node.DataplicityConnectionStatus.ToString();
            Fetcher.AddOrcanodeEvent(context, logger, node, OrcanodeEventTypes.DataplicityConnection, value);
        }

        private static void AddDataplicityAgentUpgradeStatusChangeEvent(IOrcanodeMonitorContext context, Orcanode node, ILogger logger)
        {
            string value = node.DataplicityUpgradeStatus.ToString();
            Fetcher.AddOrcanodeEvent(context, logger, node, OrcanodeEventTypes.AgentUpgradeStatus, value);
        }

        private static void AddDiskCapacityChangeEvent(IOrcanodeMonitorContext context, Orcanode node, ILogger logger)
        {
            string value = string.Format("{0}G", node.DiskCapacityInGigs);
            Fetcher.AddOrcanodeEvent(context, logger, node, OrcanodeEventTypes.SDCardSize, value);
        }
    }
}
