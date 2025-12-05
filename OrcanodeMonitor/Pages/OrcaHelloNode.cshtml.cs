// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using k8s.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.IdentityModel.Tokens;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;

namespace OrcanodeMonitor.Pages
{
    public class OrcaHelloNodeModel : PageModel
    {
        public string AksUrl => Environment.GetEnvironmentVariable("AZURE_AKS_URL") ?? "";

        private OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<OrcaHelloNodeModel> _logger;
        private string _logData;
        private OrcaHelloContainer? _container = null;
        private Orcanode? _node = null;
        private OrcaHelloNode? _k8sNode = null;
        public string Location => _node?.DisplayName ?? "Unknown";
        public string PodNamespace { get; set; }
        public string PodName => _container?.PodName ?? "Unknown";
        public string ImageName => _container?.ImageName ?? "Unknown";
        public double ContainerCpuCapacityCores => _container?.CpuCapacityCores ?? 0;
        public double ContainerCpuUsageCores => _container?.CpuUsageCores ?? 0;
        public double ContainerCpuPercent => _container?.CpuPercent ?? 0;
        private long _containerMemoryUsageInKi => _container?.MemoryUsageInKi ?? 0;
        public string ContainerMemoryUsage => $"{(_containerMemoryUsageInKi / 1024f / 1024f):F1} GiB";
        private long _containerMemoryCapacityInKi => _container?.MemoryCapacityInKi ?? 0;
        public string ContainerMemoryCapacity => $"{(_containerMemoryCapacityInKi / 1024f / 1024f):F1} GiB";
        public double ContainerMemoryPercent => _container?.MemoryPercent ?? 0;
        public string NodeName => _container?.NodeName ?? "Unknown";
        public string NodeCpuModel => _k8sNode?.CpuModel ?? "Unknown";
        public bool NodeHasAvx2 => _k8sNode?.HasAvx2 ?? false;
        public bool NodeHasAvx512 => _k8sNode?.HasAvx512 ?? false;
        public double NodeCpuPercent => _k8sNode?.CpuPercent ?? 0;
        public double NodeCpuCapacityCores => _k8sNode?.CpuCapacityCores ?? 0;
        public double NodeCpuUsageCores => _k8sNode?.CpuUsageCores ?? 0;
        private long _nodeMemoryUsageInKi => _k8sNode?.MemoryUsageInKi ?? 0;
        public string NodeMemoryUsage => $"{(_nodeMemoryUsageInKi / 1024f / 1024f):F1} GiB";
        private long _nodeMemoryCapacityInKi => _k8sNode?.MemoryCapacityInKi ?? 0;
        public string NodeMemoryCapacity => $"{(_nodeMemoryCapacityInKi / 1024f / 1024f):F1} GiB";
        public double NodeMemoryPercent => _k8sNode?.MemoryPercent ?? 0;

        public string LogData => _logData;

        public OrcaHelloNodeModel(OrcanodeMonitorContext context, ILogger<OrcaHelloNodeModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            PodNamespace = string.Empty;
            _logData = string.Empty;
        }

        public string Lag
        {
            get
            {
                TimeSpan? ts = _node?.OrcaHelloInferencePodLag;
                if (!ts.HasValue)
                {
                    return string.Empty;
                }
                return $"{Orcanode.FormatTimeSpan(ts.Value)}";
            }
        }

        public string ContainerUptime
        {
            get
            {
                if (_node?.OrcaHelloInferencePodRunningSince.HasValue ?? false)
                {
                    TimeSpan runTime = DateTime.UtcNow - _node.OrcaHelloInferencePodRunningSince.Value;
                    return $"{Orcanode.FormatTimeSpan(runTime)}";
                }
                return "None";
            }
        }

        public string NodeUptime
        {
            get
            {
                if (_k8sNode != null)
                {
                    return $"{Orcanode.FormatTimeSpan(_k8sNode.Uptime)}";
                }
                return "None";
            }
        }

        public string NodeInstanceType => _k8sNode?.InstanceType ?? "Unknown";

        public async Task<IActionResult> OnGetAsync(string podNamespace)
        {
            _node = _databaseContext.Orcanodes.Where(n => n.OrcasoundSlug == podNamespace).FirstOrDefault();
            if (_node == null)
            {
                return NotFound(); // Return a 404 error page
            }

            _container = await Fetcher.GetOrcaHelloPodAsync(podNamespace);
            if (_container == null)
            {
                return NotFound(); // Return a 404 error page
            }

            _k8sNode = await Fetcher.GetOrcaHelloNodeAsync(_container);
            if (_k8sNode == null)
            {
                return NotFound(); // Return a 404 error page
            }

            PodNamespace = podNamespace;

            _logData = await Fetcher.GetOrcaHelloLogAsync(podNamespace, _logger);
            if (_logData.IsNullOrEmpty())
            {
                return NotFound(); // Return a 404 error page
            }
            return Page();
        }
    }
}
