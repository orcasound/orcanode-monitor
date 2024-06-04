namespace OrcanodeMonitor.Core
{
    public class Orcanode
    {
        public Orcanode(string name, string nodeName, string bucket)
        {
            Name = name;
            NodeName = nodeName;
            Bucket = bucket;
        }
        public string Name { get; private set; }
        public string NodeName { get; private set; }
        public string Bucket { get; private set; }
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
        public override string ToString() => Name;
    }
}
