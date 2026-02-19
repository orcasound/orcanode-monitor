// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Net.Http;

namespace Test
{
    [TestClass]
    public class OrcaHelloFetcherTests
    {
        ILogger _logger;

        private ILogger CreateConsoleLogger()
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });
            return loggerFactory.CreateLogger<OrcaHelloFetcherTests>();
        }

        [TestInitialize]
        public void TestsInitialize()
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

            _logger = CreateConsoleLogger();
        }

        [TestMethod]
        public async Task GetOrcaHelloPodAsync_ReturnsNull_WhenClientIsNull()
        {
            // Arrange
            var fetcher = new OrcaHelloFetcher(null);
            var orcanode = new Orcanode { OrcasoundSlug = "test-slug" };

            // Act
            var result = await fetcher.GetOrcaHelloPodAsync(orcanode, _logger);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task UpdateOrcaHelloDataAsync_HandlesNullClient()
        {
            // Arrange
            var mockContext = new Mock<IOrcanodeMonitorContext>();
            var orcaHelloFetcher = new OrcaHelloFetcher(null);

            // Act
            await orcaHelloFetcher.UpdateOrcaHelloDataAsync(mockContext.Object, _logger);

            // Assert
            // Should complete without throwing an exception
            // Verify no database calls were made since client is null
            mockContext.Verify(c => c.Orcanodes, Times.Never);
        }

        [TestMethod]
        public async Task FetchPodMetricsAsync_ReturnsEmptyList_WhenClientIsNull()
        {
            // Arrange
            var fetcher = new OrcaHelloFetcher(null);
            var orcanodes = new List<Orcanode>();

            // Act
            var result = await fetcher.FetchPodMetricsAsync(orcanodes, _logger);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public async Task FetchNodeMetricsAsync_ReturnsEmptyList_WhenClientIsNull()
        {
            // Arrange
            var fetcher = new OrcaHelloFetcher(null);

            // Act
            var result = await fetcher.FetchNodeMetricsAsync(_logger);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public async Task GetOrcaHelloNodeAsync_ReturnsNull_WhenClientIsNull()
        {
            // Arrange
            var fetcher = new OrcaHelloFetcher(null);

            // Act
            var result = await fetcher.GetOrcaHelloNodeAsync("test-node", _logger);

            // Assert
            Assert.IsNull(result);
        }
    }
}