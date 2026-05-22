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
        private InferenceSystemNode? _inferenceSystemNode = null;
        private readonly ILogger<InferenceSystemNodeModel> _logger;
        public List<InferencePod> Pods => _inferenceSystemNode?.Pods ?? new List<InferencePod>();
        public string NodeName => _inferenceSystemNode?.Name ?? "Unknown";
        public string InstanceType => _inferenceSystemNode?.InstanceType ?? "Unknown";
        public string CpuModel => _inferenceSystemNode?.CpuModel ?? "Unknown";
        public bool HasAvx2 => _inferenceSystemNode?.HasAvx2 ?? false;
        public bool HasAvx512 => _inferenceSystemNode?.HasAvx512 ?? false;
        public double CpuPercent => _inferenceSystemNode?.CpuPercent ?? 0;
        public string Problems => _inferenceSystemNode?.Problems ?? "-";
        public double CpuCapacityCores => _inferenceSystemNode?.CpuCapacityCores ?? 0;
        public double CpuUsageCores => _inferenceSystemNode?.CpuUsageCores ?? 0;
        private long _memoryUsageInKi => _inferenceSystemNode?.MemoryUsageInKi ?? 0;
        public string MemoryUsage => $"{(_memoryUsageInKi / 1024f / 1024f):F1} GiB";
        public long MemoryCapacityInKi => _inferenceSystemNode?.MemoryCapacityInKi ?? 0;
        public string MemoryCapacity => $"{(MemoryCapacityInKi / 1024f / 1024f):F1} GiB";
        public double MemoryPercent => _inferenceSystemNode?.MemoryPercent ?? 0;
        public string Uptime
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
            _inferenceSystemNode = await _inferenceSystemFetcher.GetNodeAsync(nodeName, InferenceSystemFetcher.OrcaHelloInferenceContainerName, _logger);
            if (_inferenceSystemNode == null)
            {
                _inferenceSystemNode = await _inferenceSystemFetcher.GetNodeAsync(nodeName, InferenceSystemFetcher.PodsAIInferenceContainerName, _logger);
                if (_inferenceSystemNode == null)
                {
                    return NotFound(); // Return a 404 error page
                }
            }

            return Page();
        }
    }
}
