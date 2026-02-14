// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT

using k8s;
using k8s.Autorest;
using k8s.Models;
using Microsoft.Extensions.Logging;
using Moq;
using OrcanodeMonitor.Models;
using RichardSzalay.MockHttp;
using System.Net;
using System.Text.Json;

namespace OrcanodeMonitor.Core
{
    public class OrcasiteTestHelper
    {
        public static readonly string TestDeviceSerial = "7dcdf551-6283-4867-a0d4-13dc587e4233";
        private static string _solutionDirectory;

        /// <summary>
        /// Find the solution directory.
        /// </summary>
        /// <returns>Directory, or null if not found</returns>
        public static string? FindSolutionDirectory()
        {
            if (_solutionDirectory != null)
            {
                return _solutionDirectory;
            }
            string? currentDirectory = AppDomain.CurrentDomain.BaseDirectory;
            while (currentDirectory != null)
            {
                string path = Path.Combine(currentDirectory, "OrcanodeMonitor.sln");
                if (File.Exists(path))
                {
                    _solutionDirectory = currentDirectory;
                    return currentDirectory;
                }

                currentDirectory = Directory.GetParent(currentDirectory)?.FullName;
            }
            return null;
        }

        /// <summary>
        /// Get the contents of a TestData file as a string.
        /// </summary>
        /// <param name="filename">Name of file to load</param>
        /// <returns>String contents</returns>
        private static string GetStringFromFile(string filename)
        {
            string solutionDirectory = FindSolutionDirectory() ?? throw new Exception("Could not find solution directory");
            string fullPath = Path.Combine(solutionDirectory, "TestData", filename);
            if (!Path.Exists(fullPath))
            {
                return string.Empty;
            }
            return File.ReadAllText(fullPath);
        }

        /// <summary>
        /// Get a mock OrcasiteHelper along with the MockHttpMessageHandler for verification.
        /// </summary>
        /// <param name="logger">Logger instance</param>
        /// <returns>MockOrcasiteHelperContainer containing all mock components for verification</returns>
        public static MockOrcasiteHelperContainer GetMockOrcasiteHelperWithRequestVerification(ILogger logger)
        {
            var mockHttp = new MockHttpMessageHandler();
            var httpClient = mockHttp.ToHttpClient();
            var orcasiteHelper = new OrcasiteHelper(logger, httpClient);
            var container = new MockOrcasiteHelperContainer(orcasiteHelper, mockHttp);

            // Mock the GET request to fetch feeds.
            container.AddJsonResponse(
                "https://*.orcasound.net/api/json/feeds", // "?fields%5Bfeed%5D=id%2Cname%2Cnode_name%2Cslug%2Clocation_point%2Cintro_html%2Cimage_url%2Cvisible%2Cbucket%2Cbucket_region%2Ccloudfront_url%2Cdataplicity_id%2Corcahello_id",
                "OrcasiteFeeds.json");

            container.AddJsonResponse(
                "https://*.orcasound.net/api/json/detections", // "?page%5Blimit%5D=500&page%5Boffset%5D=0&fields%5Bdetection%5D=id%2Cplaylist_timestamp%2Cplayer_offset%2Ctimestamp%2Cdescription%2Csource%2Ccategory%2Cfeed_id",
                "OrcasiteDetections.json");

            // Mock the GET request to dataplicity.
            container.AddJsonResponse(
                $"https://apps.dataplicity.com/devices/{TestDeviceSerial}/",
                "DataplicityGetRequestWithSerial.json");

            container.AddJsonResponse(
                "https://api.mezmo.com/v1/config/view",
                "MezmoConfigView.json");

            container.AddJsonResponse(
                "https://api.mezmo.com/v1/usage/hosts",
                "MezmoUsageHosts.json");

            container.AddJsonResponse(
                "https://api.mezmo.com/v1/export", // "?from=1770777412&to=1770777712&hosts=rpi_point_robinson"
                "MezmoExportLog.json");

#if false
            // Mock the POST request to create a detection.
            string sampleOrcasitePostDetectionResponse = GetStringFromFile("OrcasitePostDetectionResponse.json");
            var postDetectionRequest = mockHttp.When(HttpMethod.Post, "https://*.orcasound.net/api/json/detections?fields%5Bdetection%5D=id%2Csource_ip%2Cplaylist_timestamp%2Cplayer_offset%2Clistener_count%2Ctimestamp%2Cdescription%2Cvisible%2Csource%2Ccategory%2Ccandidate_id%2Cfeed_id")
                    //.WithContent("{\"key\":\"value\"}") // Optional: match request body
                    .Respond(HttpStatusCode.Created, "application/json", sampleOrcasitePostDetectionResponse);
#endif

            container.AddJsonResponse(
                "https://apps.dataplicity.com/devices/",
                "DataplicityGetRequest.json");

            DateTime recent = DateTime.Now.AddMinutes(-1);
            long unixTimestamp = Fetcher.DateTimeToUnixTimeStamp(recent);
            string unixTimestampString = unixTimestamp.ToString();
            container.AddStringResponse("https://*.s3.amazonaws.com//latest.txt", unixTimestampString);

            return container;
        }

