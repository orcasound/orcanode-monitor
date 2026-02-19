// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using k8s.Models;

namespace OrcanodeMonitor.Models
{
    /// <summary>
    /// Lightweight summary of a Kubernetes pod (e.g., Pending, Evicted, Failed) used to
    /// populate the "Other Pods" table on the OrcaHelloPod page.
    /// </summary>
    public class OrcaHelloPodInstance
    {
        private readonly V1Pod _pod;

        public string Name => _pod.Metadata?.Name ?? string.Empty;

        /// <summary>
        /// Kubernetes phase (Pending, Running, Succeeded, Failed, Unknown).
        /// </summary>
        public string Phase => _pod.Status?.Phase ?? "Unknown";

        /// <summary>
        /// Short status shown in kubectl output: the pod Reason (e.g. "Evicted") when set,
        /// otherwise the Phase.
        /// </summary>
        public string Status
        {
            get
            {
                string? reason = _pod.Status?.Reason;
                return !string.IsNullOrEmpty(reason) ? reason : Phase;
            }
        }

        /// <summary>
        /// "ready/total" container count, e.g., "0/1".
        /// </summary>
        public string Ready
        {
            get
            {
                int total = _pod.Spec?.Containers?.Count ?? 0;
                int ready = _pod.Status?.ContainerStatuses?.Count(cs => cs.Ready) ?? 0;
                return $"{ready}/{total}";
            }
        }

        /// <summary>
        /// Total restart count across all containers.
        /// </summary>
        public int RestartCount =>
            _pod.Status?.ContainerStatuses?.Sum(cs => cs.RestartCount) ?? 0;

        /// <summary>
        /// Age of the pod since its start time (or creation timestamp as a fallback).
        /// </summary>
        public string Age
        {
            get
            {
                DateTime? startTime = _pod.Status?.StartTime ?? _pod.Metadata?.CreationTimestamp;
                if (startTime.HasValue)
                {
                    TimeSpan age = DateTime.UtcNow - startTime.Value;
                    return Orcanode.FormatTimeSpan(age);
                }
                return "Unknown";
            }
        }

        public OrcaHelloPodInstance(V1Pod pod)
        {
            _pod = pod;
        }
    }
}
