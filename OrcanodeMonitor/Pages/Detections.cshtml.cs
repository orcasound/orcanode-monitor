// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;

namespace OrcanodeMonitor.Pages
{
    public class DetectionData
    {
        public long MachinePositiveDetections;
        public long MachineNegativeDetections;
        public long MachineTotalDetections => MachinePositiveDetections + MachineNegativeDetections;
        public long HumanPositiveDetections;
        public long HumanNegativeDetections;
        public long HumanTotalDetections => HumanPositiveDetections + HumanNegativeDetections;
        public string ConfidenceThreshold = string.Empty;
    }

    public class DetectionsModel : PageModel
    {
        private OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<DetectionsModel> _logger;
        private List<Orcanode> _nodes;
        public List<Orcanode> Nodes => _nodes;

        public DetectionsModel(OrcanodeMonitorContext context, ILogger<DetectionsModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _nodes = new List<Orcanode>();
        }

        public async Task OnGetAsync()
        {
            try
            {
                // Fetch nodes for display.
#if false
                // TODO
                var nodes = await _databaseContext.Orcanodes.ToListAsync();
                _nodes = nodes.Where(n => ((n.DataplicityConnectionStatus != OrcanodeOnlineStatus.Absent) ||
                                           (n.OrcasoundStatus != OrcanodeOnlineStatus.Absent) ||
                                           (n.S3StreamStatus != OrcanodeOnlineStatus.Absent &&
                                            n.S3StreamStatus != OrcanodeOnlineStatus.Unauthorized)) &&
                                          (n.OrcasoundHost != "dev.orcasound.net"))
                              .OrderBy(n => n.DisplayName)
                              .ToList();

                // Fetch AI detection counts in parallel.
                var detectionTasks = _nodes.Select(async node => new
                {
                    Slug = node.OrcasoundSlug,
                    Count = await Fetcher.GetDetectionCountAsync(node),
                    (double? localThreshold, int? globalThreshold) = await GetModelThresholdsAsync(node.OrcasoundSlug);
                });
                var results = await Task.WhenAll(detectionTasks);
                foreach (var result in results)
                {
                    _orcaHelloDetectionCounts[result.Slug] = result.Count;

                    if (pod.ModelGlobalThreshold.HasValue && pod.ModelLocalThreshold.HasValue)
                    {
                        int globalThreshold = pod.ModelGlobalThreshold.Value;
                        int localThresholdPercent = (int)Math.Round(pod.ModelLocalThreshold.Value * 100);
                        return $"{globalThreshold} @ {localThresholdPercent}%";
                    }
                    //return "Unknown";
                }
#else
                var node = new Orcanode();
                node.OrcasoundName = "Andrews Bay";
                node.OrcasoundSlug = "andrews-bay";
                _nodes.Add(node);
                var data = new DetectionData
                {
                    HumanNegativeDetections = 1,
                    HumanPositiveDetections = 2,
                    MachineNegativeDetections = 4,
                    MachinePositiveDetections = 3,
                    ConfidenceThreshold = "3 @ 75%"
                };
                _detectionCounts[node.OrcasoundSlug] = data;

                node = new Orcanode();
                node.OrcasoundName = "Orcasound Lab";
                node.OrcasoundSlug = "orcasound-lab";
                _nodes.Add(node);
                data = new DetectionData
                {
                    HumanPositiveDetections = 3,
                    HumanNegativeDetections = 1,
                    MachinePositiveDetections = 2,
                    MachineNegativeDetections = 4,
                    ConfidenceThreshold = "3 @ 50%"
                };
                _detectionCounts[node.OrcasoundSlug] = data;
#endif
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Exception in OnGetAsync: {ex.Message}");
            }
        }

        private readonly Dictionary<string, DetectionData> _detectionCounts = new Dictionary<string, DetectionData>();

        public string GetHumanDetectionCount(Orcanode node)
        {
            if (!_detectionCounts.TryGetValue(node.OrcasoundSlug, out DetectionData? data))
            {
                return "Unknown";
            }
            if (data.HumanTotalDetections == 0)
            {
                return "None";
            }
            int percent = (int)Math.Round(data.HumanPositiveDetections * 100.0 / data.HumanTotalDetections);
            return $"{data.HumanPositiveDetections} / {data.HumanTotalDetections} ({percent}%)";
        }

        public string GetMachineDetectionCount(Orcanode node)
        {
            if (!_detectionCounts.TryGetValue(node.OrcasoundSlug, out DetectionData? data))
            {
                return "Unknown";
            }
            if (data.MachineTotalDetections == 0)
            {
                return "None";
            }
            int percent = (int)Math.Round(data.MachinePositiveDetections * 100.0 / data.MachineTotalDetections);
            return $"{data.MachinePositiveDetections} / {data.MachineTotalDetections} ({percent}%)";
        }

        /// <summary>
        /// Get the average global confidence for positive machine detections.
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public string GetPositiveMachineThreshold(Orcanode node)
        {
            int threshold = 0; // TODO
            return $"{threshold}%";
        }

        /// <summary>
        /// Get the average global confidence for negative machine detections.
        /// </summary>
        /// <param name="node"></param>
        /// <returns></returns>
        public string GetNegativeMachineThreshold(Orcanode node)
        {
            int threshold = 0; // TODO
            return $"{threshold}%";
        }

        /// <summary>
        /// Get the confidence threshold display string for a node.
        /// Format: "{globalThreshold} @ {localThreshold}%" (e.g., "3 @ 70%")
        /// </summary>
        /// <param name="node">Node to check</param>
        /// <returns>Confidence threshold string</returns>
        public string GetConfiguredConfidenceThreshold(Orcanode node)
        {
            if (!_detectionCounts.TryGetValue(node.OrcasoundSlug, out DetectionData? data))
            {
                return "Unknown";
            }
            return data.ConfidenceThreshold;
        }
    }
}
