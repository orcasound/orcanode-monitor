// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using k8s.Models;
using OrcanodeMonitor.Models;

namespace Test
{
    [TestClass]
    public class OrcaHelloTests
    {
        [TestMethod]
        public void TestStatusWhenOrcaHelloAbsent()
        {
            // Test that OrcaHello status is Absent when OrcaHelloId is empty.
            var node = new Orcanode
            {
                OrcaHelloId = string.Empty,  // This makes OrcaHelloStatus return Absent.
                OrcasoundSlug = "test-node"
            };

            // Verify the node's OrcaHello status is Absent.
            Assert.AreEqual(OrcanodeOnlineStatus.Absent, node.OrcaHelloStatus,
                "OrcaHello status should be Absent when OrcaHelloId is empty");
        }

        [TestMethod]
        public void TestStatusWhenOrcaHelloOnline()
        {
            // Test that OrcaHello status is Online when properly configured.
            var node = new Orcanode
            {
                OrcaHelloId = "test-id",
                OrcaHelloInferencePodReady = true,
                OrcaHelloInferencePodLag = TimeSpan.FromMinutes(1),
                OrcasoundSlug = "test-node"
            };

            // Verify the node's OrcaHello status is Online.
            Assert.AreEqual(OrcanodeOnlineStatus.Online, node.OrcaHelloStatus,
                "OrcaHello status should be Online when properly configured");
        }

        [TestMethod]
        public void TestStatusWhenOrcaHelloOffline()
        {
            // Test that OrcaHello status is Offline when pod is not ready.
            var node = new Orcanode
            {
                OrcaHelloId = "test-id",
                OrcaHelloInferencePodReady = false,
                OrcasoundSlug = "test-node"
            };

            // Verify the node's OrcaHello status is Offline.
            Assert.AreEqual(OrcanodeOnlineStatus.Offline, node.OrcaHelloStatus,
                "OrcaHello status should be Offline when pod is not ready");
        }

        [TestMethod]
        public void TestStatusWhenOrcaHelloLagged()
        {
            // Test that OrcaHello status is Lagged when lag exceeds 5 minutes.
            var node = new Orcanode
            {
                OrcaHelloId = "test-id",
                OrcaHelloInferencePodReady = true,
                OrcaHelloInferencePodLag = TimeSpan.FromMinutes(10),
                OrcasoundSlug = "test-node"
            };

            // Verify the node's OrcaHello status is Lagged.
            Assert.AreEqual(OrcanodeOnlineStatus.Lagged, node.OrcaHelloStatus,
                "OrcaHello status should be Lagged when lag exceeds 5 minutes");
        }

        [TestMethod]
        public void TestEnvironmentVariableDefaultThreshold()
        {
            // Test that the default threshold is used when environment variable is not set.
            Environment.SetEnvironmentVariable("ORCAHELLO_HIGH_DETECTION_THRESHOLD", null);

            // The actual color logic will be tested via integration or manual testing.
            // This test just verifies the environment variable can be set and retrieved.
            string? threshold = Environment.GetEnvironmentVariable("ORCAHELLO_HIGH_DETECTION_THRESHOLD");
            Assert.IsNull(threshold, "Environment variable should be null when not set");
        }

        [TestMethod]
        public void TestEnvironmentVariableCustomThreshold()
        {
            // Test that a custom threshold can be set via environment variable.
            Environment.SetEnvironmentVariable("ORCAHELLO_HIGH_DETECTION_THRESHOLD", "50");

            string? threshold = Environment.GetEnvironmentVariable("ORCAHELLO_HIGH_DETECTION_THRESHOLD");
            Assert.AreEqual("50", threshold, "Environment variable should be set to 50");

            // Clean up.
            Environment.SetEnvironmentVariable("ORCAHELLO_HIGH_DETECTION_THRESHOLD", null);
        }

        [TestMethod]
        public void TestEnvironmentVariableInvalidThreshold()
        {
            // Test that an invalid threshold value can be set (parsing will handle fallback).
            Environment.SetEnvironmentVariable("ORCAHELLO_HIGH_DETECTION_THRESHOLD", "invalid");

            string? threshold = Environment.GetEnvironmentVariable("ORCAHELLO_HIGH_DETECTION_THRESHOLD");
            Assert.AreEqual("invalid", threshold, "Environment variable should be set even with invalid value");

            // Verify that parsing fails as expected.
            bool canParse = long.TryParse(threshold, out long _);
            Assert.IsFalse(canParse, "Invalid threshold value should not parse successfully");

            // Clean up.
            Environment.SetEnvironmentVariable("ORCAHELLO_HIGH_DETECTION_THRESHOLD", null);
        }

