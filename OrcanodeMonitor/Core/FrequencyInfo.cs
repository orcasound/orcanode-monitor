﻿// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using FftSharp;
using OrcanodeMonitor.Models;
using System.Data;
using System.Diagnostics;
using System.Numerics;

namespace OrcanodeMonitor.Core
{
    public class FrequencyInfo
    {
        /// <summary>
        /// Number of points to plot on the graph. 1000 points provides a good balance
        /// between resolution and performance.
        /// </summary>
        private const int POINT_COUNT = 1000;

        /// <summary>
        /// Maximum frequency to analyze in Hz.
        /// Typical human hearing range is up to 20kHz.
        /// Orca calls are up to 40kHz.
        /// </summary>
        private const int MAX_FREQUENCY = 24000;

        private void FillInGraphPoints(List<string> labels, List<double> maxBucketDecibelsList, int? channel = null)
        {
            // Compute the logarithmic base needed to get PointCount points.
            double b = Math.Pow(MAX_FREQUENCY, 1.0 / POINT_COUNT);
            double logb = Math.Log(b);

            var maxBucketDecibels = new double[POINT_COUNT];
            for (int i = 0; i < POINT_COUNT; i++)
            {
                maxBucketDecibels[i] = double.NegativeInfinity;
            }
            var maxBucketFrequency = new int[POINT_COUNT];
            foreach (var pair in GetFrequencyMagnitudes(channel))
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
            double max = double.NegativeInfinity;
            for (int i = 0; i < labels.Count; i++)
            {
                if (labels[i] == label && decibels[i] > max)
                {
                    max = decibels[i];
                }
            }
            return max;
        }

        private List<string> _labels;
        public List<string> Labels => _labels;
        private List<List<object>> _channelDatasets = new List<List<object>>();
        public List<List<object>> ChannelDatasets => _channelDatasets;
        private List<List<object>> _nonHumChannelDatasets = new List<List<object>>();
        public List<List<object>> NonHumChannelDatasets => _nonHumChannelDatasets;
        private List<List<object>> _humChannelDatasets = new List<List<object>>();
        public List<List<object>> HumChannelDatasets => _humChannelDatasets;

        private void UpdateFrequencyInfo()
        {
            // Compute graph points.
            var summaryLabels = new List<string>();
            var summaryMaxBucketDecibels = new List<double>();
            FillInGraphPoints(summaryLabels, summaryMaxBucketDecibels);
            var channelLabels = new List<string>[ChannelCount];
            var channelMaxBucketDecibels = new List<double>[ChannelCount];
            for (int i = 0; i < ChannelCount; i++)
            {
                channelLabels[i] = new List<string>();
                channelMaxBucketDecibels[i] = new List<double>();
                FillInGraphPoints(channelLabels[i], channelMaxBucketDecibels[i], i);
            }

            // Collect all labels.
            var mainLabels = new HashSet<string>(summaryLabels);
            for (int i = 0; i < ChannelCount; i++)
            {
                mainLabels.UnionWith(channelLabels[i]);
            }

            // Sort labels numerically.
            _labels = mainLabels
                .Select(label => int.Parse(label)) // Convert to integers for sorting.
                .OrderBy(num => num)               // Sort in ascending order.
                .Select(num => num.ToString())     // Convert back to strings.
                .ToList();

            // Align data.
            var summaryDataset = _labels.Select(label => new
            {
                x = label,
                y = summaryLabels.Contains(label) ? GetBucketDecibels(label, summaryLabels, summaryMaxBucketDecibels) : (double?)null
            }).ToList<object>();
            for (int i = 0; i < ChannelCount; i++)
            {
                var channelDataset = _labels.Select(label => new
                {
                    x = label,
                    y = channelLabels[i].Contains(label) ? GetBucketDecibels(label, channelLabels[i], channelMaxBucketDecibels[i]) : (double?)null
                }).ToList<object>();
                _channelDatasets.Add(channelDataset);
            }
            for (int i = 0; i < ChannelCount; i++)
            {
                var nonHumChannelDataset = _labels
                    .Where(label =>
                    {
                        if (double.TryParse(label, out double frequency)) // Try to parse the label as a double
                        {
                            return !FrequencyInfo.IsHumFrequency(frequency); // Return true if it's not a hum frequency
                        }
                        return false; // If parsing fails, exclude the label
                    })
                    .Select(label => new
                    {
                        x = label,
                        y = channelLabels[i].Contains(label) ? GetBucketDecibels(label, channelLabels[i], channelMaxBucketDecibels[i]) : (double?)null
                    })
                    .ToList<object>();
                _nonHumChannelDatasets.Add(nonHumChannelDataset);
            }
            for (int i = 0; i < ChannelCount; i++)
            {
                var humChannelDataset = _labels
                    .Where(label =>
                    {
                        if (double.TryParse(label, out double frequency)) // Try to parse the label as a double
                        {
                            return FrequencyInfo.IsHumFrequency(frequency); // Return true if it's a hum frequency
                        }
                        return false; // If parsing fails, exclude the label
                    })
                    .Select(label => new
                    {
                        x = label,
                        y = channelLabels[i].Contains(label) ? GetBucketDecibels(label, channelLabels[i], channelMaxBucketDecibels[i]) : (double?)null
                    })
                    .ToList<object>();
                _humChannelDatasets.Add(humChannelDataset);
            }
        }

