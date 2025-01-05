// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;

namespace OrcanodeMonitor.Pages
{
    public class NodeEventsModel : PageModel
    {
        private readonly OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<NodeEventsModel> _logger;
        private Orcanode? _node = null;
        public string Id => _node?.ID ?? string.Empty;
        public string NodeName => _node?.DisplayName ?? "Unknown";

        [BindProperty]
        public string TimePeriod { get; set; } = "week"; // Default to 'week'
        [BindProperty]
        public string EventType { get; set; } = OrcanodeEventTypes.All; // Default to 'all'

        private DateTime SinceTime => (TimePeriod == "week") ? DateTime.UtcNow.AddDays(-7) : DateTime.UtcNow.AddMonths(-1);
        private List<OrcanodeEvent> _events;
        public List<OrcanodeEvent> RecentEvents => _events;
        public int GetUptimePercentage(string type, string timeRange)
        {
            DateTime sinceTime = (timeRange == "pastWeek") ? DateTime.UtcNow.AddDays(-7) : DateTime.UtcNow.AddMonths(-1);
            string eventType = type switch
            {
                "hydrophoneStream" => OrcanodeEventTypes.HydrophoneStream,
                "dataplicityConnection" => OrcanodeEventTypes.DataplicityConnection,
                "mezmoLogging" => OrcanodeEventTypes.MezmoLogging,
                _ => OrcanodeEventTypes.HydrophoneStream
            };
            return Orcanode.GetUptimePercentage(Id, _events, sinceTime, eventType);
        }

        public NodeEventsModel(OrcanodeMonitorContext context, ILogger<NodeEventsModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _events = new List<OrcanodeEvent>();
        }

        private void FetchEvents(ILogger logger)
        {
            _events = Fetcher.GetRecentEventsForNode(_databaseContext, Id, SinceTime, logger)
                .Where(e => e.Type == EventType || EventType == OrcanodeEventTypes.All)
                .ToList() ?? new List<OrcanodeEvent>();
        }

        public async Task OnGetAsync(string id)
        {
            _node = _databaseContext.Orcanodes.Where(n => n.ID == id).First();
            FetchEvents(_logger);
        }

        public string GetTypeClass(OrcanodeEvent item) => item.Type switch
        {
            OrcanodeEventTypes.HydrophoneStream => "hydrophoneStream",
            OrcanodeEventTypes.DataplicityConnection => "dataplicityConnection",
            OrcanodeEventTypes.MezmoLogging => "mezmoLogging",
            OrcanodeEventTypes.AgentUpgradeStatus => "agentUpgradeStatus",
            OrcanodeEventTypes.SDCardSize => "sdCardSize",
            _ => string.Empty
        };

        public string GetTimeRangeClass(OrcanodeEvent item)
        {
            DateTime OneWeekAgo = DateTime.UtcNow.AddDays(-7);
            return (item.DateTimeUtc > OneWeekAgo) ? "pastWeek" : string.Empty;
        }

        public string GetEventClasses(OrcanodeEvent item)
        {
            string classes = GetTypeClass(item) + " " + GetTimeRangeClass(item);
            return classes;
        }

        public string GetEventButtonStyle(OrcanodeEvent item)
        {
            if ((item.Type == OrcanodeEventTypes.HydrophoneStream) && !string.IsNullOrEmpty(item.Url))
            {
                return "display: inline-block;";
            }
            return "display: none;";
        }
    }
}
