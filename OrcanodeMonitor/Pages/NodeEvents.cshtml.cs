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
        private string _nodeId;
        public string Id => _nodeId;
        [BindProperty]
        public string Selected { get; set; } = "week"; // Default to 'week'
        private DateTime SinceTime => (Selected == "week") ? DateTime.UtcNow.AddDays(-7) : DateTime.UtcNow.AddMonths(-1);
        private List<OrcanodeEvent> _events;
        public List<OrcanodeEvent> RecentEvents => _events;
        public int UptimePercentage => Orcanode.GetUptimePercentage(_nodeId, _events, SinceTime);

        public NodeEventsModel(OrcanodeMonitorContext context, ILogger<NodeEventsModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _nodeId = string.Empty;
            _events = new List<OrcanodeEvent>();
        }

        private void FetchEvents()
        {
            try
            {
                _events = Fetcher.GetRecentEventsForNode(_databaseContext, _nodeId, SinceTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch events for node {NodeId}", _nodeId);
                _events = new List<OrcanodeEvent>();
            }
        }

        public void OnGet(string id)
        {
            _nodeId = id;
            FetchEvents();
        }

        public IActionResult OnPost(string selected, string id)
        {
            if (selected != "week" && selected != "month")
            {
                _logger.LogWarning("Invalid time range selected: {selected}", selected);
                return BadRequest("Invalid time range");
            }
            Selected = selected;
            _nodeId = id;
            FetchEvents();
            return Page();
        }
    }
}
