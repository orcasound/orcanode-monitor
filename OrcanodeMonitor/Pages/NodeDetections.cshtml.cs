// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;

namespace OrcanodeMonitor.Pages
{
    public class NodeDetectionsModel : PageModel
    {
        private readonly OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<NodeDetectionsModel> _logger;
        private Orcanode? _node = null;
        public string NodeName => _node?.DisplayName ?? "Unknown";
        public string Id => _node?.ID ?? string.Empty;
        public string OrcasoundSlug => _node?.OrcasoundSlug ?? string.Empty;
        private List<OrcasiteDetection> _orcasiteDetections;
        public List<OrcasiteDetection> RecentDetections => _orcasiteDetections;
        private readonly InferenceSystemFetcher _inferenceSystemFetcher;
        private List<MachineDetection> _machineDetections;
        public Dictionary<string, ModelComparisonResult> Results = new();
        public SortedDictionary<string, ConfusionMatrix> ConfusionMatrices = new();

        public NodeDetectionsModel(OrcanodeMonitorContext context, ILogger<NodeDetectionsModel> logger, InferenceSystemFetcher inferenceSystemFetcher)
        {
            _databaseContext = context;
            _logger = logger;
            _inferenceSystemFetcher = inferenceSystemFetcher;
            _orcasiteDetections = new List<OrcasiteDetection>();
            _machineDetections = new List<MachineDetection>();
        }

        /// <summary>
        /// Get source CSS class for a detection.
        /// </summary>
        /// <param name="item">Detection</param>
        /// <returns>CSS class</returns>
        public string GetSourceClass(OrcasiteDetection item) => item.DetectionSourceString;

        /// <summary>
        /// Get general (i.e., Orcasite) category CSS class for a detection.
        /// </summary>
        /// <param name="item">Detection</param>
        /// <returns>CSS class</returns>
        public static string GetGeneralCategoryClass(OrcasiteDetection item) => item.GeneralCategory.ToString().ToLowerInvariant();

        /// <summary>
        /// Get specific (i.e., PODS-AI) category CSS class for a detection.
        /// </summary>
        /// <param name="item">Detection</param>
        /// <returns>CSS class</returns>
        public string GetSpecificCategoryClass(OrcasiteDetection item) => GetSpecificCategory(item).ToString().ToLowerInvariant();

        /// <summary>
        /// Get time range CSS classes for a detection.
        /// </summary>
        /// <param name="item">Detection</param>
        /// <returns>String containing CSS classes</returns>
        public static string GetTimeRangeClass(OrcasiteDetection item)
        {
            DateTime oneWeekAgo = DateTime.UtcNow.AddDays(-7);
            if (item.Timestamp.ToUniversalTime() > oneWeekAgo)
            {
                return "pastWeek pastMonth";
            }

            DateTime oneMonthAgo = DateTime.UtcNow.AddMonths(-1);
            if (item.Timestamp.ToUniversalTime() > oneMonthAgo)
            {
                return "pastMonth";
            }

            return string.Empty;
        }

        /// <summary>
        /// Get detection CSS classes based on source, category, label, tags, and time range.
        /// </summary>
        /// <param name="item">Detection</param>
        /// <returns>String containing CSS classes</returns>
        public string GetDetectionClasses(OrcasiteDetection item)
        {
            string classes = "source-" + item.DetectionSourceString + " " +
                             "category-" + GetGeneralCategoryClass(item) + " " +
                             "label-" + GetSpecificCategoryClass(item) + " " +
                             "timeRange-" + GetTimeRangeClass(item);
            foreach (var tag in item.TagList)
            {
                if (!string.IsNullOrWhiteSpace(tag))
                {
                    classes += " tag-" + tag.Trim().ToLowerInvariant();
                }
            }
            return classes;
        }

        public static string GetTags(OrcasiteDetection item)
        {
            return item.Tags;
        }

