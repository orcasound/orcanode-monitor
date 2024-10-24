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
        private OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<IndexModel> _logger;
        private string _nodeId;
        public List<OrcanodeEvent> RecentEvents => Fetcher.GetRecentEventsForNode(_databaseContext, _nodeId);

        public NodeEventsModel(OrcanodeMonitorContext context, ILogger<IndexModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _nodeId = string.Empty;
        }

        public void OnGet(string id)
        {
            _nodeId = id;
        }
    }
}
