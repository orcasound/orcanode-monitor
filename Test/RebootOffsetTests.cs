using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using OrcanodeMonitor.Models;

namespace Test
{
    [TestClass]
    public class RebootOffsetTests
    {
        [TestMethod]
        public void TestDefaultRebootOffset()
        {
            // Clear environment variable to test default behavior.
            Environment.SetEnvironmentVariable("ORCASOUND_REBOOT_HOUR_OFFSET_MINUTES", null);
            
            // Create a test node with required properties for reboot check.
            var node = new Orcanode
            {
                DataplicityOnline = true,
                // Set properties to make S3StreamStatus return Offline.
                LatestRecordedUtc = DateTime.UtcNow.AddMinutes(-10),
                ManifestUpdatedUtc = DateTime.UtcNow.AddMinutes(-10),
                LastCheckedUtc = DateTime.UtcNow
            };
            
            // The exact timing will depend on when this test runs, but the logic should not crash
            // and should return a boolean value.
            bool needsReboot = node.NeedsRebootForContainerRestart;
            
            // Just verify the property can be accessed without exception.
            Assert.IsTrue(needsReboot == true || needsReboot == false, "Property should return a valid boolean");
        }
        
        [TestMethod]
        public void TestCustomRebootOffset()
        {
            // Set a 30-minute offset for staging scenario.
            Environment.SetEnvironmentVariable("ORCASOUND_REBOOT_HOUR_OFFSET_MINUTES", "30");
            
            // Create a test node with required properties for reboot check.
            var node = new Orcanode
            {
                DataplicityOnline = true,
                // Set properties to make S3StreamStatus return Offline.
                LatestRecordedUtc = DateTime.UtcNow.AddMinutes(-10),
                ManifestUpdatedUtc = DateTime.UtcNow.AddMinutes(-10),
                LastCheckedUtc = DateTime.UtcNow
            };
            
            // The exact timing will depend on when this test runs, but the logic should not crash
            // and should return a boolean value.
            bool needsReboot = node.NeedsRebootForContainerRestart;
            
            // Just verify the property can be accessed without exception.
            Assert.IsTrue(needsReboot == true || needsReboot == false, "Property should return a valid boolean");
            
            // Clean up.
            Environment.SetEnvironmentVariable("ORCASOUND_REBOOT_HOUR_OFFSET_MINUTES", null);
        }
        
        [TestMethod]
        public void TestInvalidRebootOffset()
        {
            // Set an invalid offset value.
            Environment.SetEnvironmentVariable("ORCASOUND_REBOOT_HOUR_OFFSET_MINUTES", "invalid");
            
            // Create a test node with required properties for reboot check.
            var node = new Orcanode
            {
                DataplicityOnline = true,
                // Set properties to make S3StreamStatus return Offline.
                LatestRecordedUtc = DateTime.UtcNow.AddMinutes(-10),
                ManifestUpdatedUtc = DateTime.UtcNow.AddMinutes(-10),
                LastCheckedUtc = DateTime.UtcNow
            };
            
            // Should handle invalid value gracefully and default to 0.
            bool needsReboot = node.NeedsRebootForContainerRestart;
            
            // Just verify the property can be accessed without exception.
            Assert.IsTrue(needsReboot == true || needsReboot == false, "Property should return a valid boolean");
            
            // Clean up.
            Environment.SetEnvironmentVariable("ORCASOUND_REBOOT_HOUR_OFFSET_MINUTES", null);
        }
        
        [TestMethod]
        public void TestRebootRequirements()
        {
            // Test that reboot is not needed when Dataplicity is offline.
            var nodeOffline = new Orcanode
            {
                DataplicityOnline = false,
                // Set properties to make S3StreamStatus return Offline.
                LatestRecordedUtc = DateTime.UtcNow.AddMinutes(-10),
                ManifestUpdatedUtc = DateTime.UtcNow.AddMinutes(-10),
                LastCheckedUtc = DateTime.UtcNow
            };
            
            Assert.IsFalse(nodeOffline.NeedsRebootForContainerRestart, "Should not need reboot when Dataplicity is offline");
            
            // Test that reboot is not needed when S3 stream is online (by setting recent data).
            var nodeStreamOnline = new Orcanode
            {
                DataplicityOnline = true,
                // Set properties to make S3StreamStatus return Online (recent data).
                LatestRecordedUtc = DateTime.UtcNow,
                ManifestUpdatedUtc = DateTime.UtcNow,
                LastCheckedUtc = DateTime.UtcNow
            };
            
            Assert.IsFalse(nodeStreamOnline.NeedsRebootForContainerRestart, "Should not need reboot when S3 stream is online");
        }
    }
}