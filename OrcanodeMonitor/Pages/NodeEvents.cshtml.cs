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

        public IActionResult OnPost(string timePeriod, string eventType, string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                _logger.LogError("Node ID cannot be empty");
                return BadRequest("Invalid node ID");
            }
            if (timePeriod.IsNullOrEmpty())
            {
                timePeriod = TimePeriod;
            }
            if (eventType.IsNullOrEmpty())
            {
                eventType = EventType;
            }
            if (timePeriod != "week" && timePeriod != "month")
            {
                _logger.LogWarning($"Invalid time range selected: {timePeriod}");
                return BadRequest("Invalid time range");
            }
            if (eventType != OrcanodeEventTypes.All && eventType != OrcanodeEventTypes.HydrophoneStream && eventType != OrcanodeEventTypes.MezmoLogging && eventType != OrcanodeEventTypes.DataplicityConnection)
            {
                _logger.LogWarning($"Invalid event type selected: {eventType}");
                return BadRequest("Invalid event type");
            }
            TimePeriod = timePeriod;
            EventType = eventType;
            _node = _databaseContext.Orcanodes.Where(n => n.ID == id).First();
            FetchEvents(_logger);
            return Page();
        }
    }
}
