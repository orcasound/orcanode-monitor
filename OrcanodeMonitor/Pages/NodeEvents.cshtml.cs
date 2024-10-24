// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Xml.Linq;

namespace OrcanodeMonitor.Pages
{
    public class NodeEventsModel : PageModel
    {
        private OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<IndexModel> _logger;
        private string _nodeId;
        public string Id => _nodeId;
        [BindProperty]
        public string Selected { get; set; } = "week"; // Default to 'week'
        private DateTime SinceTime => (Selected == "week") ? DateTime.UtcNow.AddDays(-7) : DateTime.UtcNow.AddMonths(-1);
        private List<OrcanodeEvent> _events;
        public List<OrcanodeEvent> RecentEvents => _events;
        public int UptimePercentage => Orcanode.GetUptimePercentage(_nodeId, _events, SinceTime);

        public NodeEventsModel(OrcanodeMonitorContext context, ILogger<IndexModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _nodeId = string.Empty;
            _events = new List<OrcanodeEvent>();
        }

        public void OnGet(string id)
        {
            _nodeId = id;
            _events = Fetcher.GetRecentEventsForNode(_databaseContext, _nodeId, SinceTime);
        }

        public IActionResult OnPost(string selected, string id)
        {
            Selected = selected;
            _nodeId = id;
            _events = Fetcher.GetRecentEventsForNode(_databaseContext, _nodeId, SinceTime);
            return Page();
        }
    }
}
