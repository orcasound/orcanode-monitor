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
        private readonly Dictionary<string, DetectionData> _detectionCountsPastWeek = new Dictionary<string, DetectionData>();
        private readonly Dictionary<string, DetectionData> _detectionCountsPastMonth = new Dictionary<string, DetectionData>();
        private List<Orcanode> _nodes;
        public List<Orcanode> Nodes => _nodes;

        public DetectionsModel(OrcanodeMonitorContext context, ILogger<DetectionsModel> logger, OrcaHelloFetcher orcaHelloFetcher)
        {
            _databaseContext = context;
            _logger = logger;
            _orcaHelloFetcher = orcaHelloFetcher;
            _nodes = new List<Orcanode>();
        }

        public string LastChecked
        {
            get
            {
                try
                {
                    MonitorState monitorState = MonitorState.GetFrom(_databaseContext);

                    if (monitorState.LastUpdatedTimestampUtc == null)
                    {
                        return "";
                    }
                    return Fetcher.UtcToLocalDateTime(monitorState.LastUpdatedTimestampUtc)?.ToString() ?? "";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Exception in LastChecked getter: {ex.Message}");
                    return "";
                }
            }
        }

        private Dictionary<string, DetectionData> GetDict(string timeRange) =>
            timeRange == "pastWeek" ? _detectionCountsPastWeek : _detectionCountsPastMonth;

        private void EnsureNodeEntries(Orcanode node, string confidenceThreshold)
        {
            if (!_detectionCountsPastMonth.ContainsKey(node.OrcasoundSlug))
            {
                _detectionCountsPastMonth[node.OrcasoundSlug] = new DetectionData
                {
                    MinimumPositiveMachineDetectionConfidence = long.MaxValue,
                    ConfidenceThreshold = confidenceThreshold
                };
            }
            if (!_detectionCountsPastWeek.ContainsKey(node.OrcasoundSlug))
            {
                _detectionCountsPastWeek[node.OrcasoundSlug] = new DetectionData
                {
                    MinimumPositiveMachineDetectionConfidence = long.MaxValue,
                    ConfidenceThreshold = confidenceThreshold
                };
            }
        }

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

                // Fetch additional detection details (human/machine detections, confidence levels, etc.)
                List<Detection>? detections = await Fetcher.GetRecentDetectionsAsync(_logger);

                // Fetch OrcaHello detection details for the past month to support both time ranges.
                var orcaHelloDetections = await _orcaHelloFetcher.GetRecentDetectionsAsync(_logger, "1m");

                var oneWeekAgo = DateTime.UtcNow.AddDays(-7);
                var oneMonthAgo = DateTime.UtcNow.AddMonths(-1);

                if (detections != null)
                {
                    foreach (Detection detection in detections)
                    {
                        Orcanode? node = _nodes.Where(n => n.OrcasoundFeedId == detection.NodeID).FirstOrDefault();
                        if (node == null)
                        {
                            continue;
                        }

                        bool inPastMonth = detection.Timestamp >= oneMonthAgo;
                        bool inPastWeek = detection.Timestamp >= oneWeekAgo;

                        if (!inPastMonth)
                        {
                            continue;
                        }

                        if (!_detectionCountsPastMonth.ContainsKey(node.OrcasoundSlug))
                        {
                            OrcaHelloPod? pod = await _orcaHelloFetcher.GetOrcaHelloPodAsync(node, _logger);
                            EnsureNodeEntries(node, pod?.GetConfidenceThreshold() ?? "Unknown");
                        }

                        DetectionData monthData = _detectionCountsPastMonth[node.OrcasoundSlug];
                        DetectionData weekData = _detectionCountsPastWeek[node.OrcasoundSlug];

                        if (detection.Source == "human")
                        {
                            // TODO: only count reviewed detections.
                            if (detection.Category == "whale")
                            {
                                monthData.PositiveHumanDetectionCount++;
                                if (inPastWeek) weekData.PositiveHumanDetectionCount++;
                            }
                            else
                            {
                                monthData.NegativeHumanDetectionCount++;
                                if (inPastWeek) weekData.NegativeHumanDetectionCount++;
                            }
                        }
                        else // detection.Source == "machine"
                        {
                            // Find the matching OrcaHelloDetection.
                            OrcaHelloDetection? orcaHelloDetection = orcaHelloDetections.Where(d => d.Id == detection.IdempotencyKey).FirstOrDefault();
                            if (orcaHelloDetection == null)
                            {
                                _logger.LogError($"Failed to find matching orcaHelloDetection for {detection.ID}");
                                continue;
                            }

                            // Only count reviewed detections.
                            if (!orcaHelloDetection.Reviewed)
                            {
                                continue;
                            }

                            double globalConfidence = orcaHelloDetection.Confidence;

                            if (orcaHelloDetection.IsPositive(detection))
                            {
                                monthData.PositiveMachineDetectionCount++;
                                monthData.CumulativePositiveMachineDetectionConfidence += globalConfidence;
                                if (globalConfidence < monthData.MinimumPositiveMachineDetectionConfidence)
                                {
                                    monthData.MinimumPositiveMachineDetectionConfidence = globalConfidence;
                                }

                                if (inPastWeek)
                                {
                                    weekData.PositiveMachineDetectionCount++;
                                    weekData.CumulativePositiveMachineDetectionConfidence += globalConfidence;
                                    if (globalConfidence < weekData.MinimumPositiveMachineDetectionConfidence)
                                    {
                                        weekData.MinimumPositiveMachineDetectionConfidence = globalConfidence;
                                    }
                                }
                            }
                            else
                            {
                                monthData.NegativeMachineDetectionCount++;
                                monthData.CumulativeNegativeMachineDetectionConfidence += globalConfidence;
                                if (globalConfidence > monthData.MaximumNegativeMachineDetectionConfidence)
                                {
                                    monthData.MaximumNegativeMachineDetectionConfidence = globalConfidence;
                                }

                                if (inPastWeek)
                                {
                                    weekData.NegativeMachineDetectionCount++;
                                    weekData.CumulativeNegativeMachineDetectionConfidence += globalConfidence;
                                    if (globalConfidence > weekData.MaximumNegativeMachineDetectionConfidence)
                                    {
                                        weekData.MaximumNegativeMachineDetectionConfidence = globalConfidence;
                                    }
                                }
                            }
                        }
                    }
                }

                foreach (var node in _nodes)
                {
                    if (!_detectionCountsPastMonth.ContainsKey(node.OrcasoundSlug))
                    {
                        OrcaHelloPod? pod = await _orcaHelloFetcher.GetOrcaHelloPodAsync(node, _logger);
                        EnsureNodeEntries(node, pod?.GetConfidenceThreshold() ?? "Unknown");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Exception in OnGetAsync: {ex.Message}");
            }
        }

        public string GetHumanDetectionCount(Orcanode node, string timeRange)
        {
            if (!GetDict(timeRange).TryGetValue(node.OrcasoundSlug, out DetectionData? data))
            {
                return "Unknown";
            }
            if (data.HumanDetectionCount == 0)
            {
                return "None";
            }
            return $"{data.PositiveHumanDetectionCount} / {data.HumanDetectionCount} ({(data.PositiveHumanDetectionCount / (double)data.HumanDetectionCount):P0})";
        }

        public string GetMachineDetectionCount(Orcanode node, string timeRange)
        {
            if (!GetDict(timeRange).TryGetValue(node.OrcasoundSlug, out DetectionData? data))
            {
                return "Unknown";
            }
            if (data.MachineDetectionCount == 0)
            {
                return "None";
            }
            return $"{data.PositiveMachineDetectionCount} / {data.MachineDetectionCount} ({(data.PositiveMachineDetectionCount / (double)data.MachineDetectionCount):P0})";
        }

        public string GetMachineDetectionBackgroundColor(Orcanode node, string timeRange)
        {
            if (!GetDict(timeRange).TryGetValue(node.OrcasoundSlug, out DetectionData? data))
            {
                return ColorTranslator.ToHtml(Color.White);
            }
            if (data.MachineDetectionCount == 0)
            {
                return ColorTranslator.ToHtml(Color.White);
            }
            double percentage = data.PositiveMachineDetectionCount / (double)data.MachineDetectionCount;
            if (percentage > 0.5)
            {
                return ColorTranslator.ToHtml(Color.LightGreen);
            }
            return ColorTranslator.ToHtml(Color.Yellow);
        }

        /// <summary>
        /// Get the average global confidence for positive machine detections.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="timeRange">Time range: "pastWeek" or "pastMonth"</param>
        /// <returns>Threshold percentage</returns>
        public string GetAveragePositiveMachineConfidence(Orcanode node, string timeRange)
        {
            if (!GetDict(timeRange).TryGetValue(node.OrcasoundSlug, out DetectionData? data))
            {
                return "Unknown";
            }
            if (data.PositiveMachineDetectionCount == 0)
            {
                return "-";
            }
            return $"{data.MinimumPositiveMachineDetectionConfidence:F2}% min, {data.AveragePositiveMachineDetectionConfidence:F2}% avg";
        }

        /// <summary>
        /// Get the average global confidence for negative machine detections.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="timeRange">Time range: "pastWeek" or "pastMonth"</param>
        /// <returns>Threshold percentage</returns>
        public string GetAverageNegativeMachineConfidence(Orcanode node, string timeRange)
        {
            if (!GetDict(timeRange).TryGetValue(node.OrcasoundSlug, out DetectionData? data))
            {
                return "Unknown";
            }
            if (data.NegativeMachineDetectionCount == 0)
            {
                return "-";
            }
            return $"{data.AverageNegativeMachineDetectionConfidence:F2}% avg, {data.MaximumNegativeMachineDetectionConfidence:F2}% max";
        }

        /// <summary>
        /// Get the confidence threshold display string for a node.
        /// Format: "{globalThreshold} @ {localThreshold}%" (e.g., "3 @ 70%")
        /// </summary>
        /// <param name="node">Node to check</param>
        /// <returns>Confidence threshold string</returns>
        public string GetConfiguredConfidenceThreshold(Orcanode node)
        {
            if (!_detectionCountsPastMonth.TryGetValue(node.OrcasoundSlug, out DetectionData? data))
            {
                return "Unknown";
            }
            return data.ConfidenceThreshold;
        }
    }
}
