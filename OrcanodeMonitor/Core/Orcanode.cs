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
        public DateTime? LatestRecorded { get; set; }
        public DateTime? LatestUploaded { get; set; }
        public DateTime? ManifestUpdated { get; set; }
        public override string ToString() => Name;
    }
}