        static OrcanodeOnlineStatus GetBetterStatus(OrcanodeOnlineStatus a, OrcanodeOnlineStatus b)
        {
            if (a == OrcanodeOnlineStatus.Online)
            {
                return a;
            }
            if (b == OrcanodeOnlineStatus.Online)
            {
                return b;
            }
            if (a == OrcanodeOnlineStatus.Unintelligible)
            {
                return a;
            }
            if (b == OrcanodeOnlineStatus.Unintelligible)
            {
                return b;
            }
            if (a == OrcanodeOnlineStatus.Silent)
            {
                return a;
            }
            if (b == OrcanodeOnlineStatus.Silent)
            {
                return b;
            }
            if (a == OrcanodeOnlineStatus.Absent)
            {
                return b;
            }

            // Other statuses should be the same for both.
            Debug.Assert(a == b);
            return a;
        }

        /// <summary>
        /// Given an audio clip, compute frequency info for it.
        /// </summary>
        /// <param name="data">Raw audio data as float array</param>
        /// <param name="sampleRate">Audio sample rate in Hz</param>
        /// <param name="channels">Number of audio channels</param>
        /// <param name="oldStatus">Previous online status for hysteresis</param>
        public FrequencyInfo(float[] data, uint sampleRate, int channels, OrcanodeOnlineStatus oldStatus)
        {
            ChannelCount = channels;
            FrequencyMagnitudesForChannel = new Dictionary<double, double>[channels];
            StatusForChannel = new OrcanodeOnlineStatus[channels];
            FrequencyMagnitudes = new Dictionary<double, double>();
            ComputeFrequencyMagnitudes(data, sampleRate, channels);
            Status = OrcanodeOnlineStatus.Absent;
            for (int i = 0; i < channels; i++)
            {
                StatusForChannel[i] = GetStatus(oldStatus, i);
                Status = GetBetterStatus(Status, StatusForChannel[i]);
            }
            UpdateFrequencyInfo();
        }

