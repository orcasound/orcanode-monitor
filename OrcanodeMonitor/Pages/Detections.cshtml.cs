// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;

namespace OrcanodeMonitor.Pages
{
    public class DetectionData
    {
        /// <summary>
        /// Number of positive machine detections.
        /// </summary>
        public long PositiveMachineDetectionCount;

        /// <summary>
        /// Minimum global confidence of positive machine detections.
        /// </summary>
        public long MinimumPositiveMachineDetectionConfidence;

        /// <summary>
        /// Average global confidence of positive machine detections.
        /// </summary>
        public long AveragePositiveMachineDetectionConfidence;

        /// <summary>
        /// Number of negative machine detections.
        /// </summary>
        public long NegativeMachineDetectionCount;

        /// <summary>
        /// Average global confidence of negative machine detections.
        /// </summary>
        public long AverageNegativeMachineDetectionConfidence;

        /// <summary>
        /// Average global confidence of negative machine detections.
        /// </summary>
        public long MaximumNegativeMachineDetectionConfidence;

        /// <summary>
        /// Total number of machine detections.
        /// </summary>
        public long MachineDetectionCount => PositiveMachineDetectionCount + NegativeMachineDetectionCount;

        /// <summary>
        /// Number of positive human detections.
        /// </summary>
        public long PositiveHumanDetectionCount;

        /// <summary>
        /// Number of negative human detections.
        /// </summary>
        public long NegativeHumanDetectionCount;

        /// <summary>
        /// Total number of human detections.
        /// </summary>
        public long HumanDetectionCount => PositiveHumanDetectionCount + NegativeHumanDetectionCount;

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

        private readonly Dictionary<string, long> _orcaHelloDetectionCounts = new Dictionary<string, long>();

        public async Task OnGetAsync()
        {
            try
            {
                // Fetch nodes for display.
                var nodes = await _databaseContext.Orcanodes.ToListAsync();
                _nodes = nodes.Where(n => ((n.DataplicityConnectionStatus != OrcanodeOnlineStatus.Absent) ||
                                           (n.OrcasoundStatus != OrcanodeOnlineStatus.Absent) ||
                                           (n.S3StreamStatus != OrcanodeOnlineStatus.Absent &&
                                            n.S3StreamStatus != OrcanodeOnlineStatus.Unauthorized)) &&
                                          (n.OrcasoundHost != "dev.orcasound.net"))
                              .OrderBy(n => n.DisplayName)
                              .ToList();

                // Fetch OrcaHello detection counts for each node.
                await OrcaHelloFetcher.FetchOrcaHelloDetectionCountsAsync(_nodes, _orcaHelloDetectionCounts);

                // TODO: Fetch additional detection details (human/machine detections, confidence levels, etc.)
                // For now, populate with placeholder data structure
                foreach (var node in _nodes)
                {
                    if (!_detectionCounts.ContainsKey(node.OrcasoundSlug))
                    {
                        _detectionCounts[node.OrcasoundSlug] = new DetectionData
                        {
                            ConfidenceThreshold = "Unknown"
                        };
                    }
                }
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
            if (data.HumanDetectionCount == 0)
            {
                return "None";
            }
            int percent = (int)Math.Round(data.PositiveHumanDetectionCount * 100.0 / data.HumanDetectionCount);
            return $"{data.PositiveHumanDetectionCount} / {data.HumanDetectionCount} ({percent}%)";
        }

        public string GetMachineDetectionCount(Orcanode node)
        {
            if (!_detectionCounts.TryGetValue(node.OrcasoundSlug, out DetectionData? data))
            {
                return "Unknown";
            }
            if (data.MachineDetectionCount == 0)
            {
                return "None";
            }
            int percent = (int)Math.Round(data.PositiveMachineDetectionCount * 100.0 / data.MachineDetectionCount);
            return $"{data.PositiveMachineDetectionCount} / {data.MachineDetectionCount} ({percent}%)";
        }

        /// <summary>
        /// Get the average global confidence for positive machine detections.
        /// </summary>
        /// <param name="node"></param>
        /// <returns>Threshold percentage</returns>
        public string GetAveragePositiveMachineConfidence(Orcanode node)
        {
            if (!_detectionCounts.TryGetValue(node.OrcasoundSlug, out DetectionData? data))
            {
                return "Unknown";
            }
            return $"{data.MinimumPositiveMachineDetectionConfidence}% min, {data.AveragePositiveMachineDetectionConfidence}% avg";
        }

        /// <summary>
        /// Get the average global confidence for negative machine detections.
        /// </summary>
        /// <param name="node"></param>
        /// <returns>Threshold percentage</returns>
        public string GetAverageNegativeMachineConfidence(Orcanode node)
        {
            if (!_detectionCounts.TryGetValue(node.OrcasoundSlug, out DetectionData? data))
            {
                return "Unknown";
            }
            return $"{data.AverageNegativeMachineDetectionConfidence}% avg, {data.MaximumNegativeMachineDetectionConfidence}% max";
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
