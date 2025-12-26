// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

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
        public long PlaylistTimestamp { get; set; }
        public string? FeedId { get; set; }
        public string? PlayerOffset { get; set; }
    }

    public class Detection
    {
        public string ID { get; set; } = string.Empty;
        public string NodeID { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Source { get; set; } = DetectionSource.Machine;
        public string Description { get; set; } = string.Empty;
    }

    public static class DetectionCategory
    {
        public const string All = "all";
        public const string Whale = "whale";
        public const string Vessel = "vessel";
        public const string Other = "other";
    }

    public static class DetectionSource
    {
        public const string All = "all";
        public const string Human = "human";
        public const string Machine = "machine";
    }
}
