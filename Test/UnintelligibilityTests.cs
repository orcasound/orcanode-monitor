// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using System.Net.Http;
using System.Reflection;
using System.Xml.Linq;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Models;

namespace Test
{
    [TestClass]
    public class UnintelligibilityTests
    {
        private async Task TestSampleAsync(string filename, OrcanodeOnlineStatus expected_status, OrcanodeOnlineStatus? oldStatus = null)
        {
            // Get the current directory (where the test assembly is located)
            string currentDirectory = Directory.GetCurrentDirectory();

            // Navigate to the root of the repository
            string rootDirectory = Path.GetFullPath(Path.Combine(currentDirectory, @"..\..\..\..\"));

            string filePath = Path.Combine(rootDirectory, "Test\\samples", filename);
            try
            {
                OrcanodeOnlineStatus previousStatus = oldStatus ?? expected_status;
                FrequencyInfo frequencyInfo = await FfmpegCoreAnalyzer.AnalyzeFileAsync(filePath, previousStatus);
                OrcanodeOnlineStatus status = frequencyInfo.Status;
                Assert.IsTrue(status == expected_status);
            }
            catch (Exception ex)
            {
                // We couldn't fetch the stream audio so could not update the
                // audio standard deviation. Just ignore this for now.
                var msg = ex.ToString();
                Assert.Fail(msg);
            }
        }

        [TestMethod]
        public void TestHumFrequencies()
        {
            Assert.IsTrue(FrequencyInfo.IsHumFrequency(59.1));
            Assert.IsTrue(FrequencyInfo.IsHumFrequency(60.0));
            Assert.IsTrue(FrequencyInfo.IsHumFrequency(60.9));
            Assert.IsTrue(FrequencyInfo.IsHumFrequency(120.0));
            Assert.IsTrue(FrequencyInfo.IsHumFrequency(300.0));

            Assert.IsFalse(FrequencyInfo.IsHumFrequency(0.0));
            Assert.IsFalse(FrequencyInfo.IsHumFrequency(59.0));
            Assert.IsFalse(FrequencyInfo.IsHumFrequency(61.0));
            Assert.IsFalse(FrequencyInfo.IsHumFrequency(121.0));
            Assert.IsFalse(FrequencyInfo.IsHumFrequency(299.0));
        }

        [TestMethod]
        public async Task TestSilentSample()
        {
            await TestSampleAsync("unintelligible\\live1791.ts", OrcanodeOnlineStatus.Silent);
        }

        [TestMethod]
        public async Task TestUnintelligibleSample()
        {
            // Audio hums.
            await TestSampleAsync("unintelligible\\live4869.ts", OrcanodeOnlineStatus.Unintelligible);
            await TestSampleAsync("unintelligible\\live1816b.ts", OrcanodeOnlineStatus.Unintelligible);
            await TestSampleAsync("unintelligible\\live1815.ts", OrcanodeOnlineStatus.Unintelligible);
            await TestSampleAsync("unintelligible\\live1816.ts", OrcanodeOnlineStatus.Unintelligible);
            await TestSampleAsync("unintelligible\\live5936.ts", OrcanodeOnlineStatus.Unintelligible);
        }

        [TestMethod]
        public async Task TestNormalSample()
        {
            // 2-channel sample with one silent and one normal channel.
            await TestSampleAsync("normal\\live2008.ts", OrcanodeOnlineStatus.Online);

            await TestSampleAsync("normal\\live7372.ts", OrcanodeOnlineStatus.Online);
            await TestSampleAsync("normal\\live7793.ts", OrcanodeOnlineStatus.Online);
            await TestSampleAsync("normal\\live4118.ts", OrcanodeOnlineStatus.Online);
            await TestSampleAsync("normal\\live3368.ts", OrcanodeOnlineStatus.Online);

            await TestSampleAsync("normal\\live3504.ts", OrcanodeOnlineStatus.Online);
            await TestSampleAsync("normal\\live2649.ts", OrcanodeOnlineStatus.Online);
            await TestSampleAsync("normal\\live2289.ts", OrcanodeOnlineStatus.Online);
            await TestSampleAsync("normal\\live385.ts", OrcanodeOnlineStatus.Online);
            await TestSampleAsync("normal\\live839.ts", OrcanodeOnlineStatus.Online);
            await TestSampleAsync("normal\\live1184.ts", OrcanodeOnlineStatus.Online);
        }

        [TestMethod]
        public async Task TestHysteresisBehavior()
        {
            // Bush Point file from around 5pm 11/18/2024 is relatively quiet (max magnitude 17.46).
            // Test state retention when transitioning from Online to borderline Silent.
            await TestSampleAsync("normal/live6079.ts", OrcanodeOnlineStatus.Online, OrcanodeOnlineStatus.Online);

            // Test state retention when transitioning from Silent to borderline Online.
            await TestSampleAsync("normal/live6079.ts", OrcanodeOnlineStatus.Silent, OrcanodeOnlineStatus.Silent);

            // Test clear state changes (should override hysteresis).
            await TestSampleAsync("unintelligible/live4869.ts", OrcanodeOnlineStatus.Unintelligible, OrcanodeOnlineStatus.Online);
        }
    }
}