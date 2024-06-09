// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;

namespace OrcanodeMonitor.Models
{
    public class MonitorState
    {
        private const int _singletonKey = 0;

        public MonitorState()
        {
            ID = _singletonKey;
        }

        /// <summary>
        /// Database key field.
        /// </summary>
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int ID { get; set; }
        public DateTime? LastUpdatedTimestampUtc { get; set; }

        public static MonitorState GetFrom(OrcanodeMonitorContext context)
        {
            MonitorState? state = context.MonitorState.Find(_singletonKey);
            if (state != null)
            {
                if (state.LastUpdatedTimestampUtc.HasValue &&
                    state.LastUpdatedTimestampUtc.Value.Kind == DateTimeKind.Unspecified)
                {
                    state.LastUpdatedTimestampUtc = DateTime.SpecifyKind(state.LastUpdatedTimestampUtc.Value, DateTimeKind.Utc);
                }
                return state;
            }
            state = new MonitorState();
            context.MonitorState.Add(state);
            return state;
        }
    }
}
