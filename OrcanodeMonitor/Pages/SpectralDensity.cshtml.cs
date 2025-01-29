// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.CodeAnalysis;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Diagnostics;
using System.Linq.Expressions;
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
        public double MaxSilenceMagnitude => FrequencyInfo.MaxSilenceMagnitude;
        public double MinNoiseMagnitude => FrequencyInfo.MinNoiseMagnitude;
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

        private void FillInGraphPoints(List<string> labels, List<double> maxBucketMagnitudeList, int? channel = null)
        {
            const int MaxFrequency = 24000;
            const int PointCount = 1000;

            // Compute the logarithmic base needed to get PointCount points.
            double b = Math.Pow(MaxFrequency, 1.0 / PointCount);
            double logb = Math.Log(b);

            var maxBucketMagnitude = new double[PointCount];
            var maxBucketFrequency = new int[PointCount];
            foreach (var pair in _frequencyInfo.GetFrequencyMagnitudes(channel))
            {
                double frequency = pair.Key;
                double magnitude = pair.Value;
                int bucket = (frequency < 1) ? 0 : Math.Min(PointCount - 1, (int)(Math.Log(frequency) / logb));
                if (maxBucketMagnitude[bucket] < magnitude)
                {
                    maxBucketMagnitude[bucket] = magnitude;
                    maxBucketFrequency[bucket] = (int)Math.Round(frequency);
                }
            }
            for (int i = 0; i < PointCount; i++)
            {
                if (maxBucketMagnitude[i] > 0)
                {
                    labels.Add(maxBucketFrequency[i].ToString());
                    maxBucketMagnitudeList.Add(maxBucketMagnitude[i]);
                }
            }
        }

        private double GetBucketMagnitude(string label, List<string> labels, List<double> magnitudes)
        {
            double sum = 0;
            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i] == label)
                {
                    sum += magnitudes[i];
                }
            }
            return sum;
        }

        private void UpdateFrequencyInfo()
        {
            if (_frequencyInfo == null)
            {
                return;
            }

            // Compute graph points.
            var summaryLabels = new List<string>();
            var summaryMaxBucketMagnitude = new List<double>();
            FillInGraphPoints(summaryLabels, summaryMaxBucketMagnitude);
            var channelLabels = new List<string>[_frequencyInfo.ChannelCount];
            var channelMaxBucketMagnitude = new List<double>[_frequencyInfo.ChannelCount];
            for (int i = 0; i < _frequencyInfo.ChannelCount; i++)
            {
                channelLabels[i] = new List<string>();
                channelMaxBucketMagnitude[i] = new List<double>();
                FillInGraphPoints(channelLabels[i], channelMaxBucketMagnitude[i], i);
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
                Label = label,
                Value = summaryLabels.Contains(label) ? GetBucketMagnitude(label, summaryLabels, summaryMaxBucketMagnitude) : (double?)null
            }).ToList<object>();
            var channelDatasets = new List<List<object>>();
            for (int i = 0; i < _frequencyInfo.ChannelCount; i++)
            {
                var channelDataset = _labels.Select(label => new
                {
                    Label = label,
                    Value = channelLabels[i].Contains(label) ? GetBucketMagnitude(label, channelLabels[i], channelMaxBucketMagnitude[i]) : (double?)null
                }).ToList<object>();
                channelDatasets.Add(channelDataset);
            }

            // Serialise to JSON.
            JsonSummaryDataset = JsonSerializer.Serialize(summaryDataset);
            JsonChannelDatasets = channelDatasets.Select(dataset => JsonSerializer.Serialize(dataset)).ToList();

            MaxMagnitude = (int)Math.Round(_frequencyInfo.GetMaxMagnitude());
            MaxNonHumMagnitude = (int)Math.Round(_frequencyInfo.GetMaxNonHumMagnitude());
            ChannelCount = _frequencyInfo.ChannelCount;
            Status = Orcanode.GetStatusString(_frequencyInfo.Status);
            _totalHumMagnitude = _frequencyInfo.GetTotalHumMagnitude();
            _totalNonHumMagnitude = _frequencyInfo.GetTotalNonHumMagnitude();
            SignalRatio = (int)Math.Round(100 * _frequencyInfo.GetSignalRatio());
        }

        public string JsonSummaryDataset { get; set; }
        public List<string> JsonChannelDatasets { get; set; }

        public int GetMaxMagnitude(int channel) => (int)Math.Round(_frequencyInfo?.GetMaxMagnitude(channel) ?? 0);

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
                _frequencyInfo = await Fetcher.GetLatestAudioSampleAsync(_node, result.UnixTimestampString, false, _logger);
                UpdateFrequencyInfo();
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

            _frequencyInfo = await Fetcher.GetExactAudioSampleAsync(_node, uri, _logger);
            UpdateFrequencyInfo();
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
