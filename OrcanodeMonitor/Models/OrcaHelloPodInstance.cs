// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using k8s.Models;
using OrcanodeMonitor.Core;

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

            // 2. Waiting state reason (e.g., CrashLoopBackOff, ImagePullBackOff).
            if (!string.IsNullOrEmpty(cs?.State?.Waiting?.Reason))
            {
                return cs.State.Waiting.Reason;
            }

            // 3. Terminated state reason (e.g., OOMKilled, Error).
            var term = cs?.State?.Terminated;
            if (term != null)
            {
                if (!string.IsNullOrEmpty(term.Reason))
                {
                    return term.Reason;
                }
                // Fall back to "Error" only if no reason is provided but exit code is non-zero.
                if (term.ExitCode != 0)
                {
                    return "Error";
                }
            }

            // 4. Pod-level reason (e.g., "Evicted").
            if (!string.IsNullOrEmpty(pod.Status.Reason))
            {
                return pod.Status.Reason;
            }

            // 5. Fall back to phase
            return pod.Status.Phase ?? "Unknown";
        }

        /// <summary>
        /// Short status shown in kubectl output. Returns special statuses for container errors 
        /// (ContainerStatusUnknown, Error), pod-level reasons (e.g., "Evicted"), or falls back to the Phase.
        /// </summary>
        public string Status => GetKubectlStatus(_pod);

        /// <summary>
        /// Message associated with the pod's current status, e.g., eviction reason or error message.
        /// Falls back to container-level messages when pod-level message is not available.
        /// </summary>
        public string Message
        {
            get
            {
                // Try pod-level message first.
                if (!string.IsNullOrEmpty(_pod.Status?.Message))
                {
                    return _pod.Status.Message;
                }

                // Fall back to container-level messages.
                var cs = _pod.Status?.ContainerStatuses?.FirstOrDefault();
                if (cs != null)
                {
                    // Check waiting state message.
                    if (!string.IsNullOrEmpty(cs.State?.Waiting?.Message))
                    {
                        return cs.State.Waiting.Message;
                    }

                    // Check terminated state message.
                    if (!string.IsNullOrEmpty(cs.State?.Terminated?.Message))
                    {
                        return cs.State.Terminated.Message;
                    }

                    // Check last terminated state message
                    if (!string.IsNullOrEmpty(cs.LastState?.Terminated?.Message))
                    {
                        return cs.LastState.Terminated.Message;
                    }
                }

                return string.Empty;
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

        /// <summary>
        /// Start time of the pod formatted in Pacific time (America/Los_Angeles), or "Unknown" if not available.
        /// For single-container pods, uses the container's start time if available; otherwise uses pod start time.
        /// </summary>
        public string StartTimePacific
        {
            get
            {
                DateTime? timeToFormat = null;

                // For single-container pods, prefer the container's start time.
                if (_pod.Spec?.Containers?.Count == 1)
                {
                    var cs = _pod.Status?.ContainerStatuses?.FirstOrDefault();
                    if (cs != null)
                    {
                        // Check current running container.
                        timeToFormat = cs.State?.Running?.StartedAt;

                        // Check current terminated container.
                        if (!timeToFormat.HasValue)
                        {
                            timeToFormat = cs.State?.Terminated?.StartedAt;
                        }

                        // Check last terminated state (useful for CrashLoopBackOff, waiting states).
                        if (!timeToFormat.HasValue)
                        {
                            timeToFormat = cs.LastState?.Terminated?.StartedAt;
                        }
                    }
                }

                // Fall back to pod start time if container time not available or multi-container pod.
                if (!timeToFormat.HasValue)
                {
                    timeToFormat = StartTime;
                }

                if (timeToFormat.HasValue)
                {
                    DateTime? pacificTime = Fetcher.UtcToLocalDateTime(timeToFormat.Value);
                    if (pacificTime.HasValue)
                    {
                        return pacificTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    }
                }
                return "Unknown";
            }
        }

        /// <summary>
        /// End time of the pod formatted in Pacific time (America/Los_Angeles), or "Unknown" if not available.
        /// For single-container pods, uses the container's termination time if available; otherwise returns "Unknown".
        /// </summary>
        public string EndTimePacific
        {
            get
            {
                DateTime? timeToFormat = null;

                // For single-container pods, prefer the container's termination time.
                if (_pod.Spec?.Containers?.Count == 1)
                {
                    var cs = _pod.Status?.ContainerStatuses?.FirstOrDefault();
                    timeToFormat = cs?.State?.Terminated?.FinishedAt ?? cs?.LastState?.Terminated?.FinishedAt;
                }

                if (timeToFormat.HasValue)
                {
                    DateTime? pacificTime = Fetcher.UtcToLocalDateTime(timeToFormat.Value);
                    if (pacificTime.HasValue)
                    {
                        return pacificTime.Value.ToString("yyyy-MM-dd HH:mm:ss");
                    }
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