        /// <summary>
        /// Gets the specific detection category from an orcasite detection using general category, machine prediction
        /// label, and source-specific positive detection rules.
        /// </summary>
        /// <remarks>General categories for Vessel and Human take precedence. If available,
        /// <c>GlobalPredictionLabel</c> is parsed to <see cref="DetectionSpecificCategoryEnum"/>. For OrcaHello
        /// detections without a prediction label, a positive machine detection maps to <see
        /// cref="DetectionSpecificCategoryEnum.Resident"/>.</remarks>
        /// <param name="orcasiteDetection">Detection data used to determine the specific category.</param>
        /// <returns>A <see cref="DetectionSpecificCategoryEnum"/> value matching the detection; returns <see
        /// cref="DetectionSpecificCategoryEnum.Unknown"/> when no specific category can be determined.</returns>
        public static DetectionSpecificCategoryEnum GetSpecificCategory(OrcasiteDetection orcasiteDetection)
        {
            if (orcasiteDetection.GeneralCategory == DetectionGeneralCategoryEnum.Vessel)
            {
                return DetectionSpecificCategoryEnum.Vessel;
            }
            if (orcasiteDetection.GeneralCategory == DetectionGeneralCategoryEnum.Human)
            {
                return DetectionSpecificCategoryEnum.Human;
            }

            MachineDetection? machineDetection = orcasiteDetection.MachineDetection;
            if (machineDetection == null)
            {
                return DetectionSpecificCategoryEnum.Unknown;
            }

            if (!string.IsNullOrEmpty(machineDetection?.GlobalPredictionLabel))
            {
                // Convert it to the corresponding DetectionSpecificCategoryEnum value.
                if (Enum.TryParse<DetectionSpecificCategoryEnum>(machineDetection?.GlobalPredictionLabel, true, out var specificCategory))
                {
                    return specificCategory;
                }
                return DetectionSpecificCategoryEnum.Unknown;
            }

            if ((orcasiteDetection.DefaultSpecificCategory.HasValue) &&
                (machineDetection?.IsPositive(orcasiteDetection) ?? false))
            {
                return orcasiteDetection.DefaultSpecificCategory.Value;
            }

            return DetectionSpecificCategoryEnum.Unknown;
        }

        /// <summary>
        /// Get the human-readable status of a detection based on its source, review state, and classification.
        /// </summary>
        /// <param name="orcasiteDetection">The Orcasite detection to evaluate</param>
        /// <returns>
        /// For machine detections: "Unknown", "Unreviewed", "SRKW", or "Not SRKW".
        /// For human detections: "Not whale", "Unreviewed", or "Whale".
        /// </returns>
        public string GetDetectionStatus(OrcasiteDetection orcasiteDetection)
        {
            MachineDetection? machineDetection = orcasiteDetection.MachineDetection;
            if (machineDetection != null)
            {
                if (!machineDetection.Reviewed)
                {
                    return "Unreviewed";
                }
                else if (machineDetection.IsPositive(orcasiteDetection))
                {
                    return "SRKW";
                }
                else
                {
                    return "Not SRKW";
                }
            }

            if (!orcasiteDetection.Reviewed)
            {
                return "Unreviewed";
            }
            else if (orcasiteDetection.GeneralCategory != DetectionGeneralCategoryEnum.Whale)
            {
                return "Not whale";
            }
            else
            {
                return "Whale";
            }
        }

        public async Task OnGetAsync(string slug)
        {
            _node = await _databaseContext.Orcanodes.Where(n => n.OrcasoundSlug == slug).FirstOrDefaultAsync();
            if (_node == null)
            {
                _logger.LogWarning("No orcanode found for slug {Slug}", slug);
                Response.StatusCode = 404;
                return;
            }

            List<OrcasiteDetection>? orcasiteDetections = await Fetcher.GetRecentDetectionsForNodeAsync(_node.OrcasoundFeedId, _logger, DateTime.UtcNow.AddMonths(-1));
            if (orcasiteDetections != null)
            {
                _orcasiteDetections = orcasiteDetections;
            }

            List<MachineDetection> machineDetections = await _inferenceSystemFetcher.GetRecentDetectionsAsync(timeframe: "1m", hydrophoneId: _node.S3NodeName, logger: _logger);
            if (machineDetections != null)
            {
                _machineDetections = machineDetections;
            }

            if (_orcasiteDetections != null)
            {
                if (_machineDetections != null)
                {
                    foreach (OrcasiteDetection detection in _orcasiteDetections)
                    {
                        detection.MachineDetection = _machineDetections.Where(d => d.Id == detection.IdempotencyKey).FirstOrDefault();
                    }
                }

                bool hasMachineSourceDetections = _orcasiteDetections.Any(d => string.Equals(d.Source, "machine", StringComparison.OrdinalIgnoreCase));
                bool isMachineDataUnavailable = hasMachineSourceDetections && _machineDetections.Count == 0;
                if (isMachineDataUnavailable)
                {
                    string sanitizedSlug = (slug ?? string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
                    _logger.LogWarning("Skipping node leaderboard metrics because machine detections were unavailable for node {NodeSlug}", sanitizedSlug);
                    return;
                }

                List<Orcanode> nodes = await _databaseContext.Orcanodes.ToListAsync();

                // Compute confusion matrices.
                ModelsModel.ComputeConfusionMatrices(nodes, _orcasiteDetections, ConfusionMatrices);

                // Compute model comparison results for the node.
                ModelsModel.ComputeModelComparisonResults(ConfusionMatrices, Results);
            }
        }
    }
}
