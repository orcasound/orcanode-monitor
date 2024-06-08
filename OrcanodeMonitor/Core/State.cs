// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.EntityFrameworkCore;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;

namespace OrcanodeMonitor.Core
{
    public class State
    {
#if false
        public static Data.OrcanodeMonitorContext DatabaseContext
        {
            get
            {
                if (_databaseContext == null)
                {
                    _databaseContext = new Data.OrcanodeMonitorContext();
                }
                return _databaseContext;
            }
        }
#endif
        private static List<OrcanodeEvent> _events = new List<OrcanodeEvent>();
#if false
        private static List<Orcanode> _nodes = new List<Orcanode>();
        public static List<Orcanode> Nodes => _nodes;
#endif
#if false
        public static async Task<List<Orcanode>> GetNodesAsync(OrcanodeMonitorContext context)
        {
            return await context.Orcanodes.ToListAsync();
        }
#endif

        public static DateTime? LastUpdatedTimestamp { get; set; }

        private static void AddOrcanodeStreamStatusEvent(List<OrcanodeEvent> list, Orcanode node)
        {
            string value = (node.OrcasoundOnlineStatus == OrcanodeOnlineStatus.Online) ? "up" : "OFFLINE";
            var orcanodeEvent = new OrcanodeEvent(node.OrcasoundSlug, "stream status", value, DateTime.UtcNow);
            list.Insert(0, orcanodeEvent);
        }

        public static void AddOrcanodeStreamStatusEvent(Orcanode node)
        {
            AddOrcanodeStreamStatusEvent(_events, node);
        }

        /// <summary>
        /// Get a list of the most recent events in order from most to least recent,
        /// up to a maximum of 'limit' events.
        /// </summary>
        /// <param name="limit">Maximum number of events to return</param>
        /// <returns>List of events</returns>
        public static List<OrcanodeEvent> GetEvents(int limit)
        {
            var result = new List<OrcanodeEvent>();
            int count = 0;
            foreach (OrcanodeEvent currentEvent in _events)
            {
                if (count >= limit)
                {
                    break;
                }
                result.Add(currentEvent);
                count++;
            }
            return result;
        }
    }
}
