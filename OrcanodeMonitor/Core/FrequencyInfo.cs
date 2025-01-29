// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using MathNet.Numerics.IntegralTransforms;
using OrcanodeMonitor.Models;
using System.Diagnostics;
using System.Numerics;

namespace OrcanodeMonitor.Core
{
    public class FrequencyInfo
    {
        /// <summary>
        /// Given an audio clip, compute frequency info for it.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="sampleRate"></param>
        /// <param name="channels"></param>
        /// <param name="oldStatus"></param>
        public FrequencyInfo(float[] data, int sampleRate, int channels, OrcanodeOnlineStatus oldStatus)
        {
            ChannelCount = channels;
            FrequencyMagnitudesForChannel = new Dictionary<double, double>[channels];
            StatusForChannel = new OrcanodeOnlineStatus[channels + 1];
            FrequencyMagnitudes = new Dictionary<double, double>();
            ComputeFrequencyMagnitudes(data, sampleRate, channels);
            Status = GetStatus(oldStatus);
            for (int i = 0; i < channels; i++)
            {
                StatusForChannel[i] = GetStatus(oldStatus, i);
            }
        }

        private void ComputeFrequencyMagnitudes(float[] data, int sampleRate, int channelCount)
        {
            int n = data.Length / channelCount;

            // Create an array of complex data for each channel.
            Complex[][] complexData = new Complex[channelCount][];
            for (int ch = 0; ch < channelCount; ch++)
            {
                complexData[ch] = new Complex[n];
            }

            // Populate the complex arrays with channel data.
            for (int i = 0; i < n; i++)
            {
                for (int ch = 0; ch < channelCount; ch++)
                {
                    complexData[ch][i] = new Complex(data[i * channelCount + ch], 0);
                }
            }

            // Perform Fourier transform for each channel.
            for (int ch = 0; ch < channelCount; ch++)
            {
                Fourier.Forward(complexData[ch], FourierOptions.Matlab);
                FrequencyMagnitudesForChannel[ch] = new Dictionary<double, double>();
                for (int i = 0; i < n / 2; i++)
                {
                    double magnitude = complexData[ch][i].Magnitude;
                    double frequency = (((double)i) * sampleRate) / n;
                    FrequencyMagnitudesForChannel[ch][frequency] = magnitude;
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
                    if (FrequencyMagnitudes[kvp.Key] < kvp.Value)
                    {
                        FrequencyMagnitudes[kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        // We consider anything above this average magnitude as not silence.
        const double _defaultMaxSilenceMagnitude = 20.0;
        public static double MaxSilenceMagnitude
        {
            get
            {
                string? maxSilenceMagnitudeString = Environment.GetEnvironmentVariable("ORCASOUND_MAX_SILENCE_MAGNITUDE");
                double maxSilenceMagnitude = double.TryParse(maxSilenceMagnitudeString, out var magnitude) ? magnitude : _defaultMaxSilenceMagnitude;
                return maxSilenceMagnitude;
            }
        }

        // We consider anything below this average magnitude as silence.
        const double _defaultMinNoiseMagnitude = 15.0;
        public static double MinNoiseMagnitude
        {
            get
            {
                string? minNoiseMagnitudeString = Environment.GetEnvironmentVariable("ORCASOUND_MIN_NOISE_MAGNITUDE");
                double minNoiseMagnitude = double.TryParse(minNoiseMagnitudeString, out var magnitude) ? magnitude : _defaultMinNoiseMagnitude;
                return minNoiseMagnitude;
            }
        }

        // Minimum ratio of magnitude outside the hum range to magnitude
        // within the hum range.  So far the max in a known-unintelligible
        // sample is 53% and the min in a known-good sample is 114%.
        const double _defaultMinSignalPercent = 100;
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
        public double GetSignalRatio(int? channel = null) => GetTotalNonHumMagnitude(channel) / GetTotalHumMagnitude(channel);

        // Microphone audio hum typically falls within the 50 Hz or 60 Hz
        // range. This hum is often caused by electrical interference from
        // power lines and other electronic devices.
        const double HumFrequency1 = 50.0; // Hz
        const double HumFrequency2 = 60.0; // Hz
        private static bool IsHumFrequency(double frequency, double humFrequency)
        {
            Debug.Assert(frequency >= 0.0);
            Debug.Assert(humFrequency >= 0.0);
            const double tolerance = 1.0;
            double remainder = frequency % humFrequency;
            return (remainder < tolerance || remainder > (humFrequency - tolerance));
        }

        public static bool IsHumFrequency(double frequency) => IsHumFrequency(frequency, HumFrequency1) || IsHumFrequency(frequency, HumFrequency2);

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
        /// Find the maximum magnitude outside the audio hum range.
        /// </summary>
        /// <param name="channel">Channel, or null for all</param>
        /// <returns>Magnitude</returns>
        public double GetMaxNonHumMagnitude(int? channel = null) => GetMaxNonHumMagnitude(GetFrequencyMagnitudes(channel));

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
                    if (magnitude > MinNoiseMagnitude)
                    {
                        totalNonHumMagnitude += magnitude;
                    }
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
                    if (magnitude > MinNoiseMagnitude)
                    {
                        totalHumMagnitude += magnitude;
                    }
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
            double max = GetMaxMagnitude(channel);
            if (max < MinNoiseMagnitude)
            {
                // File contains mostly silence across all frequencies.
                return OrcanodeOnlineStatus.Silent;
            }

            if ((max <= MaxSilenceMagnitude) && (oldStatus == OrcanodeOnlineStatus.Silent))
            {
                // In between the min and max silence range, so keep previous status.
                return oldStatus;
            }

            // Find the total magnitude outside the audio hum range.
            if (GetMaxNonHumMagnitude(channel) < MinNoiseMagnitude)
            {
                // Just silence outside the hum range, no signal.
                return OrcanodeOnlineStatus.Unintelligible;
            }

            double totalNonHumMagnitude = GetTotalNonHumMagnitude(channel);
            double totalHumMagnitude = GetTotalHumMagnitude(channel);
            if (totalNonHumMagnitude / totalHumMagnitude < MinSignalRatio)
            {
                // Essentially just silence outside the hum range, no signal.
                return OrcanodeOnlineStatus.Unintelligible;
            }

            // Signal outside the hum range.
            return OrcanodeOnlineStatus.Online;
        }
    }
}
