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
        private readonly ILogger<NodeEventsModel> _logger;
        private Orcanode? _node = null;
        public string NodeName => _node?.DisplayName ?? "Unknown";
        public string Id => _node?.ID ?? string.Empty;
        public string OrcasoundSlug => _node?.OrcasoundSlug ?? string.Empty;
        private List<Detection> _detections;
        public List<Detection> RecentDetections => _detections;

        public NodeDetectionsModel(OrcanodeMonitorContext context, ILogger<NodeEventsModel> logger)
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
            DateTime OneWeekAgo = DateTime.UtcNow.AddDays(-7);
            if (item.Timestamp.ToUniversalTime() > OneWeekAgo)
            {
                return "pastWeek pastMonth";
            }

            DateTime OneMonthAgo = DateTime.UtcNow.AddMonths(-1);
            if (item.Timestamp.ToUniversalTime() > OneMonthAgo)
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
            _node = await _databaseContext.Orcanodes.Where(n => n.OrcasoundSlug == slug).FirstAsync();

            string feedId = _node?.OrcasoundFeedId ?? string.Empty;
            List<Detection>? detections = await Fetcher.GetRecentDetectionsForNodeAsync(_databaseContext, feedId, _logger);
            if (detections != null)
            {
                _detections = detections;
            }
        }
    }
}
