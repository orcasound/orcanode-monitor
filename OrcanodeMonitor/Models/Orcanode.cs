// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using OrcanodeMonitor.Core;

namespace OrcanodeMonitor.Models
{
    public enum OrcanodeOnlineStatus
    {
        Absent = 0,
        Offline,
        Online,
        Unintelligible
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
        const double _defaultMinIntelligibleStreamDeviation = 175;

        public Orcanode()
        {
            // Initialize reference types.
            OrcasoundName = string.Empty;
            OrcasoundSlug = string.Empty;
            S3Bucket = string.Empty;
            S3NodeName = string.Empty;
            AgentVersion = string.Empty;
            DataplicityDescription = string.Empty;
            DataplicityName = string.Empty;
            DataplicitySerial = string.Empty;
            DisplayName = string.Empty;
        }

        #region persisted
        // Persisted fields.  If any changes are made, the database must go through a migration.
        // See https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=vs
        // for more information.

        /// <summary>
        /// Database key field. This is NOT the dataplicity serial GUID, since a node might first be
        /// detected via another mechanism before we get the dataplicity serial GUID.
        /// </summary>
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int ID { get; set; }

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
        /// The "serial" at Dataplicity.
        /// </summary>
        public string DataplicitySerial { get; set; }

        /// <summary>
        /// Last modified timestamp on the latest.txt file, in UTC.
        /// </summary>
        public DateTime? LatestUploadedUtc { get; set; }

        /// <summary>
        /// Last modified timestamp on the manifest file, in UTC.
        /// </summary>
        public DateTime? ManifestUpdatedUtc { get; set; }

        /// <summary>
        /// Last time the S3 instance was queried, in UTC.
        /// </summary>
        public DateTime? LastCheckedUtc { get; set; }
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
        /// Whether Dataplicity believes the node is online.
        /// </summary>
        public bool? DataplicityOnline { get; set; }
        public bool? DataplicityUpgradeAvailable { get; set; }

        public double? AudioStandardDeviation { get; set; }

        #endregion persisted

        #region derived
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

        private static double MinIntelligibleStreamDeviation
        {
            get
            {
                string? minIntelligibleStreamDeviationString = Environment.GetEnvironmentVariable("ORCASOUND_MIN_INTELLIGIBLE_STREAM_DEVIATION");
                double minIntelligibleStreamDeviation = double.TryParse(minIntelligibleStreamDeviationString, out var deviation) ? deviation : _defaultMinIntelligibleStreamDeviation;
                return minIntelligibleStreamDeviation;
            }
        }

        /// <summary>
        /// Value in the latest.txt file, as a Local DateTime.
        /// </summary>
        public DateTime? LatestRecordedLocal => Fetcher.UtcToLocalDateTime(LatestRecordedUtc);

        /// <summary>
        /// Last modified timestamp on the latest.txt file, in local time.
        /// </summary>
        public DateTime? LatestUploadedLocal => Fetcher.UtcToLocalDateTime(LatestUploadedUtc);

        /// <summary>
        /// Last modified timestamp on the manifest file, in local time.
        /// </summary>
        public DateTime? ManifestUpdatedLocal => Fetcher.UtcToLocalDateTime(ManifestUpdatedUtc);

        /// <summary>
        /// Last time the S3 instance was queried, in local time.
        /// </summary>
        public DateTime? LastCheckedLocal => Fetcher.UtcToLocalDateTime(LastCheckedUtc);

        /// <summary>
        /// The disk usage percentage.
        /// </summary>
        public long DiskUsagePercentage => (DiskCapacity > 0) ? (100 * DiskUsed / DiskCapacity) : 0;
        public long DiskUsedInGigs => DiskUsed / 1000000000;
        public long DiskCapacityInGigs => DiskCapacity / 1000000000;

        public OrcanodeUpgradeStatus DataplicityUpgradeStatus => DataplicityUpgradeAvailable ?? false ? OrcanodeUpgradeStatus.UpgradeAvailable : OrcanodeUpgradeStatus.UpToDate;
        public OrcanodeOnlineStatus DataplicityConnectionStatus => DataplicityOnline ?? false ? OrcanodeOnlineStatus.Online : OrcanodeOnlineStatus.Offline;

#if ORCAHELLO
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
#endif
        public OrcanodeOnlineStatus OrcasoundStatus
        {
            get
            {
                if (OrcasoundSlug.IsNullOrEmpty())
                {
                    return OrcanodeOnlineStatus.Absent;
                }
                return OrcanodeOnlineStatus.Online;
            }
        }

        public OrcanodeOnlineStatus S3StreamStatus
        {
            get
            {
                if (S3NodeName.IsNullOrEmpty())
                {
                    return OrcanodeOnlineStatus.Absent;
                }
                if (!ManifestUpdatedUtc.HasValue || !LastCheckedUtc.HasValue)
                {
                    return OrcanodeOnlineStatus.Offline;
                }
                TimeSpan manifestAge = LastCheckedUtc.Value.Subtract(ManifestUpdatedUtc.Value);
                if (manifestAge > MaxUploadDelay)
                {
                    return OrcanodeOnlineStatus.Offline;
                }
                if (AudioStandardDeviation.HasValue && (AudioStandardDeviation < MinIntelligibleStreamDeviation))
                {
                    return OrcanodeOnlineStatus.Unintelligible;
                }
                return OrcanodeOnlineStatus.Online;
            }
        }

        public string OrcasoundOnlineStatusString {
            get
            {
                // Snapshot the status.
                OrcanodeOnlineStatus status = S3StreamStatus;

                // Convert to a display string.
                return (status == OrcanodeOnlineStatus.Online) ? "up" : S3StreamStatus.ToString().ToUpper();
            }
        }
        #endregion derived

        #region methods
        public OrcanodeIftttDTO ToIftttDTO() => new OrcanodeIftttDTO(ID, DisplayName);

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

        /// <summary>
        /// Derive a human-readable display name from a Dataplcity node name.
        /// </summary>
        /// <param name="dataplicityName">The node name at Dataplicity</param>
        /// <returns>Display name string</returns>
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
        /// Derive a default S3 node name from a Dataplicity node name.
        /// </summary>
        /// <param name="dataplicityName">The node name at Dataplicity</param>
        /// <returns>Default S3 node name</returns>
        public static string DataplicityNameToS3Name(string dataplicityName)
        {
            string s3Name = dataplicityName;
            int index = dataplicityName.IndexOf(": ");
            if (index >= 0)
            {
                s3Name = dataplicityName.Substring(index + 2);
            }
            s3Name = s3Name.ToLower().Replace(' ', '_');
            if (!s3Name.StartsWith("rpi_"))
            {
                s3Name = "rpi_" + s3Name;
            }
            return s3Name;
        }

        public override string ToString() => DisplayName;

        #endregion methods
    }
}
