// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Drawing;
using System.Reflection;

namespace OrcanodeMonitor.Pages
{
    public class DetectionSourceData
    {
        /// <summary>
        /// Number of positive detections.
        /// </summary>
        public long PositiveDetectionCount;

        /// <summary>
        /// Number of negative detections.
        /// </summary>
        public long NegativeDetectionCount;

        /// <summary>
        /// Number of unreviewed detections.
        /// </summary>
        public long UnreviewedDetectionCount;

        /// <summary>
        /// Total number of reviewed detections (positive + negative).
        /// </summary>
        public long ReviewedDetectionCount => PositiveDetectionCount + NegativeDetectionCount;

        /// <summary>
        /// Total number of detections (reviewed and unreviewed).
        /// </summary>
        public long TotalDetectionCount => ReviewedDetectionCount + UnreviewedDetectionCount;

        /// <summary>
        /// Minimum global confidence of positive machine detections.
        /// </summary>
        public double MinimumPositiveDetectionConfidence = double.MaxValue;

        /// <summary>
        /// Cumulative global confidence of positive machine detections.
        /// </summary>
        public double CumulativePositiveDetectionConfidence;

        /// <summary>
        /// Average global confidence of positive machine detections.
        /// </summary>
        public long AveragePositiveDetectionConfidence
        {
            get
            {
                if (PositiveDetectionCount == 0)
                {
                    return 0;
                }
                return (long)Math.Round(CumulativePositiveDetectionConfidence * 1.0 / PositiveDetectionCount);
            }
        }

        /// <summary>
        /// Cumulative global confidence of negative machine detections.
        /// </summary>
        public double CumulativeNegativeDetectionConfidence;

        public long AverageNegativeDetectionConfidence
        {
            get
            {
                if (NegativeDetectionCount == 0)
                {
                    return 0;
                }
                return (long)Math.Round(CumulativeNegativeDetectionConfidence * 1.0 / NegativeDetectionCount);
            }
        }

        /// <summary>
        /// Average global confidence of negative machine detections.
        /// </summary>
        public double MaximumNegativeDetectionConfidence;

        public string ConfidenceThreshold = string.Empty;
    }

    public class DetectionData
    {
        public DetectionSourceData[] Source = new DetectionSourceData[(int)DetectionSource.All];

        public DetectionData()
        {
            for (int i = 0; i < Source.Length; i++)
            {
                Source[i] = new DetectionSourceData();
            }
        }
    }

    public class DetectionsModel : PageModel
    {
        private readonly OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<DetectionsModel> _logger;
        private readonly InferenceSystemFetcher _inferenceSystemFetcher;
        private readonly Dictionary<string, DetectionData> _detectionCountsPastWeek = new Dictionary<string, DetectionData>();
        private readonly Dictionary<string, DetectionData> _detectionCountsPastMonth = new Dictionary<string, DetectionData>();
        private List<Orcanode> _nodes;
        public List<Orcanode> Nodes => _nodes;

