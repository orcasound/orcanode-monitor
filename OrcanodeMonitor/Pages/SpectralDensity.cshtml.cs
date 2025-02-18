// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.CodeAnalysis;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Collections.Generic;
using System.Text.Json;
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
        private string _id;
        private OrcanodeEvent? _event = null;
        private Orcanode? _node = null;
        public string NodeName => _node?.DisplayName ?? "Unknown";
        private List<string> _labels;
        public List<string> Labels => _labels;
        public string AudioUrl => _event?.Url ?? "Unknown";
        public int MaxMagnitude { get; private set; }
        public int ChannelCount { get; private set; }
        public int TotalNonHumMagnitude => (int)Math.Round(_totalNonHumMagnitude);
        public int TotalHumMagnitude => (int)Math.Round(_totalHumMagnitude);
        private double _totalHumMagnitude;
        private double _totalNonHumMagnitude;
        private FrequencyInfo? _frequencyInfo = null;
        public int MaxNonHumMagnitude { get; private set; }
        public int SignalRatio { get; private set; }
        public string Status { get; private set; }
        private static double MagnitudeToDecibels(double magnitude)
        {
            double dB = 20 * Math.Log10(magnitude);
            return dB;
        }
        public double MaxSilenceMagnitude => FrequencyInfo.MaxSilenceMagnitude;
        public double MinNoiseMagnitude => FrequencyInfo.MinNoiseMagnitude;
        public double MaxSilenceDecibels => MagnitudeToDecibels(FrequencyInfo.MaxSilenceMagnitude);
        public double MinNoiseDecibels => MagnitudeToDecibels(FrequencyInfo.MinNoiseMagnitude);
        public string LastModified { get; private set; }

        public SpectralDensityModel(OrcanodeMonitorContext context, ILogger<SpectralDensityModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _id = string.Empty;
            Status = string.Empty;
            _labels = new List<string>();
            LastModified = string.Empty;
        }

        /// <summary>
        /// Maximum frequency to analyze in Hz.
        /// Typical human hearing range is up to 20kHz.
        /// Orca calls are up to 40kHz.
        /// </summary>
        private const int MAX_FREQUENCY = 24000;

        /// <summary>
        /// Number of points to plot on the graph. 1000 points provides a good balance
        /// between resolution and performance.
        /// </summary>
        private const int POINT_COUNT = 1000;

        private void FillInGraphPoints(List<string> labels, List<double> maxBucketDecibelsList, int? channel = null)
        {
            if (_frequencyInfo == null)
            {
                return;
            }

            // Compute the logarithmic base needed to get PointCount points.
            double b = Math.Pow(MAX_FREQUENCY, 1.0 / POINT_COUNT);
            double logb = Math.Log(b);

            var maxBucketDecibels = new double[POINT_COUNT];
            for (int i = 0; i < POINT_COUNT; i++) {
                maxBucketDecibels[i] = double.NegativeInfinity;
            }
            var maxBucketFrequency = new int[POINT_COUNT];
            foreach (var pair in _frequencyInfo.GetFrequencyMagnitudes(channel))
            {
                double frequency = pair.Key;
                double magnitude = pair.Value;
                double dB = MagnitudeToDecibels(magnitude);
                int bucket = (frequency < 1) ? 0 : Math.Min(POINT_COUNT - 1, (int)(Math.Log(frequency) / logb));
                if (maxBucketDecibels[bucket] < dB)
                {
                    maxBucketDecibels[bucket] = dB;
                    maxBucketFrequency[bucket] = (int)Math.Round(frequency);
                }
            }
            for (int i = 0; i < POINT_COUNT; i++)
            {
                if (maxBucketDecibels[i] > double.NegativeInfinity)
                {
                    labels.Add(maxBucketFrequency[i].ToString());
                    maxBucketDecibelsList.Add(maxBucketDecibels[i]);
                }
            }
        }

        private double GetBucketDecibels(string label, List<string> labels, List<double> decibels)
        {
            double max = 0;
            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i] == label && decibels[i] > max)
                {
                    max = decibels[i];
                }
            }
            return max;
        }

        private void UpdateFrequencyInfo()
        {
            if (_frequencyInfo == null)
            {
                return;
            }

            // Compute graph points.
            var summaryLabels = new List<string>();
            var summaryMaxBucketDecibels = new List<double>();
            FillInGraphPoints(summaryLabels, summaryMaxBucketDecibels);
            var channelLabels = new List<string>[_frequencyInfo.ChannelCount];
            var channelMaxBucketDecibels = new List<double>[_frequencyInfo.ChannelCount];
            for (int i = 0; i < _frequencyInfo.ChannelCount; i++)
            {
                channelLabels[i] = new List<string>();
                channelMaxBucketDecibels[i] = new List<double>();
                FillInGraphPoints(channelLabels[i], channelMaxBucketDecibels[i], i);
            }

            // Collect all labels.
            var mainLabels = new HashSet<string>(summaryLabels);
            for (int i = 0; i < _frequencyInfo.ChannelCount; i++)
            {
                mainLabels.UnionWith(channelLabels[i]);
            }
            _labels = mainLabels.ToList();

            // Align data.
            var summaryDataset = _labels.Select(label => new
            {
                x = label,
                y = summaryLabels.Contains(label) ? GetBucketDecibels(label, summaryLabels, summaryMaxBucketDecibels) : (double?)null
            }).ToList<object>();
            var channelDatasets = new List<List<object>>();
            for (int i = 0; i < _frequencyInfo.ChannelCount; i++)
            {
                var channelDataset = _labels.Select(label => new
                {
                    x = label,
                    y = channelLabels[i].Contains(label) ? GetBucketDecibels(label, channelLabels[i], channelMaxBucketDecibels[i]) : (double?)null
                }).ToList<object>();
                channelDatasets.Add(channelDataset);
            }

            // Serialise to JSON.
            JsonSummaryDataset = JsonSerializer.Serialize(summaryDataset);
            JsonChannelDatasets = JsonSerializer.Serialize(channelDatasets);

            MaxMagnitude = (int)Math.Round(_frequencyInfo.GetMaxMagnitude());
            MaxNonHumMagnitude = (int)Math.Round(_frequencyInfo.GetMaxNonHumMagnitude());
            ChannelCount = _frequencyInfo.ChannelCount;
            Status = Orcanode.GetStatusString(_frequencyInfo.Status);
            _totalHumMagnitude = _frequencyInfo.GetTotalHumMagnitude();
            _totalNonHumMagnitude = _frequencyInfo.GetTotalNonHumMagnitude();
            SignalRatio = (int)Math.Round(100 * _frequencyInfo.GetSignalRatio());
        }

        /// <summary>
        /// Gets or sets the JSON-serialized dataset containing summary frequency magnitudes.
        /// Can be used by Chart.js for visualization, but isn't currently.
        /// </summary>
        public string JsonSummaryDataset { get; set; }

        /// <summary>
        /// Gets or sets the JSON-serialized datasets containing per-channel frequency magnitudes.
        /// Used by Chart.js for visualization when multiple channels are present.
        /// </summary>
        public string JsonChannelDatasets { get; set; }

        public string GetChannelColor(int channelIndex, double alpha)
        {
            var colors = new[] {
                (54, 235, 127),   // Green
                (153, 102, 255),  // Purple
                (255, 159, 64),   // Orange
                (255, 206, 86),   // Yellow
                (75, 192, 192),   // Teal
                (255, 99, 132),   // Pink
                (54, 162, 235),   // Blue
            };
            var (r, g, b) = colors[channelIndex % colors.Length];
            return $"rgba({r}, {g}, {b}, {alpha})";
        }

        /// <summary>
        /// Gets the maximum magnitude for a specific channel.
        /// </summary>
        /// <param name="channel">The channel index to get the magnitude for.</param>
        /// <returns>The maximum magnitude for the specified channel, or 0 if no data is available.</returns>
        public int GetMaxMagnitude(int channel) => (int)Math.Round(_frequencyInfo?.GetMaxMagnitude(channel) ?? 0);

        /// <summary>
        /// Gets the maximum non-hum magnitude for a specific channel.
        /// </summary>
        /// <param name="channel">The channel index to get the magnitude for.</param>
        /// <returns>The maximum non-hum magnitude for the specified channel, or 0 if no data is available.</returns>
        public int GetMaxNonHumMagnitude(int channel) => (int)Math.Round(_frequencyInfo?.GetMaxNonHumMagnitude(channel) ?? 0);

        public int GetTotalHumMagnitude(int channel) => (int)Math.Round(_frequencyInfo?.GetTotalHumMagnitude(channel) ?? 0);

        public int GetTotalNonHumMagnitude(int channel) => (int)Math.Round(_frequencyInfo?.GetTotalNonHumMagnitude(channel) ?? 0);

        public int GetSignalRatio(int channel) => (int)Math.Round(100 * _frequencyInfo?.GetSignalRatio(channel) ?? 0);

        public string GetStatus(int channel) => Orcanode.GetStatusString(_frequencyInfo?.StatusForChannel[channel] ?? OrcanodeOnlineStatus.Absent);

        private async Task UpdateNodeFrequencyDataAsync()
        {
            if (_node == null)
            {
                return;
            }
            TimestampResult? result = await GetLatestS3TimestampAsync(_node, false, _logger);
            if (result != null)
            {
                try
                {
                    _frequencyInfo = await Fetcher.GetLatestAudioSampleAsync(_node, result.UnixTimestampString, false, _logger);
                    UpdateFrequencyInfo();

                    // Use local time.
                    LastModified = DateTime.Now.ToString();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch audio sample for node {NodeId}", _node.ID);
                }
            }
        }

        private async Task UpdateEventFrequencyDataAsync()
        {
            if (_event == null || _node == null)
            {
                return;
            }
            Uri? uri;
            if (!Uri.TryCreate(_event.Url, UriKind.Absolute, out uri) || (uri == null))
            {
                _logger.LogWarning("URI not found with event ID: {EventID}", _id);
                return;
            }

            DateTime? lastModified = await Fetcher.GetLastModifiedAsync(uri);
            LastModified = lastModified?.ToLocalTime().ToString() ?? "Unknown";

            try
            {
                _frequencyInfo = await Fetcher.GetExactAudioSampleAsync(_node, uri, _logger);
                UpdateFrequencyInfo();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to fetch audio sample for event {EventId}", _id);
            }
        }

        public async Task OnGetAsync(string id)
        {
            _id = id;

            // First see if we have a node ID.
            _node = _databaseContext.Orcanodes.Where(n => n.ID == _id).FirstOrDefault();
            if (_node != null)
            {
                await UpdateNodeFrequencyDataAsync();
                return;
            }

            // Next see if we have an event ID.
            _event = _databaseContext.OrcanodeEvents.Where(e => e.ID == _id).FirstOrDefault();
            if (_event != null)
            {
                _node = _event.Orcanode;
                await UpdateEventFrequencyDataAsync();
                return;
            }

            // Neither worked.
            _logger.LogWarning("ID not found: {ID}", _id);
        }
    }
}
