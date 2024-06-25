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

        private string GetBackgroundColor(OrcanodeOnlineStatus status)
        {
            if (status == OrcanodeOnlineStatus.Online)
            {
                return ColorTranslator.ToHtml(Color.LightGreen);
            }
            return ColorTranslator.ToHtml(Color.Red);
        }

        private string GetTextColor(OrcanodeOnlineStatus status)
        {
            if (status == OrcanodeOnlineStatus.Online)
            {
                return ColorTranslator.ToHtml(Color.FromArgb(0, 0, 238));
            }
            return ColorTranslator.ToHtml(Color.White);
        }

        public string NodeS3BackgroundColor(Orcanode node) => GetBackgroundColor(node.S3StreamStatus);

        public string NodeS3TextColor(Orcanode node) => GetTextColor(node.S3StreamStatus);

        public string NodeDataplicityBackgroundColor(Orcanode node) => GetBackgroundColor(node.DataplicityConnectionStatus);

        public string NodeDataplicityTextColor(Orcanode node) => GetTextColor(node.DataplicityConnectionStatus);

        public string NodeOrcasoundBackgroundColor(Orcanode node) => GetBackgroundColor(node.OrcasoundStatus);

        public string NodeOrcasoundTextColor(Orcanode node) => GetTextColor(node.OrcasoundStatus);

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
