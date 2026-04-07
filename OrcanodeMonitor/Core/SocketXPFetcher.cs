// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Text.Json;

namespace OrcanodeMonitor.Core
{
    public class SocketXPFetcher
    {
        private static readonly string _socketXPDevicesUrl = "https://api.socketxp.com/v1/devices/1/100";
        private static readonly string _defaultProdS3Bucket = "audio-orcasound-net";
        private static readonly string _defaultDevS3Bucket = "dev-streaming-orcasound-net";

        /// <summary>
        /// Fetch the list of devices from SocketXP, in JSON format.
        /// If a deviceId is provided, fetch just that device.  Otherwise, fetch all devices.
        /// </summary>
        /// <param name="deviceId">ID of device to fetch, or empty to fetch all</param>
        /// <param name="logger">Logger</param>
        /// <returns>JSON object containing device info</returns>
        public async static Task<string> GetSocketXPDataAsync(string deviceId, ILogger logger)
        {
            try
            {
                string? orcasound_socketxp_token = Fetcher.GetConfig("ORCASOUND_SOCKETXP_TOKEN");
                if (!Fetcher.IsOffline && (orcasound_socketxp_token == null))
                {
                    logger.LogError("ORCASOUND_SOCKETXP_TOKEN not found");
                    return string.Empty;
                }

                string url = _socketXPDevicesUrl;
                using (var request = new HttpRequestMessage
                {
                    RequestUri = new Uri(url),
                    Method = HttpMethod.Get,
                })
                {
                    // If deviceId is provided, send it in the GET body.
                    if (!string.IsNullOrEmpty(deviceId))
                    {
                        string body = "[{\"DeviceId\":\"" + deviceId + "\"}]";
                        request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
                    }

                    // Set the token if we have one (in offline mode, we won't have one).
                    if (!string.IsNullOrEmpty(orcasound_socketxp_token))
                    {
                        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", orcasound_socketxp_token);
                    }
                    using HttpResponseMessage response = await Fetcher.HttpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in GetSocketXPDataAsync: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Reboot a hydrophone node.
        /// </summary>
        /// <param name="node">Node to reboot</param>
        /// <param name="logger">Logger object</param>
        /// <returns>true on success, false on failure</returns>
        public async static Task<bool> RebootSocketXPDeviceAsync(Orcanode node, ILogger logger)
        {
            try
            {
                // Get the SocketXP auth token.
                string? orcasound_socketxp_token = Fetcher.GetConfig("ORCASOUND_SOCKETXP_TOKEN");
                if (orcasound_socketxp_token == null)
                {
                    logger.LogError("ORCASOUND_SOCKETXP_TOKEN not found");
                    return false;
                }

                const string artifactId = "87699fef-4858-4e3d-a134-bdb9f73a978a";
                string jobName = $"reboot-{node.S3NodeName}-{Random.Shared.Next()}";
                string rebootUrlString = "https://api.socketxp.com/v1/job";

                // Build the JSON body.
                var payload = new
                {
                    Name = jobName,
                    DeviceId = node.SocketXPDeviceId,
                    ArtifactId = artifactId
                };

                string json = System.Text.Json.JsonSerializer.Serialize(payload);

                using (var request = new HttpRequestMessage
                {
                    RequestUri = new Uri(rebootUrlString),
                    Method = HttpMethod.Post,
                    Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
                })
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", orcasound_socketxp_token);
                    using HttpResponseMessage response = await Fetcher.HttpClient.SendAsync(request);
                    response.EnsureSuccessStatusCode();

                    string responseContent = await response.Content.ReadAsStringAsync();

                    string? jobId = null;
                    if (!string.IsNullOrWhiteSpace(responseContent))
                    {
                        try
                        {
                            using JsonDocument responseJson = JsonDocument.Parse(responseContent);
                            JsonElement root = responseJson.RootElement;
                            if (root.ValueKind == JsonValueKind.Object &&
                                root.TryGetProperty("JobId", out JsonElement jobIdElement))
                            {
                                jobId = jobIdElement.GetString();
                            }
                        }
                        catch (JsonException)
                        {
                            logger.LogWarning("SocketXP reboot response for node {NodeName} was not valid JSON: {ResponseContent}", node.S3NodeName, responseContent);
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(jobId))
                    {
                        logger.LogInformation("Created SocketXP reboot job {JobId} for node {NodeName}", jobId, node.S3NodeName);
                    }
                    else
                    {
                        logger.LogInformation("Rebooted SocketXP node {NodeName}. Response: {ResponseContent}", node.S3NodeName, responseContent);
                    }

                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in RebootSocketXPDeviceAsync: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Update Orcanode state by querying socketxp.com.
        /// </summary>
        /// <param name="context">Database context to update</param>
        /// <param name="logger">Logger</param>
        /// <returns></returns>
        public async static Task UpdateSocketXPDataAsync(IOrcanodeMonitorContext context, ILogger logger)
        {
            try
            {
                string jsonResult = await GetSocketXPDataAsync(string.Empty, logger);
                if (jsonResult.IsNullOrEmpty())
                {
                    // Indeterminate result, so don't update anything.
                    return;
                }

                List<Orcanode> originalList = await context.Orcanodes.ToListAsync();

                // Create a list to track what nodes are no longer returned.
                var unfoundList = originalList.ToList();

                var responseObject = JsonSerializer.Deserialize<JsonElement>(jsonResult);
                if (responseObject.ValueKind != JsonValueKind.Object)
                {
                    logger.LogError($"Invalid responseObject kind in UpdateSocketXPDataAsync: {responseObject.ValueKind}");
                    return;
                }
                if (!responseObject.TryGetProperty("Devices", out var deviceArray))
                {
                    logger.LogError($"Missing Devices in UpdateSocketXPDataAsync result");
                    return;
                }
                if (deviceArray.ValueKind != JsonValueKind.Array)
                {
                    logger.LogError($"Invalid deviceArray kind in UpdateSocketXPDataAsync: {deviceArray.ValueKind}");
                    return;
                }
                foreach (JsonElement device in deviceArray.EnumerateArray())
                {
                    if (!device.TryGetProperty("DeviceName", out var deviceName))
                    {
                        logger.LogError($"Missing DeviceName in UpdateSocketXPDataAsync result");
                        continue;
                    }
                    if (deviceName.ToString().IsNullOrEmpty())
                    {
                        logger.LogError($"Empty DeviceName in UpdateSocketXPDataAsync result");
                        continue;
                    }

                    // Remove the found node from the unfound list.
                    Orcanode? oldListNode = unfoundList.Find(a => a.S3NodeName == deviceName.ToString());
                    if (oldListNode != null)
                    {
                        unfoundList.Remove(oldListNode);
                    }

                    string deviceNameValue = deviceName.ToString();
                    Orcanode? node = originalList.Find(a => a.S3NodeName == deviceNameValue);
                    if (node == null)
                    {
                        logger.LogError($"Unrecognized DeviceName '{deviceNameValue}' in UpdateSocketXPDataAsync result");
                        continue;
                    }

                    if (!device.TryGetProperty("DeviceId", out var deviceId))
                    {
                        logger.LogError($"Missing DeviceId in UpdateSocketXPDataAsync result");
                        continue;
                    }
                    if (deviceId.ToString().IsNullOrEmpty())
                    {
                        logger.LogError($"Empty DeviceId in UpdateSocketXPDataAsync result");
                        continue;
                    }
                    node.SocketXPDeviceId = deviceId.ToString();

                    long oldDiskCapacityInGigs = node.DiskCapacityInGigs;

                    if (device.TryGetProperty("CustomerSite", out var customerSite))
                    {
                        if (node.S3Bucket.IsNullOrEmpty() || (node.OrcasoundStatus == OrcanodeOnlineStatus.Absent))
                        {
                            node.S3Bucket = customerSite.ToString().ToLower().StartsWith("dev") ? _defaultDevS3Bucket : _defaultProdS3Bucket;
                        }
                    }
                    OrcanodeOnlineStatus oldStatus = node.SocketXPConnectionStatus;
                    if (device.TryGetProperty("DeviceStatus", out var deviceStatus))
                    {
                        node.SocketXPDeviceStatus = deviceStatus.ToString();
                    }
                    if (device.TryGetProperty("AgentVersion", out var agentVersion))
                    {
                        node.SocketXPAgentVersion = agentVersion.ToString();
                    }
                    if (device.TryGetProperty("SysKernelVersion", out var sysKernelVersion))
                    {
                        node.SocketXPKernelVersion = sysKernelVersion.ToString();
                    }
                    if (device.TryGetProperty("SysTotalDisk", out var sysTotalDisk) &&
                        sysTotalDisk.ValueKind == JsonValueKind.Number &&
                        sysTotalDisk.TryGetInt64(out long diskCapacityValue))
                    {
                        node.DiskCapacity = diskCapacityValue * 1024 * 1024; // Convert to bytes.
                    }
                    if (oldStatus == OrcanodeOnlineStatus.Absent)
                    {
                        // Save changes to make the node have an ID before we can
                        // possibly generate any events.
                        await Fetcher.SaveChangesAsync(context);
                    }

                    // Trigger any event changes.
                    OrcanodeOnlineStatus newStatus = node.SocketXPConnectionStatus;
                    if (newStatus != oldStatus)
                    {
                        AddSocketXPConnectionStatusEvent(context, node, logger);
                    }
                    if (oldStatus != OrcanodeOnlineStatus.Absent)
                    {
                        if (oldDiskCapacityInGigs != node.DiskCapacityInGigs)
                        {
                            AddDiskCapacityChangeEvent(context, node, logger);
                        }
                    }
                }

                // Mark any remaining unfound nodes as absent.
                foreach (var unfoundNode in unfoundList)
                {
                    var oldNode = originalList.Find(a => a.S3NodeName == unfoundNode.S3NodeName);
                    if (oldNode != null)
                    {
                        oldNode.SocketXPDeviceStatus = string.Empty;
                        oldNode.SocketXPDeviceId = string.Empty;
                        oldNode.SocketXPDeviceStatus = string.Empty;
                    }
                }

                MonitorState.GetFrom(context).LastUpdatedTimestampUtc = DateTime.UtcNow;
                await Fetcher.SaveChangesAsync(context);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in UpdateSocketXPDataAsync: {ex.Message}");
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
            try
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
                    if (node.SocketXPDeviceId.IsNullOrEmpty())
                    {
                        logger.LogWarning($"Node {node.DisplayName} needs a reboot, but has no SocketXP DeviceId.");
                        continue;
                    }
                    bool success = await RebootSocketXPDeviceAsync(node, logger);
                    if (success)
                    {
                        logger.LogInformation($"Node {node.DisplayName} rebooted successfully.");
                    }
                    else
                    {
                        logger.LogWarning($"Node {node.DisplayName} needs a reboot, but the reboot request failed.");
                    }

                    // Wait a bit to avoid hammering the SocketXP API.
                    await Task.Delay(2000);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in CheckForRebootsNeededAsync: {ex.Message}");
            }
        }

        private static void AddSocketXPConnectionStatusEvent(IOrcanodeMonitorContext context, Orcanode node, ILogger logger)
        {
            string value = node.SocketXPConnectionStatus.ToString();
            Fetcher.AddOrcanodeEvent(context, logger, node, OrcanodeEventTypes.SocketXPConnection, value);
        }

        private static void AddDiskCapacityChangeEvent(IOrcanodeMonitorContext context, Orcanode node, ILogger logger)
        {
            string value = string.Format("{0}G", node.DiskCapacityInGigs);
            Fetcher.AddOrcanodeEvent(context, logger, node, OrcanodeEventTypes.SDCardSize, value);
        }
    }
}
