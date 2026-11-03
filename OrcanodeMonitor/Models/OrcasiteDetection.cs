// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using OrcanodeMonitor.Core;
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
        Bird,
        Unknown
    }

    public class OrcasiteDetection
    {
        public string ID { get; set; } = string.Empty;
        public string NodeID { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }

        public MachineDetection? MachineDetection { get; set; }

        /// <summary>
        /// Source of detection, as an enum value: Human, OrcaHello, or PODS-AI.
        /// </summary>
        public DetectionSource DetectionSource
        {
            get
            {
                if (MachineDetection == null)
                {
                    return DetectionSource.Human;
                }

                if (!string.IsNullOrEmpty(MachineDetection.GlobalPredictionLabel) ||
                    (MachineDetection.Comments != null && MachineDetection.Comments.StartsWith("AI:")))
                {
                    return DetectionSource.PodsAI;
                }

                return DetectionSource.OrcaHello;
            }
        }

        // TODO: allow for other models by keeping modelid not using DetectionSource enum.
        public string DetectionSourceString => DetectionSource.ToString().ToLowerInvariant();

        public string OrcasiteCategory { get; set; } = string.Empty;

        /// <summary>
        /// Whale, vessel, or human.
        /// </summary>
        public DetectionGeneralCategoryEnum GeneralCategory => OrcasiteCategory.ToLowerInvariant() switch
        {
            DetectionCategory.Whale => DetectionGeneralCategoryEnum.Whale,
            DetectionCategory.Vessel => DetectionGeneralCategoryEnum.Vessel,
            DetectionCategory.Human => DetectionGeneralCategoryEnum.Human,
            _ => DetectionGeneralCategoryEnum.Other
        };

        public string Source { get; set; } = string.Empty;
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

        // TODO: Update other code to call these.
        public string Tags => MachineDetection?.Tags ?? string.Empty;
        public string[] TagList => !string.IsNullOrEmpty(Tags) ? Tags.Split(';') : [];

        public string ActualCategory
        {
            get
            {
                if (Reviewed)
                {
                    string[] tagList = TagList;
                    if (tagList.Any())
                    {
                        return tagList.First();
                    }
                    return GeneralCategory.ToString().ToLowerInvariant();
                }

                // Unless human detections show up as Reviewed then
                // they will also be "unknown" for the actual category.
                return "unknown";
            }
        }

        public string PredictedCategory
        {
            get
            {
                if (!string.IsNullOrEmpty(MachineDetection?.GlobalPredictionLabel))
                {
                    return MachineDetection.GlobalPredictionLabel;
                }
                return "resident";
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
        Human,
        OrcaHello,
        PodsAI,
        Other,
        All
    }
}