        private void ComputeFrequencyMagnitudes(float[] data, uint sampleRate, int channelCount)
        {
            if (data == null || data.Length == 0)
            {
                throw new ArgumentException("Audio data cannot be null or empty", nameof(data));
            }
            if (data.Length % channelCount != 0)
            {
                throw new ArgumentException("Data length must be divisible by channel count", nameof(data));
            }
            if (sampleRate <= 0)
            {
                throw new ArgumentException("Sample rate must be positive", nameof(sampleRate));
            }

            int n = data.Length / channelCount;
            int nextPowerOfTwo = (int)Math.Pow(2, Math.Ceiling(Math.Log(n, 2)));

            // Create frequency magnitudes for each channel.
            for (int ch = 0; ch < channelCount; ch++)
            {
                // Extract channel-specific data.
                double[] channelData = new double[nextPowerOfTwo];
                for (int i = 0; i < n; i++)
                {
                    channelData[i] = data[i * channelCount + ch];
                }

                // Apply Hann window.
                var hannWindow = new FftSharp.Windows.Hanning();
                hannWindow.ApplyInPlace(channelData);

                // Perform FFT.
                Complex[] fftResult = FFT.Forward(channelData);
                double[] magnitudes = FFT.Magnitude(fftResult);

                // Store frequency magnitudes for this channel.
                FrequencyMagnitudesForChannel[ch] = new Dictionary<double, double>(n / 2);
                for (int i = 0; i < magnitudes.Length / 2; i++) // Use only the first half (positive frequencies).
                {
                    double frequency = (((double)i) * sampleRate) / nextPowerOfTwo;
                    FrequencyMagnitudesForChannel[ch][frequency] = magnitudes[i];
                    double decibels = magnitudes[i] > 0 ? 20 * Math.Log10(magnitudes[i]) : double.NegativeInfinity; // DEBUG
                }
            }

            // Combine results from all channels.
            foreach (var channelResult in FrequencyMagnitudesForChannel)
            {
                foreach (var kvp in channelResult)
                {
                    if (!FrequencyMagnitudes.ContainsKey(kvp.Key))
                    {
                        FrequencyMagnitudes[kvp.Key] = 0;
                    }
                    FrequencyMagnitudes[kvp.Key] += kvp.Value;
                }
            }

            // Now that we have the sum take the average.
            if (channelCount > 1)
            {
                foreach (var key in FrequencyMagnitudes.Keys)
                {
                    FrequencyMagnitudes[key] /= channelCount;
                }
            }
        }

        private static double DecibelsToMagnitude(double decibels)
        {
            double magnitude = Math.Pow(10, decibels / 20);
            return magnitude;
        }

        private static double MagnitudeToDecibels(double magnitude)
        {
            double dB = 20 * Math.Log10(magnitude);
            return dB;
        }

        // We consider anything above this average decibels as not silence.
        // If this number is updated, it should also be updated in Design.md.
        const double _defaultMaxSilenceDecibels = -80;
        public static double MaxSilenceDecibels
        {
            get
            {
                string? maxSilenceDecibelsString = Environment.GetEnvironmentVariable("ORCASOUND_MAX_SILENCE_DECIBELS");
                double maxSilenceDecibels = double.TryParse(maxSilenceDecibelsString, out var decibels) ? decibels : _defaultMaxSilenceDecibels;
                return maxSilenceDecibels;
            }
        }

        public static double MaxSilenceMagnitude => DecibelsToMagnitude(MaxSilenceDecibels);

        // We consider anything below this average decibels as silence.
        // The lowest normal value we have seen is -98.
        // If this number is updated, it should also be updated in Design.md.
        const double _defaultMinNoiseDecibels = -95;
        public static double MinNoiseDecibels
        {
            get
            {
                string? minNoiseDecibelsString = Environment.GetEnvironmentVariable("ORCASOUND_MIN_NOISE_DECIBELS");
                double minNoiseDecibels = double.TryParse(minNoiseDecibelsString, out var decibels) ? decibels : _defaultMinNoiseDecibels;
                return minNoiseDecibels;
            }
        }
        public static double MinNoiseMagnitude => DecibelsToMagnitude(MinNoiseDecibels);

        // Minimum ratio of magnitude outside the hum range to magnitude
        // within the hum range.  So far the max in a known-unintelligible
        // sample is 1309% and the min in a known-good sample is 1670%.
        const double _defaultMinSignalPercent = 1400;
        private static double MinSignalRatio
        {
            get
            {
                string? minSignalPercentString = Environment.GetEnvironmentVariable("ORCASOUND_MIN_INTELLIGIBLE_SIGNAL_PERCENT");
                double minSignalPercent = double.TryParse(minSignalPercentString, out var percent) ? percent : _defaultMinSignalPercent;
                return minSignalPercent / 100.0;
            }
        }

        // Data members.

        private Dictionary<double, double> FrequencyMagnitudes { get; }
        private Dictionary<double, double>[] FrequencyMagnitudesForChannel { get; }
        public OrcanodeOnlineStatus Status { get; }
        public OrcanodeOnlineStatus[] StatusForChannel { get; }
        public int ChannelCount { get; private set; } = 0;

