// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Logging;
using Moq;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;

namespace Test
{
    [TestClass]
    public class OrcaHelloFetcherTests
    {
        [TestMethod]
        public async Task GetOrcaHelloPodAsync_ReturnsNull_WhenClientIsNull()
        {
            // Arrange
            var fetcher = new OrcaHelloFetcher(null);
            var orcanode = new Orcanode { OrcasoundSlug = "test-slug" };

            // Act
            var result = await fetcher.GetOrcaHelloPodAsync(orcanode);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task UpdateOrcaHelloDataAsync_HandlesNullClient()
        {
            // Arrange
            var mockLogger = new Mock<ILogger>();
            var mockContext = new Mock<IOrcanodeMonitorContext>();
            var orcaHelloFetcher = new OrcaHelloFetcher(null);

            // Act
            await orcaHelloFetcher.UpdateOrcaHelloDataAsync(mockContext.Object, mockLogger.Object);

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
            var result = await fetcher.FetchPodMetricsAsync(orcanodes);

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
            var result = await fetcher.FetchNodeMetricsAsync();

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
            var result = await fetcher.GetOrcaHelloNodeAsync("test-node");

            // Assert
            Assert.IsNull(result);
        }
    }
}