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
        public double MinimumPositiveMachineDetectionConfidence;

        /// <summary>
        /// Cumulative global confidence of positive machine detections.
        /// </summary>
        public double CumulativePositiveMachineDetectionConfidence;

        /// <summary>
        /// Average global confidence of positive machine detections.
        /// </summary>
        public long AveragePositiveMachineDetectionConfidence
        {
            get
            {
                if (PositiveMachineDetectionCount == 0)
                {
                    return 0;
                }
                return (long)Math.Round(CumulativePositiveMachineDetectionConfidence * 1.0 / PositiveMachineDetectionCount);
            }
        }

        /// <summary>
        /// Number of negative machine detections.
        /// </summary>
        public long NegativeMachineDetectionCount;

        /// <summary>
        /// Cumulative global confidence of negative machine detections.
        /// </summary>
        public double CumulativeNegativeMachineDetectionConfidence;

        public long AverageNegativeMachineDetectionConfidence
        {
            get
            {
                if (NegativeMachineDetectionCount == 0)
                {
                    return 0;
                }
                return (long)Math.Round(CumulativeNegativeMachineDetectionConfidence * 1.0 / NegativeMachineDetectionCount);
            }
        }

        /// <summary>
        /// Average global confidence of negative machine detections.
        /// </summary>
        public double MaximumNegativeMachineDetectionConfidence;

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
        private readonly OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<DetectionsModel> _logger;
        private readonly OrcaHelloFetcher _orcaHelloFetcher;
        private List<Orcanode> _nodes;
        public List<Orcanode> Nodes => _nodes;

        public DetectionsModel(OrcanodeMonitorContext context, ILogger<DetectionsModel> logger, OrcaHelloFetcher orcaHelloFetcher)
        {
            _databaseContext = context;
            _logger = logger;
            _orcaHelloFetcher = orcaHelloFetcher;
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
                await _orcaHelloFetcher.FetchOrcaHelloDetectionCountsAsync(_nodes, _orcaHelloDetectionCounts);

                // Fetch additional detection details (human/machine detections, confidence levels, etc.)
                List<Detection>? detections = await Fetcher.GetRecentDetectionsAsync(_logger);
                if (detections != null)
                {
                    foreach (var detection in detections)
                    {
                        Orcanode? node = _nodes.Where(n => n.OrcasoundFeedId == detection.NodeID).FirstOrDefault();
                        if (node == null)
                        {
                            continue;
                        }
                        if (!_detectionCounts.ContainsKey(node.OrcasoundSlug))
                        {
                            _detectionCounts[node.OrcasoundSlug] = new DetectionData
                            {
                                MinimumPositiveMachineDetectionConfidence = long.MaxValue
                            };
                        }
                        DetectionData data = _detectionCounts[node.OrcasoundSlug];
                        if (detection.Source == "human")
                        {
                            if (detection.Category == "whale")
                            {
                                data.PositiveHumanDetectionCount++;
                            }
                            else
                            {
                                data.NegativeHumanDetectionCount++;
                            }
                        }
                        else
                        {
                            if (detection.Category == "whale")
                            {
                                data.PositiveMachineDetectionCount++;
                                double globalConfidence = 0; // XXX TODO
                                data.CumulativePositiveMachineDetectionConfidence += globalConfidence;
                                if (globalConfidence < data.MinimumPositiveMachineDetectionConfidence)
                                {
                                    data.MinimumPositiveMachineDetectionConfidence = globalConfidence;
                                }
                            }
                            else
                            {
                                data.NegativeMachineDetectionCount++;
                                double globalConfidence = 0; // XXX TODO
                                data.CumulativeNegativeMachineDetectionConfidence = globalConfidence;
                                if (globalConfidence > data.MaximumNegativeMachineDetectionConfidence)
                                {
                                    data.MaximumNegativeMachineDetectionConfidence = globalConfidence;
                                }
                            }
                        }
                    }
                }

                foreach (var node in _nodes)
                {
                    if (!_detectionCounts.ContainsKey(node.OrcasoundSlug))
                    {
                        _detectionCounts[node.OrcasoundSlug] = new DetectionData
                        {
                            CumulativeNegativeMachineDetectionConfidence = 0,
                            CumulativePositiveMachineDetectionConfidence = 0,
                            MinimumPositiveMachineDetectionConfidence = long.MaxValue,
                            MaximumNegativeMachineDetectionConfidence = 0,
                            PositiveHumanDetectionCount = 0,
                            NegativeHumanDetectionCount = 0,
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
            if (data.MachineDetectionCount == 0)
            {
                return "-";
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
            if (data.MachineDetectionCount == 0)
            {
                return "-";
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
