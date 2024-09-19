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

        #region persisted
        // Persisted fields.  If any changes are made, the database must go through a migration.
        // See https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=vs
        // for more information.

        /// <summary>
        /// Database key field.
        /// </summary>
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ID { get; set; }
        public DateTime? LastUpdatedTimestampUtc { get; set; }
        #endregion persisted

        #region methods
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
        #endregion methods
    }
}
