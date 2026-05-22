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
        public string AksUrl => Fetcher.Configuration?["AZURE_AKS_URL"] ?? "";

        private readonly OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<OrcaHelloPodModel> _logger;
        private readonly InferenceSystemFetcher _inferenceSystemFetcher;
        private string _logData;
        private InferencePod? _pod = null;
        private Orcanode? _orcanode = null;
        private InferenceSystemNode? _inferenceSystemNode = null;
        public IList<InferencePodInstance> OtherPods { get; private set; } = new List<InferencePodInstance>();
        public string Location => _orcanode?.DisplayName ?? "Unknown";
        public string Namespace { get; set; }
        public string Name => _pod?.Name ?? "Unknown";
        public string ImageName => _pod?.ImageName ?? "Unknown";
        public double CpuCapacityCores => _pod?.CpuCapacityCores ?? 0;
        public double CpuUsageCores => _pod?.CpuUsageCores ?? 0;
        public double CpuPercent => _pod?.CpuPercent ?? 0;
        private long _memoryUsageInKi => _pod?.MemoryUsageInKi ?? 0;
        public string MemoryUsage => $"{(_memoryUsageInKi / 1024f / 1024f):F1} GiB";
        private long _memoryCapacityInKi => _pod?.MemoryCapacityInKi ?? 0;
        public string MemoryCapacity => $"{(_memoryCapacityInKi / 1024f / 1024f):F1} GiB";
        public double MemoryPercent => _pod?.MemoryPercent ?? 0;
        public string OrcasoundSlug => _orcanode?.OrcasoundSlug ?? string.Empty;

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
        public string NodeCpuModel => _inferenceSystemNode?.CpuModel ?? "Unknown";
        public bool NodeHasAvx2 => _inferenceSystemNode?.HasAvx2 ?? false;
        public bool NodeHasAvx512 => _inferenceSystemNode?.HasAvx512 ?? false;
        public double NodeCpuPercent => _inferenceSystemNode?.CpuPercent ?? 0;
        public double NodeCpuCapacityCores => _inferenceSystemNode?.CpuCapacityCores ?? 0;
        public double NodeCpuUsageCores => _inferenceSystemNode?.CpuUsageCores ?? 0;
        private long _nodeMemoryUsageInKi => _inferenceSystemNode?.MemoryUsageInKi ?? 0;
        public string NodeMemoryUsage => $"{(_nodeMemoryUsageInKi / 1024f / 1024f):F1} GiB";
        private long _nodeMemoryCapacityInKi => _inferenceSystemNode?.MemoryCapacityInKi ?? 0;
        public string NodeMemoryCapacity => $"{(_nodeMemoryCapacityInKi / 1024f / 1024f):F1} GiB";
        public double NodeMemoryPercent => _inferenceSystemNode?.MemoryPercent ?? 0;

        public string LogData => _logData;

        /// <summary>
        /// Current timestamp, in local time.
        /// </summary>
        public string NowLocal { get; private set; }

        public OrcaHelloPodModel(OrcanodeMonitorContext context, ILogger<OrcaHelloPodModel> logger, InferenceSystemFetcher inferenceSystemFetcher)
        {
            _databaseContext = context;
            _logger = logger;
            _inferenceSystemFetcher = inferenceSystemFetcher;
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
                if (_inferenceSystemNode != null)
                {
                    return $"{Orcanode.FormatTimeSpan(_inferenceSystemNode.Uptime)}";
                }
                return "None";
            }
        }

        public string NodeInstanceType => _inferenceSystemNode?.InstanceType ?? "Unknown";

        public string NodeProblems => _inferenceSystemNode?.Problems ?? "-";

        /// <summary>
        /// Get the confidence threshold display string.
        /// </summary>
        public string ConfidenceThreshold => _pod?.GetConfidenceThreshold() ?? "Unknown";

        public async Task<IActionResult> OnGetAsync(string podNamespace)
        {
            _orcanode = _databaseContext.Orcanodes.Where(n => n.OrcasoundSlug == podNamespace).FirstOrDefault();
            if (_orcanode == null)
            {
                return NotFound(); // Return a 404 error page
            }

            _pod = await _inferenceSystemFetcher.GetInferencePodByNameAsync(_orcanode, InferenceSystemFetcher.OrcaHelloInferenceContainerName, _logger);
            if (_pod == null)
            {
                return NotFound(); // Return a 404 error page
            }

            _inferenceSystemNode = await _inferenceSystemFetcher.GetNodeAsync(_pod.NodeName, InferenceSystemFetcher.OrcaHelloInferenceContainerName, _logger);
            if (_inferenceSystemNode == null)
            {
                return NotFound(); // Return a 404 error page
            }

            Namespace = podNamespace;

            OtherPods = (await _inferenceSystemFetcher.GetOtherPodsByNameAsync(_orcanode, InferenceSystemFetcher.OrcaHelloInferenceContainerName, _logger))
                .OrderByDescending(p => p.StartTime)
                .ToList();

            _logData = await _inferenceSystemFetcher.GetAIContainerLogAsync(_pod, podNamespace, _logger);
            if (_logData.IsNullOrEmpty())
            {
                return NotFound(); // Return a 404 error page
            }
            return Page();
        }
    }
}
