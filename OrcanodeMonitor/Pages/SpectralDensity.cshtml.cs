// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
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
        private string _id;
        private OrcanodeEvent? _event = null;
        private Orcanode? _node = null;
        public string NodeName => _node?.DisplayName ?? "Unknown";
        private List<string> _labels;
        private List<double> _maxBucketMagnitude;
        public List<string> Labels => _labels;
        public List<double> MaxBucketMagnitude => _maxBucketMagnitude;
        public string AudioUrl => _event?.Url ?? "Unknown";
        public int MaxMagnitude { get; private set; }
        public int MaxNonHumMagnitude { get; private set; }
        public int SignalRatio { get; private set; }
        public string Status { get; private set; }
        public double MaxSilenceMagnitude => FrequencyInfo.MaxSilenceMagnitude;
        public double MinNoiseMagnitude => FrequencyInfo.MinNoiseMagnitude;

        public SpectralDensityModel(OrcanodeMonitorContext context, ILogger<SpectralDensityModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _id = string.Empty;
            Status = string.Empty;
            _labels = new List<string>();
            _maxBucketMagnitude = new List<double>();
        }

        private void UpdateFrequencyInfo(FrequencyInfo frequencyInfo)
        {
            const int MaxFrequency = 24000;
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
                int bucket = (frequency < 1) ? 0 : Math.Min(PointCount - 1, (int)(Math.Log(frequency) / logb));
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

        private async Task UpdateNodeFrequencyDataAsync()
        {
            if (_node == null)
            {
                return;
            }
            TimestampResult? result = await GetLatestS3TimestampAsync(_node, false, _logger);
            if (result != null)
            {
                FrequencyInfo? frequencyInfo = await Fetcher.GetLatestAudioSampleAsync(_node, result.UnixTimestampString, false, _logger);
                if (frequencyInfo != null)
                {
                    UpdateFrequencyInfo(frequencyInfo);
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
            FrequencyInfo? frequencyInfo = await Fetcher.GetExactAudioSampleAsync(_node, uri, _logger);
            if (frequencyInfo != null)
            {
                UpdateFrequencyInfo(frequencyInfo);
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
