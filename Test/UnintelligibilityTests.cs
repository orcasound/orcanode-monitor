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
        private async Task TestSampleAsync(string filename, bool expected_result)
        {
            // Get the current directory (where the test assembly is located)
            string currentDirectory = Directory.GetCurrentDirectory();

            // Navigate to the root of the repository
            string rootDirectory = Path.GetFullPath(Path.Combine(currentDirectory, @"..\..\..\..\"));

            string filePath = Path.Combine(rootDirectory, "Test\\samples", filename);
            try
            {
                double audioStandardDeviation = await FfmpegCoreAnalyzer.AnalyzeFileAsync(filePath);
                bool normal = !Orcanode.IsUnintelligible(audioStandardDeviation);
                Assert.IsTrue(normal ==  expected_result);
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
            await TestSampleAsync("unintelligible\\live1791.ts", false);
            await TestSampleAsync("unintelligible\\live1815.ts", false);
            await TestSampleAsync("unintelligible\\live1816.ts", false);
        }

        [TestMethod]
        public async Task TestNormalSample()
        {
            await TestSampleAsync("normal\\live385.ts", true);
            await TestSampleAsync("normal\\live839.ts", true);
            await TestSampleAsync("normal\\live1184.ts", true);
        }
    }
}