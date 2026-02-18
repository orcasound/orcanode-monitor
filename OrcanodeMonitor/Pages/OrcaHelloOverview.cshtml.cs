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
    public class OrcaHelloOverviewModel : PageModel
    {
        private readonly OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<OrcaHelloOverviewModel> _logger;
        private readonly OrcaHelloFetcher _orcaHelloFetcher;
        public List<Orcanode> Orcanodes { get; private set; }
        public List<OrcaHelloNode> Nodes { get; private set; }
        public List<OrcaHelloPod> Pods { get; private set; }
        public string AksUrl => Fetcher.Configuration?["AZURE_AKS_URL"] ?? "";
        public string GetNodeMemoryUsage(OrcaHelloNode node)
        {
            long nodeMemoryUsageInKi = node.MemoryUsageInKi;
            return $"{(nodeMemoryUsageInKi / 1024f / 1024f):F1} GiB";
        }

        public OrcaHelloOverviewModel(OrcanodeMonitorContext context, ILogger<OrcaHelloOverviewModel> logger, OrcaHelloFetcher orcaHelloFetcher)
        {
            _databaseContext = context;
            _logger = logger;
            _orcaHelloFetcher = orcaHelloFetcher;
            Nodes = new List<OrcaHelloNode>();
            Pods = new List<OrcaHelloPod>();
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
        public string GetLocations(OrcaHelloNode node)
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
        public string GetNodeProblems(OrcaHelloNode node)
        {
            return node.Problems;
        }

        /// <summary>
        /// Get the uptime for a node as a formatted string.
        /// </summary>
        /// <param name="node">Node to check</param>
        /// <returns>Uptime string</returns>
        public string GetNodeUptime(OrcaHelloNode node)
        {
            return Orcanode.FormatTimeSpan(node.Uptime);
        }

        /// <summary>
        /// Get the reason the pod last terminated, if any.
        /// </summary>
        /// <param name="pod">Pod to check</param>
        /// <returns>Reason, in parentheses</returns>
        public string GetPodLastTerminationReason(OrcaHelloPod pod)
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
        Orcanode? GetOrcanode(OrcaHelloPod pod)
        {
            return Orcanodes.Where(n => n.OrcasoundSlug == pod.NamespaceName).FirstOrDefault();
        }

        /// <summary>
        /// Get how far behind an AI pod is running in its audio stream.
        /// </summary>
        /// <param name="pod">The OrcaHello pod to check for lag.</param>
        /// <returns>A string representation of the lag time if available, or the pod's status otherwise.</returns>
        public string GetLag(OrcaHelloPod pod)
        {
            Orcanode? node = GetOrcanode(pod);
            if (node == null)
            {
                return string.Empty;
            }
            var status = node.OrcaHelloStatus;
            if ((status == OrcanodeOnlineStatus.Lagged || status == OrcanodeOnlineStatus.Online) &&
                (node.OrcaHelloInferencePodLag.HasValue))
            {
                return $"{Orcanode.FormatTimeSpan(node.OrcaHelloInferencePodLag.Value)}";
            }
            return status.ToString();
        }

        /// <summary>
        /// Get the online status of a pod.
        /// </summary>
        /// <param name="pod">Pod</param>
        /// <returns>Status value</returns>
        public OrcanodeOnlineStatus GetPodStatus(OrcaHelloPod pod) =>
            GetOrcanode(pod)?.OrcaHelloStatus ?? OrcanodeOnlineStatus.Absent;

        /// <summary>
        /// Get the HTML color for a pod's uptime text.
        /// </summary>
        /// <param name="pod">Pod</param>
        /// <returns>HTML color string</returns>
        public string GetPodUptimeTextColor(OrcaHelloPod pod)
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
        public string GetPodUptime(OrcaHelloPod pod)
        {
            Orcanode? node = GetOrcanode(pod);
            if (node == null)
            {
                return string.Empty;
            }
            if (node.OrcaHelloInferencePodRunningSince.HasValue)
            {
                TimeSpan runTime = DateTime.UtcNow - node.OrcaHelloInferencePodRunningSince.Value;
                return $"{Orcanode.FormatTimeSpan(runTime)}";
            }
            return "None";
        }

        /// <summary>
        /// Get the HTML background color for a pod's restarts cell.
        /// </summary>
        /// <param name="pod">Pod</param>
        /// <returns>HTML color string</returns>
        public string GetPodRestartsBackgroundColor(OrcaHelloPod pod)
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
        public string GetPodDetectionsBackgroundColor(OrcaHelloPod pod)
        {
            Orcanode? node = GetOrcanode(pod);
            return IndexModel.GetNodeOrcaHelloDetectionsBackgroundColor(node, pod.DetectionCount);
        }

        /// <summary>
        /// Get the HTML background color for a pod's lag cell.
        /// </summary>
        /// <param name="pod">Pod</param>
        /// <returns>HTML color string</returns>
        public string GetPodLagBackgroundColor(OrcaHelloPod pod)
        {
            Orcanode? node = GetOrcanode(pod);
            if (node == null)
            {
                return ColorTranslator.ToHtml(Color.Red);
            }
            return IndexModel.GetBackgroundColor(node.OrcaHelloStatus, node.OrcasoundStatus);
        }

        /// <summary>
        /// Get the HTML background color for a pod's uptime cell.
        /// </summary>
        /// <param name="pod">Pod</param>
        /// <returns>HTML color string</returns>
        public string GetPodUptimeBackgroundColor(OrcaHelloPod pod)
        {
            Orcanode? node = GetOrcanode(pod);
            if (node == null)
            {
                return ColorTranslator.ToHtml(Color.Red);
            }
            if (node.OrcaHelloStatus == OrcanodeOnlineStatus.Online || node.OrcaHelloStatus == OrcanodeOnlineStatus.Lagged)
            {
                DateTime? since = node.OrcaHelloInferencePodRunningSince;
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
            List<OrcaHelloPod> pods = await _orcaHelloFetcher.FetchPodMetricsAsync(Orcanodes);
            Pods = pods.OrderBy(n => n.NamespaceName).ToList();

            List<OrcaHelloNode> nodes = await _orcaHelloFetcher.FetchNodeMetricsAsync();
            Nodes = nodes.OrderBy(n => n.Name).ToList();
        }
    }
}
