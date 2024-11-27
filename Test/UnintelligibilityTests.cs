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
        private async Task TestSampleAsync(string filename, OrcanodeOnlineStatus expected_status)
        {
            // Get the current directory (where the test assembly is located)
            string currentDirectory = Directory.GetCurrentDirectory();

            // Navigate to the root of the repository
            string rootDirectory = Path.GetFullPath(Path.Combine(currentDirectory, @"..\..\..\..\"));

            string filePath = Path.Combine(rootDirectory, "Test\\samples", filename);
            try
            {
                OrcanodeOnlineStatus status = await FfmpegCoreAnalyzer.AnalyzeFileAsync(filePath);
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
        public async Task TestUnintelligibleSample()
        {
            await TestSampleAsync("unintelligible\\live4869.ts", OrcanodeOnlineStatus.Unintelligible);
            await TestSampleAsync("unintelligible\\live1816b.ts", OrcanodeOnlineStatus.Unintelligible);
            await TestSampleAsync("unintelligible\\live1791.ts", OrcanodeOnlineStatus.Unintelligible);
            await TestSampleAsync("unintelligible\\live1815.ts", OrcanodeOnlineStatus.Unintelligible);
            await TestSampleAsync("unintelligible\\live1816.ts", OrcanodeOnlineStatus.Unintelligible);
        }

        [TestMethod]
        public async Task TestNormalSample()
        {
            await TestSampleAsync("normal\\live3504.ts", OrcanodeOnlineStatus.Online);
            await TestSampleAsync("normal\\live2649.ts", OrcanodeOnlineStatus.Online);
            await TestSampleAsync("normal\\live2289.ts", OrcanodeOnlineStatus.Online);
            await TestSampleAsync("normal\\live385.ts", OrcanodeOnlineStatus.Online);
            await TestSampleAsync("normal\\live839.ts", OrcanodeOnlineStatus.Online);
            await TestSampleAsync("normal\\live1184.ts", OrcanodeOnlineStatus.Online);

            // Bush Point file from arond 5pm 11/18/2024 is relatively quiet (max amplitude 17.46).
            await TestSampleAsync("normal\\live6079.ts", OrcanodeOnlineStatus.Online);
        }
    }
}