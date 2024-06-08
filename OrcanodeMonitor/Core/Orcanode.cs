// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using System.ComponentModel.DataAnnotations;

namespace OrcanodeMonitor.Core
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
                int maxUploadDelayMinutes = (int.TryParse(maxUploadDelayMinutesString, out var minutes)) ? minutes : _defaultMaxUploadDelayMinutes;
                return TimeSpan.FromMinutes(maxUploadDelayMinutes);
            }
        }
        public Orcanode(string orcasoundName, string s3nodeName, string s3bucket, string orcasoundSlug)
        {
            OrcasoundName = orcasoundName;

            // Convert an Orcasound name of "Beach Camp at Sunset Bay" to just "Sunset Bay".)
            string displayName = orcasoundName;
            int atIndex = orcasoundName.IndexOf(" at ");
            if (atIndex >= 0)
            {
                displayName = orcasoundName.Substring(atIndex + 4);
            }
            DisplayName = displayName;

            S3NodeName = s3nodeName;
            S3Bucket = s3bucket;
            OrcasoundSlug = orcasoundSlug;
        }
        public Orcanode(string dataplicityName)
        {
            DataplicityName = dataplicityName;

            string displayName = dataplicityName;
            int atIndex = dataplicityName.IndexOf(": ");
            if (atIndex >= 0)
            {
                displayName = dataplicityName.Substring(atIndex + 2);
            }
            DisplayName = displayName;
        }
        /// <summary>
        /// Human-readable name.
        /// </summary>
        public string DisplayName { get; private set; }
        /// <summary>
        /// Human-readable name at Orcasound.
        /// </summary>
        public string OrcasoundName { get; private set; }
        /// <summary>
        /// The URI path component from the "node_name" field obtained from orcasound.net.
        /// </summary>
        public string S3NodeName { get; private set; }
        /// <summary>
        /// The hostname component from the "bucket" field obtained from orcasound.net
        /// </summary>
        public string S3Bucket { get; private set; }
        /// <summary>
        /// The URI path component from the "slug" field obtained from orcasound.net.
        /// </summary>
        public string OrcasoundSlug { get; private set; }
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
        /// The id ("serial") at Dataplicity.
        /// </summary>
        public string DataplicityId { get; set; }
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
        public long DiskUsagePercentage => (100 * DiskUsed) / DiskCapacity;
        public long DiskUsedInGigs => DiskUsed / 1000000000;
        public long DiskCapacityInGigs => DiskCapacity / 1000000000;

        /// <summary>
        /// Whether Dataplicity believes the node is online.
        /// </summary>
        public bool? DataplicityOnline { get; set; }
        public bool? DataplicityUpgradeAvailable { get; set; }
        public OrcanodeUpgradeStatus DataplicityUpgradeStatus => (DataplicityUpgradeAvailable ?? false) ? OrcanodeUpgradeStatus.UpgradeAvailable : OrcanodeUpgradeStatus.UpToDate;
        public OrcanodeOnlineStatus DataplicityStatus => (DataplicityOnline ?? false) ? OrcanodeOnlineStatus.Online : OrcanodeOnlineStatus.Offline;
        public string? OrcaHelloName { get
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
        public string? LastOrcaHelloDetectionComments { get; set; }
        public bool? LastOrcaHelloDetectionFound { get; set; }
        public OrcanodeOnlineStatus OrcasoundOnlineStatus
        {
            get
            {
                if (OrcasoundSlug == null)
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
                return OrcanodeOnlineStatus.Online;
            }
        }
        public override string ToString() => DisplayName;
    }
}