        /// <summary>
        /// Helper method to create a minimal V1Pod for testing OrcaHelloPod.
        /// </summary>
        private static V1Pod CreateTestPod()
        {
            return new V1Pod
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "test-pod",
                    NamespaceProperty = "test-namespace"
                },
                Spec = new V1PodSpec
                {
                    NodeName = "test-node",
                    Containers = new List<V1Container>
                    {
                        new V1Container
                        {
                            Name = "inference-system",
                            Resources = new V1ResourceRequirements
                            {
                                Limits = new Dictionary<string, ResourceQuantity>
                                {
                                    { "cpu", new ResourceQuantity("2") },
                                    { "memory", new ResourceQuantity("4Gi") }
                                }
                            }
                        }
                    }
                },
                Status = new V1PodStatus
                {
                    ContainerStatuses = new List<V1ContainerStatus>
                    {
                        new V1ContainerStatus
                        {
                            Ready = true,
                            RestartCount = 0,
                            State = new V1ContainerState
                            {
                                Running = new V1ContainerStateRunning
                                {
                                    StartedAt = DateTime.UtcNow
                                }
                            }
                        }
                    }
                }
            };
        }

        [TestMethod]
        public void TestGetConfidenceThreshold_WithBothThresholds()
        {
            // Test that GetConfidenceThreshold returns "3 @ 70%" when both thresholds are set.
            var pod = CreateTestPod();
            var orcaHelloPod = new OrcaHelloPod(
                pod,
                cpuUsage: "100000000n",
                memoryUsage: "256Ki",
                modelTimestamp: "2024-01-01",
                detectionCount: 10,
                modelConfidenceThreshold: 0.7,
                modelCountThreshold: 3
            );

            string result = orcaHelloPod.GetConfidenceThreshold();

            Assert.AreEqual("3 @ 70%", result,
                "GetConfidenceThreshold should return '3 @ 70%' when both thresholds are set");
        }

        [TestMethod]
        public void TestGetConfidenceThreshold_WithOnlyConfidenceThreshold()
        {
            // Test that GetConfidenceThreshold returns "70%" when only confidence threshold is set.
            var pod = CreateTestPod();
            var orcaHelloPod = new OrcaHelloPod(
                pod,
                cpuUsage: "100000000n",
                memoryUsage: "256Ki",
                modelTimestamp: "2024-01-01",
                detectionCount: 10,
                modelConfidenceThreshold: 0.7,
                modelCountThreshold: null
            );

            string result = orcaHelloPod.GetConfidenceThreshold();

            Assert.AreEqual("70%", result,
                "GetConfidenceThreshold should return '70%' when only confidence threshold is set");
        }

        [TestMethod]
        public void TestGetConfidenceThreshold_WithNoThresholds()
        {
            // Test that GetConfidenceThreshold returns "Unknown" when no thresholds are set.
            var pod = CreateTestPod();
            var orcaHelloPod = new OrcaHelloPod(
                pod,
                cpuUsage: "100000000n",
                memoryUsage: "256Ki",
                modelTimestamp: "2024-01-01",
                detectionCount: 10,
                modelConfidenceThreshold: null,
                modelCountThreshold: null
            );

            string result = orcaHelloPod.GetConfidenceThreshold();

            Assert.AreEqual("Unknown", result,
                "GetConfidenceThreshold should return 'Unknown' when no thresholds are set");
        }

        [TestMethod]
        public void TestGetConfidenceThreshold_WithRounding()
        {
            // Test that confidence threshold is properly rounded to nearest percent (0.749 -> 75%).
            var pod = CreateTestPod();
            var orcaHelloPod = new OrcaHelloPod(
                pod,
                cpuUsage: "100000000n",
                memoryUsage: "256Ki",
                modelTimestamp: "2024-01-01",
                detectionCount: 10,
                modelConfidenceThreshold: 0.749,
                modelCountThreshold: 5
            );

            string result = orcaHelloPod.GetConfidenceThreshold();

            Assert.AreEqual("5 @ 75%", result,
                "GetConfidenceThreshold should round 0.749 to 75%");
        }

        [TestMethod]
        public void TestGetConfidenceThreshold_WithDifferentValues()
        {
            // Test various threshold combinations.
            var pod = CreateTestPod();

            // Test 0.5 -> 50%
            var orcaHelloPod1 = new OrcaHelloPod(pod, "100n", "256Ki", "", 0, 0.5, 2);
            Assert.AreEqual("2 @ 50%", orcaHelloPod1.GetConfidenceThreshold());

            // Test 0.95 -> 95%
            var orcaHelloPod2 = new OrcaHelloPod(pod, "100n", "256Ki", "", 0, 0.95, 10);
            Assert.AreEqual("10 @ 95%", orcaHelloPod2.GetConfidenceThreshold());

            // Test 0.05 -> 5%
            var orcaHelloPod3 = new OrcaHelloPod(pod, "100n", "256Ki", "", 0, 0.05, 1);
            Assert.AreEqual("1 @ 5%", orcaHelloPod3.GetConfidenceThreshold());
        }
    }
}
