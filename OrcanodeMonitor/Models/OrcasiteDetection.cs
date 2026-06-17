// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using System.Text.Json.Serialization;

namespace OrcanodeMonitor.Models
{
    public class OrcasiteDetectionResponse
    {
        public List<OrcasiteDetectionData>? Data { get; set; }
    }

    public class OrcasiteDetectionData
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public OrcasiteDetectionAttributes? Attributes { get; set; }
    }

    public class OrcasiteDetectionAttributes
    {
        public DateTime Timestamp { get; set; }
        public string? Description { get; set; }
        public string? Source { get; set; }
        public string? Category { get; set; }

        // TODO: can we use this instead of JsonPropertyName?
        // var deserializer = new DeserializerBuilder().WithNamingConvention(UnderscoredNamingConvention.Instance).Build();
        [JsonPropertyName("playlist_timestamp")]
        public long PlaylistTimestamp { get; set; }

        [JsonPropertyName("feed_id")]
        public string? FeedId { get; set; }

        [JsonPropertyName("player_offset")]
        public string? PlayerOffset { get; set; }
        [JsonPropertyName("idempotency_key")]
        public string? IdempotencyKey { get; set; }
    }

    public class OrcasiteDetection
    {
        public string ID { get; set; } = string.Empty;
        public string NodeID { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Source { get; set; } = OrcasiteDetectionSource.Machine;
        public string Description { get; set; } = string.Empty;
        public string IdempotencyKey { get; set; } = string.Empty;

        public override string ToString()
        {
            return $"{Source} {Timestamp}";
        }

        public bool Reviewed
        {
            get
            {
                // Heuristic since reviewed is not a property.
                if (Category != "whale")
                {
                    return true;
                }
                else if (string.IsNullOrEmpty(Description))
                {
                    return false;
                }
                else
                {
                    return true;
                }
            }
        }
    }

    public static class OrcasiteDetectionCategory
    {
        public const string All = "all";
        public const string Whale = "whale";
        public const string Vessel = "vessel";
        public const string Other = "other";
    }

    public static class OrcasiteDetectionSource
    {
        public const string All = "all";
        public const string Human = "human";
        public const string Machine = "machine";
    }
}
