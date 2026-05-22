// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Models;

namespace OrcanodeMonitor.Pages
{
    public class InferenceSystemNodeModel : PageModel
    {
        private readonly InferenceSystemFetcher _inferenceSystemFetcher;
        private InferenceSystemNode? _orcaHelloNode = null;
        private readonly ILogger<InferenceSystemNodeModel> _logger;
        public List<InferencePod> Pods => _orcaHelloNode?.Pods ?? new List<InferencePod>();
        public string NodeName => _orcaHelloNode?.Name ?? "Unknown";
        public string InstanceType => _orcaHelloNode?.InstanceType ?? "Unknown";
        public string CpuModel => _orcaHelloNode?.CpuModel ?? "Unknown";
        public bool HasAvx2 => _orcaHelloNode?.HasAvx2 ?? false;
        public bool HasAvx512 => _orcaHelloNode?.HasAvx512 ?? false;
        public double CpuPercent => _orcaHelloNode?.CpuPercent ?? 0;
        public string Problems => _orcaHelloNode?.Problems ?? "-";
        public double CpuCapacityCores => _orcaHelloNode?.CpuCapacityCores ?? 0;
        public double CpuUsageCores => _orcaHelloNode?.CpuUsageCores ?? 0;
        private long _memoryUsageInKi => _orcaHelloNode?.MemoryUsageInKi ?? 0;
        public string MemoryUsage => $"{(_memoryUsageInKi / 1024f / 1024f):F1} GiB";
        public long MemoryCapacityInKi => _orcaHelloNode?.MemoryCapacityInKi ?? 0;
        public string MemoryCapacity => $"{(MemoryCapacityInKi / 1024f / 1024f):F1} GiB";
        public double MemoryPercent => _orcaHelloNode?.MemoryPercent ?? 0;
        public string Uptime
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

        public bool IsOrcaHelloPod(InferencePod pod)
        {
            return pod.Name.StartsWith(InferenceSystemFetcher.OrcaHelloInferenceContainerName + "-");
        }

        public bool IsPodsAIPod(InferencePod pod)
        {
            return pod.Name.StartsWith(InferenceSystemFetcher.PodsAIInferenceContainerName + "-");
        }

        /// <summary>
        /// Current timestamp, in local time.
        /// </summary>
        public string NowLocal { get; private set; }

        public InferenceSystemNodeModel(InferenceSystemFetcher inferenceSystemFetcher, ILogger<InferenceSystemNodeModel> logger)
        {
            _inferenceSystemFetcher = inferenceSystemFetcher;
            _logger = logger;
            NowLocal = Fetcher.UtcToLocalDateTime(DateTime.UtcNow)?.ToString() ?? "Unknown";
        }

        public async Task<IActionResult> OnGetAsync(string nodeName)
        {
            _orcaHelloNode = await _inferenceSystemFetcher.GetNodeAsync(nodeName, InferenceSystemFetcher.OrcaHelloInferenceContainerName, _logger);
            if (_orcaHelloNode == null)
            {
                return NotFound(); // Return a 404 error page
            }

            return Page();
        }
    }
}
