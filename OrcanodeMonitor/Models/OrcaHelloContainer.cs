// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using k8s.Models;

namespace OrcanodeMonitor.Models
{
    public class OrcaHelloContainer
    {
        private readonly V1Pod _pod;
        public string PodName => _pod.Metadata?.Name ?? string.Empty;
        public string NamespaceName => _pod.Metadata?.NamespaceProperty ?? string.Empty;
        public string NodeName => _pod.Spec?.NodeName ?? string.Empty;
        public string LastTerminationReason { get; private set; }
        public double CpuUsageCores { get; private set; }
        public double CpuCapacityCores { get; private set; }
        public double CpuPercent => CpuUsageCores / CpuCapacityCores * 100.0;
        public long MemoryUsageInKi { get; private set; }
        public long MemoryCapacityInKi { get; private set; }
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
            if (limits != null)
            {
                CpuCapacityCores = limits.ContainsKey("cpu") ? limits["cpu"].ToInt64() : 0;
                MemoryCapacityInKi = limits.ContainsKey("memory") ? limits["memory"].ToInt64() / 1024 : 0;
            }

            var latest = pod.Status?.ContainerStatuses?
                .Select(cs => new {
                    Status = cs,
                    StartedAt = cs.State?.Running?.StartedAt ?? cs.LastState?.Terminated?.StartedAt
                })
                .Where(x => x.StartedAt != null)
                .OrderByDescending(x => x.StartedAt)
                .FirstOrDefault();

            LastTerminationReason = latest?.Status.LastState?.Terminated?.Reason ?? "Unknown";
        }
    }
}
