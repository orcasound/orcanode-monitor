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

        private void FetchEvents(ILogger logger)
        {
            _events = Fetcher.GetRecentEventsForNode(_databaseContext, _nodeId, SinceTime, logger) ?? new List<OrcanodeEvent>();
        }

        public void OnGet(string id)
        {
            _nodeId = id;
            FetchEvents(_logger);
        }

        public IActionResult OnPost(string selected, string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                _logger.LogError("Node ID cannot be empty");
                return BadRequest("Invalid node ID");
            }
            if (selected != "week" && selected != "month")
            {
                _logger.LogWarning("Invalid time range selected: {selected}", selected);
                return BadRequest("Invalid time range");
            }
            Selected = selected;
            _nodeId = id;
            FetchEvents(_logger);
            return Page();
        }
    }
}
