// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
namespace OrcanodeMonitor.Core
{
    public class State
    {
        private static List<OrcanodeEvent> _events = new List<OrcanodeEvent>();
        private static EnumerateNodesResult? _lastResult;
        private static void AddOrcanodeEvent(List<OrcanodeEvent> list, Orcanode node, DateTime resultTimestamp)
        {
            DateTime eventTimestampUtc = node.ManifestUpdatedUtc.HasValue ? node.ManifestUpdatedUtc.Value : resultTimestamp.ToUniversalTime();
            var orcanodeEvent = new OrcanodeEvent(node.OrcasoundSlug, node.OrcasoundOnlineStatus, eventTimestampUtc);
            list.Add(orcanodeEvent);
        }

        public static void SetLastResult(EnumerateNodesResult result)
        {
            var newEvents = new List<OrcanodeEvent>();
            foreach (Orcanode nodeNewState in result.NodeList)
            {
                OrcanodeOnlineStatus newStatus = nodeNewState.OrcasoundOnlineStatus;
                if (_lastResult == null)
                {
                    AddOrcanodeEvent(newEvents, nodeNewState, result.Timestamp);
                    continue;
                }
                Orcanode? nodeOldState = _lastResult.NodeList.Find(node => node.OrcasoundSlug == nodeNewState.OrcasoundSlug);
                OrcanodeOnlineStatus oldStatus = (nodeOldState != null) ? nodeOldState.OrcasoundOnlineStatus : OrcanodeOnlineStatus.Offline;
                if (newStatus != oldStatus)
                {
                    AddOrcanodeEvent(newEvents, nodeNewState, result.Timestamp);
                }
            }

            // Sort new events.
            var ascendingOrder = newEvents.OrderBy(e => e.DateTime);
            foreach (OrcanodeEvent orcanodeEvent in ascendingOrder)
            {
                // Insert latest event at the beginning.
                _events.Insert(0, orcanodeEvent);
            }

            _lastResult = result;
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
        public static EnumerateNodesResult? LastResult => _lastResult;
        public static Orcanode? GetNode(string slug) => LastResult?.NodeList.Find(item => item.OrcasoundSlug == slug);

        // TODO: persist state across restarts.
    }
}
