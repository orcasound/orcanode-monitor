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

        /// <summary>
        /// Gets the name of the pod from its metadata.
        /// </summary>
        public string Name => _pod.Metadata?.Name ?? string.Empty;

        /// <summary>
        /// Returns a string representation of the pod instance using its name.
        /// </summary>
        /// <returns>The pod name</returns>
        public override string ToString() => this.Name;

        /// <summary>
        /// Kubernetes phase (Pending, Running, Succeeded, Failed, Unknown).
        /// </summary>
        public string Phase => _pod.Status?.Phase ?? "Unknown";

        /// <summary>
        /// Determines the kubectl-style status string for a pod, mimicking the logic used by kubectl.
        /// Checks for special conditions like ContainerStatusUnknown, non-zero exit codes, evictions,
        /// and falls back to the pod phase.
        /// </summary>
        /// <param name="pod">The Kubernetes pod to evaluate</param>
        /// <returns>A status string representing the pod's current state</returns>
        string GetKubectlStatus(V1Pod pod)
        {
            // Guard against null status
            if (pod.Status == null)
            {
                return "Unknown";
            }

            var cs = pod.Status.ContainerStatuses?.FirstOrDefault();

            // 1. ContainerStatusUnknown (waiting OR terminated)?
            if (cs?.State?.Waiting?.Reason == "ContainerStatusUnknown" ||
                cs?.State?.Terminated?.Reason == "ContainerStatusUnknown")
            {
                return "ContainerStatusUnknown";
            }

            // 2. Terminated with non-zero exit code -> Error.
            var term = cs?.State?.Terminated;
            if (term != null && term.ExitCode != 0)
            {
                return "Error";
            }

            // 3. Pod-level reason (e.g., "Evicted").
            if (!string.IsNullOrEmpty(pod.Status.Reason))
            {
                return pod.Status.Reason;
            }

            // 4. Fall back to phase
            return pod.Status.Phase ?? "Unknown";
        }

        /// <summary>
        /// Short status shown in kubectl output: the pod Reason (e.g., "Evicted") when set,
        /// otherwise the Phase.
        /// </summary>
        public string Status => GetKubectlStatus(_pod);

        /// <summary>
        /// Message associated with the pod's current status, e.g., eviction reason or error message.
        /// </summary>
        public string Message => _pod.Status?.Message ?? string.Empty;

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
        /// Start time of the pod (or creation timestamp as a fallback), used for sorting.
        /// </summary>
        public DateTime? StartTime => _pod.Status?.StartTime ?? _pod.Metadata?.CreationTimestamp;

        /// <summary>
        /// Age of the pod since its start time (or creation timestamp as a fallback).
        /// </summary>
        public string Age
        {
            get
            {
                if (StartTime.HasValue)
                {
                    TimeSpan age = DateTime.UtcNow - StartTime.Value;
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
