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

        public string AksUrl => Fetcher.Configuration?["AZURE_AKS_URL"] ?? "";

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
                try
                {
                    MonitorState monitorState = MonitorState.GetFrom(_databaseContext);

                    if (monitorState.LastUpdatedTimestampUtc == null)
                    {
                        return "";
                    }
                    return Fetcher.UtcToLocalDateTime(monitorState.LastUpdatedTimestampUtc)?.ToString() ?? "";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Exception in LastChecked getter: {ex.Message}");
                    return "";
                }
            }
        }

        /// <summary>
        /// Get the background color associated with a status value.
        /// </summary>
        /// <param name="status">Status value</param>
        /// <param name="orcasoundStatus">Orcasound status value</param>
        /// <returns>HTML color string</returns>
        public static string GetBackgroundColor(OrcanodeOnlineStatus status, OrcanodeOnlineStatus? orcasoundStatus = null)
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

        public static string GetTextColor(string backgroundColor)
        {
            if (backgroundColor == ColorTranslator.ToHtml(Color.Red))
            {
                return ColorTranslator.ToHtml(Color.White);
            }
            return ColorTranslator.ToHtml(Color.FromArgb(0, 0, 238));
        }

        public string NodeS3BackgroundColor(Orcanode node) => GetBackgroundColor(node.S3StreamStatus, node.OrcasoundStatus);

        public string NodeS3TextColor(Orcanode node) => GetTextColor(NodeS3BackgroundColor(node));

        private readonly Dictionary<string, long> _orcaHelloDetectionCounts = new Dictionary<string, long>();

        public long GetOrcaHelloDetectionCount(Orcanode node)
        {
            if (!_orcaHelloDetectionCounts.TryGetValue(node.OrcasoundSlug, out long count))
            {
                return 0;
            }
            return count;
        }

        /// <summary>
        /// Gets the background color for the OrcaHello detections of the specified node.
        /// </summary>
        /// <param name="node">Node, or null if none</param>
        /// <param name="detectionCount">Detection count</param>
        /// <returns>HTML color string</returns>
        public static string GetNodeOrcaHelloDetectionsBackgroundColor(Orcanode? node, long detectionCount)
        {
            if (node == null)
            {
                return ColorTranslator.ToHtml(Color.Red);
            }

            // Light Red if OrcaHello Status is "Absent".
            if (node.OrcaHelloStatus == OrcanodeOnlineStatus.Absent)
            {
                return ColorTranslator.ToHtml(LightRed);
            }

            // Yellow if detections value is unusually high.
            string? highThresholdString = Fetcher.Configuration?["ORCAHELLO_HIGH_DETECTION_THRESHOLD"];
            long highThreshold = 150; // Default threshold
            if (!string.IsNullOrEmpty(highThresholdString) && long.TryParse(highThresholdString, out long parsedThreshold))
            {
                highThreshold = parsedThreshold;
            }

            if (detectionCount >= highThreshold)
            {
                return ColorTranslator.ToHtml(Color.Yellow);
            }

            // Light Green otherwise.
            return ColorTranslator.ToHtml(Color.LightGreen);
        }

        /// <summary>
        /// Get the background color for the OrcaHello detections cell of the specified node.
        /// </summary>
        /// <param name="node">Node</param>
        /// <returns>HTML color string</returns>
        public string NodeOrcaHelloDetectionsBackgroundColor(Orcanode node)
        {
            long detectionCount = GetOrcaHelloDetectionCount(node);
            return GetNodeOrcaHelloDetectionsBackgroundColor(node, detectionCount);
        }

        public string NodeOrcaHelloDetectionsTextColor(Orcanode node) => GetTextColor(NodeOrcaHelloDetectionsBackgroundColor(node));

        public string NodeOrcaHelloTextColor(Orcanode node) => GetTextColor(NodeOrcaHelloStatusBackgroundColor(node));

        public string NodeOrcaHelloStatusBackgroundColor(Orcanode node) => GetBackgroundColor(node.OrcaHelloStatus, node.OrcasoundStatus);

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

        public static Color LightRed => Color.FromArgb(0xff, 0xcc, 0xcb);

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
            try
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

                await OrcaHelloFetcher.FetchOrcaHelloDetectionCountsAsync(_nodes, _orcaHelloDetectionCounts);

                _recentEvents = await Fetcher.GetRecentEventsAsync(_databaseContext, DateTime.UtcNow.AddDays(-7), _logger) ?? new List<OrcanodeEvent>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Exception in OnGetAsync: {ex.Message}");
            }
        }

        public string GetEventClasses(OrcanodeEvent item)
        {
            string classes = NodeEventsModel.GetTypeClass(item) + " " + NodeEventsModel.GetTimeRangeClass(item);
            return classes;
        }
    }
}
