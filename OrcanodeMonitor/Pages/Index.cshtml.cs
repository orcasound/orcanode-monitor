// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Drawing;

namespace OrcanodeMonitor.Pages
{
    public class IndexModel : PageModel
    {
        private OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<IndexModel> _logger;
        private List<Orcanode> _nodes;
        public List<Orcanode> Nodes => _nodes;
        private const int _maxEventCountToDisplay = 20;
        public List<OrcanodeEvent> RecentEvents => Fetcher.GetEvents(_databaseContext, _maxEventCountToDisplay);

        public IndexModel(OrcanodeMonitorContext context, ILogger<IndexModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
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

        public string NodeOrcasoundColor(Orcanode node)
        {
            OrcanodeOnlineStatus status = node.OrcasoundOnlineStatus;
            if (status == OrcanodeOnlineStatus.Online)
            {
                return ColorTranslator.ToHtml(Color.LightGreen);
            }
            return ColorTranslator.ToHtml(Color.Red);
        }

        public string NodeDataplicityColor(Orcanode node)
        {
            OrcanodeOnlineStatus status = node.DataplicityConnectionStatus;
            if (status == OrcanodeOnlineStatus.Offline)
            {
                return ColorTranslator.ToHtml(Color.Red);
            }
            return ColorTranslator.ToHtml(Color.LightGreen);
        }

        public string NodeOrcaHelloDetectionColor(Orcanode node)
        {
            if (node.LastOrcaHelloDetectionFound == null)
            {
                return ColorTranslator.ToHtml(Color.White);
            }
            if (node.LastOrcaHelloDetectionFound == false)
            {
                return ColorTranslator.ToHtml(Color.Yellow);
            }
            return ColorTranslator.ToHtml(Color.LightGreen);
        }

        public string NodeDataplicityUpgradeColor(Orcanode node)
        {
            OrcanodeUpgradeStatus status = node.DataplicityUpgradeStatus;
            if (status == OrcanodeUpgradeStatus.UpgradeAvailable)
            {
                return ColorTranslator.ToHtml(Color.Yellow);
            }
            return ColorTranslator.ToHtml(Color.LightGreen);
        }

        public async Task OnGetAsync()
        {
            _nodes = await _databaseContext.Orcanodes.OrderBy(n => n.DisplayName).ToListAsync();
        }
    }
}