        /// <summary>
        /// URL at which the original audio sample can be found.
        /// </summary>
        public string AudioSampleUrl { get; set; } = string.Empty;

        public Dictionary<double, double> GetFrequencyMagnitudes(int? channel = null)
        {
            return (channel.HasValue) ? FrequencyMagnitudesForChannel[channel.Value] : FrequencyMagnitudes;
        }

        public double GetMaxMagnitude(int? channel = null) => GetFrequencyMagnitudes(channel).Values.Max();

        /// <summary>
        /// Compute the ratio between non-hum and hum frequencies in the audio signal.
        /// This ratio helps determine if the signal is intelligible or just noise.
        /// </summary>
        /// <param name="channel">Channel number, or null for an aggregate</param>
        /// <returns>
        /// The ratio of non-hum to hum frequencies. A higher ratio indicates a clearer signal.
        /// Returns 0 when no hum is detected to avoid division by zero.
        /// </returns>
        /// <remarks>
        /// The ratio is calculated by dividing the total magnitude of non-hum frequencies
        /// by the total magnitude of hum frequencies (50Hz and 60Hz bands).
        /// A minimum value of 1 is used for hum magnitude to prevent division by zero.
        /// </remarks>
        public double GetSignalRatio(int? channel = null)
        {
            double hum = Math.Max(GetTotalHumMagnitude(channel), 1);
            return GetTotalNonHumMagnitude(channel) / hum;
        }

        // Microphone audio hum typically falls within the 60 Hz
        // range. This hum is often caused by electrical interference from
        // power lines and other electronic devices.
        const double HumFrequency60 = 60.0; // Hz
        public static bool IsHumFrequency(double frequency, double humFrequency)
        {
            if (frequency == 0.0)
            {
                return false;
            }
            Debug.Assert(frequency > 0.0);
            Debug.Assert(humFrequency >= 0.0);
            const double tolerance = 1.05;
            double remainder = frequency % humFrequency;
            return (remainder < tolerance || remainder > (humFrequency - tolerance));
        }

        public static bool IsHumFrequency(double frequency) => IsHumFrequency(frequency, HumFrequency60);

        /// <summary>
        /// Find the maximum magnitude outside the audio hum range among a set of frequency magnitudes.
        /// </summary>
        /// <returns>Magnitude</returns>
        private double GetMaxNonHumMagnitude(Dictionary<double, double> frequencyMagnitudes)
        {
            double maxNonHumMagnitude = 0;
            foreach (var pair in frequencyMagnitudes)
            {
                double frequency = pair.Key;
                double magnitude = pair.Value;
                if (!IsHumFrequency(frequency))
                {
                    if (maxNonHumMagnitude < magnitude)
                    {
                        maxNonHumMagnitude = magnitude;
                    }
                }
            }
            return maxNonHumMagnitude;
        }

        /// <summary>
        /// Find the average magnitude outside the audio hum range among a set of frequency magnitudes.
        /// </summary>
        /// <returns>Magnitude</returns>
        private double GetAverageNonHumMagnitude(Dictionary<double, double> frequencyMagnitudes)
        {
            double totalNonHumMagnitude = 0;
            int count = 0;
            foreach (var pair in frequencyMagnitudes)
            {
                double frequency = pair.Key;
                double magnitude = pair.Value;
                if (magnitude < MinNoiseMagnitude)
                {
                    // Don't count silent frequencies.
                    continue;
                }
                if (!IsHumFrequency(frequency))
                {
                    totalNonHumMagnitude += magnitude;
                    count++;
                }
            }
            return (count > 0) ? (totalNonHumMagnitude / count) : 0;
        }

