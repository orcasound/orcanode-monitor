// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using k8s;
using k8s.Models;

namespace OrcanodeMonitor.Models
{
    public class OrcaHelloNode
    {
        readonly V1Node _node;
        public string InstanceType { get; private set; }
        public double CpuUsageCores { get; private set; }
        public double CpuCapacityCores { get; private set; }
        public double CpuPercent => CpuCapacityCores > 0 ? (100.0 * CpuUsageCores / CpuCapacityCores) : 0;
        public long MemoryUsageInKi { get; private set; }
        public long MemoryCapacityInKi { get; private set; }
        public double MemoryPercent => MemoryCapacityInKi > 0 ? (100.0 * MemoryUsageInKi / MemoryCapacityInKi) : 0;
        public string CpuModel { get; private set; }
        public bool HasAvx2 { get; private set; }
        public bool HasAvx512 { get; private set; }
        public DateTime? CreationTimestamp => _node.Metadata.CreationTimestamp;
        public TimeSpan Uptime
        {
            get
            {
                if (CreationTimestamp.HasValue)
                {
                    return DateTime.UtcNow - CreationTimestamp.Value;
                }
                return TimeSpan.Zero;
            }
        }

        private static string GetLabelStringValue(IDictionary<string, string> labels, string key)
        {
            return labels.TryGetValue(key, out var value) ? value : "Unknown";
        }

        private static bool GetLabelBoolValue(IDictionary<string, string> labels, string key)
        {
            return labels.ContainsKey(key);
        }

        public OrcaHelloNode(V1Node node, string cpuUsage, string memoryUsage, string lscpuOutput)
        {
            _node = node;

            long nanocores = long.Parse(cpuUsage.Replace("n", ""));
            CpuUsageCores = nanocores / 1_000_000_000.0;
            CpuCapacityCores = node.Status.Allocatable["cpu"].ToDouble();

            if (!long.TryParse(memoryUsage.Replace("Ki", ""), out long memUsageKi))
            {
                memUsageKi = 0;
            }
            MemoryUsageInKi = memUsageKi;
            MemoryCapacityInKi = node.Status.Allocatable["memory"].ToInt64() / 1024;

            InstanceType = GetLabelStringValue(node.Metadata.Labels, "node.kubernetes.io/instance-type");

#if false
            // NFD would return labels but NFD consumes CPU and Memory which we want to keep for the AI use.
            CpuModel = GetLabelStringValue(node.Metadata.Labels, "feature.node.kubernetes.io/cpu-model");
            HasAvx2 = GetLabelBoolValue(node.Metadata.Labels, "feature.node.kubernetes.io/cpu-avx2");
            HasAvx512 = GetLabelBoolValue(node.Metadata.Labels, "feature.node.kubernetes.io/cpu-avx512");
#endif

            // Parse CPU model.
            CpuModel = "Unknown";
            foreach (var line in lscpuOutput.Split('\n'))
            {
                if (line.StartsWith("Model name"))
                {
                    var parts = line.Split(':');
                    if (parts.Length >= 2)
                    {
                        CpuModel = parts[1].Trim();
                    }
                }
            }

            // Check flags.
            HasAvx2 = lscpuOutput.Contains("avx2");
            HasAvx512 = lscpuOutput.Contains("avx512");
        }
    }
}