        public DetectionsModel(OrcanodeMonitorContext context, ILogger<DetectionsModel> logger, InferenceSystemFetcher inferenceSystemFetcher)
        {
            _databaseContext = context;
            _logger = logger;
            _inferenceSystemFetcher = inferenceSystemFetcher;
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

        private void EnsureNodeEntries(Dictionary<string, DetectionData> dictionary, Orcanode node, string confidenceThreshold, DetectionSource source)
        {
            if (!dictionary.ContainsKey(node.OrcasoundSlug))
            {
                dictionary[node.OrcasoundSlug] = new DetectionData();
            }

            var data = dictionary[node.OrcasoundSlug];
            data.Source[(int)source].ConfidenceThreshold = confidenceThreshold;
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

                var oneWeekAgo = DateTime.UtcNow.AddDays(-7);
                var oneMonthAgo = DateTime.UtcNow.AddMonths(-1);

                // Fetch additional detection details (human/machine detections, confidence levels, etc.)
                // Pass oneMonthAgo so that pagination stops once records older than a month are reached.
                List<OrcasiteDetection>? detections = await Fetcher.GetRecentDetectionsAsync(_logger, oneMonthAgo);

                // Fetch machine detection details for the past month to support both time ranges.
                var machineDetections = await _inferenceSystemFetcher.GetRecentDetectionsAsync(timeframe: "1m", hydrophoneId: "all", logger: _logger);

                if (detections != null)
                {
                    foreach (OrcasiteDetection detection in detections)
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
                            InferencePod? inferencePod = await _inferenceSystemFetcher.GetInferencePodByNameAsync(node, InferenceSystemFetcher.OrcaHelloInferenceContainerName, _logger);
                            EnsureNodeEntries(_detectionCountsPastMonth, node, inferencePod?.GetConfidenceThreshold() ?? "Unknown", DetectionSource.OrcaHello);
                            EnsureNodeEntries(_detectionCountsPastWeek, node, inferencePod?.GetConfidenceThreshold() ?? "Unknown", DetectionSource.OrcaHello);

                            inferencePod = await _inferenceSystemFetcher.GetInferencePodByNameAsync(node, InferenceSystemFetcher.PodsAIInferenceContainerName, _logger);
                            EnsureNodeEntries(_detectionCountsPastMonth, node, inferencePod?.GetConfidenceThreshold() ?? "Unknown", DetectionSource.PodsAI);
                            EnsureNodeEntries(_detectionCountsPastWeek, node, inferencePod?.GetConfidenceThreshold() ?? "Unknown", DetectionSource.PodsAI);
                        }

                        DetectionData monthData = _detectionCountsPastMonth[node.OrcasoundSlug];
                        DetectionData weekData = _detectionCountsPastWeek[node.OrcasoundSlug];
                        DetectionSourceData sourceMonthData = monthData.Source[(int)detection.Source];
                        DetectionSourceData sourceWeekData = weekData.Source[(int)detection.Source];

                        if (detection.Source == DetectionSource.Human)
                        {
                            if (!detection.Reviewed)
                            {
                                sourceMonthData.UnreviewedDetectionCount++;
                                if (inPastWeek)
                                {
                                    sourceWeekData.UnreviewedDetectionCount++;
                                }
                                continue;
                            }
                            if (detection.GeneralCategory == DetectionGeneralCategoryEnum.Whale)
                            {
                                sourceMonthData.PositiveDetectionCount++;
                                if (inPastWeek) sourceWeekData.PositiveDetectionCount++;
                            }
                            else
                            {
                                sourceMonthData.NegativeDetectionCount++;
                                if (inPastWeek) sourceWeekData.NegativeDetectionCount++;
                            }
                        }
                        else // Machine detections.
                        {
                            // Find the matching InferenceSystemDetection.
                            MachineDetection? inferenceSystemDetection = machineDetections.Where(d => d.Id == detection.IdempotencyKey).FirstOrDefault();
                            if (inferenceSystemDetection == null)
                            {
                                _logger.LogError($"Failed to find matching inferenceSystemDetection for {detection.ID}");
                                continue;
                            }

                            // Count unreviewed detections separately; only reviewed detections affect the confirmed/total stats.
                            if (!inferenceSystemDetection.Reviewed)
                            {
                                sourceMonthData.UnreviewedDetectionCount++;
                                if (inPastWeek)
                                {
                                    sourceWeekData.UnreviewedDetectionCount++;
                                }
                                continue;
                            }

                            double globalConfidence = inferenceSystemDetection.Confidence;

                            if (inferenceSystemDetection.IsPositive(detection))
                            {
                                sourceMonthData.PositiveDetectionCount++;
                                sourceMonthData.CumulativePositiveDetectionConfidence += globalConfidence;
                                if (globalConfidence < sourceMonthData.MinimumPositiveDetectionConfidence)
                                {
                                    sourceMonthData.MinimumPositiveDetectionConfidence = globalConfidence;
                                }

                                if (inPastWeek)
                                {
                                    sourceWeekData.PositiveDetectionCount++;
                                    sourceWeekData.CumulativePositiveDetectionConfidence += globalConfidence;
                                    if (globalConfidence < sourceWeekData.MinimumPositiveDetectionConfidence)
                                    {
                                        sourceWeekData.MinimumPositiveDetectionConfidence = globalConfidence;
                                    }
                                }
                            }
                            else
                            {
                                sourceMonthData.NegativeDetectionCount++;
                                sourceMonthData.CumulativeNegativeDetectionConfidence += globalConfidence;
                                if (globalConfidence > sourceMonthData.MaximumNegativeDetectionConfidence)
                                {
                                    sourceMonthData.MaximumNegativeDetectionConfidence = globalConfidence;
                                }

                                if (inPastWeek)
                                {
                                    sourceWeekData.NegativeDetectionCount++;
                                    sourceWeekData.CumulativeNegativeDetectionConfidence += globalConfidence;
                                    if (globalConfidence > sourceWeekData.MaximumNegativeDetectionConfidence)
                                    {
                                        sourceWeekData.MaximumNegativeDetectionConfidence = globalConfidence;
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
                        InferencePod? pod = await _inferenceSystemFetcher.GetInferencePodByNameAsync(node, InferenceSystemFetcher.OrcaHelloInferenceContainerName, _logger);
                        EnsureNodeEntries(_detectionCountsPastMonth, node, pod?.GetConfidenceThreshold() ?? "Unknown", DetectionSource.OrcaHello);
                        EnsureNodeEntries(_detectionCountsPastWeek, node, pod?.GetConfidenceThreshold() ?? "Unknown", DetectionSource.OrcaHello);

                        pod = await _inferenceSystemFetcher.GetInferencePodByNameAsync(node, InferenceSystemFetcher.PodsAIInferenceContainerName, _logger);
                        EnsureNodeEntries(_detectionCountsPastMonth, node, pod?.GetConfidenceThreshold() ?? "Unknown", DetectionSource.PodsAI);
                        EnsureNodeEntries(_detectionCountsPastWeek, node, pod?.GetConfidenceThreshold() ?? "Unknown", DetectionSource.PodsAI);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Exception in OnGetAsync: {ex.Message}");
            }
        }

        public string GetDetectionCount(Orcanode node, string timeRange, DetectionSource source)
        {
            if (!GetDict(timeRange).TryGetValue(node.OrcasoundSlug, out DetectionData? data))
            {
                return "Unknown";
            }
            DetectionSourceData sourceData = data.Source[(int)source];

            // If there are no detections for the specified source at all, report "None".
            if (sourceData.TotalDetectionCount == 0)
            {
                return "None";
            }

            // If there are detections but none have been reviewed yet, show the total
            // without attempting to compute a percentage on a zero denominator.
            if (sourceData.ReviewedDetectionCount == 0)
            {
                return $"0 / 0 of {sourceData.TotalDetectionCount}";
            }

            string result = $"{sourceData.PositiveDetectionCount} / {sourceData.ReviewedDetectionCount} ({(sourceData.PositiveDetectionCount / (double)sourceData.ReviewedDetectionCount):P0})";
            if (sourceData.UnreviewedDetectionCount > 0)
            {
                result += $" of {sourceData.TotalDetectionCount}";
            }
            return result;
        }

        public string GetDetectionBackgroundColor(Orcanode node, string timeRange, DetectionSource source)
        {
            if (!GetDict(timeRange).TryGetValue(node.OrcasoundSlug, out DetectionData? data))
            {
                return ColorTranslator.ToHtml(Color.White);
            }
            DetectionSourceData sourceData = data.Source[(int)source];

            if (sourceData.ReviewedDetectionCount == 0)
            {
                return ColorTranslator.ToHtml(Color.White);
            }
            double percentage = sourceData.PositiveDetectionCount / (double)sourceData.ReviewedDetectionCount;
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
        public string GetAveragePositiveConfidence(Orcanode node, string timeRange, DetectionSource source)
        {
            if (!GetDict(timeRange).TryGetValue(node.OrcasoundSlug, out DetectionData? data))
            {
                return "Unknown";
            }
            DetectionSourceData sourceData = data.Source[(int)source];

            if (sourceData.PositiveDetectionCount == 0)
            {
                return "-";
            }
            return $"{sourceData.MinimumPositiveDetectionConfidence:F2}% min, {sourceData.AveragePositiveDetectionConfidence:F2}% avg";
        }

        /// <summary>
        /// Get the average global confidence for negative machine detections.
        /// </summary>
        /// <param name="node"></param>
        /// <param name="timeRange">Time range: "pastWeek" or "pastMonth"</param>
        /// <returns>Threshold percentage</returns>
        public string GetAverageNegativeConfidence(Orcanode node, string timeRange, DetectionSource source)
        {
            if (!GetDict(timeRange).TryGetValue(node.OrcasoundSlug, out DetectionData? data))
            {
                return "Unknown";
            }
            DetectionSourceData sourceData = data.Source[(int)source];

            if (sourceData.NegativeDetectionCount == 0)
            {
                return "-";
            }
            return $"{sourceData.AverageNegativeDetectionConfidence:F2}% avg, {sourceData.MaximumNegativeDetectionConfidence:F2}% max";
        }

        /// <summary>
        /// Get the machine confidence threshold display string for a node.
        /// Format: "{globalThreshold} @ {localThreshold}%" (e.g., "3 @ 70%")
        /// </summary>
        /// <param name="node">Node to check</param>
        /// <returns>Confidence threshold string</returns>
        public string GetConfiguredConfidenceThreshold(Orcanode node, DetectionSource source)
        {
            if (!_detectionCountsPastMonth.TryGetValue(node.OrcasoundSlug, out DetectionData? data))
            {
                return "Unknown";
            }
            DetectionSourceData sourceData = data.Source[(int)source];

            return sourceData.ConfidenceThreshold;
        }
    }
}
