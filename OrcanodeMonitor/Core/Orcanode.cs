// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
namespace OrcanodeMonitor.Core
{
    public enum OrcanodeStatus
    {
        Offline = 0,
        Online
    }
    public class Orcanode
    {
        /// <summary>
        /// If the manifest file is older than this, the node will be considered offline.
        /// TODO: allow max upload delay to be configurable.
        /// </summary>
        TimeSpan _maxUploadDelay = TimeSpan.FromMinutes(2);
        public Orcanode(string name, string nodeName, string bucket, string slug)
        {
            Name = name;
            NodeName = nodeName;
            Bucket = bucket;
            Slug = slug;
        }
        public string Name { get; private set; }
        public string NodeName { get; private set; }
        public string Bucket { get; private set; }
        public string Slug { get; private set; }
        /// <summary>
        /// Value in the latest.txt file, as a UTC DateTime.
        /// </summary>
        public DateTime? LatestRecorded { get; set; }
        /// <summary>
        /// Last modified timestamp on the latest.txt file, in UTC.
        /// </summary>
        public DateTime? LatestUploaded { get; set; }
        /// <summary>
        /// Last modified timestamp on the manifest file, in UTC.
        /// </summary>
        public DateTime? ManifestUpdated { get; set; }
        public OrcanodeStatus Status
        {
            get
            {
                if (!ManifestUpdated.HasValue)
                {
                    return OrcanodeStatus.Offline;
                }
                TimeSpan manifestAge = DateTime.Now.Subtract(ManifestUpdated.Value);
                if (manifestAge > _maxUploadDelay)
                {
                    return OrcanodeStatus.Offline;
                }
                return OrcanodeStatus.Online;
            }
        }
        public override string ToString() => Name;
    }
}
