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
        public OrcanodeEventIftttMeta(int id, DateTime timestamp)
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
        public OrcanodeIftttEventDTO(int id, string nodeName, string slug, string type, string value, DateTime timestamp)
        {
            Slug = slug;
            Type = type;
            Value = value;
            Meta = new OrcanodeEventIftttMeta(id, timestamp);
            Description = string.Format("{0} orcanode stream was detected as {1}", nodeName, Value);
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

    public class OrcanodeEvent
    {
        public OrcanodeEvent()
        {
        }

        public OrcanodeEvent(Orcanode node, string type, string value, DateTime timestamp)
        {
            Slug = node.OrcasoundSlug;
            Type = type;
            Value = value;
            DateTimeUtc = timestamp;
            OrcanodeId = node.ID;
        }
        public OrcanodeIftttEventDTO ToIftttEventDTO() => new OrcanodeIftttEventDTO(ID, NodeName, Slug, Type, Value, DateTimeUtc);

        /// <summary>
        /// Database key for an event.
        /// </summary>
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ID { get; set; }

        public string Slug { get; set; }
        public string Type { get; set; }
        public string Value { get; set; }

        /// <summary>
        /// Foreign Key for an Orcanode.
        /// </summary>
        public int OrcanodeId { get; set; }

        // Navigation property that uses OrcanodeId.
        public virtual Orcanode Orcanode { get; set; }

        public string NodeName => Orcanode?.DisplayName ?? "<Unknown>";

        public DateTime DateTimeUtc { get; set; }
        public DateTime DateTimeLocal => Fetcher.UtcToLocalDateTime(DateTimeUtc).Value;

        public override string ToString()
        {
            return string.Format("{0} {1} => {2} at {3}", Slug, Type, Value, Fetcher.UtcToLocalDateTime(DateTimeUtc));
        }

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
    }
}
