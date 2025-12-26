// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using k8s;
using k8s.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Mono.TextTemplating;
using OrcanodeMonitor.Api;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Dynamic;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace OrcanodeMonitor.Core
{
    public class Fetcher
    {
        private static TimeZoneInfo _pacificTimeZone = TimeZoneInfo.FindSystemTimeZoneById("America/Los_Angeles");
        private static HttpClient _httpClient = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        private static string _orcasoundProdSite = "live.orcasound.net";
        private static string _orcasoundFeedsUrlPath = "/api/json/feeds";
        private static string _dataplicityDevicesUrl = "https://apps.dataplicity.com/devices/";
        private static DateTime _unixEpoch = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
        private static string _iftttServiceKey = Environment.GetEnvironmentVariable("IFTTT_SERVICE_KEY") ?? "<unknown>";
        public static bool IsReadOnly = false;
        private static string _defaultProdS3Bucket = "audio-orcasound-net";
        private static string _defaultDevS3Bucket = "dev-streaming-orcasound-net";
        public static string IftttServiceKey => _iftttServiceKey;
        private static readonly Kubernetes? _k8sClient = GetK8sClient();

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
        /// Find a node using the serial number value at Dataplicity.
        /// </summary>
        /// <param name="nodes">Database table to look in and potentially update</param>
        /// <param name="serial">Dataplicity serial number to look for</param>
        /// <param name="connectionStatus">Dataplicity connection status</param>
        /// <returns></returns>
        private static Orcanode? FindOrcanodeByDataplicitySerial(List<Orcanode> nodes, string serial, out OrcanodeOnlineStatus connectionStatus)
        {
            foreach (Orcanode node in nodes)
            {
                if (node.DataplicitySerial == serial)
                {
                    connectionStatus = node.DataplicityConnectionStatus;
                    return node;
                }
            }

            connectionStatus = OrcanodeOnlineStatus.Absent;
            return null;
        }

        /// <summary>
        /// Create a node.
        /// </summary>
        /// <param name="nodeList">Database table to update</param>
        /// <returns></returns>
        private static Orcanode CreateOrcanode(DbSet<Orcanode> nodeList)
        {
            var newNode = new Orcanode()
            {
                ID = Guid.NewGuid().ToString(),
                PartitionValue = 1
            };

            nodeList.Add(newNode);
            return newNode;
        }

        /// <summary>
        /// Find or create a node using the serial number value at Dataplicity.
        /// </summary>
        /// <param name="nodeList">Database table to look in and potentially update</param>
        /// <param name="serial">Dataplicity serial number to look for</param>
        /// <param name="connectionStatus">Returns the dataplicity connection status</param>
        /// <returns></returns>
        private static Orcanode FindOrCreateOrcanodeByDataplicitySerial(DbSet<Orcanode> nodeList, string serial, out OrcanodeOnlineStatus connectionStatus)
        {
            Orcanode? node = FindOrcanodeByDataplicitySerial(nodeList.ToList(), serial, out connectionStatus);
            if (node != null)
            {
                return node;
            }

            connectionStatus = OrcanodeOnlineStatus.Absent;
            Orcanode newNode = CreateOrcanode(nodeList);
            newNode.DataplicitySerial = serial;
            return newNode;
        }

        private static Orcanode? FindOrcanodeByOrcasoundFeedId(List<Orcanode> nodes, string feedId)
        {
            foreach (Orcanode node in nodes)
            {
                if (node.OrcasoundFeedId == feedId)
                {
                    return node;
                }
            }

            return null;
        }

        /// <summary>
        /// Look for an Orcanode by Orcasound name in a list.
        /// </summary>
        /// <param name="nodes">Orcanode list to look in</param>
        /// <param name="orcasoundName">Name to look for</param>
        /// <returns>Node found</returns>
        private static Orcanode? FindOrcanodeByOrcasoundName(List<Orcanode> nodes, string orcasoundName)
        {
            foreach (Orcanode node in nodes)
            {
                if (node.OrcasoundName == orcasoundName)
                {
                    return node;
                }

                // See if we can match a node name derived from dataplicity.
                if (node.OrcasoundName.IsNullOrEmpty() && orcasoundName.Contains(node.DisplayName))
                {
                    node.OrcasoundName = orcasoundName;
                    return node;
                }
            }

            return null;
        }

        /// <summary>
        /// Get whether the new container status is better than the old one.
        /// </summary>
        /// <param name="oldStatus">Old status</param>
        /// <param name="newStatus">New status</param>
        /// <returns>True if new is better, false if old is better</returns>
        private static bool IsBetterContainerStatus(V1ContainerStatus? oldStatus, V1ContainerStatus newStatus)
        {
            if (oldStatus == null)
            {
                return true;
            }

            var oldState = oldStatus.State;
            var newState = newStatus.State;

            bool oldRunning = oldState?.Running != null;
            bool newRunning = newState?.Running != null;
            if (newRunning && !oldRunning)
            {
                return true;
            }

            if (!newRunning && oldRunning)
            {
                return false;
            }

            bool oldTerminated = oldState?.Terminated != null;
            bool newTerminated = newState?.Terminated != null;
            // Prefer non-terminated over terminated when neither is running.
            if (!newTerminated && oldTerminated)
            {
                return true;
            }

            if (newTerminated && !oldTerminated)
            {
                return false;
            }

            // If both running, prefer the most recent start
            if (newRunning && oldRunning)
            {
                DateTime? nStart = newState!.Running!.StartedAt;
                DateTime? oStart = oldState!.Running!.StartedAt;
                if (nStart.HasValue && oStart.HasValue)
                {
                    return nStart > oStart;
                }

                if (nStart.HasValue)
                {
                    return true;
                }

                if (oStart.HasValue)
                {
                    return false;
                }
            }

            // If both terminated, prefer the most recent finish (use State.Terminated).
            if (newTerminated && oldTerminated)
            {
                DateTime? nFinish = newState!.Terminated!.FinishedAt;
                DateTime? oFinish = oldState!.Terminated!.FinishedAt;
                if (nFinish.HasValue && oFinish.HasValue)
                {
                    return nFinish > oFinish;
                }

                if (nFinish.HasValue)
                {
                    return true;
                }

                if (oFinish.HasValue)
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Get the best status of any inference container in the pod.
        /// </summary>
        /// <param name="pod">pod to look in</param>
        /// <returns>Container status</returns>
        private static V1ContainerStatus? GetBestContainerStatus(V1Pod pod)
        {
            if (pod.Status?.ContainerStatuses == null)
            {
                return null;
            }

            V1ContainerStatus? bestPodStatus = null;
            foreach (V1ContainerStatus podStatus in pod.Status.ContainerStatuses)
            {
                if (IsBetterContainerStatus(bestPodStatus, podStatus))
                {
                    bestPodStatus = podStatus;
                }
            }
            return bestPodStatus;
        }

        /// <summary>
        /// Get the best pod status.
        /// </summary>
        /// <param name="nodepods">List of pods</param>
        /// <param name="bestPod">Returns the best pod</param>
        /// <param name="bestPodStatus">Returns the best pod status</param>
        private static void GetBestPodStatus(IEnumerable<V1Pod> nodepods, out V1Pod? bestPod, out V1ContainerStatus? bestPodStatus)
        {
            bestPod = null;
            bestPodStatus = null;
            foreach (var pod in nodepods)
            {
                if (pod.Status?.ContainerStatuses == null)
                {
                    continue;
                }

                // Only process inference-system pods, skipping any benchmark and other auxiliary pods.
                var podName = pod.Metadata?.Name;
                if (string.IsNullOrEmpty(podName) || !podName.StartsWith("inference-system"))
                {
                    continue;
                }

                foreach (V1ContainerStatus podStatus in pod.Status.ContainerStatuses)
                {
                    if (IsBetterContainerStatus(bestPodStatus, podStatus))
                    {
                        bestPod = pod;
                        bestPodStatus = podStatus;
                    }
                }
            }
        }

        /// <summary>
        /// A pod is considered stable if it has been running for at least this many hours.
        /// </summary>
        const int RestartStabilityHours = 6;

        private static Kubernetes? GetK8sClient()
        {
            string? k8sCACert = Environment.GetEnvironmentVariable("KUBERNETES_CA_CERT");
            if (k8sCACert == null)
            {
                return null;
            }
            byte[] caCertBytes = Convert.FromBase64String(k8sCACert);
            using (var caCert = new X509Certificate2(caCertBytes))
            {
                string? host = Environment.GetEnvironmentVariable("KUBERNETES_SERVICE_HOST");
                if (host == null)
                {
                    return null;
                }
                string? accessToken = Environment.GetEnvironmentVariable("KUBERNETES_TOKEN");
                if (accessToken == null)
                {
                    return null;
                }
                var config = new KubernetesClientConfiguration
                {
                    Host = host,
                    AccessToken = accessToken,
                    SslCaCerts = new X509Certificate2Collection(caCert)
                };

                var client = new Kubernetes(config);
                return client;
            }
        }

        /// <summary>
        /// Exec into a pod running to get the output of a command.
        /// </summary>
        /// <param name="podName">name of pod to exec into</param>
        /// <param name="namespaceName">pod namespace</param>
        /// <param name="cmd">The command to execute in the pod.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the combined standard output and error from the specified command executed in the pod, as a string.</returns>
        private async static Task<string> GetPodCommandOutput(string podName, string namespaceName, string[] cmd)
        {
            Kubernetes? client = _k8sClient;
            if (client == null)
            {
                return string.Empty;
            }

            var sb = new StringBuilder();
            ExecAsyncCallback callback = async (stdIn, stdOut, stdErr) =>
            {
                using var readerOut = new StreamReader(stdOut);
                using var readerErr = new StreamReader(stdErr);

                string outText = await readerOut.ReadToEndAsync();
                string errText = await readerErr.ReadToEndAsync();

                sb.Append(outText);
                sb.Append(errText);
            };

            try
            {
                await client.NamespacedPodExecAsync(
                    name: podName,
                    @namespace: namespaceName,
                    container: null,
                    command: cmd,
                    tty: false,
                    action: callback,
                    cancellationToken: CancellationToken.None
                );
            }
            catch (Exception ex)
            {
                // Optionally log the exception here if logging is available
                Console.Error.WriteLine($"[GetPodCommandOutput] Error retrieving node info for '{namespaceName}': {ex.Message}");
                return string.Empty;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Exec into a pod running to get CPU info.
        /// </summary>
        /// <param name="podName">name of pod to exec into</param>
        /// <param name="namespaceName">namespace of pod to exec into</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the combined standard output and error from the <c>lscpu</c> command executed in the pod, as a string.</returns>
        private async static Task<string> GetPodLscpuOutputAsync(string podName, string namespaceName)
        {
            string[] cmd = { "lscpu" };
            return await GetPodCommandOutput(podName, namespaceName, cmd);
        }


        /// <summary>
        /// Exec into a pod running to get model info.
        /// </summary>
        /// <param name="pod">pod to exec into</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the combined standard output and error from the command executed in the pod, as a string.</returns>
        private async static Task<string> GetPodModelTimestampAsync(V1Pod pod)
        {
            string[] command = { "stat", "-c", "%y", "/usr/src/app/model/model.pkl" };
            return await GetPodCommandOutput(pod.Metadata.Name, pod.Metadata.NamespaceProperty, command);
        }

        /// <summary>
        /// Get an object representing a node hosting an OrcaHello inference pod.
        /// </summary>
        /// <param name="nodeName">OrcaHello node name</param>
        /// <returns>OrcaHelloNode</returns>
        public async static Task<OrcaHelloNode?> GetOrcaHelloNodeAsync(string nodeName)
        {
            Kubernetes? client = _k8sClient;
            if (client == null)
            {
                return null;
            }

            try
            {
                V1Node node = await client.ReadNodeAsync(nodeName);

                NodeMetricsList nodeMetricsList = await client.GetKubernetesNodesMetricsAsync();
                NodeMetrics? nodeMetrics = nodeMetricsList.Items.FirstOrDefault(n => n.Metadata.Name == nodeName);
                string cpuUsage = nodeMetrics?.Usage.TryGetValue("cpu", out var cpu) == true ? cpu.ToString() : "0n";
                string memoryUsage = nodeMetrics?.Usage.TryGetValue("memory", out var mem) == true ? mem.ToString() : "0Ki";

                string lscpuOutput = string.Empty;
                V1PodList v1Pods = await client.ListPodForAllNamespacesAsync();
                List<V1Pod> allPodsOnNode = v1Pods.Items
                    .Where(p => p.Spec.NodeName == nodeName)
                    .ToList();

                GetBestPodStatus(allPodsOnNode, out V1Pod? bestPod, out V1ContainerStatus? bestContainerStatus);
                if (bestPod != null && bestPod.Metadata != null)
                {
                    lscpuOutput = await GetPodLscpuOutputAsync(bestPod.Metadata.Name, bestPod.Metadata.NamespaceProperty);
                }

                PodMetricsList podMetrics = await client.GetKubernetesPodsMetricsAsync();
                return new OrcaHelloNode(node, cpuUsage, memoryUsage, lscpuOutput, v1Pods.Items, podMetrics.Items);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[GetOrcaHelloNodeAsync] Error retrieving node info for '{nodeName}': {ex}");
                return null;
            }
        }

        /// <summary>
        /// Get an object representing an OrcaHello inference pod.
        /// </summary>
        /// <param name="orcanode">Orcanode object associated with the pod</param>
        /// <param name="namespaceName">Namespace name</param>
        /// <returns>OrcaHelloPod</returns>
        public async static Task<OrcaHelloPod?> GetOrcaHelloPodAsync(Orcanode orcanode, string namespaceName)
        {
            Kubernetes? client = _k8sClient;
            if (client == null)
            {
                return null;
            }

            V1PodList pods = await client.ListNamespacedPodAsync(namespaceName);
            GetBestPodStatus(pods.Items, out V1Pod? bestPod, out V1ContainerStatus? bestContainerStatus);
            if (bestPod == null)
            {
                return null;
            }

            PodMetricsList? metricsList = await client.GetKubernetesPodsMetricsByNamespaceAsync(namespaceName);
            PodMetrics? podMetric = metricsList?.Items?.FirstOrDefault(n => n.Metadata?.Name?.StartsWith("inference-system-") == true);
            var container = podMetric?.Containers.FirstOrDefault(c => c.Name == "inference-system");
            string cpuUsage = container?.Usage?.TryGetValue("cpu", out var cpu) == true ? cpu.ToString() : "0n";
            string memoryUsage = container?.Usage?.TryGetValue("memory", out var mem) == true ? mem.ToString() : "0Ki";

            string modelTimestamp = await GetPodModelTimestampAsync(bestPod);

            long detectionCount = await GetDetectionCountAsync(orcanode);

            return new OrcaHelloPod(bestPod, cpuUsage, memoryUsage, modelTimestamp, detectionCount);
        }

        /// <summary>
        /// Get pod logs
        /// </summary>
        /// <param name="container">Container</param>
        /// <param name="namespaceName">Namespace</param>
        /// <param name="logger">Logger</param>
        /// <returns>Log</returns>
        public async static Task<string> GetOrcaHelloLogAsync(OrcaHelloPod? container, string namespaceName, ILogger logger)
        {
            if (container == null)
            {
                return string.Empty;
            }
            string podName = container.Name;

            Kubernetes? client = _k8sClient;
            if (client == null)
            {
                return string.Empty;
            }

            try
            {
                Stream? logs = await client.ReadNamespacedPodLogAsync(
                    name: podName,
                    namespaceParameter: namespaceName,
                    tailLines: 300);
                if (logs == null)
                {
                    return string.Empty;
                }
                using var reader = new StreamReader(logs);
                string text = reader.ReadToEnd();

                // Split into lines, filter, and rejoin
                var filtered = string.Join(
                    Environment.NewLine,
                    Regex.Split(text, "\r?\n")
                        .Where(line => !line.StartsWith("INSTRUMENTATION KEY:", StringComparison.OrdinalIgnoreCase))
                );

                return filtered;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in GetOrcaHelloLogAsync: {ex.Message}");
                return string.Empty;
            }
        }

        /// <summary>
        /// Update the list of Orcanodes using data about InferenceSystem containers in Azure.
        /// </summary>
        /// <param name="context">Database context to update</param>
        /// <param name="logger">Logger</param>
        /// <returns></returns>
        public async static Task UpdateOrcaHelloDataAsync(OrcanodeMonitorContext context, ILogger logger)
        {
            try
            {
                Kubernetes? client = _k8sClient;
                if (client == null)
                {
                    return;
                }

                // Get a snapshot to use during the loop to avoid multiple queries.
                var foundList = await context.Orcanodes.ToListAsync();

                // Create a list to track what nodes are no longer returned.
                var unfoundList = foundList.ToList();

                var pods = await client.ListPodForAllNamespacesAsync();
                foreach (Orcanode node in foundList)
                {
                    string slug = node.OrcasoundSlug;
                    if (slug.IsNullOrEmpty())
                    {
                        // No slug, so can't match.
                        continue;
                    }
                    var nodepods = pods.Items.Where(p => p.Metadata?.NamespaceProperty == slug);
                    GetBestPodStatus(nodepods, out V1Pod? bestPod, out V1ContainerStatus? bestContainerStatus);
                    node.OrcaHelloInferenceRestartCount = 0;
                    if (bestPod != null)
                    {
                        node.OrcaHelloId = bestPod.Metadata?.Name ?? string.Empty;

                        // Remove the returned node from the unfound list only when a pod matched.
                        var nodeToRemove = unfoundList.Find(a => a.OrcasoundSlug == node.OrcasoundSlug);
                        if (nodeToRemove != null)
                        {
                            unfoundList.Remove(nodeToRemove);
                        }

                        string podName = bestPod?.Metadata?.Name ?? string.Empty;
                        if (string.IsNullOrEmpty(podName))
                        {
                            continue;
                        }

                        if (bestContainerStatus?.Ready ?? false)
                        {
                            Stream? logs = await client.ReadNamespacedPodLogAsync(
                                name: podName,
                                namespaceParameter: slug,
                                tailLines: 300);
                            if (logs != null)
                            {
                                int lastLiveIndex = -1;
                                using var reader = new StreamReader(logs);
                                string line;
                                while ((line = await reader.ReadLineAsync()) != null)
                                {
                                    Match m = Regex.Match(line, @"(?<=live)\d+(?=\.ts)");
                                    if (m.Success)
                                    {
                                        lastLiveIndex = int.Parse(m.Value);
                                    }
                                }

                                if (lastLiveIndex >= 0)
                                {
                                    // TODO: below is the second call to GetLatestS3TimestampAsync.
                                    // We should cache result from before instead of calling it a second time.
                                    TimestampResult? result = await GetLatestS3TimestampAsync(node, true, logger);
                                    if (result?.Offset != null)
                                    {
                                        DateTimeOffset offset = result.Offset.Value;
                                        DateTimeOffset clipEndTime = offset.AddSeconds((lastLiveIndex * 10) + 12);
                                        DateTimeOffset now = DateTimeOffset.UtcNow;
                                        node.OrcaHelloInferencePodLag = now - clipEndTime;
                                    }
                                }
                            }
                        }
                        else
                        {
                            // Pod is not ready; clear any stale lag value.
                            node.OrcaHelloInferencePodLag = null;
                        }
                    }
                    else
                    {
                        // No pod matched; clear any stale ID.
                        node.OrcaHelloId = string.Empty;
                    }

                    if (bestContainerStatus != null)
                    {
                        node.OrcaHelloInferenceImage = bestContainerStatus.Image ?? string.Empty;
                        node.OrcaHelloInferencePodReady = bestContainerStatus.Ready;
                        DateTime? runningSince = bestContainerStatus.State?.Running?.StartedAt;
                        if (runningSince != null)
                        {
                            node.OrcaHelloInferencePodRunningSince = runningSince;
                        }
                        if ((runningSince == null) || (DateTime.UtcNow - runningSince < TimeSpan.FromHours(RestartStabilityHours)))
                        {
                            node.OrcaHelloInferenceRestartCount = bestContainerStatus.RestartCount;
                        }
                    }
                    else
                    {
                        node.OrcaHelloInferenceImage = string.Empty;
                        node.OrcaHelloInferencePodReady = false;
                    }
                }

                // Mark any remaining unfound nodes as absent.
                foreach (Orcanode unfoundNode in unfoundList)
                {
                    Orcanode? oldNode = null;
                    if (!unfoundNode.OrcasoundFeedId.IsNullOrEmpty())
                    {
                        oldNode = FindOrcanodeByOrcasoundFeedId(foundList, unfoundNode.OrcasoundFeedId);
                    }
                    else if (!unfoundNode.DataplicitySerial.IsNullOrEmpty())
                    {
                        oldNode = FindOrcanodeByDataplicitySerial(foundList, unfoundNode.DataplicitySerial, out OrcanodeOnlineStatus connectionStatus);
                    }
                    if (oldNode != null)
                    {
                        oldNode.OrcaHelloId = String.Empty;
                    }
                }

                MonitorState.GetFrom(context).LastUpdatedTimestampUtc = DateTime.UtcNow;
                await SaveChangesAsync(context);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in UpdateOrcaHelloDataAsync: {ex.Message}");
            }
        }

        public async static Task<string> GetDataplicityDataAsync(string serial, ILogger logger)
        {
            try
            {
                string? orcasound_dataplicity_token = Environment.GetEnvironmentVariable("ORCASOUND_DATAPLICITY_TOKEN");
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
                    using HttpResponseMessage response = await _httpClient.SendAsync(request);
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
                string? orcasound_dataplicity_token = Environment.GetEnvironmentVariable("ORCASOUND_DATAPLICITY_TOKEN");
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
                    using HttpResponseMessage response = await _httpClient.SendAsync(request);
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
        public async static Task UpdateDataplicityDataAsync(OrcanodeMonitorContext context, ILogger logger)
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

                    Orcanode node = FindOrCreateOrcanodeByDataplicitySerial(context.Orcanodes, serial.ToString(), out OrcanodeOnlineStatus oldStatus);
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
                        await SaveChangesAsync(context);
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
                    var oldNode = FindOrcanodeByDataplicitySerial(originalList, unfoundNode.DataplicitySerial, out OrcanodeOnlineStatus unfoundNodeStatus);
                    if (oldNode != null)
                    {
                        oldNode.DataplicityOnline = null;
                    }
                }

                MonitorState.GetFrom(context).LastUpdatedTimestampUtc = DateTime.UtcNow;
                await SaveChangesAsync(context);
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
        /// <param name="logger"></param>
        /// <returns></returns>
        public async static Task CheckForRebootsNeededAsync(OrcanodeMonitorContext context, ILogger logger)
        {
            var originalList = await context.Orcanodes.ToListAsync();
            foreach (Orcanode? node in originalList)
            {
                if (!node.NeedsRebootForContainerRestart)
                {
                    continue;
                }
                if (IsReadOnly)
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

        /// <summary>
        /// Get Orcasound data
        /// </summary>
        /// <param name="context"></param>
        /// <param name="site"></param>
        /// <param name="logger"></param>
        /// <returns>null on error, or JsonElement on success</returns>
        private async static Task<JsonElement?> GetOrcasoundDataAsync(OrcanodeMonitorContext context, string site, ILogger logger)
        {
            string url = "https://" + site + _orcasoundFeedsUrlPath;
            try
            {
                string json = await _httpClient.GetStringAsync(url);
                if (json.IsNullOrEmpty())
                {
                    // Error.
                    return null;
                }
                dynamic response = JsonSerializer.Deserialize<ExpandoObject>(json);
                if (response == null)
                {
                    // Error.
                    logger.LogError("Couldn't deserialize JSON in GetOrcasoundDataAsync");
                    return null;
                }
                JsonElement dataArray = response.data;
                if (dataArray.ValueKind != JsonValueKind.Array)
                {
                    // Error.
                    logger.LogError($"Invalid dataArray kind in GetOrcasoundDataAsync: {dataArray.ValueKind}");
                    return null;
                }
                return dataArray;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in GetOrcasoundDataAsync: {ex.Message}");
                return null;
            }
        }

        private static void UpdateOrcasoundNode(JsonElement feed, List<Orcanode> foundList, List<Orcanode> unfoundList, OrcanodeMonitorContext context, string site, ILogger logger)
        {
            if (!feed.TryGetProperty("id", out var feedId))
            {
                logger.LogError($"Missing id in UpdateOrcasoundNode");
                return;
            }
            if (!feed.TryGetProperty("attributes", out JsonElement attributes))
            {
                logger.LogError($"Missing attributes in UpdateOrcasoundNode");
                return;
            }
            if (!attributes.TryGetProperty("name", out var name))
            {
                logger.LogError($"Missing name in UpdateOrcasoundNode");
                return;
            }
            string orcasoundName = name.ToString();
            if (!attributes.TryGetProperty("dataplicity_id", out var dataplicity_id))
            {
                logger.LogError($"Missing dataplicity_id in UpdateOrcasoundNode");
                return;
            }
            bool hidden = false;
            if (attributes.TryGetProperty("hidden", out var hiddenElement))
            {
                hidden = hiddenElement.GetBoolean();
            }
            string dataplicitySerial = dataplicity_id.ToString();

            // Remove the found node from the unfound list.
            Orcanode? oldListNode = unfoundList.Find(a => a.OrcasoundFeedId == feedId.ToString());
            if (oldListNode != null)
            {
                unfoundList.Remove(oldListNode);
            }

            Orcanode? node = null;
            node = FindOrcanodeByOrcasoundFeedId(foundList, feedId.ToString());
            if (node == null)
            {
                // We didn't used to store the feed ID, only the name, so try again by name.
                Orcanode? possibleNode = FindOrcanodeByOrcasoundName(foundList, orcasoundName);
                if ((possibleNode != null) && possibleNode.OrcasoundFeedId.IsNullOrEmpty())
                {
                    node = possibleNode;
                    oldListNode = unfoundList.Find(a => a.OrcasoundName == orcasoundName);
                    if (oldListNode != null)
                    {
                        unfoundList.Remove(oldListNode);
                    }
                }
            }

            // See if we can find a node by dataplicity ID, so that if a node
            // shows up in dataplicity first and Orcasite later, we don't create a
            // duplicate entry.
            Orcanode? dataplicityNode = null;
            if (!dataplicitySerial.IsNullOrEmpty())
            {
                dataplicityNode = FindOrcanodeByDataplicitySerial(foundList, dataplicitySerial, out OrcanodeOnlineStatus oldStatus);
                if (dataplicityNode != null)
                {
                    if (node == null)
                    {
                        node = dataplicityNode;
                    }
                    else if (node != dataplicityNode)
                    {
                        // We have duplicate nodes to merge. In theory we shouldn't have any
                        // node state for the dataplicity-only node. (TODO: verify this)
                        logger.LogWarning($"Merging duplicate nodes for {node.DataplicitySerial}");
                        node.DataplicityDescription = dataplicityNode.DataplicityDescription;
                        node.DataplicityName = dataplicityNode.DataplicityName;
                        node.DataplicityOnline = dataplicityNode.DataplicityOnline;
                        node.AgentVersion = dataplicityNode.AgentVersion;
                        node.DiskCapacity = dataplicityNode.DiskCapacity;
                        node.DiskUsed = dataplicityNode.DiskUsed;
                        node.DataplicityUpgradeAvailable = dataplicityNode.DataplicityUpgradeAvailable;
                        context.Orcanodes.Remove(dataplicityNode);
                    }
                }
            }

            if (node == null)
            {
                node = CreateOrcanode(context.Orcanodes);
                node.OrcasoundName = name.ToString();
            }

            if (!dataplicitySerial.IsNullOrEmpty())
            {
                if (!node.DataplicitySerial.IsNullOrEmpty() && dataplicitySerial != node.DataplicitySerial)
                {
                    // TODO: The orcasound entry for the node changed its dataplicity_id.
                    logger.LogWarning($"dataplicity_id changed for {node.DisplayName} from {node.DataplicitySerial} to {dataplicitySerial}");
                }
                node.DataplicitySerial = dataplicitySerial;
            }

            if (node.OrcasoundFeedId.IsNullOrEmpty())
            {
                node.OrcasoundFeedId = feedId.ToString();
            }
            if (!site.IsNullOrEmpty())
            {
                node.OrcasoundHost = site;
            }
            if (orcasoundName != node.OrcasoundName)
            {
                // We just detected a name change.
                node.OrcasoundName = orcasoundName;
            }
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
            if (attributes.TryGetProperty("visible", out var visible))
            {
                node.OrcasoundVisible = visible.GetBoolean();
            }
        }

        private async static Task UpdateOrcasoundSiteDataAsync(OrcanodeMonitorContext context, string site, List<Orcanode> foundList, List<Orcanode> unfoundList, ILogger logger)
        {
            JsonElement? dataArray = await GetOrcasoundDataAsync(context, site, logger);
            if (dataArray.HasValue)
            {
                foreach (JsonElement feed in dataArray.Value.EnumerateArray())
                {
                    UpdateOrcasoundNode(feed, foundList, unfoundList, context, site, logger);
                }
            }
        }

        /// <summary>
        /// Saves changes to the database if the application is not in read-only mode.
        /// </summary>
        /// <param name="context">The database context to save changes for.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private static async Task SaveChangesAsync(OrcanodeMonitorContext context)
        {
            if (!IsReadOnly)
            {
                await context.SaveChangesAsync();
            }
        }

        /// <summary>
        /// Update the current list of Orcanodes using data from orcasound.net.
        /// </summary>
        /// <param name="context">Database context to update</param>
        /// <param name="logger"></param>
        /// <returns></returns>
        public async static Task UpdateOrcasoundDataAsync(OrcanodeMonitorContext context, ILogger logger)
        {
            try
            {
                var foundList = await context.Orcanodes.ToListAsync();

                // Create a list to track what nodes are no longer returned.
                var unfoundList = foundList.ToList();

                await UpdateOrcasoundSiteDataAsync(context, _orcasoundProdSite, foundList, unfoundList, logger);

                // Mark any remaining unfound nodes as absent.
                foreach (var unfoundNode in unfoundList)
                {
                    var oldNode = FindOrcanodeByOrcasoundFeedId(foundList, unfoundNode.OrcasoundFeedId);
                    if (oldNode != null)
                    {
                        oldNode.OrcasoundFeedId = String.Empty;
                    }
                }

                MonitorState.GetFrom(context).LastUpdatedTimestampUtc = DateTime.UtcNow;
                await SaveChangesAsync(context);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in UpdateOrcasoundDataAsync: {ex.Message}");
            }
        }

        public async static Task UpdateS3DataAsync(OrcanodeMonitorContext context, ILogger logger)
        {
            try
            {
                List<Orcanode> nodes = await context.Orcanodes.ToListAsync();
                foreach (Orcanode node in nodes)
                {
                    if (!node.S3NodeName.IsNullOrEmpty())
                    {
                        await Fetcher.UpdateS3DataAsync(context, node, logger);
                    }
                }

                MonitorState.GetFrom(context).LastUpdatedTimestampUtc = DateTime.UtcNow;
                await SaveChangesAsync(context);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in UpdateS3DataAsync: {ex.Message}");
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

        public class TimestampResult
        {
            public string UnixTimestampString { get; }
            public DateTimeOffset? Offset { get; }
            public TimestampResult(string unixTimestampString, DateTimeOffset? offset)
            {
                UnixTimestampString = unixTimestampString;
                Offset = offset;
            }
        }

        public async static Task<TimestampResult?> GetLatestS3TimestampAsync(Orcanode node, bool updateNode, ILogger logger)
        {
            string url = "https://" + node.S3Bucket + ".s3.amazonaws.com/" + node.S3NodeName + "/latest.txt";
            using HttpResponseMessage response = await _httpClient.GetAsync(url);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                logger.LogError($"{node.S3NodeName} not found on S3");

                // Absent.
                if (updateNode)
                {
                    node.LatestRecordedUtc = null;
                }
                return null;
            }
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                logger.LogError($"{node.S3NodeName} got access denied on S3");

                // Access denied.
                if (updateNode)
                {
                    node.LatestRecordedUtc = DateTime.MinValue;
                }
                return null;
            }
            if (!response.IsSuccessStatusCode)
            {
                logger.LogError($"{node.S3NodeName} got status {response.StatusCode} on S3");

                return null;
            }

            string content = await response.Content.ReadAsStringAsync();
            string unixTimestampString = content.TrimEnd();
            var result = new TimestampResult(unixTimestampString, response.Content.Headers.LastModified);
            return result;
        }

        /// <summary>
        /// Update the timestamps for a given Orcanode by querying files on S3.
        /// </summary>
        /// <param name="context">Database context</param>
        /// <param name="node">Orcanode to update</param>
        /// <param name="logger">Logger</param>
        /// <returns></returns>
        public async static Task UpdateS3DataAsync(OrcanodeMonitorContext context, Orcanode node, ILogger logger)
        {
            OrcanodeOnlineStatus oldStatus = node.S3StreamStatus;
            TimestampResult? result = await GetLatestS3TimestampAsync(node, true, logger);
            if (result == null)
            {
                OrcanodeOnlineStatus newStatus = node.S3StreamStatus;
                if (newStatus != oldStatus)
                {
                    // Log event if it just went absent. Other events will be logged
                    // inside the call to UpdateManifestTimestampAsync() below.
                    AddHydrophoneStreamStatusEvent(context, logger, node, null);
                }
                return;
            }
            string unixTimestampString = result.UnixTimestampString;
            DateTime? latestRecorded = UnixTimeStampStringToDateTimeUtc(unixTimestampString);
            if (latestRecorded.HasValue)
            {
                node.LatestRecordedUtc = latestRecorded?.ToUniversalTime();

                DateTimeOffset? offset = result.Offset;
                if (offset.HasValue)
                {
                    node.LatestUploadedUtc = offset.Value.UtcDateTime;
                }
            }

            await UpdateManifestTimestampAsync(context, node, unixTimestampString, logger);
        }

        /// <summary>
        /// Get a list of the most recent events in order from most to least recent,
        /// up to a maximum of 'limit' events.
        /// </summary>
        /// <param name="context">Database context</param>
        /// <param name="limit">Maximum number of events to return</param>
        /// <returns>List of events</returns>
        public static async Task<List<OrcanodeEvent>> GetEventsAsync(OrcanodeMonitorContext context, int limit)
        {
            List<OrcanodeEvent> orcanodeEvents = await context.OrcanodeEvents.OrderByDescending(e => e.DateTimeUtc).Take(limit).ToListAsync();
            return orcanodeEvents;
        }

        /// <summary>
        /// Get recent events.
        /// </summary>
        /// <param name="context">Database context</param>
        /// <param name="since">Time to get events since</param>
        /// <param name="logger">Logger</param>
        /// <returns>null on error, or list of events on success</returns>
        public static async Task<List<OrcanodeEvent>?> GetRecentEventsAsync(OrcanodeMonitorContext context, DateTime since, ILogger logger)
        {
            try
            {
                List<OrcanodeEvent> events = await context.OrcanodeEvents.Where(e => e.DateTimeUtc >= since).OrderByDescending(e => e.DateTimeUtc).ToListAsync();
                return events;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to fetch recent events");
                return null;
            }
        }

        private static List<OrcanodeEvent> AddOlderEvent(List<OrcanodeEvent> orcanodeEvents, List<OrcanodeEvent> events, DateTime since, string type)
        {
            OrcanodeEvent? olderEvent = events.Where(e => (e.DateTimeUtc < since) && (e.Type == type)).FirstOrDefault();
            if (olderEvent == null)
            {
                return orcanodeEvents;
            }
            else
            {
                return orcanodeEvents.Append(olderEvent).OrderByDescending(e => e.DateTimeUtc).ToList();
            }
        }

        /// <summary>
        /// Get recent events for a node.
        /// </summary>
        /// <param name="context">database context</param>
        /// <param name="id">ID of node to get events for</param>
        /// <param name="since">UTC time to get events since</param>
        /// <param name="logger">Logger</param>
        /// <returns>null on error, or list of events on success</returns>
        public static async Task<List<OrcanodeEvent>?> GetRecentEventsForNodeAsync(OrcanodeMonitorContext context, string id, DateTime since, ILogger logger)
        {
            try
            {
                List<OrcanodeEvent> events = await context.OrcanodeEvents.Where(e => e.OrcanodeId == id).OrderByDescending(e => e.DateTimeUtc).ToListAsync();
                List<OrcanodeEvent> orcanodeEvents = events.Where(e => e.DateTimeUtc >= since).ToList();

                // Add one older event per type we can filter on.
                orcanodeEvents = AddOlderEvent(orcanodeEvents, events, since, OrcanodeEventTypes.HydrophoneStream);
                orcanodeEvents = AddOlderEvent(orcanodeEvents, events, since, OrcanodeEventTypes.DataplicityConnection);
                orcanodeEvents = AddOlderEvent(orcanodeEvents, events, since, OrcanodeEventTypes.MezmoLogging);

                return orcanodeEvents;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to fetch events for node {id}");
                return null;
            }
        }

        /// <summary>
        /// Get recent detections for a node.
        /// </summary>
        /// <param name="feedId">Orcasound feed ID of node to get detections for</param>
        /// <param name="logger">Logger</param>
        /// <returns>null on error, or list of detections on success</returns>
        public static async Task<List<Detection>?> GetRecentDetectionsForNodeAsync(string feedId, ILogger logger)
        {
            string site = _orcasoundProdSite;
            string url = $"https://{site}/api/json/detections?page%5Blimit%5D=500&page%5Boffset%5D=0&fields%5Bdetection%5D=id%2Cplaylist_timestamp%2Cplayer_offset%2Ctimestamp%2Cdescription%2Csource%2Ccategory%2Cfeed_id&filter[feed_id]={feedId}";

            try
            {
                string jsonString = await _httpClient.GetStringAsync(url);
                if (jsonString.IsNullOrEmpty())
                {
                    // Error.
                    return null;
                }

                var response = JsonSerializer.Deserialize<DetectionResponse>(
                    jsonString,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );
                if (response?.Data == null)
                {
                    return null;
                }

                List<Detection> detections =
                    response.Data.Select(d => new Detection
                    {
                        ID = d.Id,
                        NodeID = d.Attributes?.FeedId ?? string.Empty,
                        Timestamp = d.Attributes?.Timestamp ?? default,
                        Source = d.Attributes?.Source ?? string.Empty,
                        Description = d.Attributes?.Description ?? string.Empty,
                        Category = d.Attributes?.Category ?? string.Empty
                    }).ToList();

                return detections;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Exception in GetRecentDetectionsForNodeAsync: {ex.Message}");
                return null;
            }
        }

        public static void AddOrcanodeEvent(OrcanodeMonitorContext context, ILogger logger, Orcanode node, string type, string value, string? url = null)
        {
            logger.LogInformation($"Orcanode event: {node.DisplayName} {type} {value}");
            var orcanodeEvent = new OrcanodeEvent(node, type, value, DateTime.UtcNow, url);
            context.OrcanodeEvents.Add(orcanodeEvent);
        }

        private static void AddDataplicityConnectionStatusEvent(OrcanodeMonitorContext context, Orcanode node, ILogger logger)
        {
            string value = node.DataplicityConnectionStatus.ToString();
            AddOrcanodeEvent(context, logger, node, OrcanodeEventTypes.DataplicityConnection, value);
        }

        private static void AddDataplicityAgentUpgradeStatusChangeEvent(OrcanodeMonitorContext context, Orcanode node, ILogger logger)
        {
            string value = node.DataplicityUpgradeStatus.ToString();
            AddOrcanodeEvent(context, logger, node, OrcanodeEventTypes.AgentUpgradeStatus, value);
        }

        private static void AddDiskCapacityChangeEvent(OrcanodeMonitorContext context, Orcanode node, ILogger logger)
        {
            string value = string.Format("{0}G", node.DiskCapacityInGigs);
            AddOrcanodeEvent(context, logger, node, OrcanodeEventTypes.SDCardSize, value);
        }

        private static void AddHydrophoneStreamStatusEvent(OrcanodeMonitorContext context, ILogger logger, Orcanode node, string? url)
        {
            string value = node.OrcasoundOnlineStatusString;
            AddOrcanodeEvent(context, logger, node, OrcanodeEventTypes.HydrophoneStream, value, url);
        }

        public async static Task<FrequencyInfo?> GetLatestAudioSampleAsync(Orcanode node, string unixTimestampString, bool updateNode, ILogger logger)
        {
            string url = "https://" + node.S3Bucket + ".s3.amazonaws.com/" + node.S3NodeName + "/hls/" + unixTimestampString + "/live.m3u8";
            using HttpResponseMessage response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                if (updateNode)
                {
                    // Set the manifest updated time to be old, since it would be the timestamp of the
                    // previous manifest file. The actual value doesn't matter, just that it's older than
                    // MaxUploadDelay ago so the stream will appear as offline.
                    node.ManifestUpdatedUtc = DateTime.MinValue;
                    node.LastCheckedUtc = DateTime.UtcNow;
                }
                return null;
            }

            DateTimeOffset? offset = response.Content.Headers.LastModified;
            if (!offset.HasValue)
            {
                if (updateNode)
                {
                    node.LastCheckedUtc = DateTime.UtcNow;
                }
                return null;
            }

            if (updateNode)
            {
                node.ManifestUpdatedUtc = offset.Value.UtcDateTime;
                node.LastCheckedUtc = DateTime.UtcNow;
            }

            // Download manifest.
            Uri baseUri = new Uri(url);
            string manifestContent = await _httpClient.GetStringAsync(url);

            // Get a recent filename in the manifest.
            // Sometimes the manifest is updated before the file is actually available,
            // so we try to get the penultimate .ts file in the manifest.
            string[] lines = manifestContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            int lineNumber = lines.Count();
            lineNumber = (lineNumber > 3) ? lineNumber - 3 : lineNumber - 1;
            string lastLine = lines[lineNumber];
            Uri newUri = new Uri(baseUri, lastLine);
            return await GetExactAudioSampleAsync(node, newUri, logger);
        }

        /// <summary>
        /// Retrieves the last modified date of a resource via HTTP HEAD request.
        /// </summary>
        /// <param name="uri">The URI of the resource to check.</param>
        /// <returns>The last modified date in UTC, or null if not available.</returns>
        public async static Task<DateTime?> GetLastModifiedAsync(Uri uri)
        {
            using var headRequest = new HttpRequestMessage(HttpMethod.Head, uri);
            using var headResponse = await _httpClient.SendAsync(headRequest);
            DateTime? lastModified = headResponse.Content.Headers.LastModified?.UtcDateTime;
            return lastModified;
        }

        public async static Task<FrequencyInfo?> GetExactAudioSampleAsync(Orcanode node, Uri uri, ILogger logger)
        {
            OrcanodeOnlineStatus oldStatus = node.S3StreamStatus;

            try
            {
                using Stream stream = await _httpClient.GetStreamAsync(uri);
#if true
                FrequencyInfo frequencyInfo = await FfmpegCoreAnalyzer.AnalyzeAudioStreamAsync(stream, oldStatus);
#else
                FrequencyInfo frequencyInfo = await FfmpegCoreAnalyzer.AnalyzeFileAsync("output-2channels.wav", oldStatus);
#endif
                frequencyInfo.AudioSampleUrl = uri.AbsoluteUri;
                return frequencyInfo;
            }
            catch (Exception ex)
            {
                // We couldn't fetch the stream audio so could not update the
                // audio standard deviation. Just ignore this for now.
                logger.LogError(ex, $"Exception in UpdateManifestTimestampAsync: {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Update the ManifestUpdated timestamp for a given Orcanode by querying S3.
        /// </summary>
        /// <param name="context">Database context</param>
        /// <param name="node">Orcanode to update</param>
        /// <param name="unixTimestampString">Value in the latest.txt file</param>
        /// <param name="logger">Logger</param>
        /// <returns></returns>
        public async static Task UpdateManifestTimestampAsync(OrcanodeMonitorContext context, Orcanode node, string unixTimestampString, ILogger logger)
        {
            OrcanodeOnlineStatus oldStatus = node.S3StreamStatus;

            FrequencyInfo? frequencyInfo = await GetLatestAudioSampleAsync(node, unixTimestampString, true, logger);
            if (frequencyInfo != null)
            {
                node.AudioStreamStatus = frequencyInfo.Status;

                // Compute an exponential weighted moving average of the non-hum decibel level.
                double newValue = frequencyInfo.GetAverageNonHumDecibels();
                if (node.DecibelLevel == null || node.DecibelLevel == double.NegativeInfinity)
                {
                    node.DecibelLevel = newValue;
                }
                else
                {
                    // Let it be a moving average across a day.
                    double alpha = 1.0 / PeriodicTasks.PollsPerDay;
                    node.DecibelLevel = (alpha * newValue) + ((1 - alpha) * node.DecibelLevel);
                }

                // Do the same for the hum decibel level.
                newValue = frequencyInfo.GetAverageHumDecibels();
                if (node.HumDecibelLevel == null || node.HumDecibelLevel == double.NegativeInfinity)
                {
                    node.HumDecibelLevel = newValue;
                }
                else
                {
                    // Let it be a moving average across a day.
                    double alpha = 1.0 / PeriodicTasks.PollsPerDay;
                    node.HumDecibelLevel = (alpha * newValue) + ((1 - alpha) * node.HumDecibelLevel);
                }
            }
            node.AudioStandardDeviation = 0.0;

            OrcanodeOnlineStatus newStatus = node.S3StreamStatus;
            if (newStatus != oldStatus)
            {
                AddHydrophoneStreamStatusEvent(context, logger, node, frequencyInfo?.AudioSampleUrl);
            }
        }

        /// <summary>
        /// Check whether a request includes the correct IFTTT-Service-Key value.
        /// </summary>
        /// <param name="request">HTTP request received</param>
        /// <returns>null on success, ObjectResult if failed</returns>
        public static ErrorResponse? CheckIftttServiceKey(HttpRequest request)
        {
            if (request.Headers.TryGetValue("IFTTT-Service-Key", out var values) &&
    values.Any())
            {
                string value = values.First() ?? String.Empty;
                if (value == Fetcher.IftttServiceKey)
                {
                    return null;
                }
            }
            string errorMessage = "Unauthorized access";
            var errorResponse = new ErrorResponse
            {
                Errors = new List<ErrorItem>
                {
                    new ErrorItem { Message = errorMessage }
                }
            };
            return errorResponse;
        }

        /// <summary>
        /// Get the AI detection count for a given location.
        /// </summary>
        /// <param name="orcanode">Node to check</param>
        /// <returns>Count of AI detections</returns>
        public static async Task<long> GetDetectionCountAsync(Orcanode orcanode)
        {
            try
            {
                string location = Uri.EscapeDataString(orcanode.OrcaHelloDisplayName);
                var uri = new Uri($"https://aifororcasdetections.azurewebsites.net/api/detections?Timeframe=1w&Location={location}&RecordsPerPage=1");

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await _httpClient.SendAsync(request);

                // Try to get the custom header
                if (!response.Headers.TryGetValues("totalnumberrecords", out var values))
                {
                    return 0;
                }

                string headerValue = values?.FirstOrDefault() ?? string.Empty;
                if (!long.TryParse(headerValue, out long totalRecords))
                {
                    return 0;
                }

                return totalRecords;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>
        /// Get a list of OrcaHelloPod objects.
        /// </summary>
        /// <param name="orcanodes">List of orcanodes</param>
        /// <returns>List of OrcaHelloPod objects</returns>
        public static async Task<List<OrcaHelloPod>> FetchPodMetricsAsync(List<Orcanode> orcanodes)
        {
            var resultList = new List<OrcaHelloPod>();
            Kubernetes? client = _k8sClient;
            if (client == null)
            {
                return resultList;
            }

            try
            {
                V1PodList allPods = await client.ListPodForAllNamespacesAsync();
                PodMetricsList? metricsList = await client.GetKubernetesPodsMetricsAsync();
                foreach (V1Pod pod in allPods)
                {
                    if (pod.Metadata == null || !pod.Metadata.Name.StartsWith("inference-system-"))
                    {
                        continue;
                    }
                    // Filter out non-Running pods (including Terminating, Pending, etc.)
                    if (pod.Status?.Phase != "Running")
                    {
                        continue;
                    }
                    V1ContainerStatus? status = GetBestContainerStatus(pod);
                    if (status?.Ready != true)
                    {
                        continue;
                    }
                    PodMetrics? podMetrics = metricsList?.Items.FirstOrDefault(n => n.Metadata.Name == pod.Metadata.Name);
                    var container = podMetrics?.Containers.FirstOrDefault(c => c.Name == "inference-system");
                    string cpuUsage = container?.Usage?.TryGetValue("cpu", out var cpu) == true ? cpu.ToString() : "0n";
                    string memoryUsage = container?.Usage?.TryGetValue("memory", out var mem) == true ? mem.ToString() : "0Ki";

                    Orcanode? orcanode = orcanodes.Find(a => a.OrcasoundSlug == pod.Metadata.NamespaceProperty);
                    long detectionCount = (orcanode != null) ? await GetDetectionCountAsync(orcanode) : 0;

                    var orcaHelloPod = new OrcaHelloPod(pod, cpuUsage, memoryUsage, modelTimestamp: string.Empty, detectionCount);
                    resultList.Add(orcaHelloPod);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[FetchContainerMetricsAsync] Error retrieving container metrics: {ex}");
            }

            return resultList;
        }

        /// <summary>
        /// Get a list of OrcaHelloNode objects.
        /// </summary>
        /// <returns>List of OrcaHelloNode objects</returns>
        public static async Task<List<OrcaHelloNode>> FetchNodeMetricsAsync()
        {
            var resultList = new List<OrcaHelloNode>();
            Kubernetes? client = _k8sClient;
            if (client == null)
            {
                return resultList;
            }

            try
            {
                V1NodeList allNodes = await client.ListNodeAsync();
                NodeMetricsList metricsList = await client.GetKubernetesNodesMetricsAsync();
                V1PodList v1Pods = await client.ListPodForAllNamespacesAsync();
                PodMetricsList podMetrics = await client.GetKubernetesPodsMetricsAsync();
                foreach (V1Node node in allNodes)
                {
                    NodeMetrics? nodeMetrics = metricsList.Items.FirstOrDefault(n => n.Metadata.Name == node.Metadata.Name);
                    string cpuUsage = nodeMetrics?.Usage.TryGetValue("cpu", out var cpu) == true ? cpu.ToString() : "0n";
                    string memoryUsage = nodeMetrics?.Usage.TryGetValue("memory", out var mem) == true ? mem.ToString() : "0Ki";

                    string lscpuOutput = string.Empty;
                    var allPodsOnNode = v1Pods.Items.Where(c => c.Spec.NodeName == node.Metadata.Name);
                    GetBestPodStatus(allPodsOnNode, out V1Pod? bestPod, out V1ContainerStatus? bestContainerStatus);
                    if (bestPod != null && bestPod.Metadata != null)
                    {
                        lscpuOutput = await GetPodLscpuOutputAsync(bestPod.Metadata.Name, bestPod.Metadata.NamespaceProperty);
                    }

                    var orcaHelloNode = new OrcaHelloNode(node, cpuUsage, memoryUsage, lscpuOutput, v1Pods.Items, podMetrics.Items);
                    resultList.Add(orcaHelloNode);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[FetchNodeMetricsAsync] Error retrieving node metrics: {ex}");
            }

            return resultList;
        }
    }
}
