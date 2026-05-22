// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Drawing;
namespace OrcanodeMonitor.Pages
{
    public class PodsAIOverviewModel : PageModel
    {
        private readonly OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<PodsAIOverviewModel> _logger;
        private readonly InferenceSystemFetcher _inferenceSystemFetcher;
        public List<Orcanode> Orcanodes { get; private set; }
        public List<InferenceSystemNode> Nodes { get; private set; }
        public List<InferencePod> Pods { get; private set; }
        public string AksUrl => Fetcher.Configuration?["AZURE_AKS_URL"] ?? "";
        public string GetNodeMemoryUsage(InferenceSystemNode node)
        {
            long nodeMemoryUsageInKi = node.MemoryUsageInKi;
            return $"{(nodeMemoryUsageInKi / 1024f / 1024f):F1} GiB";
        }

        public PodsAIOverviewModel(OrcanodeMonitorContext context, ILogger<PodsAIOverviewModel> logger, InferenceSystemFetcher inferenceSystemFetcher)
        {
            _databaseContext = context;
            _logger = logger;
            _inferenceSystemFetcher = inferenceSystemFetcher;
            Nodes = new List<InferenceSystemNode>();
            Pods = new List<InferencePod>();
            Orcanodes = new List<Orcanode>();
            NowLocal = Fetcher.UtcToLocalDateTime(DateTime.UtcNow)?.ToString() ?? "Unknown";
        }

        /// <summary>
        /// Current timestamp, in local time.
        /// </summary>
        public string NowLocal { get; private set; }

        /// <summary>
        /// Get a list of Kubernetes namespaces of pods running on a given node.
        /// </summary>
        /// <param name="node">VMSS node</param>
        /// <returns>Comma-separated list of namespaces</returns>
        public string GetLocations(InferenceSystemNode node)
        {
            // Get unique namespace names for pods running on the given node.
            var namespaces = Pods
                .Where(c => c.NodeName == node.Name)
                .Select(c => c.NamespaceName)
                .Distinct();
            return string.Join(", ", namespaces);
        }

        /// <summary>
        /// Get the problems reported for a node.
        /// </summary>
        /// <param name="node">Node to check</param>
        /// <returns>Problems string</returns>
        public string GetNodeProblems(InferenceSystemNode node)
        {
            return node.Problems;
        }

        /// <summary>
        /// Get the uptime for a node as a formatted string.
        /// </summary>
        /// <param name="node">Node to check</param>
        /// <returns>Uptime string</returns>
        public string GetNodeUptime(InferenceSystemNode node)
        {
            return Orcanode.FormatTimeSpan(node.Uptime);
        }

        /// <summary>
        /// Get the reason the pod last terminated, if any.
        /// </summary>
        /// <param name="pod">Pod to check</param>
        /// <returns>Reason, in parentheses</returns>
        public string GetPodLastTerminationReason(InferencePod pod)
        {
            if (string.IsNullOrEmpty(pod.LastTerminationReason))
            {
                return string.Empty;
            }
            return "(" + pod.LastTerminationReason + ")";
        }

        /// <summary>
        /// Get the Orcanode associated with a given OrcaHello pod.
        /// </summary>
        /// <param name="pod">pod</param>
        /// <returns>Orcanode object, or null on error</returns>
        Orcanode? GetOrcanode(InferencePod pod)
        {
            return Orcanodes.Where(n => n.OrcasoundSlug == pod.NamespaceName).FirstOrDefault();
        }

        /// <summary>
        /// Get how far behind an AI pod is running in its audio stream.
        /// </summary>
        /// <param name="pod">The PODS-AI pod to check for lag.</param>
        /// <returns>A string representation of the lag time if available, or the pod's status otherwise.</returns>
        public string GetLag(InferencePod pod)
        {
            Orcanode? node = GetOrcanode(pod);
            if (node == null)
            {
                return string.Empty;
            }
            if (node.S3StreamStatus == OrcanodeOnlineStatus.Offline)
            {
                return OrcanodeOnlineStatus.Offline.ToString();
            }
            var status = node.PodsAIStatus;
            if ((status == OrcanodeOnlineStatus.Lagged || status == OrcanodeOnlineStatus.Online) &&
                (node.PodsAIInferencePodLag.HasValue))
            {
                return $"{Orcanode.FormatTimeSpan(node.PodsAIInferencePodLag.Value)}";
            }
            return status.ToString();
        }

        /// <summary>
        /// Get the online status of a pod.
        /// </summary>
        /// <param name="pod">Pod</param>
        /// <returns>Status value</returns>
        public OrcanodeOnlineStatus GetPodStatus(InferencePod pod) =>
            GetOrcanode(pod)?.PodsAIStatus ?? OrcanodeOnlineStatus.Absent;
        /// <summary>
        /// Get the HTML color for a pod's uptime text.
        /// </summary>
        /// <param name="pod">Pod</param>
        /// <returns>HTML color string</returns>
        public string GetPodUptimeTextColor(InferencePod pod)
        {
            Orcanode? node = GetOrcanode(pod);
            if (node == null)
            {
                return ColorTranslator.ToHtml(Color.Red);
            }
            return IndexModel.GetTextColor(GetPodUptimeBackgroundColor(pod));
        }

