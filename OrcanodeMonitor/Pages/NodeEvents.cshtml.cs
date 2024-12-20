// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using MathNet.Numerics.Statistics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.IdentityModel.Tokens;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Xml.Linq;
using static OrcanodeMonitor.Core.Fetcher;

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
        public int UptimePercentage => Orcanode.GetUptimePercentage(Id, _events, SinceTime, (EventType == OrcanodeEventTypes.All) ? OrcanodeEventTypes.HydrophoneStream : EventType);

        public NodeEventsModel(OrcanodeMonitorContext context, ILogger<NodeEventsModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _events = new List<OrcanodeEvent>();
        }

        private void FetchEvents(ILogger logger)
        {
            _events = Fetcher.GetRecentEventsForNode(_databaseContext, Id, SinceTime, logger).Where(e => e.Type == EventType || EventType == OrcanodeEventTypes.All).ToList() ?? new List<OrcanodeEvent>();
        }

        public async Task OnGetAsync(string id)
        {
            _node = _databaseContext.Orcanodes.Where(n => n.ID == id).First();
            FetchEvents(_logger);
        }

        public string GetTypeClass(OrcanodeEvent item)
        {
            if (item.Type == OrcanodeEventTypes.HydrophoneStream)
            {
                return "hydrophoneStream";
            }
            else if (item.Type == OrcanodeEventTypes.DataplicityConnection)
            {
                return "dataplicityConnection";
            }
            else if (item.Type == OrcanodeEventTypes.MezmoLogging)
            {
                return "mezmoLogging";
            }
            else if (item.Type == OrcanodeEventTypes.AgentUpgradeStatus)
            {
                return "agentUpgradeStatus";
            }
            else if (item.Type == OrcanodeEventTypes.SDCardSize)
            {
                return "sdCardSize";
            }
            else
            {
                return string.Empty;
            }
        }

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
    }
}