        /// <summary>
        /// Wrapper class to hold both OrcasiteHelper and MockHttpMessageHandler together
        /// for dependency injection scenarios.
        /// </summary>
        public class MockOrcasiteHelperContainer
        {
            public OrcasiteHelper Helper { get; }
            public MockHttpMessageHandler MockHttp { get; }

            public MockOrcasiteHelperContainer(OrcasiteHelper helper, MockHttpMessageHandler mockHttp)
            {
                Helper = helper;
                MockHttp = mockHttp;
            }

            public MockOrcasiteHelperContainer(ILogger<OrcasiteHelper> logger)
            {
                var container = GetMockOrcasiteHelperWithRequestVerification(logger);
                Helper = container.Helper;
                MockHttp = container.MockHttp;
            }

            /// <summary>
            /// Add a mock response for a given URL with JSON content from a file.
            /// </summary>
            /// <param name="url">URL when queried to return the content</param>
            /// <param name="filename">File containing the JSON content to return</param>
            public void AddJsonResponse(string url, string filename)
            {
                string content = GetStringFromFile(filename);
                AddStringResponse(url, content, "application/json");
            }

            /// <summary>
            /// Add a mock response for a given URL with content to return.
            /// </summary>
            /// <param name="url">URL when queried to return the content</param>
            /// <param name="content">Content to return</param>
            /// <param name="mediaType">Media type of content to return</param>
            public void AddStringResponse(string url, string content, string mediaType = "text/plain")
            {
                var request = MockHttp.When(HttpMethod.Get, url).Respond(mediaType, content);
            }
        }

        public static List<JsonElement> GetSampleOrcaHelloDetections()
        {
            string sampleOrcaHelloDetection = GetStringFromFile("OrcaHelloDetection.json");
            JsonElement testDocument = JsonDocument.Parse(sampleOrcaHelloDetection).RootElement;
            var documents = new List<JsonElement> { testDocument };
            return documents;
        }

        /// <summary>
        /// Get a mock OrcaHelloFetcher for a given node.  Currently this only
        /// supports one node, but it could be a list of nodes in the future.
        /// </summary>
        /// <param name="node">Orcanode</param>
        /// <returns>OrcaHelloFetcher</returns>
        public static OrcaHelloFetcher GetMockOrcaHelloFetcher(Orcanode node)
        {
            var mockCoreV1 = new Mock<ICoreV1Operations>();
            var mockCustomObjects = new Mock<ICustomObjectsOperations>();
            var mockK8s = new Mock<IKubernetes>();

            mockK8s.Setup(k => k.CoreV1).Returns(mockCoreV1.Object);
            mockK8s.Setup(k => k.CustomObjects).Returns(mockCustomObjects.Object);

            string namespaceName = node.OrcasoundSlug;

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

            // Set up the CoreV1 operations mock to return the pod list.
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

            // Mock the pod metrics response (GetKubernetesPodsMetricsByNamespaceAsync).
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

            return new OrcaHelloFetcher(mockK8s.Object);
        }
    }
}