        /// <summary>
        /// Get the pod uptime as a formatted string.
        /// </summary>
        /// <param name="pod">Pod</param>
        /// <returns>Uptime string</returns>
        public string GetPodUptime(InferencePod pod)
        {
            Orcanode? node = GetOrcanode(pod);
            if (node == null)
            {
                return string.Empty;
            }
            if (node.PodsAIInferencePodRunningSince.HasValue)
            {
                TimeSpan runTime = DateTime.UtcNow - node.PodsAIInferencePodRunningSince.Value;
                return $"{Orcanode.FormatTimeSpan(runTime)}";
            }
            return "None";
        }

        /// <summary>
        /// Get the HTML background color for a pod's restarts cell.
        /// </summary>
        /// <param name="pod">Pod</param>
        /// <returns>HTML color string</returns>
        public string GetPodRestartsBackgroundColor(InferencePod pod)
        {
            if (pod.RestartCount == 0)
            {
                return ColorTranslator.ToHtml(Color.LightGreen);
            }
            if (pod.RestartCount <= 3)
            {
                return ColorTranslator.ToHtml(Color.Yellow);
            }
            return ColorTranslator.ToHtml(IndexModel.LightRed);
        }

        /// <summary>
        /// Get the HTML background color for a pod's detections cell.
        /// </summary>
        /// <param name="pod">Pod</param>
        /// <returns>HTML color string</returns>
        public string GetPodDetectionsBackgroundColor(InferencePod pod)
        {
            Orcanode? node = GetOrcanode(pod);
            return IndexModel.GetNodeMachineDetectionsBackgroundColor(node, pod.DetectionCount);
        }

        /// <summary>
        /// Get the HTML background color for a pod's lag cell.
        /// </summary>
        /// <param name="pod">Pod</param>
        /// <returns>HTML color string</returns>
        public string GetPodLagBackgroundColor(InferencePod pod)
        {
            Orcanode? node = GetOrcanode(pod);
            if (node == null)
            {
                return ColorTranslator.ToHtml(Color.Red);
            }
            if (node.S3StreamStatus == OrcanodeOnlineStatus.Offline)
            {
                return IndexModel.GetBackgroundColor(node.S3StreamStatus, node.OrcasoundStatus);
            }
            return IndexModel.GetBackgroundColor(node.PodsAIStatus, node.OrcasoundStatus);
        }

        /// <summary>
        /// Get the HTML background color for a pod's uptime cell.
        /// </summary>
        /// <param name="pod">Pod</param>
        /// <returns>HTML color string</returns>
        public string GetPodUptimeBackgroundColor(InferencePod pod)
        {
            Orcanode? node = GetOrcanode(pod);
            if (node == null)
            {
                return ColorTranslator.ToHtml(Color.Red);
            }
            if (node.PodsAIStatus == OrcanodeOnlineStatus.Online || node.PodsAIStatus == OrcanodeOnlineStatus.Lagged)
            {
                DateTime? since = node.PodsAIInferencePodRunningSince;
                if (since.HasValue)
                {
                    var ts = DateTime.UtcNow - since.Value;
                    if (ts > TimeSpan.FromHours(1))
                    {
                        return ColorTranslator.ToHtml(Color.LightGreen);
                    }

                    return ColorTranslator.ToHtml(Color.Yellow);
                }
            }
            var orcasoundStatus = node.OrcasoundStatus;
            if (orcasoundStatus != OrcanodeOnlineStatus.Online)
            {
                return ColorTranslator.ToHtml(IndexModel.LightRed);
            }
            return ColorTranslator.ToHtml(Color.Red);
        }

        public async Task OnGetAsync()
        {
            var orcanodes = await _databaseContext.Orcanodes.ToListAsync();
            Orcanodes = orcanodes.Where(n => ((n.DataplicityConnectionStatus != OrcanodeOnlineStatus.Absent) ||
                                       (n.OrcasoundStatus != OrcanodeOnlineStatus.Absent) ||
                                       (n.S3StreamStatus != OrcanodeOnlineStatus.Absent &&
                                        n.S3StreamStatus != OrcanodeOnlineStatus.Unauthorized)) &&
                                      (n.OrcasoundHost != "dev.orcasound.net"))
                          .OrderBy(n => n.DisplayName)
                          .ToList();

            // Fetch pods and nodes for display.
            List<InferencePod> pods = await _inferenceSystemFetcher.FetchPodMetricsAsync(orcanodes, InferenceSystemFetcher.PodsAIInferenceContainerName, _logger);
            Pods = pods.OrderBy(n => n.NamespaceName).ToList();

            List<InferenceSystemNode> nodes = await _inferenceSystemFetcher.FetchNodeMetricsAsync(_logger, InferenceSystemFetcher.PodsAIInferenceContainerName);
            Nodes = nodes.OrderBy(n => n.Name).ToList();
        }
    }
}
