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
    public class InferenceSystemFetcherTests
    {
        private ILogger<InferenceSystemFetcherTests> _logger;
        private ILoggerFactory _loggerFactory;

        private ILogger<InferenceSystemFetcherTests> CreateConsoleLogger()
        {
            _loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Debug);
            });
            return _loggerFactory.CreateLogger<InferenceSystemFetcherTests>();
        }

        [TestInitialize]
        public void TestsInitialize()
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");

            _logger = CreateConsoleLogger();
        }

        [TestCleanup]
        public void TestsCleanup()
        {
            _loggerFactory?.Dispose();
        }

        private async Task GetInferencePodAsync_ReturnsNull_WhenClientIsNull(string containerName)
        {
            // Arrange
            var fetcher = new InferenceSystemFetcher(null);
            var orcanode = new Orcanode { OrcasoundSlug = "test-slug" };

            // Act
            var result = await fetcher.GetInferencePodByNameAsync(orcanode, containerName, _logger);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task GetOrcaHelloPodAsync_ReturnsNull_WhenClientIsNull()
        {
            await GetInferencePodAsync_ReturnsNull_WhenClientIsNull(InferenceSystemFetcher.OrcaHelloInferenceContainerName);
        }

        [TestMethod]
        public async Task GetPodsAIPodAsync_ReturnsNull_WhenClientIsNull()
        {
            await GetInferencePodAsync_ReturnsNull_WhenClientIsNull(InferenceSystemFetcher.PodsAIInferenceContainerName);
        }

        private async Task UpdateInferenceDataAsync_HandlesNullClient(string containerName, string fieldPrefix)
        {
            // Arrange
            var mockContext = new Mock<IOrcanodeMonitorContext>();
            var orcaHelloFetcher = new InferenceSystemFetcher(null);

            // Act
            await orcaHelloFetcher.UpdateInferenceSystemDataAsync(mockContext.Object, containerName, fieldPrefix, _logger);

            // Assert
            // Should complete without throwing an exception
            // Verify no database calls were made since client is null
            mockContext.Verify(c => c.Orcanodes, Times.Never);
        }

        [TestMethod]
        public async Task UpdateOrcaHelloDataAsync_HandlesNullClient()
        {
            await UpdateInferenceDataAsync_HandlesNullClient(InferenceSystemFetcher.OrcaHelloInferenceContainerName, InferenceSystemFetcher.OrcaHelloFieldPrefix);
        }

        [TestMethod]
        public async Task UpdatePodsAIDataAsync_HandlesNullClient()
        {
            await UpdateInferenceDataAsync_HandlesNullClient(InferenceSystemFetcher.PodsAIInferenceContainerName, InferenceSystemFetcher.PodsAIFieldPrefix);
        }

        private async Task FetchMetricsAsync_ReturnsEmptyList_WhenClientIsNull(string containerName)
        {
            // Arrange
            var fetcher = new InferenceSystemFetcher(null);
            var orcanodes = new List<Orcanode>();

            // Act
            var result = await fetcher.FetchPodMetricsAsync(orcanodes, containerName, _logger);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public async Task FetchOrcaHelloPodMetricsAsync_ReturnsEmptyList_WhenClientIsNull()
        {
            await FetchMetricsAsync_ReturnsEmptyList_WhenClientIsNull(InferenceSystemFetcher.OrcaHelloInferenceContainerName);
        }

        [TestMethod]
        public async Task FetchPodsAIPodMetricsAsync_ReturnsEmptyList_WhenClientIsNull()
        {
            await FetchMetricsAsync_ReturnsEmptyList_WhenClientIsNull(InferenceSystemFetcher.PodsAIInferenceContainerName);
        }

        [TestMethod]
        public async Task FetchPodsAIMetricsAsync_ReturnsEmptyList_WhenClientIsNull()
        {
            // Arrange
            var fetcher = new InferenceSystemFetcher(null);
            var orcanodes = new List<Orcanode>();

            // Act
            var result = await fetcher.FetchPodsAIMetricsAsync(orcanodes, _logger);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public async Task FetchOrcaHelloNodeMetricsAsync_ReturnsEmptyList_WhenClientIsNull()
        {
            // Arrange
            var fetcher = new InferenceSystemFetcher(null);

            // Act
            var result = await fetcher.FetchNodeMetricsAsync(_logger, "inference-system");

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public async Task FetchPodsAINodeMetricsAsync_ReturnsEmptyList_WhenClientIsNull()
        {
            // Arrange
            var fetcher = new InferenceSystemFetcher(null);

            // Act
            var result = await fetcher.FetchNodeMetricsAsync(_logger, "pods-ai-inference-system");

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Count);
        }

        private async Task GetInferenceNodeAsync_ReturnsNull_WhenClientIsNull(string containerName)
        {
            // Arrange
            var fetcher = new InferenceSystemFetcher(null);

            // Act
            var result = await fetcher.GetInferenceNodeAsync("test-node", containerName, _logger);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task GetOrcaHelloNodeAsync_ReturnsNull_WhenClientIsNull()
        {
            await GetInferenceNodeAsync_ReturnsNull_WhenClientIsNull(InferenceSystemFetcher.OrcaHelloInferenceContainerName);
        }

        [TestMethod]
        public async Task GetPodsAINodeAsync_ReturnsNull_WhenClientIsNull()
        {
            await GetInferenceNodeAsync_ReturnsNull_WhenClientIsNull(InferenceSystemFetcher.PodsAIInferenceContainerName);
        }

        [TestMethod]
        public void GetLagFromSegmentLine_ReturnsNull_ForNonMatchingLine()
        {
            // Arrange
            string line = "2026-04-04 18:28:00,422 INFO Some other log message";

            // Act
            TimeSpan? result = InferenceSystemFetcher.GetLagFromSegmentLine(line);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetLagFromSegmentLine_ReturnsNull_ForEmptyLine()
        {
            // Act
            TimeSpan? result = InferenceSystemFetcher.GetLagFromSegmentLine(string.Empty);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void GetLagFromSegmentLine_ReturnsCorrectLag_ForNewFormatLine()
        {
            // Arrange
            // Log written at 18:28:00, audio started at 18:25:57, duration=60s -> ends at 18:26:57
            // Lag = 18:28:00.422 - 18:26:57 = 63.422 seconds
            string line = "2026-04-04 18:28:00,422 INFO Segment: folder=1775286025, indices=[4113:4119), start=2026-04-04T18:25:57Z, duration=60.0s";

            // Act
            TimeSpan? result = InferenceSystemFetcher.GetLagFromSegmentLine(line);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(TimeSpan.FromSeconds(63.422), result.Value);
        }

        [TestMethod]
        public void GetLagFromSegmentLine_ReturnsCorrectLag_WithNonRoundDuration()
        {
            // Arrange
            // Log written at 18:30:00,000 UTC, audio started at 18:28:00Z, duration=45.5s -> ends at 18:28:45.5Z
            // Lag = 18:30:00 - 18:28:45.5 = 74.5 seconds
            string line = "2026-04-04 18:30:00,000 INFO Segment: folder=1234567890, indices=[100:110), start=2026-04-04T18:28:00Z, duration=45.5s";

            // Act
            TimeSpan? result = InferenceSystemFetcher.GetLagFromSegmentLine(line);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(TimeSpan.FromSeconds(74.5), result.Value);
        }

        [TestMethod]
        public void GetLagFromSegmentLine_ReturnsNull_ForOldFormatLine()
        {
            // Arrange - old format log line containing "live\d+.ts" pattern
            string line = "Processing file live42.ts from stream";

            // Act
            TimeSpan? result = InferenceSystemFetcher.GetLagFromSegmentLine(line);

            // Assert
            Assert.IsNull(result, "Old format log line should not match new Segment format");
        }
    }
}