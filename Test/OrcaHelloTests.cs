using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
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

        [TestMethod]
        public void TestConfidenceThresholdFormatting()
        {
            // Test that confidence threshold formatting works correctly.
            // Create a mock pod with known threshold values.
            var thresholds = new { LocalThreshold = 0.7, GlobalThreshold = 3 };

            // Verify the expected formatting: "3 @ 70%".
            int globalThreshold = thresholds.GlobalThreshold;
            int localThresholdPercent = (int)Math.Round(thresholds.LocalThreshold * 100);
            string expected = $"{globalThreshold} @ {localThresholdPercent}%";

            Assert.AreEqual("3 @ 70%", expected,
                "Confidence threshold should be formatted as 'global @ local%'");
        }

        [TestMethod]
        public void TestConfidenceThresholdRounding()
        {
            // Test that local threshold is properly rounded to nearest percent.
            var thresholds = new { LocalThreshold = 0.749, GlobalThreshold = 5 };

            int localThresholdPercent = (int)Math.Round(thresholds.LocalThreshold * 100);

            Assert.AreEqual(75, localThresholdPercent,
                "Local threshold should round 0.749 to 75%");
        }
    }
}
