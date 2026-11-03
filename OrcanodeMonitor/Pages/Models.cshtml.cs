// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
namespace OrcanodeMonitor.Pages
{
    public class ModelsModel : PageModel
    {
        private readonly ILogger<ModelsModel> _logger;
        private readonly InferenceSystemFetcher _inferenceSystemFetcher;
        private List<Orcanode> _nodes = new();
        private readonly OrcanodeMonitorContext _databaseContext;
        public Dictionary<string, ModelComparisonResult> Results = new();
        public SortedDictionary<string, ConfusionMatrix> ConfusionMatrices = new();

        /// <summary>
        /// Current timestamp, in local time.
        /// </summary>
        public string NowLocal { get; private set; }

        public ModelsModel(OrcanodeMonitorContext context, ILogger<ModelsModel> logger, InferenceSystemFetcher inferenceSystemFetcher)
        {
            _databaseContext = context;
            _logger = logger;
            _inferenceSystemFetcher = inferenceSystemFetcher;
            NowLocal = Fetcher.UtcToLocalDateTime(DateTime.UtcNow)?.ToString() ?? "Unknown";
        }

        /// <summary>
        /// Get the asp-page target for a model's overview page, or null if there isn't one.
        /// </summary>
        public string? GetOverviewPage(string modelName) => modelName switch
        {
            "orcahello" => "/OrcaHelloOverview",
            "podsai" => "/PodsAIOverview",
            _ => null
        };

        /// <summary>
        /// Get the display name for a model.
        /// TODO: combine with NodeDetectionsModel.GetSourceLabel() to avoid duplication.
        /// </summary>
        public string GetDisplayName(string modelName) => modelName switch
        {
            "orcahello" => "OrcaHello",
            "podsai" => "PODS-AI",
            _ => modelName
        };   

        public async Task OnGetAsync(string slug)
        {
            // TODO: share some code with Detections.cshtml.cs.

            // Fetch nodes.
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
            if (detections != null)
            {
                // Fetch machine detection details for the past month to support both time ranges.
                var machineDetections = await _inferenceSystemFetcher.GetRecentDetectionsAsync(timeframe: "1m", hydrophoneId: "all", logger: _logger);

                foreach (OrcasiteDetection detection in detections)
                {
                    Orcanode? node = _nodes.Where(n => n.OrcasoundFeedId == detection.NodeID).FirstOrDefault();
                    if (node == null)
                    {
                        continue;
                    }

                    bool inPastMonth = detection.Timestamp >= oneMonthAgo;
                    if (!inPastMonth)
                    {
                        continue;
                    }
                    bool inPastWeek = detection.Timestamp >= oneWeekAgo;

                    detection.MachineDetection = machineDetections.Where(d => d.Id == detection.IdempotencyKey).FirstOrDefault();

                    string source = detection.DetectionSourceString;
                    if (!Results.ContainsKey(source))
                    {
                        var result = new ModelComparisonResult
                        {
                            Name = source
                        };
                        Results[source] = result;
                    }

                    if (!ConfusionMatrices.ContainsKey(source))
                    {
                        ConfusionMatrices[source] = new ConfusionMatrix
                        {
                            ModelName = source
                        };
                    }
                    ConfusionMatrix matrix = ConfusionMatrices[source];

                    // Update ConfusionMatrix entry.
                    string predictedCategory = detection.PredictedCategory;
                    if (!matrix.PredictedClasses.Contains(predictedCategory))
                    {
                        matrix.PredictedClasses.Add(predictedCategory);
                        // TODO: can PredictedClasses be computed from Rows?
                    }
                    string actualCategory = detection.ActualCategory;

                    // TODO: move the following code block to a helper in ConfusionMatrix.
                    if (!matrix.Rows.ContainsKey(actualCategory))
                    {
                        matrix.Rows[actualCategory] = new ConfusionMatrixRow()
                        {
                            ActualClass = actualCategory
                        };
                    }
                    ConfusionMatrixRow row = matrix.Rows[actualCategory];
                    if (!row.PredictedCounts.ContainsKey(predictedCategory))
                    {
                        row.PredictedCounts[predictedCategory] = 0;
                    }
                    row.PredictedCounts[predictedCategory]++;
                }

                // Compute ModelComparisonResult entries from ConfusionMatrix entries.
                foreach (ConfusionMatrix matrix in ConfusionMatrices.Values)
                {
                    if (matrix.Rows == null)
                    {
                        continue;
                    }
                    if (!Results.ContainsKey(matrix.ModelName))
                    {
                        Results[matrix.ModelName] = new ModelComparisonResult
                        {
                            Name = matrix.ModelName
                        };
                    }
                    ModelComparisonResult result = Results[matrix.ModelName];
                    foreach (var row in matrix.Rows.Values)
                    {
                        result.Evaluated += row.Total;
                        if (row.ActualClass == row.PredictedCounts.Keys.FirstOrDefault())
                        {
                            result.Correct += row.PredictedCounts[row.ActualClass];
                        }

                        if (row.ActualClass == "resident")
                        {
                            long falsePositives = row.PredictedCounts.Where(kvp => kvp.Key != "resident").Sum(kvp => kvp.Value);
                            long falseNegatives = matrix.Rows.Where(r => r.Key != "resident").Sum(r => r.Value.PredictedCounts.ContainsKey("resident") ? r.Value.PredictedCounts["resident"] : 0);
                            result.ResidentFalsePositiveRate = (double)falsePositives / matrix.Total;
                            result.ResidentFalseNegativeRate = (double)falseNegatives / matrix.Total;
                        }

                        if (row.ActualClass == "transient")
                        {
                            long falsePositives = row.PredictedCounts.Where(kvp => kvp.Key != "transient").Sum(kvp => kvp.Value);
                            long falseNegatives = matrix.Rows.Where(r => r.Key != "transient").Sum(r => r.Value.PredictedCounts.ContainsKey("transient") ? r.Value.PredictedCounts["transient"] : 0);
                            result.TransientFalsePositiveRate = (double)falsePositives / matrix.Total;
                            result.TransientFalseNegativeRate = (double)falseNegatives / matrix.Total;
                        }

                        if (row.ActualClass == "humpback")
                        {
                            long falsePositives = row.PredictedCounts.Where(kvp => kvp.Key != "humpback").Sum(kvp => kvp.Value);
                            long falseNegatives = matrix.Rows.Where(r => r.Key != "humpback").Sum(r => r.Value.PredictedCounts.ContainsKey("humpback") ? r.Value.PredictedCounts["humpback"] : 0);
                            result.HumpbackFalsePositiveRate = (double)falsePositives / matrix.Total;
                            result.HumpbackFalseNegativeRate = (double)falseNegatives / matrix.Total;
                        }
                    }
                    result.F1 = matrix.GetWhaleF1();
                }
            }
        }
    }
}
