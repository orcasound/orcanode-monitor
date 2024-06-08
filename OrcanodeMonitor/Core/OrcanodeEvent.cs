// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace OrcanodeMonitor.Core
{
    public class OrcanodeEventMeta
    {
        public OrcanodeEventMeta(DateTime timestamp)
        {
            Id = Guid.NewGuid();
            UnixTimestamp = Fetcher.DateTimeToUnixTimeStamp(timestamp);
        }
        [JsonPropertyName("id")]
        public Guid Id { get; private set; }
        [JsonPropertyName("timestamp")]
        public long UnixTimestamp { get; private set; }
    }

    public class OrcanodeEvent
    {
        public OrcanodeEvent(string slug, OrcanodeOnlineStatus status, DateTime timestamp)
        {
            Slug = slug;
            Status = status;
            Meta = new OrcanodeEventMeta(timestamp);
        }
        [JsonPropertyName("slug")]
        public string Slug { get; private set; }
        [JsonPropertyName("status")]
        public OrcanodeOnlineStatus Status { get; private set; }
        [JsonPropertyName("meta")]
        public OrcanodeEventMeta Meta { get; private set; }
        public override string ToString()
        {
            return String.Format("{0} {1} at {2}", Slug, Status, Fetcher.UnixTimeStampToDateTimeLocal(Meta.UnixTimestamp));
        }
        [JsonPropertyName("timestamp")]
        public DateTime? DateTime => Fetcher.UnixTimeStampToDateTimeLocal(Meta.UnixTimestamp);
        [JsonPropertyName("description")]
        public string Description { get
            {
                string nodeName = State.GetNode(Slug)?.DisplayName ?? "<Unknown>";
                if (Status == OrcanodeOnlineStatus.Offline)
                {
                    return String.Format("{0} was detected as OFFLINE", nodeName);
                }
                return String.Format("{0} was detected as up", nodeName);
            }
        }
    }
}
