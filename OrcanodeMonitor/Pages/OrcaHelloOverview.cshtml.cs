// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;

namespace OrcanodeMonitor.Pages
{
    public class OrcaHelloOverviewModel : PageModel
    {
        private readonly OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<OrcaHelloOverviewModel> _logger;
        public List<Orcanode> Orcanodes { get; private set; }
        public List<OrcaHelloNode> Nodes { get; private set; }
        public List<OrcaHelloContainer> Containers { get; private set; }
        public string AksUrl => Environment.GetEnvironmentVariable("AZURE_AKS_URL") ?? "";
        public OrcaHelloOverviewModel(OrcanodeMonitorContext context, ILogger<OrcaHelloOverviewModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            Nodes = new List<OrcaHelloNode>();
            Containers = new List<OrcaHelloContainer>();
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
            // Get unique namespace names for containers running on the given node.
            var namespaces = Containers
                .Where(c => c.NodeName == node.Name)
                .Select(c => c.NamespaceName)
                .Distinct();
            return string.Join(", ", namespaces);
        }

        /// <summary>
        /// Get how far behind an AI container is running in its audio stream.
        /// </summary>
        /// <param name="container">The OrcaHello container to check for lag.</param>
        /// <returns>A string representation of the lag time if available, or the container's status otherwise.</returns>
        public string GetLag(OrcaHelloContainer container)
        {
            Orcanode? node = Orcanodes.Where(n => n.OrcasoundSlug == container.NamespaceName).FirstOrDefault();
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

            // Fetch containers and nodes for display.
            List<OrcaHelloContainer> containers = await Fetcher.FetchContainerMetricsAsync();
            Containers = containers.OrderBy(n => n.NamespaceName).ToList();

            List<OrcaHelloNode> nodes = await Fetcher.FetchNodeMetricsAsync(containers);
            Nodes = nodes.OrderBy(n => n.Name).ToList();
        }
    }
}
