// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.CodeAnalysis;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion.Internal;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System.Collections.Generic;
using System.Globalization;
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
        public List<string> Labels => _frequencyInfo?.Labels ?? new List<string>();
        public string AudioUrl => _event?.Url ?? "Unknown";
        public double MaxMagnitude { get; private set; }
        public int ChannelCount { get; private set; }
        public double TotalNonHumMagnitude => _totalNonHumMagnitude;
        public double TotalHumMagnitude => _totalHumMagnitude;
        private double _totalHumMagnitude;
        private double _totalNonHumMagnitude;
        private double _averageHumDecibels;
        private double _averageNonHumDecibels;
        public double AverageHumDecibels => _averageHumDecibels;
        public double AverageNonHumDecibels => _averageNonHumDecibels;
        private FrequencyInfo? _frequencyInfo = null;
        public double MaxNonHumMagnitude { get; private set; }
        public int SignalRatio { get; private set; }
        public string Status { get; private set; }
        private static double MagnitudeToDecibels(double magnitude)
        {
            double dB = 20 * Math.Log10(magnitude);
            return dB;
        }
        public double MaxSilenceDecibels => FrequencyInfo.MaxSilenceDecibels;
        public double MinNoiseDecibels => FrequencyInfo.MinNoiseDecibels;

        /// <summary>
        /// Last modified timestamp, in local time.
        /// </summary>
        public string LastModifiedLocal { get; private set; }

        public SpectralDensityModel(OrcanodeMonitorContext context, ILogger<SpectralDensityModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _id = string.Empty;
            Status = string.Empty;
            LastModifiedLocal = string.Empty;
            JsonChannelDatasets = string.Empty;
            JsonHumChannelDatasets = string.Empty;
            JsonNonHumChannelDatasets = string.Empty;
        }

        private void UpdateFrequencyInfo()
        {
            if (_frequencyInfo == null)
            {
                return;
            }

            // Serialise to JSON.
            JsonChannelDatasets = JsonSerializer.Serialize(_frequencyInfo.ChannelDatasets);
            JsonNonHumChannelDatasets = JsonSerializer.Serialize(_frequencyInfo.NonHumChannelDatasets);
            JsonHumChannelDatasets = JsonSerializer.Serialize(_frequencyInfo.HumChannelDatasets);

            MaxMagnitude = _frequencyInfo.GetMaxMagnitude();
            MaxNonHumMagnitude = _frequencyInfo.GetMaxNonHumMagnitude();
            ChannelCount = _frequencyInfo.ChannelCount;
            Status = Orcanode.GetStatusString(_frequencyInfo.Status);
            _totalHumMagnitude = _frequencyInfo.GetTotalHumMagnitude();
            _totalNonHumMagnitude = _frequencyInfo.GetTotalNonHumMagnitude();
            _averageHumDecibels = _frequencyInfo.GetAverageHumDecibels();
            _averageNonHumDecibels = _frequencyInfo.GetAverageNonHumDecibels();
            SignalRatio = (int)Math.Round(100 * _frequencyInfo.GetSignalRatio());
        }

        /// <summary>
        /// Gets or sets the JSON-serialized datasets containing per-channel frequency magnitudes.
        /// </summary>
        public string JsonChannelDatasets { get; set; }

        /// <summary>
        /// Gets or sets the JSON-serialized datasets containing per-channel frequency magnitudes
        /// for non-hum frequencies.
        /// </summary>
        public string JsonNonHumChannelDatasets { get; set; }

        /// <summary>
        /// Gets or sets the JSON-serialized datasets containing per-channel frequency magnitudes.
        /// for hum frequencies.
        /// </summary>
        public string JsonHumChannelDatasets { get; set; }

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
        public double GetMaxMagnitude(int channel) => _frequencyInfo?.GetMaxMagnitude(channel) ?? 0;

        /// <summary>
        /// Gets the maximum non-hum magnitude for a specific channel.
        /// </summary>
        /// <param name="channel">The channel index to get the magnitude for.</param>
        /// <returns>The maximum non-hum magnitude for the specified channel, or 0 if no data is available.</returns>
        public double GetMaxNonHumMagnitude(int channel) => _frequencyInfo?.GetMaxNonHumMagnitude(channel) ?? 0;

        public double GetTotalHumMagnitude(int channel) => _frequencyInfo?.GetTotalHumMagnitude(channel) ?? 0;

        public double GetTotalNonHumMagnitude(int channel) => _frequencyInfo?.GetTotalNonHumMagnitude(channel) ?? 0;
        public double GetAverageHumDecibels(int channel) => _frequencyInfo?.GetAverageHumDecibels(channel) ?? double.NaN;
        public double GetAverageNonHumDecibels(int channel) => _frequencyInfo?.GetAverageNonHumDecibels(channel) ?? double.NaN;

        public int GetSignalRatio(int channel) => (int)Math.Round(100 * _frequencyInfo?.GetSignalRatio(channel) ?? 0);

        public string GetStatus(int channel) => Orcanode.GetStatusString(_frequencyInfo?.StatusForChannel[channel] ?? OrcanodeOnlineStatus.Absent);

        /// <summary>
        /// Update the node frequency info using the latest audio.
        /// </summary>
        /// <returns></returns>
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
                    LastModifiedLocal = Fetcher.UtcToLocalDateTime(DateTime.UtcNow)?.ToString() ?? "Unknown";
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to fetch audio sample for node {NodeId}", _node.ID);
                }
            }
        }

        private DateTime? TryParseDateTime(string timestamp)
        {
            if (!DateTime.TryParseExact(
                timestamp,
                "yyyy-MM-ddTHH-mm-ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out DateTime dt))
            {
                return null;
            }
            return dt;
        }

        /// <summary>
        /// Update the node frequency info using audio from a given timestamp.
        /// </summary>
        /// <returns></returns>
        private async Task UpdateNodeFrequencyDataAsync(string timestamp)
        {
            if (_node == null)
            {
                return;
            }

            DateTime? dateTime = TryParseDateTime(timestamp);
            if (!dateTime.HasValue)
            {
                return;
            }

            TimestampResult? result = await S3Fetcher.GetS3TimestampAsync(_node, dateTime.Value, _logger);
            if (result != null)
            {
                try
                {
                    _frequencyInfo = await S3Fetcher.GetAudioSampleAsync(_node, result.UnixTimestampString, dateTime.Value, _logger);
                    UpdateFrequencyInfo();

                    // Use local time.
                    LastModifiedLocal = dateTime.ToString() ?? "Unknown";
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
            LastModifiedLocal = UtcToLocalDateTime(lastModified)?.ToString() ?? "Unknown";

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

        /// <summary>
        /// View the spectral density for an event or the latest on a node.
        /// </summary>
        /// <param name="id">node ID or event ID</param>
        /// <param name="id">timestamp in the format "yyyy-MM-ddTHH-mm-ss", or "now"</param>
        /// <returns></returns>
        public async Task OnGetAsync(string id, string timestamp)
        {
            _id = id;

            // First see if we have a node ID.
            _node = _databaseContext.Orcanodes.Where(n => n.ID == _id).FirstOrDefault();
            if (_node != null)
            {
                if (timestamp == "now")
                {
                    await UpdateNodeFrequencyDataAsync();
                }
                else
                {
                    await UpdateNodeFrequencyDataAsync(timestamp);
                }
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
