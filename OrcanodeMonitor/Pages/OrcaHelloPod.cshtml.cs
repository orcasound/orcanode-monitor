// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.IdentityModel.Tokens;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;

namespace OrcanodeMonitor.Pages
{
    public class OrcaHelloPodModel : PageModel
    {
        public string AksUrl => Environment.GetEnvironmentVariable("AZURE_AKS_URL") ?? "";

        private OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<OrcaHelloPodModel> _logger;
        private string _logData;
        private OrcaHelloPod? _pod = null;
        private Orcanode? _orcanode = null;
        private OrcaHelloNode? _orcaHelloNode = null;
        public string Location => _orcanode?.DisplayName ?? "Unknown";
        public string Namespace { get; set; }
        public string Name => _pod?.Name ?? "Unknown";
        public string ImageName => _pod?.ImageName ?? "Unknown";
        public string ModelTimestamp => _pod?.ModelTimestamp ?? "Unknown";
        public double CpuCapacityCores => _pod?.CpuCapacityCores ?? 0;
        public double CpuUsageCores => _pod?.CpuUsageCores ?? 0;
        public double CpuPercent => _pod?.CpuPercent ?? 0;
        private long _memoryUsageInKi => _pod?.MemoryUsageInKi ?? 0;
        public string MemoryUsage => $"{(_memoryUsageInKi / 1024f / 1024f):F1} GiB";
        private long _memoryCapacityInKi => _pod?.MemoryCapacityInKi ?? 0;
        public string MemoryCapacity => $"{(_memoryCapacityInKi / 1024f / 1024f):F1} GiB";
        public double MemoryPercent => _pod?.MemoryPercent ?? 0;

        /// <summary>
        /// Get the reason (in parentheses) the pod last terminated, if any.
        /// </summary>
        public string LastTerminationReason
        {
            get
            {
                if (_pod == null || string.IsNullOrEmpty(_pod.LastTerminationReason))
                {
                    return string.Empty;
                }
                return "(" + _pod.LastTerminationReason + ")";
            }
        }

        /// <summary>
        /// Number of times this pod has been restarted.
        /// </summary>
        public long RestartCount => _pod?.RestartCount ?? 0;

        public string NodeName => _pod?.NodeName ?? "Unknown";
        public string NodeCpuModel => _orcaHelloNode?.CpuModel ?? "Unknown";
        public bool NodeHasAvx2 => _orcaHelloNode?.HasAvx2 ?? false;
        public bool NodeHasAvx512 => _orcaHelloNode?.HasAvx512 ?? false;
        public double NodeCpuPercent => _orcaHelloNode?.CpuPercent ?? 0;
        public double NodeCpuCapacityCores => _orcaHelloNode?.CpuCapacityCores ?? 0;
        public double NodeCpuUsageCores => _orcaHelloNode?.CpuUsageCores ?? 0;
        private long _nodeMemoryUsageInKi => _orcaHelloNode?.MemoryUsageInKi ?? 0;
        public string NodeMemoryUsage => $"{(_nodeMemoryUsageInKi / 1024f / 1024f):F1} GiB";
        private long _nodeMemoryCapacityInKi => _orcaHelloNode?.MemoryCapacityInKi ?? 0;
        public string NodeMemoryCapacity => $"{(_nodeMemoryCapacityInKi / 1024f / 1024f):F1} GiB";
        public double NodeMemoryPercent => _orcaHelloNode?.MemoryPercent ?? 0;

        public string LogData => _logData;

        /// <summary>
        /// Current timestamp, in local time.
        /// </summary>
        public string NowLocal { get; private set; }

        public OrcaHelloPodModel(OrcanodeMonitorContext context, ILogger<OrcaHelloPodModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            Namespace = string.Empty;
            _logData = string.Empty;
            NowLocal = Fetcher.UtcToLocalDateTime(DateTime.UtcNow)?.ToString() ?? "Unknown";
        }

        public long DetectionCount => _pod?.DetectionCount ?? 0;

        public string Lag
        {
            get
            {
                TimeSpan? ts = _orcanode?.OrcaHelloInferencePodLag;
                if (!ts.HasValue)
                {
                    return string.Empty;
                }
                return $"{Orcanode.FormatTimeSpan(ts.Value)}";
            }
        }

        public string Uptime
        {
            get
            {
                if (_orcanode?.OrcaHelloInferencePodRunningSince.HasValue ?? false)
                {
                    TimeSpan runTime = DateTime.UtcNow - _orcanode.OrcaHelloInferencePodRunningSince.Value;
                    return $"{Orcanode.FormatTimeSpan(runTime)}";
                }
                return "None";
            }
        }

        public string NodeUptime
        {
            get
            {
                if (_orcaHelloNode != null)
                {
                    return $"{Orcanode.FormatTimeSpan(_orcaHelloNode.Uptime)}";
                }
                return "None";
            }
        }

        public string NodeInstanceType => _orcaHelloNode?.InstanceType ?? "Unknown";

        public async Task<IActionResult> OnGetAsync(string podNamespace)
        {
            _orcanode = _databaseContext.Orcanodes.Where(n => n.OrcasoundSlug == podNamespace).FirstOrDefault();
            if (_orcanode == null)
            {
                return NotFound(); // Return a 404 error page
            }

            _pod = await Fetcher.GetOrcaHelloPodAsync(_orcanode, podNamespace);
            if (_pod == null)
            {
                return NotFound(); // Return a 404 error page
            }

            _orcaHelloNode = await Fetcher.GetOrcaHelloNodeAsync(_pod.NodeName);
            if (_orcaHelloNode == null)
            {
                return NotFound(); // Return a 404 error page
            }

            Namespace = podNamespace;

            _logData = await Fetcher.GetOrcaHelloLogAsync(_pod, podNamespace, _logger);
            if (_logData.IsNullOrEmpty())
            {
                return NotFound(); // Return a 404 error page
            }
            return Page();
        }
    }
}
