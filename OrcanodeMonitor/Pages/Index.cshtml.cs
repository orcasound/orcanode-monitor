// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OrcanodeMonitor.Core;
using System.Drawing;

namespace OrcanodeMonitor.Pages
{
    public class IndexModel : PageModel
    {
        private readonly ILogger<IndexModel> _logger;
        public List<Orcanode> Nodes => State.LastResult?.NodeList ?? new List<Orcanode>();
        private const int _maxEventCountToDisplay = 20;
        public List<OrcanodeEvent> RecentEvents => State.GetEvents(_maxEventCountToDisplay);

        public IndexModel(ILogger<IndexModel> logger)
        {
            _logger = logger;
        }
        public string LastChecked
        {
            get
            {
                if (State.LastResult == null)
                {
                    return "";
                }
                return Fetcher.UtcToLocalDateTime(State.LastResult.Timestamp).ToString();
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
            OrcanodeOnlineStatus status = node.DataplicityStatus;
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

        public void OnGet()
        {

        }
    }
}
