// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using OrcanodeMonitor.Core;

namespace OrcanodeMonitor.Models
{
    public enum OrcanodeOnlineStatus
    {
        Absent = 0,
        Offline,
        Online
    }
    public enum OrcanodeUpgradeStatus
    {
        Unknown = 0,
        UpToDate = 1,
        UpgradeAvailable
    }

    public class OrcanodeIftttDTO
    {
        public OrcanodeIftttDTO(int id, string displayName)
        {
            ID = id;
            DisplayName = displayName;
        }
        [JsonPropertyName("ID")]
        public int ID { get; private set; }
        [JsonPropertyName("display_name")]
        public string DisplayName { get; private set; }
        public override string ToString() => DisplayName;
    }

    public class Orcanode
    {
        const int _defaultMaxUploadDelayMinutes = 2;
        /// <summary>
        /// If the manifest file is older than this, the node will be considered offline.
        /// </summary>
        private static TimeSpan MaxUploadDelay
        {
            get
            {
                string? maxUploadDelayMinutesString = Environment.GetEnvironmentVariable("ORCASOUND_MAX_UPLOAD_DELAY_MINUTES");
                int maxUploadDelayMinutes = int.TryParse(maxUploadDelayMinutesString, out var minutes) ? minutes : _defaultMaxUploadDelayMinutes;
                return TimeSpan.FromMinutes(maxUploadDelayMinutes);
            }
        }

        /// <summary>
        /// Database key field. This is NOT the dataplicity serial GUID, since a node might first be
        /// detected via another mechanism before we get the dataplicity serial GUID.
        /// </summary>
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ID { get; set; }

        public Orcanode()
        {
            // Initialize reference types.
            LastOrcaHelloDetectionComments = string.Empty;
            OrcasoundName = string.Empty;
            OrcasoundSlug = string.Empty;
            S3Bucket = string.Empty;
            S3NodeName = string.Empty;
            AgentVersion = string.Empty;
            DataplicityDescription = string.Empty;
            DataplicityName = string.Empty;
            DataplicitySerial = string.Empty;
        }

        public OrcanodeIftttDTO ToIftttDTO() => new OrcanodeIftttDTO(ID, DisplayName);

        /// <summary>
        /// The "serial" at Dataplicity.
        /// </summary>
        public string DataplicitySerial { get; set; }

        public static string OrcasoundNameToDisplayName(string orcasoundName)
        {
            // Convert an Orcasound name of "Beach Camp at Sunset Bay" to just "Sunset Bay".)
            string displayName = orcasoundName;
            int atIndex = orcasoundName.IndexOf(" at ");
            if (atIndex >= 0)
            {
                displayName = orcasoundName.Substring(atIndex + 4);
            }
            return displayName;
        }

        public static string DataplicityNameToDisplayName(string dataplicityName)
        {
            string displayName = dataplicityName;
            int index = dataplicityName.IndexOf(": ");
            if (index >= 0)
            {
                displayName = dataplicityName.Substring(index + 2);
            }
            index = dataplicityName.IndexOf("Rpi ");
            if (index >= 0)
            {
                displayName = dataplicityName.Substring(index + 4);
            }
            return displayName;
        }

        /// <summary>
        /// Human-readable name.
        /// </summary>
        [Required]
        public string DisplayName { get; set; }
        /// <summary>
        /// Human-readable name at Orcasound.
        /// </summary>
        public string OrcasoundName { get; set; }
        /// <summary>
        /// The URI path component from the "node_name" field obtained from orcasound.net.
        /// </summary>
        public string S3NodeName { get; set; }
        /// <summary>
        /// The hostname component from the "bucket" field obtained from orcasound.net
        /// </summary>
        public string S3Bucket { get; set; }
        /// <summary>
        /// The URI path component from the "slug" field obtained from orcasound.net.
        /// </summary>
        public string OrcasoundSlug { get; set; }
        /// <summary>
        /// Value in the latest.txt file, as a UTC DateTime.
        /// </summary>
        public DateTime? LatestRecordedUtc { get; set; }
        /// <summary>
        /// Value in the latest.txt file, as a Local DateTime.
        /// </summary>
        public DateTime? LatestRecordedLocal => Fetcher.UtcToLocalDateTime(LatestRecordedUtc);
        /// <summary>
        /// Last modified timestamp on the latest.txt file, in UTC.
        /// </summary>
        public DateTime? LatestUploadedUtc { get; set; }
        /// <summary>
        /// Last modified timestamp on the latest.txt file, in local time.
        /// </summary>
        public DateTime? LatestUploadedLocal => Fetcher.UtcToLocalDateTime(LatestUploadedUtc);

