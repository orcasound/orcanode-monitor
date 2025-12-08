// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
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
        private List<OrcanodeEvent> _events;
        private List<Orcanode> _nodes;
        public List<Orcanode> Nodes => _nodes;
        public List<OrcanodeEvent> RecentEvents => _recentEvents;
        private List<OrcanodeEvent> _recentEvents;

        public string AksUrl => Environment.GetEnvironmentVariable("AZURE_AKS_URL") ?? "";

        public IndexModel(OrcanodeMonitorContext context, ILogger<IndexModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _events = new List<OrcanodeEvent>();
            _nodes = new List<Orcanode>();
            _recentEvents = new List<OrcanodeEvent>();
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
                return Fetcher.UtcToLocalDateTime(monitorState.LastUpdatedTimestampUtc)?.ToString() ?? "";
            }
        }

        private string GetBackgroundColor(OrcanodeOnlineStatus status, OrcanodeOnlineStatus? orcasoundStatus = null)
        {
            if (status == OrcanodeOnlineStatus.Online)
            {
                return ColorTranslator.ToHtml(Color.LightGreen);
            }
            if (status == OrcanodeOnlineStatus.Hidden || status == OrcanodeOnlineStatus.NoView ||
                status == OrcanodeOnlineStatus.Unstable || status == OrcanodeOnlineStatus.Lagged)
            {
                return ColorTranslator.ToHtml(Color.Yellow);
            }
            if (orcasoundStatus.HasValue && (orcasoundStatus != OrcanodeOnlineStatus.Online))
            {
                return ColorTranslator.ToHtml(LightRed);
            }
            return ColorTranslator.ToHtml(Color.Red);
        }

        private string GetTextColor(string backgroundColor)
        {
            if (backgroundColor == ColorTranslator.ToHtml(Color.Red))
            {
                return ColorTranslator.ToHtml(Color.White);
            }
            return ColorTranslator.ToHtml(Color.FromArgb(0, 0, 238));
        }

        public string NodeS3BackgroundColor(Orcanode node) => GetBackgroundColor(node.S3StreamStatus, node.OrcasoundStatus);

        public string NodeS3TextColor(Orcanode node) => GetTextColor(NodeS3BackgroundColor(node));

        public string NodeOrcaHelloStatus(Orcanode node)
        {
            var status = node.OrcaHelloStatus;
            if ((status == OrcanodeOnlineStatus.Lagged || status == OrcanodeOnlineStatus.Online) &&
                (node.OrcaHelloInferencePodLag.HasValue))
            {
                return $"{Orcanode.FormatTimeSpan(node.OrcaHelloInferencePodLag.Value)}";
            }
            return status.ToString();
        }

        private readonly Dictionary<string, long> _orcaHelloDetectionCounts = new Dictionary<string, long>();

        public long GetOrcaHelloDetectionCount(Orcanode node)
        {
            if (!_orcaHelloDetectionCounts.TryGetValue(node.OrcasoundSlug, out long count))
            {
                return 0;
            }
            return count;
        }

        public string NodeOrcaHelloUptime(Orcanode node)
        {
            if (node.OrcaHelloInferencePodRunningSince.HasValue)
            {
                TimeSpan runTime = DateTime.UtcNow - node.OrcaHelloInferencePodRunningSince.Value;
                return $"{Orcanode.FormatTimeSpan(runTime)}";
            }
            return "None";
        }

        public string NodeOrcaHelloTextColor(Orcanode node) => GetTextColor(NodeOrcaHelloStatusBackgroundColor(node));

        public string NodeOrcaHelloStatusBackgroundColor(Orcanode node) => GetBackgroundColor(node.OrcaHelloStatus, node.OrcasoundStatus);

        public string NodeOrcaHelloUptimeBackgroundColor(Orcanode node)
        {
            if (node.OrcaHelloStatus == OrcanodeOnlineStatus.Online || node.OrcaHelloStatus == OrcanodeOnlineStatus.Lagged)
            {
                DateTime? since = node.OrcaHelloInferencePodRunningSince;
                if (since.HasValue)
                {
                    var ts = DateTime.UtcNow - since.Value;
                    if (ts > TimeSpan.FromHours(1))
                    {
                        return ColorTranslator.ToHtml(Color.LightGreen);
                    }

                    return ColorTranslator.ToHtml(Color.Yellow);
                }
            }
            var orcasoundStatus = node.OrcasoundStatus;
            if (orcasoundStatus != OrcanodeOnlineStatus.Online)
            {
                return ColorTranslator.ToHtml(LightRed);
            }
            return ColorTranslator.ToHtml(Color.Red);
        }


        /// <summary>
        /// Gets the text color for the Mezmo status of the specified node.
        /// </summary>
        /// <param name="node">The Orcanode instance.</param>
        /// <returns>A string representation of the text color.</returns>
        public string NodeMezmoTextColor(Orcanode node) => GetTextColor(NodeMezmoBackgroundColor(node));

        /// <summary>
        /// Gets the background color for the Mezmo status of the specified node.
        /// </summary>
        /// <param name="node">The Orcanode instance.</param>
        /// <returns>A string representation of the background color.</returns>
        public string NodeMezmoBackgroundColor(Orcanode node) => GetBackgroundColor(node.MezmoStatus, node.OrcasoundStatus);

        public string NodeDataplicityBackgroundColor(Orcanode node) => GetBackgroundColor(node.DataplicityConnectionStatus, node.OrcasoundStatus);

        public string NodeDataplicityTextColor(Orcanode node) => GetTextColor(NodeDataplicityBackgroundColor(node));

        public string NodeOrcasoundBackgroundColor(Orcanode node)
        {
            string color = GetBackgroundColor(node.OrcasoundStatus);
            if ((node.Type != "Live") && (color == ColorTranslator.ToHtml(Color.Red)))
            {
                return ColorTranslator.ToHtml(LightRed);
            }
            return color;
        }

        private Color LightRed => Color.FromArgb(0xff, 0xcc, 0xcb);

        public string NodeOrcasoundTextColor(Orcanode node) => GetTextColor(NodeOrcasoundBackgroundColor(node));

        public string NodeRealDecibelLevelBackgroundColor(Orcanode node)
        {
            string value = node.RealDecibelLevelForDisplay;
            if (value == "N/A")
            {
                return ColorTranslator.ToHtml(LightRed);
            }
            long level = long.Parse(value);
            if (level < FrequencyInfo.MinNoiseDecibels)
            {
                return ColorTranslator.ToHtml(LightRed);
            }
            if (level < FrequencyInfo.MaxSilenceDecibels)
            {
                return ColorTranslator.ToHtml(Color.Yellow);
            }
            return ColorTranslator.ToHtml(Color.LightGreen);
        }

        public string NodeHumDecibelLevelBackgroundColor(Orcanode node)
        {
            string value = node.HumDecibelLevelForDisplay;
            if (value == "N/A")
            {
                return ColorTranslator.ToHtml(LightRed);
            }
            long level = long.Parse(value);
            if (level < FrequencyInfo.MinNoiseDecibels)
            {
                return ColorTranslator.ToHtml(Color.LightGreen);
            }
            if (level < FrequencyInfo.MaxSilenceDecibels)
            {
                return ColorTranslator.ToHtml(Color.Yellow);
            }
            return ColorTranslator.ToHtml(LightRed);
        }

        private DateTime SinceTime => DateTime.UtcNow.AddDays(-7);

        public int GetUptimePercentage(Orcanode node) => Orcanode.GetUptimePercentage(node.ID, _events, SinceTime, OrcanodeEventTypes.HydrophoneStream);

        public string NodeUptimePercentageBackgroundColor(Orcanode node)
        {
            int value = GetUptimePercentage(node);
            if (value < 1)
            {
                if (node.OrcasoundStatus != OrcanodeOnlineStatus.Online)
                {
                    return ColorTranslator.ToHtml(LightRed);
                }
                return ColorTranslator.ToHtml(Color.Red);
            }
            else if (value > 99)
            {
                return ColorTranslator.ToHtml(Color.LightGreen);
            }

            return ColorTranslator.ToHtml(Color.Yellow);
        }
        public string NodeUptimePercentageTextColor(Orcanode node) => GetTextColor(NodeUptimePercentageBackgroundColor(node));

        public string NodeDataplicityUpgradeColor(Orcanode node)
        {
            OrcanodeUpgradeStatus status = node.DataplicityUpgradeStatus;
            if (status == OrcanodeUpgradeStatus.UpgradeAvailable)
            {
                return ColorTranslator.ToHtml(Color.Yellow);
            }
            return ColorTranslator.ToHtml(Color.LightGreen);
        }

        public string NodeDiskUsagePercentageColor(Orcanode node)
        {
            long percentage = node.DiskUsagePercentage;
            if (percentage < 75)
            {
                return ColorTranslator.ToHtml(Color.LightGreen);
            }
            if (percentage < 90)
            {
                return ColorTranslator.ToHtml(Color.Yellow);
            }
            return ColorTranslator.ToHtml(Color.Red);
        }

        public async Task OnGetAsync()
        {
            // Fetch nodes for display.
            var nodes = await _databaseContext.Orcanodes.ToListAsync();
            _nodes = nodes.Where(n => ((n.DataplicityConnectionStatus != OrcanodeOnlineStatus.Absent) ||
                                       (n.OrcasoundStatus != OrcanodeOnlineStatus.Absent) ||
                                       (n.S3StreamStatus != OrcanodeOnlineStatus.Absent &&
                                        n.S3StreamStatus != OrcanodeOnlineStatus.Unauthorized)) &&
                                      (n.OrcasoundHost != "dev.orcasound.net"))
                          .OrderBy(n => n.DisplayName)
                          .ToList();

            // Fetch events for uptime computation.
            var events = await _databaseContext.OrcanodeEvents.ToListAsync();
            _events = events.Where(e => e.Type == OrcanodeEventTypes.HydrophoneStream).ToList();

            // Fetch AI detection counts in parallel.
            var detectionTasks = _nodes.Select(async node => new
            {
                Slug = node.OrcasoundSlug,
                Count = await Fetcher.GetContainerDetectionCountAsync(node)
            });
            var results = await Task.WhenAll(detectionTasks);
            foreach (var result in results)
            {
                _orcaHelloDetectionCounts[result.Slug] = result.Count;
            }

            _recentEvents = await Fetcher.GetRecentEventsAsync(_databaseContext, DateTime.UtcNow.AddDays(-7), _logger) ?? new List<OrcanodeEvent>();
        }

        public string GetEventClasses(OrcanodeEvent item)
        {
            string classes = NodeEventsModel.GetTypeClass(item) + " " + NodeEventsModel.GetTimeRangeClass(item);
            return classes;
        }
    }
}
