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

        public static string OrcaHelloSourceId => "orcahello";
        public static string PodsAISourceId => "podsai";
        public static string HumanSourceId => "human";

        /// <summary>
        /// Gets the default specific detection category for any sources that
        /// are binary detectors.
        /// </summary>
        public DetectionSpecificCategoryEnum? DefaultSpecificCategory => DetectionSourceString switch
        {
            "orcahello" => DetectionSpecificCategoryEnum.Resident,
            _ => null
        };

        /// <summary>
        /// Gets a normalized detection source identifier.
        /// </summary>
        /// <remarks>Uses `MachineDetection.ModelId` when present; otherwise uses `DetectionSource`. The
        /// returned value is trimmed when sourced from `ModelId` and converted to lowercase using invariant
        /// culture.</remarks>
        public string DetectionSourceString
        {
            get
            {
                if (MachineDetection == null)
                {
                    return HumanSourceId;
                }

                if (!string.IsNullOrWhiteSpace(MachineDetection.ModelId))
                {
                    string modelId = MachineDetection.ModelId.Trim().ToLowerInvariant();
                    if (modelId.StartsWith("orcasound/orcahello-srkw-detector-v1"))
                    {
                        return OrcaHelloSourceId;
                    }
                    if (modelId.StartsWith("davethaler/whale-call-detector"))
                    {
                        return PodsAISourceId;
                    }

                    // For any other models, use the model ID directly.
                    return modelId;
                }

                return "unknown";
            }
        }

        public string SourceLabel => GetModelDisplayName(DetectionSourceString);

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

        /// <summary>
        /// Gets the display name for a model identifier.
        /// </summary>
        /// <param name="modelName">Model identifier to format.</param>
        /// <returns>Mapped display name for known model identifiers; otherwise, the original model identifier.</returns>
        public static string GetModelDisplayName(string modelName) => modelName switch
        {
            "human" => "Human",
            "podsai" => "PODS-AI",
            "orcahello" => "OrcaHello",
            _ => modelName
        };

        public override string ToString()
        {
            return $"{Source} {Timestamp}";
        }

        /// <summary>
        /// Gets a value indicating whether the detection is considered reviewed.
        /// </summary>
        /// <remarks>Review status is inferred heuristically because it is not stored
        /// explicitly.</remarks>
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

        public string Tags => MachineDetection?.Tags ?? string.Empty;
        public string[] TagList => !string.IsNullOrEmpty(Tags) ? Tags.Split(';') : [];

        /// <summary>
        /// Determines whether a normalized tag matches a known whale-related tag.
        /// </summary>
        /// <param name="normalizedTag">Normalized tag to evaluate.</param>
        /// <returns><see langword="true"/> when the tag matches a known whale-related value; otherwise, <see langword="false"/>.</returns>
        public static bool IsWhaleTag(string normalizedTag)
        {
            string[] whaleTags = { "srkw", "resident", "transient", "humpback", "orca", "whale" };
            return whaleTags.Contains(normalizedTag);
        }

        /// <summary>
        /// Gets the effective category.
        /// </summary>
        /// <remarks>Returns "unreviewed" when a machine detection exists and is not reviewed, or when no
        /// machine detection exists and the item is not reviewed. Otherwise, uses the first tag when available, mapping
        /// "srkw" to "resident". If no tags are present, returns the lowercase invariant name of the general
        /// category.</remarks>
        public string ActualCategory
        {
            get
            {
                if ((MachineDetection != null && !MachineDetection.Reviewed) ||
                    (MachineDetection == null && !Reviewed))
                {
                    return "unreviewed";
                }

                string[] tagList = TagList;

                // Prioritize the first whale tag, then the first non-whale tag.
                string? firstNonWhaleTag = null;
                foreach (string tag in tagList)
                {
                    if (!string.IsNullOrWhiteSpace(tag))
                    {
                        string normalizedTag = tag.Trim().ToLowerInvariant();
                        normalizedTag = (normalizedTag == "srkw") ? "resident" : normalizedTag;
                        if (IsWhaleTag(normalizedTag))
                        {
                            return normalizedTag;
                        }
                        if (firstNonWhaleTag == null)
                        {
                            firstNonWhaleTag = normalizedTag;
                        }
                    }
                }
                if (firstNonWhaleTag != null)
                {
                    return firstNonWhaleTag;
                }
                if (MachineDetection?.Found == "yes")
                {
                    // Default to resident when a machine detection is found but no tags are present.
                    return "resident";
                }

                return GeneralCategory.ToString().ToLowerInvariant();
            }
        }

        /// <summary>
        /// Gets the predicted category label for the detection result.
        /// </summary>
        /// <remarks>Uses the machine prediction label when available; otherwise returns "whale" for
        /// human-sourced detections and "resident" as the default.</remarks>
        public string PredictedCategory
        {
            get
            {
                if (!string.IsNullOrEmpty(MachineDetection?.GlobalPredictionLabel))
                {
                    return MachineDetection.GlobalPredictionLabel.Trim().ToLowerInvariant();
                }
                if (Source == "human")
                {
                    return "whale";
                }

                // A binary model that predicts "resident" vs "not resident" is used
                // for machine detections that don't provide a GlobalPredictionLabel,
                // so we return "resident" as the default.
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
