// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using System.ComponentModel.DataAnnotations;
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

    public class OrcanodeIftttEventDTO
    {
        public OrcanodeIftttEventDTO(Guid id, string slug, string type, string value, DateTime timestamp)
        {
            Slug = slug;
            Type = type;
            Value = value;
            Meta = new OrcanodeEventIftttMeta(id, timestamp);
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
        public string Description
        {
            get
            {
                string nodeName = State.GetNode(Slug)?.DisplayName ?? "<Unknown>";
                return string.Format("{0} was detected as {1}", nodeName, Value);
            }
        }
    }

    public class OrcanodeEvent
    {
        public OrcanodeEvent(string slug, string type, string value, DateTime timestamp)
        {
            ID = Guid.NewGuid();
            Slug = slug;
            Type = type;
            Value = value;
            DateTime = timestamp;
        }
        public OrcanodeIftttEventDTO ToIftttEventDTO() => new OrcanodeIftttEventDTO(ID, Slug, Type, Value, DateTime);
        public Guid ID { get; private set; }
        public string Slug { get; private set; }
        public string Type { get; private set; }
        public string Value { get; private set; }
        public Guid NodeId { get; private set; }
        public DateTime DateTime { get; private set; }

        public override string ToString()
        {
            return string.Format("{0} {1} {2} at {3}", Slug, Type, Value, Fetcher.UtcToLocalDateTime(DateTime));
        }

        public string Description
        {
            get
            {
                string nodeName = State.GetNode(Slug)?.DisplayName ?? "<Unknown>";
                return string.Format("{0} was detected as {1}", nodeName, Value);
            }
        }
    }
}
