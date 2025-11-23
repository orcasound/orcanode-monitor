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

        [DataTestMethod]
        [DataRow(59.0)]
        [DataRow(60.0)]
        [DataRow(61.0)]
        [DataRow(120.0)]
        [DataRow(300.0)]
        public void TestHumFrequencies_Hum(double freq)
        {
            Assert.IsTrue(FrequencyInfo.IsHumFrequency(freq));
        }

        [DataTestMethod]
        [DataRow(0.0)]
        [DataRow(58.9)]
        [DataRow(61.1)]
        [DataRow(121.1)]
        [DataRow(298.9)]
        public void TestHumFrequencies_NonHum(double freq)
        {
            Assert.IsFalse(FrequencyInfo.IsHumFrequency(freq));
        }

        [TestMethod]
        public async Task TestSilentSample()
        {
            await TestSampleAsync("unintelligible\\live1791.ts", OrcanodeOnlineStatus.Silent);
        }

        // Audio hums.
        [TestMethod]
        [Ignore("Hum detection disabled - see issue #434")]
        public async Task TestUnintelligibleSample4869()
        {
            await TestSampleAsync("unintelligible\\live4869.ts", OrcanodeOnlineStatus.Unintelligible);
        }

        [TestMethod]
        [Ignore("Hum detection disabled - see issue #434")]
        public async Task TestUnintelligibleSample1816b()
        {
            await TestSampleAsync("unintelligible\\live1816b.ts", OrcanodeOnlineStatus.Unintelligible);
        }

        [TestMethod]
        [Ignore("Hum detection disabled - see issue #434")]
        public async Task TestUnintelligibleSample1815()
        {
            await TestSampleAsync("unintelligible\\live1815.ts", OrcanodeOnlineStatus.Unintelligible);
        }

        [TestMethod]
        [Ignore("Hum detection disabled - see issue #434")]
        public async Task TestUnintelligibleSample1816()
        {
            await TestSampleAsync("unintelligible\\live1816.ts", OrcanodeOnlineStatus.Unintelligible);
        }

        [TestMethod]
        [Ignore("Hum detection disabled - see issue #434")]
        public async Task TestUnintelligibleSample5936()
        {
            await TestSampleAsync("unintelligible\\live5936.ts", OrcanodeOnlineStatus.Unintelligible);
        }

        [TestMethod]
        public async Task TestNormalSample()
        {
            await TestSampleAsync("normal\\live7648.ts", OrcanodeOnlineStatus.Online);

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
        public async Task TestHysteresisBehavior1()
        {
            // Bush Point file from around 5pm 11/18/2024 is relatively quiet (max magnitude 17.46).
            // Test state retention when transitioning from Online to borderline Silent.
            await TestSampleAsync("normal/live6079.ts", OrcanodeOnlineStatus.Online, OrcanodeOnlineStatus.Online);
        }

        [TestMethod]
        public async Task TestHysteresisBehavior2()
        {
            // Test state retention when transitioning from Silent to borderline Online.
            await TestSampleAsync("normal/live6079.ts", OrcanodeOnlineStatus.Silent, OrcanodeOnlineStatus.Silent);
        }

        [TestMethod]
        [Ignore("Hum detection disabled - see issue #434")]
        public async Task TestHysteresisBehavior3()
        {
            // Test clear state changes (should override hysteresis).
            await TestSampleAsync("unintelligible/live4869.ts", OrcanodeOnlineStatus.Unintelligible, OrcanodeOnlineStatus.Online);
        }
    }
}