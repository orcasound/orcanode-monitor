// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using k8s;
using k8s.Models;

namespace OrcanodeMonitor.Models
{
    public class OrcaHelloNode
    {
        readonly V1Node _node;

        /// <summary>
        /// VMSS node name.
        /// </summary>
        public string Name => _node.Metadata.Name;
        private readonly List<OrcaHelloPod> _pods = new List<OrcaHelloPod>();
        public List<OrcaHelloPod> Pods => _pods;

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
        public DateTime? CreationTimestamp => _node.Metadata?.CreationTimestamp;
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

        public OrcaHelloNode(V1Node node, string cpuUsage, string memoryUsage, string lscpuOutput, IEnumerable<V1Pod> v1Pods, IEnumerable<PodMetrics> podMetricsList)
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
            MemoryCapacityInKi = node.Status?.Allocatable?.TryGetValue("memory", out var memAlloc) == true
                ? memAlloc.ToInt64() / 1024
                : 0;

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

            foreach (V1Pod pod in v1Pods)
            {
                if (pod.Spec?.NodeName != Name)
                {
                    continue;
                }

                // Skip terminated pods (Succeeded/Failed).
                if (pod.Status?.Phase == "Succeeded" || pod.Status?.Phase == "Failed")
                {
                    continue;
                }

                PodMetrics? podMetrics = podMetricsList.Where(pm => pm.Metadata.Name == pod.Metadata.Name).FirstOrDefault();
                var container = podMetrics?.Containers.FirstOrDefault(c => c.Name == "inference-system");
                string cpuUsagePod = container?.Usage?.TryGetValue("cpu", out var cpu) == true ? cpu.ToString() : "0n";
                string memoryUsagePod = container?.Usage?.TryGetValue("memory", out var mem) == true ? mem.ToString() : "0Ki";

                OrcaHelloPod orcaPod = new OrcaHelloPod(pod, cpuUsagePod, memoryUsagePod, string.Empty, 0);
                _pods.Add(orcaPod);
            }
        }
    }
}
