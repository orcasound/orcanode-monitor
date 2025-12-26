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

        public static string GetSourceClass(Detection item) => item.Source switch
        {
            DetectionSource.Machine => "machine",
            DetectionSource.Human => "human",
            _ => string.Empty
        };

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

        public string GetDetectionClasses(Detection item)
        {
            string classes = GetSourceClass(item) + " " + GetTimeRangeClass(item);
            return classes;
        }

        public string GetSpectralDensityId(DateTime dateTime)
        {
            string timestamp = dateTime.ToString("yyyy-MM-ddTHH-mm-ss");
            return $"{_node?.ID}/{timestamp}";
        }

        public string GetSpectralDensityId(Detection item) => GetSpectralDensityId(item.Timestamp);

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