        /// <summary>
        /// Find the magnitude deviation outside the audio hum range among a set of frequency magnitudes.
        /// </summary>
        /// <returns>Magnitude</returns>
        private double GetDeviationNonHumMagnitude(Dictionary<double, double> frequencyMagnitudes)
        {
            /* Compute the standard deviation:
             * 1. Calculate the mean (average) of the data points.
             * 2. Find the squared differences from the mean for each data point.
             * 3. Calculate the average of the squared differences (variance).
             * 4. Take the square root of the variance to get the standard deviation.
             */
            double average = GetAverageNonHumMagnitude(frequencyMagnitudes);
            double totalNonHumDifference = 0;
            int count = 0;
            foreach (var pair in frequencyMagnitudes)
            {
                double frequency = pair.Key;
                double magnitude = pair.Value;
                if (magnitude < MinNoiseMagnitude)
                {
                    // Don't count silent frequencies.
                    continue;
                }
                if (!IsHumFrequency(frequency))
                {
                    double deviation = magnitude - average;
                    totalNonHumDifference += deviation * deviation;
                    count++;
                }
            }
            double averageNonHumDifference = (count > 0) ? (totalNonHumDifference / count) : 0;
            double standardDeviation = Math.Sqrt(averageNonHumDifference);
            return standardDeviation;
        }

        /// <summary>
        /// Find the average magnitude inside the audio hum range among a set of frequency magnitudes.
        /// </summary>
        /// <returns>Magnitude</returns>
        private double GetAverageHumMagnitude(Dictionary<double, double> frequencyMagnitudes)
        {
            double totalHumMagnitude = 0;
            int count = 0;
            foreach (var pair in frequencyMagnitudes)
            {
                double frequency = pair.Key;
                double magnitude = pair.Value;
                if (magnitude < MinNoiseMagnitude)
                {
                    // Don't count silent frequencies.
                    continue;
                }
                if (IsHumFrequency(frequency))
                {
                    totalHumMagnitude += magnitude;
                    count++;
                }
            }
            return (count > 0) ? (totalHumMagnitude / count) : 0;
        }

        /// <summary>
        /// Find the maximum magnitude outside the audio hum range.
        /// </summary>
        /// <param name="channel">Channel, or null for all</param>
        /// <returns>Magnitude</returns>
        public double GetMaxNonHumMagnitude(int? channel = null) => GetMaxNonHumMagnitude(GetFrequencyMagnitudes(channel));

#if false
        /// <summary>
        /// Find the average magnitude outside the audio hum range.
        /// </summary>
        /// <param name="channel">Channel, or null for all</param>
        /// <returns>Magnitude</returns>
        public double GetAverageNonHumMagnitude(int? channel = null) => GetAverageNonHumMagnitude(GetFrequencyMagnitudes(channel));

        /// <summary>
        /// Find the magnitude deviation outside the audio hum range.
        /// </summary>
        /// <param name="channel">Channel, or null for all</param>
        /// <returns>Magnitude</returns>
        public double GetDeviationNonHumMagnitude(int? channel = null) => GetDeviationNonHumMagnitude(GetFrequencyMagnitudes(channel));

        /// <summary>
        /// Find the average magnitude inside the audio hum range.
        /// </summary>
        /// <param name="channel">Channel, or null for all</param>
        /// <returns>Magnitude</returns>
        public double GetAverageHumMagnitude(int? channel = null) => GetAverageHumMagnitude(GetFrequencyMagnitudes(channel));

        /// <summary>
        /// Find the maximum decibels outside the audio hum range.
        /// </summary>
        /// <param name="channel">Channel, or null for all</param>
        /// <returns>Decibels</returns>
        public double GetMaxNonHumDecibels(int? channel = null) => MagnitudeToDecibels(GetMaxNonHumMagnitude(channel));
#endif

        /// <summary>
        /// Find the average decibels outside the audio hum range.
        /// </summary>
        /// <param name="channel">Channel, or null for all</param>
        /// <returns>Decibels</returns>
        public double GetAverageNonHumDecibels(int? channel = null) => GetAverageDecibels(NonHumChannelDatasets, channel);

#if false
        /// <summary>
        /// Find the decibel deviation outside the audio hum range.
        /// Note: This is not currently used, but might be in the future.
        /// </summary>
        /// <param name="channel">Channel, or null for all</param>
        /// <returns>Decibels</returns>
        public double GetDeviationNonHumDecibels(int? channel = null) => MagnitudeToDecibels(GetDeviationNonHumMagnitude(channel));
#endif

