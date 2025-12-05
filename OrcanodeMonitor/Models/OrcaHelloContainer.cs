// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using k8s.Models;

namespace OrcanodeMonitor.Models
{
    public class OrcaHelloContainer
    {
        private V1Pod _pod;
        public string PodName => _pod.Metadata?.Name ?? string.Empty;
        public string NamespaceName => _pod.Metadata?.NamespaceProperty ?? string.Empty;
        public string NodeName => _pod.Spec.NodeName;
        public double CpuUsageCores { get; set; }
        public double CpuCapacityCores { get; set; }
        public double CpuPercent => CpuUsageCores / CpuCapacityCores * 100.0;
        public long MemoryUsageInKi { get; set; }
        public long MemoryCapacityInKi { get; set; }
        public double MemoryPercent => 100.0 * MemoryUsageInKi / MemoryCapacityInKi;
        public string ImageName
        {
            get
            {
                // From the spec (desired state)
                foreach (var container in _pod.Spec.Containers)
                {
                    return container.Image;
                }
                return string.Empty;
            }
        }
        public OrcaHelloContainer(V1Pod pod, string cpuUsage, string memoryUsage)
        {
            _pod = pod;

            long nanocores = long.Parse(cpuUsage.Replace("n", ""));
            CpuUsageCores = nanocores / 1_000_000_000.0;

            MemoryUsageInKi = long.Parse(memoryUsage.Replace("Ki", ""));

            V1Container? container = pod.Spec.Containers.FirstOrDefault(c => c.Name == "inference-system");
            var limits = container?.Resources?.Limits;
            string memoryLimit = string.Empty;
            if (limits != null)
            {
                CpuCapacityCores = limits.ContainsKey("cpu") ? limits["cpu"].ToInt64() : 0;
                MemoryCapacityInKi = limits.ContainsKey("memory") ? limits["memory"].ToInt64() / 1024 : 0;
            }
        }
    }
}
