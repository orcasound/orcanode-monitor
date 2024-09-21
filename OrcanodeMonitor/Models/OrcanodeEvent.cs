// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using OrcanodeMonitor.Core;

namespace OrcanodeMonitor.Models
{
    public class OrcanodeEventIftttMeta
    {
        public OrcanodeEventIftttMeta(string id, DateTime timestamp)
        {
            Id = id.ToString();
            UnixTimestamp = Fetcher.DateTimeToUnixTimeStamp(timestamp);
        }
        [JsonPropertyName("id")]
        public string Id { get; private set; }
        [JsonPropertyName("timestamp")]
        public long UnixTimestamp { get; private set; }
    }

    /// <summary>
    /// Data Transfer Object for IFTTT events.
    /// </summary>
    public class OrcanodeIftttEventDTO
    {
        public OrcanodeIftttEventDTO(string id, string nodeName, string slug, string type, string value, DateTime timestamp)
        {
            Slug = slug;
            Type = type;
            Value = value;
            Meta = new OrcanodeEventIftttMeta(id, timestamp);
            Description = string.Format("{0} {1} was detected as {2}", nodeName, type, value);
        }
        [JsonPropertyName("slug")]
        public string Slug { get; private set; }
        [JsonPropertyName("type")]
        public string Type { get; private set; }
        [JsonPropertyName("value")]
        public string Value { get; private set; }
        [JsonPropertyName("meta")]
        public OrcanodeEventIftttMeta Meta { get; private set; }
        public override string ToString()
        {
            return string.Format("{0} {1} {2} at {3}", Slug, Type, Value, Fetcher.UnixTimeStampToDateTimeLocal(Meta.UnixTimestamp));
        }
        [JsonPropertyName("created_at")]
        public DateTime? CreatedAt => Fetcher.UnixTimeStampToDateTimeUtc(Meta.UnixTimestamp);
        [JsonPropertyName("description")]
        public string Description { get; private set; }
    }

    // Instances of this class are persisted in a SQL database.  If any changes
    // are made to persisted fields, the database must go through a migration.
    // See https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=vs
    // for more information.
    public class OrcanodeEvent
    {
        public OrcanodeEvent()
        {
        }

        public OrcanodeEvent(Orcanode node, string type, string value, DateTime timestamp,int currentYear,Guid guidId)
        {
            Slug = node.OrcasoundSlug;
            Type = type;
            Value = value;
            DateTimeUtc = timestamp;
            OrcanodeId = node.ID;
            year = currentYear.ToString();
            ID = guidId.ToString();
        }

        #region persisted
        // Persisted fields.  If any changes are made, the database must go through a migration.
        // See https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=vs
        // for more information.

        /// <summary>
        /// Database key for an event.
        /// </summary>
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public string ID { get; set; }

        public string Slug { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }

        /// <summary>
        /// Foreign Key for an Orcanode.
        /// </summary>
        public string OrcanodeId { get; set; }

        // Navigation property that uses OrcanodeId.
        public virtual Orcanode Orcanode { get; set; }

        public DateTime DateTimeUtc { get; set; }

        public string year { get; set; }

        #endregion persisted

        #region derived

        public string NodeName => Orcanode?.DisplayName ?? "<Unknown>";

        public DateTime DateTimeLocal => Fetcher.UtcToLocalDateTime(DateTimeUtc).Value;

        public string Description
        {
            get
            {
                // Convert type from old value to new value.
                // TODO: do a migration in the database itself and remove this code.
                string type = Type;
                if (type == "stream status")
                {
                    type = "hydrophone stream";
                }

                return string.Format("{0} {1} was detected as {2}", NodeName, type, Value);
            }
        }

        #endregion derived

        #region methods
        public OrcanodeIftttEventDTO ToIftttEventDTO() => new OrcanodeIftttEventDTO(ID, NodeName, Slug, Type, Value, DateTimeUtc);

        public override string ToString()
        {
            return string.Format("{0} {1} => {2} at {3}", Slug, Type, Value, Fetcher.UtcToLocalDateTime(DateTimeUtc));
        }

        #endregion methods
    }
}
