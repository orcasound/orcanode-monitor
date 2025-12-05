// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using k8s.Models;

namespace OrcanodeMonitor.Models
{
    public class OrcaHelloContainer
    {
        private V1Pod _pod;
        public string PodName => _pod.Metadata?.Name ?? string.Empty;
        public string NodeName => _pod.Spec.NodeName;
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
        public OrcaHelloContainer(V1Pod pod)
        {
            _pod = pod;
        }
    }
}
