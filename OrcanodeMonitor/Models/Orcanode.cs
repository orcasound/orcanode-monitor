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
        Unintelligible,
        Hidden,
        Unauthorized,
        NoView,
        Silent,
    }
    public enum OrcanodeUpgradeStatus
    {
        Unknown = 0,
        UpToDate = 1,
        UpgradeAvailable
    }

    public class OrcanodeIftttDTO
    {
        public OrcanodeIftttDTO(string id, string displayName)
        {
            ID = id;
            DisplayName = displayName;
        }
        [JsonPropertyName("ID")]
        public string ID { get; private set; }
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
            ID = string.Empty;
            OrcasoundName = string.Empty;
            OrcasoundSlug = string.Empty;
            OrcasoundFeedId = string.Empty;
            OrcasoundHost = string.Empty;
            S3Bucket = string.Empty;
            S3NodeName = string.Empty;
            AgentVersion = string.Empty;
            DataplicityDescription = string.Empty;
            DataplicityName = string.Empty;
            DataplicitySerial = string.Empty;
            OrcaHelloId = string.Empty;
            PartitionValue = 1;
            MezmoLogSize = 0;
            MezmoViewId = string.Empty;
            DecibelLevel = 0;
            HumDecibelLevel = 0;
        }

        #region persisted
        // Persisted fields.  If any changes are made, the database must go through a migration.
        // See https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/?tabs=vs
        // for more information.  For example, if adding a field called FooBar, then
        // from Package Manager Console do:
        // * Add-Migration AddFooBar
        //
        // When ready to deploy to an Azure SQL database:
        // 1. Stop the remote service.
        // 2. Apply the migration from a developer command shell:
        //     dotnet ef database update --connection "Server=tcp:orcasound-server.database.windows.net,1433;Initial Catalog=OrcasoundFreeDatabase;Encrypt=True;TrustServerCertificate=False;Connection Timeout=30;Authentication=\"Active Directory Default\";Pooling=False;"
        // 3. Publish from Visual Studio using the appropriate publish profile.
        // 4. Start the remote service.

        /// <summary>
        /// Database key field. This is NOT the dataplicity serial GUID, since a node might first be
        /// detected via another mechanism before we get the dataplicity serial GUID.  Nor is it
        /// the Orcasound feed id, since a node is typically detected by dataplicity first when
        /// no Orcasound feed id exists.
        /// </summary>

        public string ID { get; set; }

        /// <summary>
        /// Human-readable name at Orcasound.
        /// </summary>
        public string OrcasoundName { get; set; }

        /// <summary>
        /// The "id" field from the Orcasound feeds API.
        /// </summary>
        public string OrcasoundFeedId { get; set; }

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
        /// Value in the S3 latest.txt file, as a UTC DateTime.
        /// </summary>
        public DateTime? LatestRecordedUtc { get; set; }

        /// <summary>
        /// The "serial" at Dataplicity.
        /// </summary>
        public string DataplicitySerial { get; set; }

        /// <summary>
        /// Last modified timestamp on the S3 latest.txt file, in UTC.
        /// </summary>
        public DateTime? LatestUploadedUtc { get; set; }

        /// <summary>
        /// Last modified timestamp on the S3 manifest file, in UTC.
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

        /// <summary>
        /// Measure of real volume.  Negative infinity is absolute silence.
        /// </summary>
        public double? DecibelLevel { get; set; }

        public string RealDecibelLevelForDisplay {
            get
            {
                if (DecibelLevel == null || DecibelLevel == double.NegativeInfinity)
                {
                    return "N/A";
                }
                return ((int)Math.Round(DecibelLevel ?? double.NegativeInfinity)).ToString();
            }
        }

        /// <summary>
        /// Measure of hum volume. Negative infinity is absolute silence.
        /// </summary>
        public double? HumDecibelLevel { get; set; }

        public string HumDecibelLevelForDisplay
        {
            get
            {
                if (HumDecibelLevel == null || HumDecibelLevel == double.NegativeInfinity)
                {
                    return "N/A";
                }
                return ((int)Math.Round(HumDecibelLevel ?? double.NegativeInfinity)).ToString();
            }
        }

        /// <summary>
        /// Whether the node is visible on the orcasound website.
        /// </summary>
        public bool? OrcasoundVisible { get; set; }

        /// <summary>
        /// The "id" field from the OrcaHello hydrophones API.
        /// </summary>
        public string OrcaHelloId { get; set; }

        /// <summary>
        /// Partition key fixed value.
        /// </summary>
        public int PartitionValue { get; set; }

        /// <summary>
        /// Audio stream status of most recent sample (defaults to absent).
        /// </summary>
        public OrcanodeOnlineStatus? AudioStreamStatus { get; set;  }

        /// <summary>
        /// Orcasound site host (defaults to empty).
        /// </summary>
        public string OrcasoundHost { get; set; }

        /// <summary>
        /// Mezmo view ID.
        /// </summary>
        public string MezmoViewId { get; set; }

        /// <summary>
        /// Mezmo log size.
        /// </summary>
        public int? MezmoLogSize { get; set; }

        #endregion persisted

        #region derived


        /// <summary>
        /// Mezmo status of the node.
        /// </summary>
        public OrcanodeOnlineStatus MezmoStatus
        {
            get
            {
                if (!this.MezmoLogSize.HasValue)
                {
                    return OrcanodeOnlineStatus.Absent;
                }
                if (this.MezmoLogSize.Value == 0)
                {
                    // No recent log entries, so the logger must be offline.
                    return OrcanodeOnlineStatus.Offline;
                }
                if (this.MezmoViewId.IsNullOrEmpty())
                {
                    return OrcanodeOnlineStatus.NoView;
                }
                return OrcanodeOnlineStatus.Online;
            }
        }

        public string DisplayName
        {
            get
            {
                if (!this.OrcasoundName.IsNullOrEmpty())
                {
                    return this.OrcasoundName;
                }
                return Orcanode.DataplicityNameToDisplayName(this.DataplicityName);
            }
        }

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
        public OrcanodeOnlineStatus DataplicityConnectionStatus {
            get
            {
                if (!DataplicityOnline.HasValue)
                {
                    return OrcanodeOnlineStatus.Absent;
                }
                return (DataplicityOnline.Value) ? OrcanodeOnlineStatus.Online : OrcanodeOnlineStatus.Offline;
            }
        }

        public OrcanodeOnlineStatus OrcaHelloStatus => OrcaHelloId.IsNullOrEmpty() ? OrcanodeOnlineStatus.Absent : OrcanodeOnlineStatus.Online;

        public OrcanodeOnlineStatus OrcasoundStatus
        {
            get
            {
                if (OrcasoundSlug.IsNullOrEmpty())
                {
                    return OrcanodeOnlineStatus.Absent;
                }
                if (!(OrcasoundVisible ?? true))
                {
                    return OrcanodeOnlineStatus.Hidden;
                }
                return OrcanodeOnlineStatus.Online;
            }
        }

        public OrcanodeOnlineStatus S3StreamStatus
        {
            get
            {
                if (!LatestRecordedUtc.HasValue)
                {
                    return OrcanodeOnlineStatus.Absent;
                }
                if (LatestRecordedUtc == DateTime.MinValue)
                {
                    return OrcanodeOnlineStatus.Unauthorized;
                }
                if (!ManifestUpdatedUtc.HasValue || !LastCheckedUtc.HasValue)
                {
                    return OrcanodeOnlineStatus.Absent;
                }
                TimeSpan manifestAge = LastCheckedUtc.Value.Subtract(ManifestUpdatedUtc.Value);
                if (manifestAge > MaxUploadDelay)
                {
                    return OrcanodeOnlineStatus.Offline;
                }

                if (AudioStreamStatus == OrcanodeOnlineStatus.Absent && AudioStandardDeviation != 0.0)
                {
                    AudioStreamStatus = OrcanodeOnlineStatus.Online;
                }

                return AudioStreamStatus ?? OrcanodeOnlineStatus.Absent;
            }
        }

        public string Type
        {
            get
            {
                if (this.S3Bucket.StartsWith("dev"))
                {
                    return "Dev";
                }
                if (this.DataplicityName.StartsWith("live", StringComparison.OrdinalIgnoreCase))
                {
                    return "Live";
                }
                if (this.DataplicityName.StartsWith("dev", StringComparison.OrdinalIgnoreCase))
                {
                    return "Dev";
                }
                if (this.DataplicityName.StartsWith("beta", StringComparison.OrdinalIgnoreCase))
                {
                    return "Beta";
                }
                return "Unknown";
            }
        }

        public static string OnlineString => "up";

        // Convert to a display string.
        public static string GetStatusString(OrcanodeOnlineStatus status) => (status == OrcanodeOnlineStatus.Online) ? OnlineString : status.ToString().ToUpper();

        public string OrcasoundOnlineStatusString {
            get
            {
                // Snapshot the status.
                OrcanodeOnlineStatus status = S3StreamStatus;

                // Convert to a display string.
                return GetStatusString(status);
            }
        }

        public bool NeedsRebootForContainerRestart
        {
            get
            {
                if (!(DataplicityOnline ?? false))
                {
                    // Dataplicity must be online.
                    return false;
                }
                if (this.S3StreamStatus != OrcanodeOnlineStatus.Offline)
                {
                    // S3 Stream status must be offline.
                    return false;
                }
                TimeSpan interval = PeriodicTasks.FrequencyToPoll;
                DateTime utcNow = DateTime.UtcNow;
                
                // Get the configured hour offset for reboot timing
                string? hourOffsetString = Environment.GetEnvironmentVariable("ORCASOUND_REBOOT_HOUR_OFFSET_MINUTES");
                int hourOffsetMinutes = int.TryParse(hourOffsetString, out var offset) ? offset : 0;
                
                // Calculate time since the configured offset within the hour
                DateTime hourWithOffset = utcNow.Date.AddHours(utcNow.Hour).AddMinutes(hourOffsetMinutes);
                TimeSpan sinceOffsetTime = utcNow - hourWithOffset;
                
                // Handle case where offset pushes us to next hour
                if (sinceOffsetTime < TimeSpan.Zero)
                {
                    sinceOffsetTime = sinceOffsetTime.Add(TimeSpan.FromHours(1));
                }
                
                if (sinceOffsetTime >= interval)
                {
                    // Only reboot within the first polling interval after the offset time.
                    // This is so we only try a reboot at most once per hour.
                    return false;
                }
                return true;
            }
        }
        #endregion derived

        #region methods

        public OrcanodeIftttDTO ToIftttDTO() => new OrcanodeIftttDTO(ID, DisplayName);

        private static bool IsStateOnline(string state) => (state == "up" || state == "Online");

        /// <summary>
        /// Calculates the uptime percentage for a node based on its events since a specified date.
        /// Only events matching the specified type are considered when calculating uptime.
        /// The uptime is calculated based on the online/offline state indicated by these events.
        /// </summary>
        /// <param name="orcanodeId">The ID of the node to calculate uptime for</param>
        /// <param name="events">List of node events</param>
        /// <param name="since">The start date for uptime calculation</param>
        /// <param name="type">The specific event type to calculate uptime for (e.g., "hydrophone stream")</param>
        /// <returns>Uptime percentage as an integer between 0 and 100</returns>
        /// <exception cref="ArgumentException">Thrown when orcanodeId is null or empty</exception>
        public static int GetUptimePercentage(string orcanodeId, List<OrcanodeEvent> events, DateTime since, string type)
        {
            if (string.IsNullOrEmpty(orcanodeId))
            {
                throw new ArgumentException("Node ID cannot be null or empty", nameof(orcanodeId));
            }
            if (since > DateTime.UtcNow)
            {
                throw new ArgumentException("Start date cannot be in the future", nameof(since));
            }
            if (events == null)
            {
                return 0;
            }

            TimeSpan up = TimeSpan.Zero;
            TimeSpan down = TimeSpan.Zero;
            DateTime start = since;
            string lastValue = string.Empty;

            // Get events sorted by date to ensure correct chronological processing.
            var nodeEvents = events
                   .Where(e => e.OrcanodeId == orcanodeId && e.Type == type)
                   .OrderBy(e => e.DateTimeUtc)
                   .ToList();

            // Compute uptime percentage by looking at OrcanodeEvents over the past week.
            foreach (OrcanodeEvent e in nodeEvents)
            {
                if (e.DateTimeUtc <= since)
                {
                    // Event is too old.
                    lastValue = e.Value;
                    continue;
                }
                DateTime current = e.DateTimeUtc;

                // When collecting "all" events, we only want to count time
                // starting with the first event for the node, not across all
                // time or we'd end up with about 0% uptime.
                // For other time ranges, use the full week or month.
                if (start > DateTime.MinValue)
                {
                    if (IsStateOnline(lastValue))
                    {
                        up += (current - start);
                    }
                    else
                    {
                        down += (current - start);
                    }
                }
                start = current;
                lastValue = e.Value;
            }

            // Account for the reminder of the time until now.
            DateTime now = DateTime.UtcNow;
            if (IsStateOnline(lastValue))
            {
                up += now - start;
            }
            else
            {
                down += now - start;
            }

            TimeSpan totalTime = up + down;
            if (totalTime == TimeSpan.Zero)
            {
                return 0;
            }
            int percentage = (int)((100.0 * up) / totalTime + 0.5);
            return percentage;
        }

        /// <summary>
        /// Derive a human-readable display name from a Dataplicity node name.
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
