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
            if (status == OrcanodeOnlineStatus.Hidden || status == OrcanodeOnlineStatus.NoView)
            {
                return ColorTranslator.ToHtml(Color.Yellow);
            }
            return ColorTranslator.ToHtml(Color.Red);
        }

        private string GetTextColor(OrcanodeOnlineStatus status)
        {
            if (status == OrcanodeOnlineStatus.Online ||
                status == OrcanodeOnlineStatus.Hidden ||
                status == OrcanodeOnlineStatus.NoView)
            {
                return ColorTranslator.ToHtml(Color.FromArgb(0, 0, 238));
            }
            return ColorTranslator.ToHtml(Color.White);
        }

        public string NodeS3BackgroundColor(Orcanode node) => GetBackgroundColor(node.S3StreamStatus);

        public string NodeS3TextColor(Orcanode node) => GetTextColor(node.S3StreamStatus);

        public string NodeOrcaHelloTextColor(Orcanode node) => GetTextColor(node.OrcaHelloStatus);

        public string NodeOrcaHelloBackgroundColor(Orcanode node) => GetBackgroundColor(node.OrcaHelloStatus);

        /// <summary>
        /// Gets the text color for the Mezmo status of the specified node.
        /// </summary>
        /// <param name="node">The Orcanode instance.</param>
        /// <returns>A string representation of the text color.</returns>
        public string NodeMezmoTextColor(Orcanode node) => GetTextColor(node.MezmoStatus);

        /// <summary>
        /// Gets the background color for the Mezmo status of the specified node.
        /// </summary>
        /// <param name="node">The Orcanode instance.</param>
        /// <returns>A string representation of the background color.</returns>
        public string NodeMezmoBackgroundColor(Orcanode node) => GetBackgroundColor(node.MezmoStatus);

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
            var nodes = await _databaseContext.Orcanodes.ToListAsync();
            _nodes = nodes.Where(n => ((n.DataplicityConnectionStatus != OrcanodeOnlineStatus.Absent) ||
                                       (n.OrcasoundStatus != OrcanodeOnlineStatus.Absent) ||
                                       (n.S3StreamStatus != OrcanodeOnlineStatus.Absent &&
                                        n.S3StreamStatus != OrcanodeOnlineStatus.Unauthorized)) &&
                                      (n.OrcasoundHost != "dev.orcasound.net"))
                          .OrderBy(n => n.DisplayName)
                          .ToList();
        }
    }
}
