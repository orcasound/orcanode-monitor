// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using Microsoft.AspNetCore.Builder;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;

namespace Test
{
    [TestClass]
    public class FetcherTests
    {
        OrcanodeMonitorContext _context;
        Mock<ILogger> _mockLogger;
        ILogger _logger => _mockLogger.Object;
        OrcasiteTestHelper.MockOrcasiteHelperContainer _container;
        HttpClient _httpClient;

        [TestInitialize]
        public void FetcherTestsInitialize()
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

            var options = new DbContextOptionsBuilder<OrcanodeMonitorContext>()
                            .UseInMemoryDatabase(databaseName: "FetcherTestsDatabase")
                            .Options;
            _context = new OrcanodeMonitorContext(options);

            _mockLogger = new Mock<ILogger>();

            _container = OrcasiteTestHelper.GetMockOrcasiteHelperWithRequestVerification(_logger);

            _httpClient = _container.MockHttp.ToHttpClient();

            var builder = WebApplication.CreateBuilder();
            if (builder.Environment.IsDevelopment())
            {
                builder.Configuration.AddUserSecrets<FetcherTests>();
            }
            Fetcher.Initialize(builder.Configuration, _httpClient);
            Fetcher.IsOffline = true; // Using mock HTTP client, so we're in offline/test mode
        }

        [TestCleanup]
        public void FetcherTestsCleanup()
        {
            Fetcher.Uninitialize();
        }

        [TestMethod]
        public async Task TestGetDataplicityDataAsync()
        {
            string result = await DataplicityFetcher.GetDataplicityDataAsync(string.Empty, _logger);
            Assert.IsFalse(result.IsNullOrEmpty(), "GetDataplicityDataAsync failed");
        }

        [TestMethod]
        public async Task TestGetDataplicityDataWithSerialAsync()
        {
            string result = await DataplicityFetcher.GetDataplicityDataAsync(OrcasiteTestHelper.TestDeviceSerial, _logger);
            Assert.IsFalse(result.IsNullOrEmpty(), "GetDataplicityDataAsync failed");
        }

        [TestMethod]
        public async Task TestUpdateDataplicityDataAsync()
        {
            await DataplicityFetcher.UpdateDataplicityDataAsync(_context, _logger);
        }

        [TestMethod]
        public async Task TestUpdateOrcasoundDataAsync()
        {
            await Fetcher.UpdateOrcasoundDataAsync(_context, _logger);
        }

        [TestMethod]
        public async Task TestGetLatestS3TimestampAsync()
        {
            var node = new Orcanode();
            node.S3Bucket = "rpi_test";
            Fetcher.TimestampResult? result = await Fetcher.GetLatestS3TimestampAsync(node, false, _logger);
            Assert.IsNotNull(result);
            Assert.IsFalse(string.IsNullOrEmpty(result.UnixTimestampString));
        }

        [TestMethod]
        public async Task TestUpdateS3DataAsync()
        {
            await Fetcher.UpdateS3DataAsync(_context, _logger);
        }

        [TestMethod]
        public async Task TestGetOrcaHelloPodAsync()
        {
            // Arrange - Create mock Kubernetes client with all required operations.
            var node = new Orcanode { OrcasoundSlug = "andrews-bay" };
            OrcaHelloFetcher fetcher = OrcasiteTestHelper.GetMockOrcaHelloFetcher(node);

            // Act
            var pod = await fetcher.GetOrcaHelloPodAsync(node);

            // Assert
            Assert.IsNotNull(pod, "Pod should not be null");
            Assert.AreEqual("inference-system-andrews-bay", pod.Name);
            Assert.AreEqual(node.OrcasoundSlug, pod.NamespaceName);
            Assert.AreEqual("test-node", pod.NodeName);
            Assert.AreEqual(0, pod.RestartCount);
        }
    }
}
