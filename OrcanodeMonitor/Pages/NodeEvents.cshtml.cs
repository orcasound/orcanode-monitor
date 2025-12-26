// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Text.Json;

namespace OrcanodeMonitor.Pages
{
    public class NodeEventsModel : PageModel
    {
        private readonly OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<NodeEventsModel> _logger;
        private Orcanode? _node = null;

        public List<string> Labels => _labels;
        private List<string> _labels;

        private List<int> _dataplicityStatus;
        public List<int> DataplicityStatus => _dataplicityStatus;

        private List<int> _mezmoStatus;
        public List<int> MezmoStatus => _mezmoStatus;

        private List<int> _hydrophoneStreamStatus;
        public List<int> HydrophoneStreamStatus => _hydrophoneStreamStatus;

        public string Id => _node?.ID ?? string.Empty;
        public string OrcasoundSlug => _node?.OrcasoundSlug ?? string.Empty;
        public string NodeName => _node?.DisplayName ?? "Unknown";

        private List<OrcanodeEvent> _events;
        public List<OrcanodeEvent> RecentEvents => _events;
        public int GetUptimePercentage(string type, string timeRange)
        {
            DateTime sinceTime = DateTime.MinValue;
            if (timeRange == "pastWeek")
            {
                sinceTime = DateTime.UtcNow.AddDays(-7);
            }
            else if (timeRange == "pastMonth")
            {
                sinceTime = DateTime.UtcNow.AddMonths(-1);
            }

            string eventType = type switch
            {
                "hydrophoneStream" => OrcanodeEventTypes.HydrophoneStream,
                "dataplicityConnection" => OrcanodeEventTypes.DataplicityConnection,
                "mezmoLogging" => OrcanodeEventTypes.MezmoLogging,
                _ => OrcanodeEventTypes.HydrophoneStream
            };
            return Orcanode.GetUptimePercentage(Id, _events, sinceTime, eventType);
        }

        public NodeEventsModel(OrcanodeMonitorContext context, ILogger<NodeEventsModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _events = new List<OrcanodeEvent>();
            _labels = new List<string>();
            _dataplicityStatus = new List<int>();
            _mezmoStatus = new List<int>();
            _hydrophoneStreamStatus = new List<int>();
            JsonDataplicityData = string.Empty;
            JsonMezmoData = string.Empty;
            JsonHydrophoneStreamData = string.Empty;
        }

        private static int StatusStringToInt(string value)
        {
            int status = value.ToLower() switch
            {
                "up" or "online" => 3,
                "unintelligible" => 2,
                "silent" => 2,
                "down" or "offline" => 1,
                "unauthorized" => 0,
                "noview" => 0,
                "absent" => 0,
                _ => 0
            };
            return status;
        }

        public string JsonDataplicityData { get; set; }
        public string JsonMezmoData { get; set; }
        public string JsonHydrophoneStreamData { get; set; }

        private void AddCurrentEvent(List<OrcanodeEvent> events, DateTime now)
        {
            if (!events.Any())
            {
                return; // Nothing to do.
            }

            OrcanodeEvent last = events.Last();
            if (last.Orcanode == null)
            {
                return;
            }

            if (last.DateTimeUtc == now)
            {
                return;
            }

            var current = new OrcanodeEvent(last.Orcanode, last.Type, last.Value, now, null);
            events.Add(current);
        }

        private string CreateJsonDataset(string type, List<DateTime> allTimestamps, DateTime now)
        {
            List<OrcanodeEvent> dataset = _events.Where(e => e.Type == type).OrderBy(e => e.DateTimeUtc).ToList();
            AddCurrentEvent(dataset, now);

            var alignedDataset = allTimestamps.Select(timestamp =>
            {
                var matchingEvent = dataset.FirstOrDefault(e => e.DateTimeUtc == timestamp);
                return new
                {
                    Timestamp = timestamp,
                    StateValue = matchingEvent != null ? StatusStringToInt(matchingEvent.Value) : (int?)null
                };
            }).ToList();

            string json = JsonSerializer.Serialize(alignedDataset);
            return json;
        }

        private async Task FetchEventsAsync(ILogger logger)
        {
            List<OrcanodeEvent>? events = await Fetcher.GetRecentEventsForNodeAsync(_databaseContext, Id, DateTime.MinValue, logger);
            _events = events?.ToList() ?? new List<OrcanodeEvent>();

            if (!_events.Any())
            {
                return; // Nothing to do.
            }

            DateTime now = DateTime.UtcNow;
            List<DateTime> allTimestamps = _events.Select(e => e.DateTimeUtc).Union(new List<DateTime> { now }).OrderBy(t => t).ToList();
            JsonDataplicityData = CreateJsonDataset(OrcanodeEventTypes.DataplicityConnection, allTimestamps, now);
            JsonMezmoData = CreateJsonDataset(OrcanodeEventTypes.MezmoLogging, allTimestamps, now);
            JsonHydrophoneStreamData = CreateJsonDataset(OrcanodeEventTypes.HydrophoneStream, allTimestamps, now);
        }

        public async Task OnGetAsync(string id)
        {
            _node = await _databaseContext.Orcanodes.Where(n => n.ID == id).FirstAsync();
            await FetchEventsAsync(_logger);
        }

        public static string GetTypeClass(OrcanodeEvent item) => item.Type switch
        {
            OrcanodeEventTypes.HydrophoneStream => "hydrophoneStream",
            OrcanodeEventTypes.DataplicityConnection => "dataplicityConnection",
            OrcanodeEventTypes.MezmoLogging => "mezmoLogging",
            OrcanodeEventTypes.AgentUpgradeStatus => "agentUpgradeStatus",
            OrcanodeEventTypes.SDCardSize => "sdCardSize",
            _ => string.Empty
        };

        public static string GetTimeRangeClass(OrcanodeEvent item)
        {
            DateTime OneWeekAgo = DateTime.UtcNow.AddDays(-7);
            if (item.DateTimeUtc > OneWeekAgo)
            {
                return "pastWeek pastMonth";
            }

            DateTime OneMonthAgo = DateTime.UtcNow.AddMonths(-1);
            if (item.DateTimeUtc > OneMonthAgo)
            {
                return "pastMonth";
            }

            return string.Empty;
        }

        public string GetEventClasses(OrcanodeEvent item)
        {
            string classes = GetTypeClass(item) + " " + GetTimeRangeClass(item);
            return classes;
        }

        public string GetEventButtonStyle(OrcanodeEvent item)
        {
            if ((item.Type == OrcanodeEventTypes.HydrophoneStream) && !string.IsNullOrEmpty(item.Url))
            {
                return "display: inline-block;";
            }
            return "display: none;";
        }
    }
}
