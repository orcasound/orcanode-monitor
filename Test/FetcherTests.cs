// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using k8s;
using k8s.Autorest;
using k8s.KubeConfigModels;
using k8s.Models;
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
using System.ComponentModel;
using System.Text.Json;

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
            Fetcher.Initialize(builder.Configuration, _httpClient, _mockLogger.Object);
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
            var mockCoreV1 = new Mock<ICoreV1Operations>();
            var mockCustomObjects = new Mock<ICustomObjectsOperations>();
            var mockK8s = new Mock<IKubernetes>();

            mockK8s.Setup(k => k.CoreV1).Returns(mockCoreV1.Object);
            mockK8s.Setup(k => k.CustomObjects).Returns(mockCustomObjects.Object);

            var node = new Orcanode { OrcasoundSlug = "andrews-bay" };
            string namespaceName = "andrews-bay";

            // Create a mock pod to return.
            var mockPod = new V1Pod
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "inference-system-andrews-bay",
                    NamespaceProperty = namespaceName
                },
                Spec = new V1PodSpec
                {
                    NodeName = "test-node",
                    Containers = new List<V1Container>
                    {
                        new V1Container
                        {
                            Name = "inference-system",
                            Image = "orcaconservancy.io/inference-system:latest",
                            Resources = new V1ResourceRequirements
                            {
                                Limits = new Dictionary<string, ResourceQuantity>
                                {
                                    { "cpu", new ResourceQuantity("2") },
                                    { "memory", new ResourceQuantity("4Gi") }
                                }
                            }
                        }
                    }
                },
                Status = new V1PodStatus
                {
                    Phase = "Running",
                    ContainerStatuses = new List<V1ContainerStatus>
                    {
                        new V1ContainerStatus
                        {
                            Name = "inference-system",
                            Ready = true,
                            RestartCount = 0,
                            State = new V1ContainerState
                            {
                                Running = new V1ContainerStateRunning
                                {
                                    StartedAt = DateTime.UtcNow.AddHours(-1)
                                }
                            }
                        }
                    }
                }
            };

            var podList = new V1PodList
            {
                Items = new List<V1Pod> { mockPod }
            };

            // Setup the CoreV1 operations mock to return the pod list
            mockCoreV1.Setup(c => c.ListNamespacedPodWithHttpMessagesAsync(
                namespaceName,
                It.IsAny<bool?>(),           // allowWatchBookmarks
                It.IsAny<string>(),          // continueParameter
                It.IsAny<string>(),          // fieldSelector
                It.IsAny<string>(),          // labelSelector
                It.IsAny<int?>(),            // limit
                It.IsAny<string>(),          // resourceVersion
                It.IsAny<string>(),          // resourceVersionMatch
                It.IsAny<bool?>(),           // sendInitialEvents
                It.IsAny<int?>(),            // timeoutSeconds
                It.IsAny<bool?>(),           // watch
                It.IsAny<bool?>(),           // pretty
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>(), // customHeaders
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HttpOperationResponse<V1PodList>
                {
                    Body = podList
                });

            // Mock the pod metrics response (GetKubernetesPodsMetricsByNamespaceAsync)
            var podMetricsJson = JsonSerializer.Serialize(new
            {
                kind = "PodMetricsList",
                apiVersion = "metrics.k8s.io/v1beta1",
                metadata = new { },
                items = new[]
                {
                    new
                    {
                        metadata = new
                        {
                            name = "inference-system-andrews-bay",
                            @namespace = namespaceName
                        },
                        timestamp = DateTime.UtcNow,
                        window = "30s",
                        containers = new[]
                        {
                            new
                            {
                                name = "inference-system",
                                usage = new
                                {
                                    cpu = "100", // 100m
                                    memory = "256" // 256Mi
                                }
                            }
                        }
                    }
                }
            });

            var podMetricsElement = JsonSerializer.Deserialize<JsonElement>(podMetricsJson);

            mockCustomObjects.Setup(c => c.GetNamespacedCustomObjectWithHttpMessagesAsync(
                "metrics.k8s.io",
                "v1beta1",
                namespaceName,
                "pods",
                string.Empty,
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                It.IsAny<CancellationToken>()))
                .ReturnsAsync(new HttpOperationResponse<object>
                {
                    Body = podMetricsElement
                });

            // Mock the ConfigMap for hydrophone-configs
            var configMap = new V1ConfigMap
            {
                Metadata = new V1ObjectMeta
                {
                    Name = "hydrophone-configs",
                    NamespaceProperty = namespaceName
                },
                Data = new Dictionary<string, string>
                {
                    { "model_timestamp", DateTime.UtcNow.AddDays(-1).ToString("yyyy-MM-ddTHH:mm:ssZ") },
                    { "local_threshold", "0.7" },
                    { "global_threshold", "3" }
                }
            };

            mockCoreV1.Setup(c => c.ReadNamespacedConfigMapWithHttpMessagesAsync(
                "hydrophone-configs",
                namespaceName,
                It.IsAny<bool?>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<string>>>(),
                It.IsAny<System.Threading.CancellationToken>()))
                .ReturnsAsync(new HttpOperationResponse<V1ConfigMap>
                {
                    Body = configMap
                });

            var fetcher = new OrcaHelloFetcher(mockK8s.Object);

            // Act
            var pod = await fetcher.GetOrcaHelloPodAsync(node, namespaceName);

            // Assert
            Assert.IsNotNull(pod, "Pod should not be null");
            Assert.AreEqual("inference-system-andrews-bay", pod.Name);
            Assert.AreEqual(namespaceName, pod.NamespaceName);
            Assert.AreEqual("test-node", pod.NodeName);
            Assert.AreEqual(0, pod.RestartCount);
        }
    }
}
