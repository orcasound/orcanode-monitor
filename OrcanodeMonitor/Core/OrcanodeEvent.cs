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
        public OrcanodeEvent(string slug, OrcanodeStatus status, DateTime timestamp)
        {
            Slug = slug;
            Status = status;
            Meta = new OrcanodeEventMeta(timestamp);
        }
        [JsonPropertyName("slug")]
        public string Slug { get; private set; }
        [JsonPropertyName("status")]
        public OrcanodeStatus Status { get; private set; }
        [JsonPropertyName("meta")]
        public OrcanodeEventMeta Meta { get; private set; }
        public override string ToString()
        {
            return String.Format("{0} {1} at {2}", Slug, Status, Fetcher.UnixTimeStampToDateTime(Meta.UnixTimestamp));
        }
        public DateTime? DateTime => Core.Fetcher.UnixTimeStampToDateTime(Meta.UnixTimestamp)?.ToLocalTime();
        public string Description { get
            {
                string nodeName = State.GetNode(Slug)?.Name ?? "<Unknown>";
                if (Status == OrcanodeStatus.Offline)
                {
                    return String.Format("{0} went OFFLINE", nodeName);
                }
                return String.Format("{0} was detected as up", nodeName);
            }
        }
    }
}
