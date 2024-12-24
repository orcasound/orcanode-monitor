// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using static OrcanodeMonitor.Core.Fetcher;

namespace OrcanodeMonitor.Pages
{
    /// <summary>
    /// Razor Page model for spectral density visualization.
    /// Handles retrieval and processing of frequency data for display.
    /// </summary>
    public class SpectralDensityModel : PageModel
    {
        private readonly OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<SpectralDensityModel> _logger;
        private string _nodeId;
        public string Id => _nodeId;
        public string NodeName { get; private set; }
        private List<string> _labels;
        private List<double> _maxBucketMagnitude;
        public List<string> Labels => _labels;
        public List<double> MaxBucketMagnitude => _maxBucketMagnitude;
        public int MaxMagnitude { get; private set; }
        public int MaxNonHumMagnitude { get; private set; }
        public int SignalRatio { get; private set; }
        public string Status { get; private set; }

        public SpectralDensityModel(OrcanodeMonitorContext context, ILogger<SpectralDensityModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _nodeId = string.Empty;
            NodeName = "Unknown";
        }

        private async Task UpdateFrequencyDataAsync()
        {
            _labels = new List<string> { };
            _maxBucketMagnitude = new List<double> { };
            Orcanode? node = _databaseContext.Orcanodes.Where(n => n.ID == _nodeId).FirstOrDefault();
            if (node == null)
            {
                _logger.LogWarning("Node not found with ID: {NodeId}", _nodeId);
                return;
            }
            NodeName = node.DisplayName;
            TimestampResult? result = await GetLatestS3TimestampAsync(node, false, _logger);
            if (result != null)
            {
                FrequencyInfo? frequencyInfo = await Fetcher.GetLatestAudioSampleAsync(node, result.UnixTimestampString, false, _logger);
                if (frequencyInfo != null)
                {
                    const int MaxFrequency = 23000;
                    const int PointCount = 1000;

                    // Compute the logarithmic base needed to get PointCount points.
                    double b = Math.Pow(MaxFrequency, 1.0 / PointCount);
                    double logb = Math.Log(b);

                    double maxMagnitude = frequencyInfo.MaxMagnitude;
                    var maxBucketMagnitude = new double[PointCount];
                    var maxBucketFrequency = new int[PointCount];

                    foreach (var pair in frequencyInfo.FrequencyMagnitudes)
                    {
                        double frequency = pair.Key;
                        double magnitude = pair.Value;
                        int bucket = (frequency < 1) ? 0 : (int)(Math.Log(frequency) / logb);
                        if (maxBucketMagnitude[bucket] < magnitude)
                        {
                            maxBucketMagnitude[bucket] = magnitude;
                            maxBucketFrequency[bucket] = (int)Math.Round(frequency);
                        }
                    }

                    // Fill in graph points.
                    for (int i = 0; i < PointCount; i++)
                    {
                        if (maxBucketMagnitude[i] > 0)
                        {
                            _labels.Add(maxBucketFrequency[i].ToString());
                            _maxBucketMagnitude.Add(maxBucketMagnitude[i]);
                        }
                    }

                    double maxNonHumMagnitude = frequencyInfo.GetMaxNonHumMagnitude();
                    MaxMagnitude = (int)Math.Round(maxMagnitude);
                    MaxNonHumMagnitude = (int)Math.Round(maxNonHumMagnitude);
                    SignalRatio = (int)Math.Round(100 * maxNonHumMagnitude / maxMagnitude);
                    Status = Orcanode.GetStatusString(frequencyInfo.Status);
                }
            }
        }

        public async Task OnGetAsync(string id)
        {
            _nodeId = id;
            await UpdateFrequencyDataAsync();
        }
    }
}
