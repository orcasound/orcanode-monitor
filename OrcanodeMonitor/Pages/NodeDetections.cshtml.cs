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
        public static string GetSourceClass(OrcasiteDetection item) => item.Source.ToString().ToLowerInvariant();

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
        /// Get detection CSS classes based on source, category, and time range.
        /// </summary>
        /// <param name="item">Detection</param>
        /// <returns>String containing CSS classes</returns>
        public string GetDetectionClasses(OrcasiteDetection item)
        {
            string classes = "source-" + GetSourceClass(item) + " " +
                             "category-" + GetGeneralCategoryClass(item) + " " +
                             "label-" + GetSpecificCategoryClass(item) + " " +
                             "timeRange-" + GetTimeRangeClass(item);
            return classes;
        }

        public string GetTags(OrcasiteDetection item)
        {
            MachineDetection? machineDetection = _machineDetections.FirstOrDefault(d => d.Id == item.IdempotencyKey);
            if (machineDetection == null)
            {
                return string.Empty;
            }

            return machineDetection.Tags ?? string.Empty;
        }

        public DetectionSpecificCategoryEnum GetSpecificCategory(OrcasiteDetection orcasiteDetection)
        {
            if (orcasiteDetection.GeneralCategory == DetectionGeneralCategoryEnum.Vessel)
            {
                return DetectionSpecificCategoryEnum.Vessel;
            }
            if (orcasiteDetection.GeneralCategory == DetectionGeneralCategoryEnum.Human)
            {
                return DetectionSpecificCategoryEnum.Human;
            }

            MachineDetection? machineDetection = _machineDetections.FirstOrDefault(d => d.Id == orcasiteDetection.IdempotencyKey);
            if (machineDetection == null)
            {
                return DetectionSpecificCategoryEnum.Unknown;
            }

            if (orcasiteDetection.Source == DetectionSource.PodsAI)
            {
                // Convert it to the corresponding DetectionSpecificCategoryEnum value.
                if (Enum.TryParse<DetectionSpecificCategoryEnum>(machineDetection?.GlobalPredictionLabel, true, out var specificCategory))
                {
                    return specificCategory;
                }

                return DetectionSpecificCategoryEnum.Unknown;
            }

            if (orcasiteDetection.Source == DetectionSource.OrcaHello)
            {
                if (machineDetection.IsPositive(orcasiteDetection))
                {
                    return DetectionSpecificCategoryEnum.Resident;
                }
                return DetectionSpecificCategoryEnum.Unknown;
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
            if (orcasiteDetection.Source == DetectionSource.OrcaHello ||
                orcasiteDetection.Source == DetectionSource.PodsAI)
            {
                MachineDetection? machineDetection = _machineDetections.FirstOrDefault(d => d.Id == orcasiteDetection.IdempotencyKey);
                if (machineDetection == null)
                {
                    return "Unknown";
                }
                else if (!machineDetection.Reviewed)
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
        }
    }
}