        /// <summary>
        /// Last modified timestamp on the manifest file, in UTC.
        /// </summary>
        public DateTime? ManifestUpdatedUtc { get; set; }

        /// <summary>
        /// Last modified timestamp on the manifest file, in local time.
        /// </summary>
        public DateTime? ManifestUpdatedLocal => Fetcher.UtcToLocalDateTime(ManifestUpdatedUtc);
        /// <summary>
        /// Last time the S3 instance was queried, in UTC.
        /// </summary>
        public DateTime? LastCheckedUtc { get; set; }
        /// <summary>
        /// Last time the S3 instance was queried, in local time.
        /// </summary>
        public DateTime? LastCheckedLocal => Fetcher.UtcToLocalDateTime(LastCheckedUtc);
        /// <summary>
        /// The name of the node at Dataplicity.
        /// </summary>
        public string DataplicityName { get; set; }
        /// <summary>
        /// The description at Dataplicity.
        /// </summary>
        public string DataplicityDescription { get; set; }
        /// <summary>
        /// The agent version as reported by Dataplicity.
        /// </summary>
        public string AgentVersion { get; set; }
        /// <summary>
        /// The disk capacity as reported by Dataplicity.
        /// </summary>
        public long DiskCapacity { get; set; }
        /// <summary>
        /// The disk used value as reported by Dataplicity.
        /// </summary>
        public long DiskUsed { get; set; }
        /// <summary>
        /// The disk usage percentage.
        /// </summary>
        public long DiskUsagePercentage => (DiskCapacity > 0) ? (100 * DiskUsed / DiskCapacity) : 0;
        public long DiskUsedInGigs => DiskUsed / 1000000000;
        public long DiskCapacityInGigs => DiskCapacity / 1000000000;

        /// <summary>
        /// Whether Dataplicity believes the node is online.
        /// </summary>
        public bool? DataplicityOnline { get; set; }
        public bool? DataplicityUpgradeAvailable { get; set; }
        public OrcanodeUpgradeStatus DataplicityUpgradeStatus => DataplicityUpgradeAvailable ?? false ? OrcanodeUpgradeStatus.UpgradeAvailable : OrcanodeUpgradeStatus.UpToDate;
        public OrcanodeOnlineStatus DataplicityStatus => DataplicityOnline ?? false ? OrcanodeOnlineStatus.Online : OrcanodeOnlineStatus.Offline;
        public string OrcaHelloName
        {
            get
            {
                if (DisplayName == null) return string.Empty;

                // Any special cases here, since OrcaHello does not support
                // node enumeration, nor does it use the same names as
                // Dataplicity or Orcasound.net.
                if (DisplayName == "Orcasound Lab") return "Haro Strait";

                return DisplayName;
            }
        }
        public DateTime? LastOrcaHelloDetectionTimestamp { get; set; }
        public int? LastOrcaHelloDetectionConfidence { get; set; }
        public string LastOrcaHelloDetectionComments { get; set; }
        public bool? LastOrcaHelloDetectionFound { get; set; }

        private static OrcanodeOnlineStatus GetOrcasoundOnlineStatus(string slug, DateTime? manifestUpdatedUtc, DateTime? lastCheckedUtc)
        {
            if (slug.IsNullOrEmpty())
            {
                return OrcanodeOnlineStatus.Absent;
            }
            if (!manifestUpdatedUtc.HasValue || !lastCheckedUtc.HasValue)
            {
                return OrcanodeOnlineStatus.Offline;
            }
            TimeSpan manifestAge = lastCheckedUtc.Value.Subtract(manifestUpdatedUtc.Value);
            if (manifestAge > MaxUploadDelay)
            {
                return OrcanodeOnlineStatus.Offline;
            }
            return OrcanodeOnlineStatus.Online;
        }

        public OrcanodeOnlineStatus OrcasoundOnlineStatus => GetOrcasoundOnlineStatus(OrcasoundSlug, ManifestUpdatedUtc, LastCheckedUtc);
        public override string ToString() => DisplayName;
    }
}
