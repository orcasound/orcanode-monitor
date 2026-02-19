// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using k8s;
using k8s.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace OrcanodeMonitor.Core
{
    public class OrcaHelloDetectionLocation
    {
        public string Name { get; set; } = string.Empty;
        public double Longitude { get; set; }
        public double Latitude { get; set; }
    }

    public class OrcaHelloDetectionAnnotation
    {
        public int Id { get; set; }
        public double StartTime { get; set; }
        public double EndTime { get; set; }
        public double Confidence { get; set; }
    }

    public class OrcaHelloDetection
    {
        public string Id { get; set; } = string.Empty;
        public string AudioUri { get; set; } = string.Empty;
        public string SpectrogramUri { get; set; } = string.Empty;
        public OrcaHelloDetectionLocation Location { get; set; } = new();
        public string Timestamp { get; set; } = string.Empty;
        public List<OrcaHelloDetectionAnnotation> Annotations { get; set; } = new();
        public bool Reviewed { get; set; }
        public string? Found { get; set; }
        public string? Comments { get; set; }
        public double Confidence { get; set; }
        public string? Moderator { get; set; }
        public string Moderated { get; set; } = string.Empty;
        public string? Tags { get; set; }
        public override string ToString()
        {
            return $"{Location.Name}@{Timestamp}";
        }
        public bool IsPositive(Detection orcasiteDetection)
        {
            if (Found.ToLower() == "yes")
            {
                return true; // SRKW
            }
            return false;
        }
    }

    public class OrcaHelloFetcher
    {
        private readonly IKubernetes? _k8sClient;
        public IKubernetes? K8sClient => _k8sClient;

        public OrcaHelloFetcher(IKubernetes? k8sClient)
        {
            _k8sClient = k8sClient;
        }

        /// <summary>
        /// Create a Kubernetes client from environment variables.
        /// </summary>
        /// <param name="logger">Logger</param>
        /// <returns>Kubernetes client or null</returns>
        public static IKubernetes? CreateK8sClient(ILogger logger)
        {
            if (Fetcher.IsOffline)
            {
                return null;
            }
            string? k8sCACert = Fetcher.GetConfig("KUBERNETES_CA_CERT");
            if (k8sCACert == null)
            {
                logger.LogError("[CreateK8sClient] No KUBERNETES_CA_CERT");
                return null;
            }
            byte[] caCertBytes = Convert.FromBase64String(k8sCACert);
            var caCert = new X509Certificate2(caCertBytes);
            string? host = Fetcher.GetConfig("KUBERNETES_SERVICE_HOST");
            if (string.IsNullOrEmpty(host))
            {
                logger.LogError("[CreateK8sClient] No KUBERNETES_SERVICE_HOST");
                return null;
            }
            string? accessToken = Fetcher.GetConfig("KUBERNETES_TOKEN");
            if (string.IsNullOrEmpty(accessToken))
            {
                logger.LogError("[CreateK8sClient] No KUBERNETES_TOKEN");
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

        /// <summary>
        /// Get an object representing a node hosting an OrcaHello inference pod.
        /// </summary>
        /// <param name="nodeName">OrcaHello node name</param>
        /// <param name="logger">Logger</param>
        /// <returns>OrcaHelloNode</returns>
        public async Task<OrcaHelloNode?> GetOrcaHelloNodeAsync(string nodeName, ILogger logger)
        {
            IKubernetes? client = _k8sClient;
            if (client == null)
            {
                return null;
            }

            try
            {
                V1Node node = await client.CoreV1.ReadNodeAsync(nodeName);

                NodeMetricsList nodeMetricsList = await client.GetKubernetesNodesMetricsAsync();
                NodeMetrics? nodeMetrics = nodeMetricsList.Items.FirstOrDefault(n => n.Metadata.Name == nodeName);
                string cpuUsage = nodeMetrics?.Usage.TryGetValue("cpu", out var cpu) == true ? cpu.ToString() : "0n";
                string memoryUsage = nodeMetrics?.Usage.TryGetValue("memory", out var mem) == true ? mem.ToString() : "0Ki";

                string lscpuOutput = string.Empty;
                V1PodList v1Pods = await client.CoreV1.ListPodForAllNamespacesAsync();
                List<V1Pod> allPodsOnNode = v1Pods.Items
                    .Where(p => p.Spec.NodeName == nodeName)
                    .ToList();

                GetBestPodStatus(allPodsOnNode, out V1Pod? bestPod, out V1ContainerStatus? bestContainerStatus);
                if (bestPod != null && bestPod.Metadata != null)
                {
                    lscpuOutput = await GetPodLscpuOutputAsync(bestPod.Metadata.Name, bestPod.Metadata.NamespaceProperty, logger);
                }

                PodMetricsList podMetrics = await client.GetKubernetesPodsMetricsAsync();
                return new OrcaHelloNode(node, cpuUsage, memoryUsage, lscpuOutput, v1Pods.Items, podMetrics.Items);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[GetOrcaHelloNodeAsync] Error retrieving node info for '{NodeName}'", nodeName);
                return null;
            }
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

            // If both running, prefer the most recent start.
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
        /// <param name="pod">Pod to look in</param>
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

        /// <summary>
        /// Exec into a pod running to get the output of a command.
        /// </summary>
        /// <param name="podName">Name of pod to exec into</param>
        /// <param name="namespaceName">Pod namespace</param>
        /// <param name="cmd">The command to execute in the pod.</param>
        /// <param name="logger">Logger</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the combined standard output and error from the specified command executed in the pod, as a string.</returns>
        private async Task<string> GetPodCommandOutput(string podName, string namespaceName, string[] cmd, ILogger logger)
        {
            IKubernetes? client = _k8sClient;
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
                logger.LogError(ex, "[GetPodCommandOutput] Error retrieving node info for '{NamespaceName}'", namespaceName);
                return string.Empty;
            }

            return sb.ToString();
        }

        /// <summary>
        /// Exec into a pod running to get CPU info.
        /// </summary>
        /// <param name="podName">Name of pod to exec into</param>
        /// <param name="namespaceName">Namespace of pod to exec into</param>
        /// <param name="logger">Logger</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the combined standard output and error from the <c>lscpu</c> command executed in the pod, as a string.</returns>
        private async Task<string> GetPodLscpuOutputAsync(string podName, string namespaceName, ILogger logger)
        {
            string[] cmd = { "lscpu" };
            return await GetPodCommandOutput(podName, namespaceName, cmd, logger);
        }

        /// <summary>
        /// Exec into a pod running to get model info.
        /// </summary>
        /// <param name="pod">Pod to exec into</param>
        /// <param name="logger">Logger</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the combined standard output and error from the command executed in the pod, as a string.</returns>
        private async Task<string> GetPodModelTimestampAsync(V1Pod pod, ILogger logger)
        {
            if (pod.Metadata == null)
            {
                return string.Empty;
            }

            string[] command = { "stat", "-c", "%y", "/usr/src/app/model/model.pkl" };
            return await GetPodCommandOutput(pod.Metadata.Name, pod.Metadata.NamespaceProperty, command, logger);
        }

        /// <summary>
        /// Get model thresholds from the hydrophone-configs ConfigMap.
        /// </summary>
        /// <param name="namespaceName">Namespace name</param>
        /// <param name="logger">Logger</param>
        /// <returns>Tuple of (localThreshold, globalThreshold) or (null, null) if not found</returns>
        private async Task<(double? localThreshold, int? globalThreshold)> GetModelThresholdsAsync(string namespaceName, ILogger logger)
        {
            IKubernetes? client = _k8sClient;
            if (client == null)
            {
                return (null, null);
            }

            try
            {
                V1ConfigMap configMap = await client.CoreV1.ReadNamespacedConfigMapAsync(
                    name: "hydrophone-configs",
                    namespaceParameter: namespaceName);

                if (configMap.Data != null && configMap.Data.TryGetValue("config.yml", out string? yamlContent))
                {
                    var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(UnderscoredNamingConvention.Instance)
                        .Build();

                    var config = deserializer.Deserialize<Dictionary<string, object>>(yamlContent);

                    double? localThreshold = null;
                    int? globalThreshold = null;

                    if (config.TryGetValue("model_local_threshold", out object? localValue))
                    {
                        if (localValue is double d)
                        {
                            localThreshold = d;
                        }
                        else if (double.TryParse(localValue?.ToString(), out double parsed))
                        {
                            localThreshold = parsed;
                        }
                    }

                    if (config.TryGetValue("model_global_threshold", out object? globalValue))
                    {
                        if (globalValue is int i)
                        {
                            globalThreshold = i;
                        }
                        else if (int.TryParse(globalValue?.ToString(), out int parsed))
                        {
                            globalThreshold = parsed;
                        }
                    }

                    return (localThreshold, globalThreshold);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[GetModelThresholdsAsync] Error retrieving ConfigMap for namespace '{NamespaceName}'", namespaceName);
            }

            return (null, null);
        }

        /// <summary>
        /// Get an object representing an OrcaHello inference pod.
        /// </summary>
        /// <param name="orcanode">Orcanode object associated with the pod</param>
        /// <param name="logger">Logger</param>
        /// <returns>OrcaHelloPod</returns>
        public async Task<OrcaHelloPod?> GetOrcaHelloPodAsync(Orcanode orcanode, ILogger logger)
        {
            IKubernetes? client = _k8sClient;
            if (client == null)
            {
                logger.LogError("[GetOrcaHelloPodAsync] Kubernetes client is null");
                return null;
            }

            string namespaceName = orcanode.OrcasoundSlug;
            V1PodList pods = await client.CoreV1.ListNamespacedPodAsync(namespaceName);
            GetBestPodStatus(pods.Items, out V1Pod? bestPod, out V1ContainerStatus? bestContainerStatus);
            if (bestPod == null)
            {
                logger.LogError("[GetOrcaHelloPodAsync] Best pod is null");
                return null;
            }

            PodMetricsList? metricsList = await client.GetKubernetesPodsMetricsByNamespaceAsync(namespaceName);
            PodMetrics? podMetric = metricsList?.Items?.FirstOrDefault(n => n.Metadata?.Name?.StartsWith("inference-system-") == true);
            var container = podMetric?.Containers?.FirstOrDefault(c => c.Name == "inference-system");
            string cpuUsage = container?.Usage?.TryGetValue("cpu", out var cpu) == true ? cpu.ToString() : "0n";
            string memoryUsage = container?.Usage?.TryGetValue("memory", out var mem) == true ? mem.ToString() : "0Ki";

            string modelTimestamp = await GetPodModelTimestampAsync(bestPod, logger);

            long detectionCount = await GetOrcaHelloDetectionCountAsync(orcanode, logger);

            (double? localThreshold, int? globalThreshold) = await GetModelThresholdsAsync(namespaceName, logger);

            return new OrcaHelloPod(bestPod, cpuUsage, memoryUsage, modelTimestamp, detectionCount, localThreshold, globalThreshold);
        }

        /// <summary>
        /// Get the list of inference-system pods in the namespace that are not the currently
        /// running (best) pod.  These are the Pending, Evicted, Failed, etc. instances that
        /// should appear in the "Other Pods" table on the OrcaHelloPod page.
        /// </summary>
        /// <param name="orcanode">Orcanode whose namespace is to be queried</param>
        /// <returns>List of non-best pods, or an empty list if none are found</returns>
        public async Task<IList<OrcaHelloPodInstance>> GetOtherPodsAsync(Orcanode orcanode)
        {
            IKubernetes? client = _k8sClient;
            if (client == null)
            {
                return new List<OrcaHelloPodInstance>();
            }

            string namespaceName = orcanode.OrcasoundSlug;
            V1PodList pods = await client.CoreV1.ListNamespacedPodAsync(namespaceName);

            GetBestPodStatus(pods.Items, out V1Pod? bestPod, out _);
            string bestPodName = bestPod?.Metadata?.Name ?? string.Empty;

            return pods.Items
                .Where(p =>
                {
                    string? name = p.Metadata?.Name;
                    return !string.IsNullOrEmpty(name)
                        && name.StartsWith("inference-system")
                        && name != bestPodName;
                })
                .Select(p => new OrcaHelloPodInstance(p))
                .ToList();
        }

        /// <summary>
        /// Get pod logs
        /// </summary>
        /// <param name="container">Container</param>
        /// <param name="namespaceName">Namespace</param>
        /// <param name="logger">Logger</param>
        /// <returns>Log</returns>
        public async Task<string> GetOrcaHelloLogAsync(OrcaHelloPod? container, string namespaceName, ILogger logger)
        {
            if (container == null)
            {
                return string.Empty;
            }
            string podName = container.Name;

            IKubernetes? client = _k8sClient;
            if (client == null)
            {
                return string.Empty;
            }

            try
            {
                Stream? logs = await client.CoreV1.ReadNamespacedPodLogAsync(
                    name: podName,
                    namespaceParameter: namespaceName,
                    tailLines: 300);
                if (logs == null)
                {
                    return string.Empty;
                }
                using var reader = new StreamReader(logs);
                string text = await reader.ReadToEndAsync();

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
                logger.LogError(ex, "Exception in GetOrcaHelloLogAsync");
                return string.Empty;
            }
        }

        /// <summary>
        /// Update the list of Orcanodes using data about InferenceSystem containers in Azure.
        /// </summary>
        /// <param name="context">Database context to update</param>
        /// <param name="logger">Logger</param>
        /// <returns></returns>
        public async Task UpdateOrcaHelloDataAsync(IOrcanodeMonitorContext context, ILogger logger)
        {
            try
            {
                IKubernetes? client = _k8sClient;
                if (client == null)
                {
                    return;
                }

                // Get a snapshot to use during the loop to avoid multiple queries.
                var foundList = await context.Orcanodes.ToListAsync();

                // Create a list to track what nodes are no longer returned.
                var unfoundList = foundList.ToList();

                var pods = await client.CoreV1.ListPodForAllNamespacesAsync();
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
                            Stream? logs = await client.CoreV1.ReadNamespacedPodLogAsync(
                                name: podName,
                                namespaceParameter: slug,
                                tailLines: 300);
                            if (logs != null)
                            {
                                int lastLiveIndex = -1;
                                using var reader = new StreamReader(logs);
                                string? line;
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
                                    Fetcher.TimestampResult? result = await Fetcher.GetLatestS3TimestampAsync(node, true, logger);
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
                        oldNode = Fetcher.FindOrcanodeByOrcasoundFeedId(foundList, unfoundNode.OrcasoundFeedId);
                    }
                    else if (!unfoundNode.DataplicitySerial.IsNullOrEmpty())
                    {
                        oldNode = Fetcher.FindOrcanodeByDataplicitySerial(foundList, unfoundNode.DataplicitySerial, out OrcanodeOnlineStatus connectionStatus);
                    }
                    if (oldNode != null)
                    {
                        oldNode.OrcaHelloId = String.Empty;
                    }
                }

                MonitorState.GetFrom(context).LastUpdatedTimestampUtc = DateTime.UtcNow;
                await Fetcher.SaveChangesAsync(context);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Exception in UpdateOrcaHelloDataAsync");
            }
        }

        /// <summary>
        /// Get all OrcaHello detections in the given timeframe.
        /// </summary>
        /// <param name="logger">Logger</param>
        /// <param name="timeframe">Timeframe string for the API (e.g., "1w" for one week, "1m" for one month)</param>
        /// <returns>List of AI detections in the given timeframe</returns>
        public async Task<List<OrcaHelloDetection>> GetRecentDetectionsAsync(ILogger logger, string timeframe = "1w")
        {
            long pageCount = 1;
            var allDetections = new List<OrcaHelloDetection>();

            try
            {
                for (long page = 1; page <= pageCount; page++)
                {
                    // The API is paginated, so we need to loop through pages until we've retrieved them all.
                    var uri = new Uri($"https://aifororcasdetections.azurewebsites.net/api/detections?Timeframe={timeframe}&Location=all&RecordsPerPage=50&Page={page}");

                    using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                    using var response = await Fetcher.HttpClient.SendAsync(request);

                    // Get the total number of pages from the custom header. If the header is missing or invalid,
                    // we'll just return the first page of results.
                    if (response.Headers.TryGetValues("totalamountpages", out var values))
                    {
                        string headerValue = values?.FirstOrDefault() ?? string.Empty;
                        if (long.TryParse(headerValue, out long totalAmountPages))
                        {
                            pageCount = totalAmountPages;
                        }
                    }

                    string jsonString = await response.Content.ReadAsStringAsync();
                    var detections = JsonSerializer.Deserialize<List<OrcaHelloDetection>>(
                        jsonString,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );

                    if (detections != null)
                    {
                        allDetections.AddRange(detections);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[GetOrcaHelloDetectionsAsync] Error retrieving detections");
            }

            return allDetections;
        }

        /// <summary>
        /// Get the number of OrcaHello detections for a given location in the past week.
        /// </summary>
        /// <param name="orcanode">Node to check</param>
        /// <param name="logger">Logger instance</param>
        /// <returns>Count of AI detections in the past week</returns>
        public async Task<long> GetOrcaHelloDetectionCountAsync(Orcanode orcanode, ILogger logger)
        {
            try
            {
                string location = Uri.EscapeDataString(orcanode.OrcaHelloDisplayName);

                // Ask for a record of 1 page just to get the total count in a header.
                // This should be more efficient than querying GetOrcaHelloDetectionsAsync
                // to enumerate all of them just to get the count.
                var uri = new Uri($"https://aifororcasdetections.azurewebsites.net/api/detections?Timeframe=1w&Location={location}&RecordsPerPage=1");

                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                using var response = await Fetcher.HttpClient.SendAsync(request);
                if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    return 0;
                }
                string jsonString = await response.Content.ReadAsStringAsync();
                var detections = JsonSerializer.Deserialize<List<OrcaHelloDetection>>(
                    jsonString,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                // Try to get the custom header.
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
            catch (Exception ex)
            {
                logger.LogError(ex, "[GetOrcaHelloDetectionCountAsync] Error retrieving detections");
                return 0;
            }
        }

        /// <summary>
        /// Fetch OrcaHello detection counts in parallel.
        /// </summary>
        /// <param name="nodes">List of Orcanodes</param>
        /// <param name="counts">Dictionary of counts</param>
        /// <param name="logger">Logger instance</param>
        /// <returns></returns>
        public async Task FetchOrcaHelloDetectionCountsAsync(List<Orcanode> nodes, Dictionary<string, long> counts, ILogger logger)
        {
            var detectionTasks = nodes.Select(async node => new
            {
                Slug = node.OrcasoundSlug,
                Count = await GetOrcaHelloDetectionCountAsync(node, logger)
            });
            var results = await Task.WhenAll(detectionTasks);
            foreach (var result in results)
            {
                counts[result.Slug] = result.Count;
            }
        }

        /// <summary>
        /// Get a list of OrcaHelloPod objects.
        /// </summary>
        /// <param name="orcanodes">List of orcanodes</param>
        /// <param name="logger">Logger instance</param>
        /// <returns>List of OrcaHelloPod objects</returns>
        public async Task<List<OrcaHelloPod>> FetchPodMetricsAsync(List<Orcanode> orcanodes, ILogger logger)
        {
            var resultList = new List<OrcaHelloPod>();
            IKubernetes? client = _k8sClient;
            if (client == null)
            {
                return resultList;
            }

            try
            {
                V1PodList allPods = await client.CoreV1.ListPodForAllNamespacesAsync();
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
                    PodMetrics? podMetrics = metricsList?.Items?.FirstOrDefault(n => n.Metadata.Name == pod.Metadata.Name);
                    var container = podMetrics?.Containers?.FirstOrDefault(c => c.Name == "inference-system");
                    string cpuUsage = container?.Usage?.TryGetValue("cpu", out var cpu) == true ? cpu.ToString() : "0n";
                    string memoryUsage = container?.Usage?.TryGetValue("memory", out var mem) == true ? mem.ToString() : "0Ki";

                    Orcanode? orcanode = orcanodes.Find(a => a.OrcasoundSlug == pod.Metadata.NamespaceProperty);
                    long detectionCount = (orcanode != null) ? await GetOrcaHelloDetectionCountAsync(orcanode, logger) : 0;

                    (double? localThreshold, int? globalThreshold) = await GetModelThresholdsAsync(pod.Metadata.NamespaceProperty, logger);

                    var orcaHelloPod = new OrcaHelloPod(pod, cpuUsage, memoryUsage, modelTimestamp: string.Empty, detectionCount, localThreshold, globalThreshold);
                    resultList.Add(orcaHelloPod);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[FetchPodMetricsAsync] Error retrieving container metrics");
            }

            return resultList;
        }

        /// <summary>
        /// Get a list of OrcaHelloNode objects.
        /// </summary>
        /// <param name="logger">Logger instance</param>
        /// <returns>List of OrcaHelloNode objects</returns>
        public async Task<List<OrcaHelloNode>> FetchNodeMetricsAsync(ILogger logger)
        {
            var resultList = new List<OrcaHelloNode>();
            IKubernetes? client = _k8sClient;
            if (client == null)
            {
                return resultList;
            }

            try
            {
                V1NodeList allNodes = await client.CoreV1.ListNodeAsync();
                NodeMetricsList metricsList = await client.GetKubernetesNodesMetricsAsync();
                V1PodList v1Pods = await client.CoreV1.ListPodForAllNamespacesAsync();
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
                        lscpuOutput = await GetPodLscpuOutputAsync(bestPod.Metadata.Name, bestPod.Metadata.NamespaceProperty, logger);
                    }

                    var orcaHelloNode = new OrcaHelloNode(node, cpuUsage, memoryUsage, lscpuOutput, v1Pods.Items, podMetrics.Items);
                    resultList.Add(orcaHelloNode);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "[FetchNodeMetricsAsync] Error retrieving node metrics");
            }

            return resultList;
        }
    }
}
