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
        private List<Detection> _detections;
        public List<Detection> RecentDetections => _detections;

        public NodeDetectionsModel(OrcanodeMonitorContext context, ILogger<NodeDetectionsModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _detections = new List<Detection>();
        }

        /// <summary>
        /// Get source CSS class for a detection.
        /// </summary>
        /// <param name="item">Detection</param>
        /// <returns>CSS class</returns>
        public static string GetSourceClass(Detection item) => item.Source switch
        {
            DetectionSource.Machine => "machine",
            DetectionSource.Human => "human",
            _ => string.Empty
        };

        /// <summary>
        /// Get time range CSS classes for a detection.
        /// </summary>
        /// <param name="item">Detection</param>
        /// <returns>String containing CSS classes</returns>
        public static string GetTimeRangeClass(Detection item)
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
        /// Get detection CSS classes based on source and time range.
        /// </summary>
        /// <param name="item">Detection</param>
        /// <returns>String containing CSS classes</returns>
        public string GetDetectionClasses(Detection item)
        {
            string classes = GetSourceClass(item) + " " + GetTimeRangeClass(item);
            return classes;
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

            List<Detection>? detections = await Fetcher.GetRecentDetectionsForNodeAsync(_node.OrcasoundFeedId, _logger);
            if (detections != null)
            {
                _detections = detections;
            }
        }
    }
}
