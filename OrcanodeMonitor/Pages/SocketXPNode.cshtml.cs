// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.IdentityModel.Tokens;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;

namespace OrcanodeMonitor.Pages
{
    public class SocketXPNodeModel : PageModel
    {
        private OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<SocketXPNodeModel> _logger;
        private string _deviceId;
        private string _jsonData;

        public SocketXPNodeModel(OrcanodeMonitorContext context, ILogger<SocketXPNodeModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _deviceId = string.Empty;
            _jsonData = string.Empty;
        }

        public string LastChecked
        {
            get
            {
                MonitorState monitorState = MonitorState.GetFrom(_databaseContext);

                if (monitorState.LastUpdatedTimestampUtc == null)
                {
                    return "";
                }
                return Fetcher.UtcToLocalDateTime(monitorState.LastUpdatedTimestampUtc).ToString();
            }
        }

        public string JsonData => _jsonData;

        public async Task<IActionResult> OnGetAsync(string deviceId)
        {
            _deviceId = deviceId;
            string rawJson = await SocketXPFetcher.GetSocketXPDataAsync(deviceId, _logger);
            if (rawJson.IsNullOrEmpty())
            {
                return NotFound(); // Returns a 404 error page
            }
            var formatter = new JsonFormatter();
            _jsonData = formatter.FormatJson(rawJson);
            return Page();
        }
    }
}
