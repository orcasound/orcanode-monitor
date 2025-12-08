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
        public string ModelTimestamp { get; private set; }
        public double CpuUsageCores { get; private set; }
        public double CpuCapacityCores { get; private set; }
        public double CpuPercent => CpuCapacityCores > 0 ? (100.0 * CpuUsageCores / CpuCapacityCores) : 0;
        public long MemoryUsageInKi { get; private set; }
        public long MemoryCapacityInKi { get; private set; }

        /// <summary>
        /// Number of times this container has been restarted.
        /// </summary>
        public long RestartCount { get; private set; }

        /// <summary>
        /// Number of detections in the past week.
        /// </summary>
        public long DetectionCount { get; private set; }

        public double MemoryPercent => MemoryCapacityInKi > 0 ? (100.0 * MemoryUsageInKi / MemoryCapacityInKi) : 0;
        public string MemoryUsage => $"{(MemoryUsageInKi / 1024f / 1024f):F1} GiB";
        public string MemoryCapacity => $"{(MemoryCapacityInKi / 1024f / 1024f):F1} GiB";

        /// <summary>
        /// Get the image name not including the "orcaconservancy.io/" prefix.
        /// </summary>
        public string ImageName
        {
            get
            {
                string imageName = _pod.Spec?.Containers?.FirstOrDefault()?.Image ?? string.Empty;
                int index = imageName.IndexOf('/');
                return (index >= 0) ? imageName.Substring(index + 1) : imageName;
            }
        }

        public OrcaHelloContainer(V1Pod pod, string cpuUsage, string memoryUsage, string modelTimestamp, long detectionCount)
        {
            _pod = pod;
            ModelTimestamp = modelTimestamp;
            DetectionCount = detectionCount;

            long nanocores = long.Parse(cpuUsage.Replace("n", ""));
            CpuUsageCores = nanocores / 1_000_000_000.0;

            MemoryUsageInKi = long.Parse(memoryUsage.Replace("Ki", ""));

            V1Container? container = pod.Spec.Containers.FirstOrDefault(c => c.Name == "inference-system");
            var limits = container?.Resources?.Limits;
            if (limits != null)
            {
                CpuCapacityCores = (limits.TryGetValue("cpu", out var cpuValue)) ? cpuValue.ToInt64() : 0;
                MemoryCapacityInKi = (limits.TryGetValue("memory", out var memoryValue)) ? (memoryValue.ToInt64() / 1024) : 0;
            }

            var latest = pod.Status?.ContainerStatuses?
                .Select(cs => new
                {
                    Status = cs,
                    StartedAt = cs.State?.Running?.StartedAt ?? cs.LastState?.Terminated?.StartedAt
                })
                .Where(x => x.StartedAt != null)
                .OrderByDescending(x => x.StartedAt)
                .FirstOrDefault();

            LastTerminationReason = latest?.Status.LastState?.Terminated?.Reason ?? string.Empty;
            RestartCount = latest?.Status.RestartCount ?? 0;
        }
    }
}
