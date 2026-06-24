// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using System.Runtime.ExceptionServices;
using System.Text.Json.Serialization;

namespace OrcanodeMonitor.Models
{
    public class DetectionResponse
    {
        public List<DetectionData>? Data { get; set; }
    }

    public class DetectionData
    {
        public string Id { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public DetectionAttributes? Attributes { get; set; }
    }

    public class DetectionAttributes
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

    public enum DetectionGeneralCategoryEnum
    {
        Whale,
        Vessel,
        Human,
        Other
    }

    public enum DetectionSpecificCategoryEnum
    {
        Water,
        Resident,
        Transient,
        Humpback,
        Vessel,
        Jingle,
        Human,
        Unknown
    }

    public class OrcasiteDetection
    {
        public string ID { get; set; } = string.Empty;
        public string NodeID { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }

        public static DetectionSource GetSource(string source, string comments)
        {
            if (source.ToLower() == "human")
            {
                return DetectionSource.Human;
            }
            else if (comments.StartsWith("AI:"))
            {
                return DetectionSource.PodsAI;
            }
            else
            {
                return DetectionSource.OrcaHello;
            }
        }

        public string OrcasiteCategory { get; set; } = string.Empty;

        /// <summary>
        /// Whale, vessel, or human.
        /// </summary>
        public DetectionGeneralCategoryEnum GeneralCategory => OrcasiteCategory.ToLower() switch
        {
            DetectionCategory.Whale => DetectionGeneralCategoryEnum.Whale,
            DetectionCategory.Vessel => DetectionGeneralCategoryEnum.Vessel,
            DetectionCategory.Human => DetectionGeneralCategoryEnum.Human,
            _ => DetectionGeneralCategoryEnum.Other
        };

        public DetectionSource Source { get; set; } = DetectionSource.OrcaHello;
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
                if (GeneralCategory != DetectionGeneralCategoryEnum.Whale)
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

    public static class DetectionCategory
    {
        public const string All = "all";
        public const string Whale = "whale";
        public const string Vessel = "vessel";
        public const string Human = "human";
        public const string Other = "other";
    }

    public enum DetectionSource
    {
        All,
        Human,
        OrcaHello,
        PodsAI
    }
}
