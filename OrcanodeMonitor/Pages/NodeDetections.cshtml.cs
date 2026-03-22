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
        private readonly OrcaHelloFetcher _orcaHelloFetcher;
        private List<OrcaHelloDetection> _orcaHelloDetections;

        public NodeDetectionsModel(OrcanodeMonitorContext context, ILogger<NodeDetectionsModel> logger, OrcaHelloFetcher orcaHelloFetcher)
        {
            _databaseContext = context;
            _logger = logger;
            _orcaHelloFetcher = orcaHelloFetcher;
            _orcasiteDetections = new List<OrcasiteDetection>();
            _orcaHelloDetections = new List<OrcaHelloDetection>();
        }

        /// <summary>
        /// Get source CSS class for a detection.
        /// </summary>
        /// <param name="item">Detection</param>
        /// <returns>CSS class</returns>
        public static string GetSourceClass(OrcasiteDetection item) => item.Source switch
        {
            DetectionSource.Machine => "machine",
            DetectionSource.Human => "human",
            _ => string.Empty
        };

        /// <summary>
        /// Get category CSS class for a detection.
        /// </summary>
        /// <param name="item">Detection</param>
        /// <returns>CSS class</returns>
        public static string GetCategoryClass(OrcasiteDetection item) => item.Category switch
        {
            DetectionCategory.Whale => "whale",
            DetectionCategory.Vessel => "vessel",
            DetectionCategory.Other => "other",
            _ => string.Empty
        };

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
            string classes = GetSourceClass(item) + " " + GetCategoryClass(item) + " " + GetTimeRangeClass(item);
            return classes;
        }

        public string GetDetectionStatus(OrcasiteDetection orcasiteDetection)
        {
            if (orcasiteDetection.Source == DetectionSource.Machine)
            {
                OrcaHelloDetection? orcaHelloDetection = _orcaHelloDetections.FirstOrDefault(d => d.Id == orcasiteDetection.IdempotencyKey);
                if (orcaHelloDetection == null)
                {
                    return "Unknown";
                }
                else if (!orcaHelloDetection.Reviewed)
                {
                    return "Unreviewed";
                }
                else if (orcaHelloDetection.IsPositive(orcasiteDetection))
                {
                    return "SRKW";
                }
                else
                {
                    return "Not SRKW";
                }
            }

            if (orcasiteDetection.Category != "whale")
            {
                return "Not whale";
            }
            else if (string.IsNullOrEmpty(orcasiteDetection.Description))
            {
                return "Unreviewed";
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

            List<OrcaHelloDetection> orcaHelloDetections = await _orcaHelloFetcher.GetRecentDetectionsAsync(timeframe: "1m", hydrophoneId: _node.S3NodeName, logger: _logger);
            if (orcaHelloDetections != null)
            {
                _orcaHelloDetections = orcaHelloDetections;
            }
        }
    }
}
