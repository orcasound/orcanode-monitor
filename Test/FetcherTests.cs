// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using k8s.KubeConfigModels;
using Microsoft.AspNetCore.Builder;
using Microsoft.Azure.Cosmos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using System.ComponentModel;

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
                            .UseCosmos(
                                "CONNECTION",
                                databaseName: "DATABASENAME",
                                options => { options.ConnectionMode(ConnectionMode.Gateway); }).Options;
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
            Assert.IsNotNull(result, "GetDataplicityDataAsync failed");
        }

        [TestMethod]
        public async Task TestGetDataplicityDataWithSerialAsync()
        {
            string result = await DataplicityFetcher.GetDataplicityDataAsync(OrcasiteTestHelper.TestDeviceSerial, _logger);
            Assert.IsNotNull(result, "GetDataplicityDataAsync failed");
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
            var node = new OrcanodeMonitor.Models.Orcanode();
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
            var node = new OrcanodeMonitor.Models.Orcanode();
            string namespaceName = "andrews-bay";
            var pod = await OrcaHelloFetcher.GetOrcaHelloPodAsync(node, namespaceName);
            Assert.IsNotNull(pod);
        }
    }
}