        /// <summary>
        /// Find the average decibels in the specified datasets.
        /// </summary>
        /// <param name="datasets">Datasets to check</param>
        /// <param name="channel">Channel, or null for all</param>
        /// <returns>Decibels</returns>
        private double GetAverageDecibels(List<List<object>> datasets, int? channel = null)
        {
            int index = 0;
            int count = 0;
            double totalDecibels = 0;
            foreach (var dataset in datasets)
            {
                if (channel == null || channel == index)
                {
                    foreach (var point in dataset)
                    {
                        double decibels = ((dynamic)point).y;
                        if (decibels > double.NegativeInfinity)
                        {
                            totalDecibels += decibels;
                            count++;
                        }
                    }
                }
                index++;
            }
            return totalDecibels / (count > 0 ? count : 1); // Avoid division by zero
        }


        /// <summary>
        /// Find the average decibels inside the audio hum range.
        /// </summary>
        /// <param name="channel">Channel, or null for all</param>
        /// <returns>Decibels</returns>
        public double GetAverageHumDecibels(int? channel = null) => GetAverageDecibels(HumChannelDatasets, channel);

        /// <summary>
        /// Find the total magnitude outside the audio hum range among a given set of frequency magnitudes.
        /// </summary>
        /// <returns>Magnitude</returns>
        private double GetTotalNonHumMagnitude(Dictionary<double, double> frequencyMagnitudes)
        {
            double totalNonHumMagnitude = 0;
            foreach (var pair in frequencyMagnitudes)
            {
                double frequency = pair.Key;
                double magnitude = pair.Value;
                if (!IsHumFrequency(frequency))
                {
                    totalNonHumMagnitude += magnitude;
                }
            }
            return totalNonHumMagnitude;
        }

        /// <summary>
        /// Find the total magnitude outside the audio hum range.
        /// </summary>
        /// <param name="channel">Channel, or null for all</param>
        /// <returns>Magnitude</returns>
        public double GetTotalNonHumMagnitude(int? channel = null) => GetTotalNonHumMagnitude(GetFrequencyMagnitudes(channel));

        /// <summary>
        /// Find the total magnitude of the audio hum range among a given set of frequency magnitudes.
        /// </summary>
        /// <returns>Magnitude</returns>
        public double GetTotalHumMagnitude(Dictionary<double, double> frequencyMagnitudes)
        {
            double totalHumMagnitude = 0;
            foreach (var pair in frequencyMagnitudes)
            {
                double frequency = pair.Key;
                double magnitude = pair.Value;
                if (IsHumFrequency(frequency))
                {
                    totalHumMagnitude += magnitude;
                }
            }
            return totalHumMagnitude;
        }

        /// <summary>
        /// Find the total magnitude of the audio hum range.
        /// </summary>
        /// <param name="channel">Channel, or null for all</param>
        /// <returns>Magnitude</returns>
        public double GetTotalHumMagnitude(int? channel = null) => GetTotalHumMagnitude(GetFrequencyMagnitudes(channel));

        private OrcanodeOnlineStatus GetStatus(OrcanodeOnlineStatus oldStatus, int? channel = null)
        {
            double maxMagnitude = GetMaxMagnitude(channel);
            double maxDecibels = MagnitudeToDecibels(maxMagnitude);
            if (maxDecibels < MinNoiseDecibels)
            {
                // File contains mostly silence across all frequencies.
                return OrcanodeOnlineStatus.Silent;
            }

            if (maxDecibels <= MaxSilenceDecibels)
            {
                // In between the min and max silence range, so keep previous status.
                return oldStatus;
            }

            // Find the total magnitude outside the audio hum range.
            double maxNonHumMagnitude = GetMaxNonHumMagnitude(channel);
            double maxNonHumDecibels = MagnitudeToDecibels(maxNonHumMagnitude);
            if (maxNonHumDecibels < MinNoiseDecibels)
            {
                // Just silence outside the hum range, no signal.
                return OrcanodeOnlineStatus.Unintelligible;
            }

            double totalNonHumMagnitude = GetTotalNonHumMagnitude(channel);
            double totalHumMagnitude = GetTotalHumMagnitude(channel);
            double signalRatio = totalNonHumMagnitude / totalHumMagnitude;
            if (signalRatio < MinSignalRatio)
            {
                // Essentially just silence outside the hum range, no signal.
                return OrcanodeOnlineStatus.Unintelligible;
            }

            // Signal outside the hum range.
            return OrcanodeOnlineStatus.Online;
        }
    }
}
