using System;
using OrcanodeMonitor.Models;

public class TestOffset
{
    public static void Main()
    {
        Console.WriteLine("Testing reboot offset functionality...");
        
        // Create a test node that would need a reboot.
        var node = new Orcanode
        {
            DataplicityOnline = true,
            LatestRecordedUtc = DateTime.UtcNow.AddMinutes(-10),
            ManifestUpdatedUtc = DateTime.UtcNow.AddMinutes(-10),
            LastCheckedUtc = DateTime.UtcNow
        };
        
        // Test default behavior (no offset).
        Environment.SetEnvironmentVariable("ORCASOUND_REBOOT_HOUR_OFFSET_MINUTES", null);
        Console.WriteLine($"Current UTC time: {DateTime.UtcNow}");
        Console.WriteLine($"Current hour: {DateTime.UtcNow.Hour}");
        Console.WriteLine($"Minutes past hour: {DateTime.UtcNow.Minute}");
        bool needsReboot1 = node.NeedsRebootForContainerRestart;
        Console.WriteLine($"Default (no offset): NeedsReboot = {needsReboot1}");
        
        // Test with 30-minute offset.
        Environment.SetEnvironmentVariable("ORCASOUND_REBOOT_HOUR_OFFSET_MINUTES", "30");
        bool needsReboot2 = node.NeedsRebootForContainerRestart;
        Console.WriteLine($"30-minute offset: NeedsReboot = {needsReboot2}");
        
        // Test with 45-minute offset.
        Environment.SetEnvironmentVariable("ORCASOUND_REBOOT_HOUR_OFFSET_MINUTES", "45");
        bool needsReboot3 = node.NeedsRebootForContainerRestart;
        Console.WriteLine($"45-minute offset: NeedsReboot = {needsReboot3}");
        
        Console.WriteLine("Test completed successfully!");
    }
}