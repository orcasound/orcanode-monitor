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
        public OrcanodeEventIftttMeta(Guid id, DateTime timestamp)
        {
            Id = id;
            UnixTimestamp = Fetcher.DateTimeToUnixTimeStamp(timestamp);
        }
        [JsonPropertyName("id")]
        public Guid Id { get; private set; }
        [JsonPropertyName("timestamp")]
        public long UnixTimestamp { get; private set; }
    }

    /// <summary>
    /// Data Transfer Object for IFTTT events.
    /// </summary>
    public class OrcanodeIftttEventDTO
    {
        public OrcanodeIftttEventDTO(Guid id, string slug, string nodeName, string type, string value, DateTime timestamp)
        {
            Slug = slug;
            Type = type;
            Value = value;
            Meta = new OrcanodeEventIftttMeta(id, timestamp);
            Description = string.Format("{0} was detected as {1}", nodeName, Value);
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
        [JsonPropertyName("timestamp")]
        public DateTime? DateTime => Fetcher.UnixTimeStampToDateTimeLocal(Meta.UnixTimestamp);
        [JsonPropertyName("description")]
        public string Description { get; private set; }
    }

    public class OrcanodeEvent
    {
        public OrcanodeEvent()
        {
            ID = Guid.NewGuid();
        }

        public OrcanodeEvent(string slug, string type, string value, DateTime timestamp)
        {
            ID = Guid.NewGuid();
            Slug = slug;
            Type = type;
            Value = value;
            DateTime = timestamp;
        }
        public OrcanodeIftttEventDTO ToIftttEventDTO() => new OrcanodeIftttEventDTO(ID, NodeName, Slug, Type, Value, DateTime);

        /// <summary>
        /// Database key for an event.
        /// </summary>
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public Guid ID { get; set; }
        
        public string Slug { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }

        /// <summary>
        /// Foreign Key for an Orcanode.
        /// </summary>
        public Guid OrcanodeId { get; set; }

        // Navigation property that uses OrcanodeId.
        public Orcanode Orcanode { get; set; }

        public string NodeName => Orcanode?.DisplayName ?? "<Unknown>";

        public DateTime DateTime { get; set; }

        public override string ToString()
        {
            return string.Format("{0} {1} {2} at {3}", Slug, Type, Value, Fetcher.UtcToLocalDateTime(DateTime));
        }

        public string Description => string.Format("{0} was detected as {1}", NodeName, Value);
    }
}
