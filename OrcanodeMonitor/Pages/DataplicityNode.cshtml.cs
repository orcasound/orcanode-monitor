// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Xml.Linq;

namespace OrcanodeMonitor.Pages
{
    public class DataplicityNodeModel : PageModel
    {
        private OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<DataplicityNodeModel> _logger;
        private string _serial;
        private string _jsonData;

        public DataplicityNodeModel(OrcanodeMonitorContext context, ILogger<DataplicityNodeModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _serial = string.Empty;
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

        public async Task<IActionResult> OnGetAsync(string serial)
        {
            _serial = serial;
            string rawJson = await Fetcher.GetDataplicityDataAsync(serial, _logger);
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
