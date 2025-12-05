// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using k8s.Models;

namespace OrcanodeMonitor.Models
{
    public class OrcaHelloNode
    {
        V1Node _node;
        public double CpuUsageCores { get; set; }
        public double CpuCapacityCores { get; set; }
        public double CpuPercent => CpuUsageCores / CpuCapacityCores * 100.0;
        public long MemoryUsageInKi { get; set; }
        public long MemoryCapacityInKi { get; set; }
        public double MemoryPercent => 100.0 * MemoryUsageInKi / MemoryCapacityInKi;

        public OrcaHelloNode(V1Node node, string cpuUsed, string memoryUsed)
        {
            _node = node;

            long nanocores = long.Parse(cpuUsed.Replace("n", ""));
            CpuUsageCores = nanocores / 1_000_000_000.0;
            CpuCapacityCores = node.Status.Capacity["cpu"].ToDouble();

            MemoryUsageInKi = long.Parse(memoryUsed.Replace("Ki", ""));
            string memoryCapacity = node.Status.Capacity["memory"].ToString();
            MemoryCapacityInKi = long.Parse(memoryCapacity.Replace("Ki", ""));
        }
    }
}
