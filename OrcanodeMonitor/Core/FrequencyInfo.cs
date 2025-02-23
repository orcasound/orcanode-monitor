// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using MathNet.Numerics.IntegralTransforms;
using OrcanodeMonitor.Models;
using System.Diagnostics;
using System.Numerics;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace OrcanodeMonitor.Core
{
    public class FrequencyInfo
    {
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
            Status = GetStatus(oldStatus);
            for (int i = 0; i < channels; i++)
            {
                StatusForChannel[i] = GetStatus(oldStatus, i);
            }
        }

        private void ComputeFrequencyMagnitudes(float[] data, uint sampleRate, int channelCount, int windowSize = 8192)
        {
            if (data == null || data.Length == 0)
            {
                throw new ArgumentException("Audio data cannot be null or empty", nameof(data));
            }
            if (sampleRate <= 0)
            {
                throw new ArgumentException("Sample rate must be positive", nameof(sampleRate));
            }
            if (data.Length % channelCount != 0)
            {
                throw new ArgumentException("Data length must be divisible by channel count", nameof(data));
            }

            int overlap = windowSize / 2; // 50% overlap
            int totalSamples = data.Length / channelCount;
            int totalWindows = (totalSamples - windowSize) / overlap + 1;

            // Create an array of complex data for each channel.
            Complex[][] complexData = new Complex[channelCount][];
            for (int ch = 0; ch < channelCount; ch++)
            {
                complexData[ch] = new Complex[windowSize];
                FrequencyMagnitudesForChannel[ch] = new Dictionary<double, double>();
            }

            // Apply overlapping windows and perform FFT.
            for (int ch = 0; ch < channelCount; ch++)
            {
                for (int windowIndex = 0; windowIndex < totalWindows; windowIndex++)
                {
                    // Apply the Hann window and populate complex data arrays for each window.
                    for (int i = 0; i < windowSize; i++)
                    {
                        int dataIndex = (windowIndex * overlap * channelCount) + (i * channelCount) + ch;
                        double window = 0.5 * (1 - Math.Cos(2 * Math.PI * i / (windowSize - 1))); // Hann window
                        complexData[ch][i] = new Complex(data[dataIndex] * window, 0);
                    }

                    // Perform the FFT on the current window.
                    Fourier.Forward(complexData[ch], FourierOptions.Matlab);

                    // Only the first half of the FFT output contains useful information,
                    // since the second half corresponds to negative frequencies.
                    for (int i = 0; i < windowSize / 2; i++)
                    {
                        double magnitude = complexData[ch][i].Magnitude;
                        double frequency = (((double)i) * sampleRate) / windowSize;
                        if (!FrequencyMagnitudesForChannel[ch].ContainsKey(frequency))
                        {
                            FrequencyMagnitudesForChannel[ch][frequency] = 0;
                        }
                        if (FrequencyMagnitudesForChannel[ch][frequency] < magnitude)
                        {
                            FrequencyMagnitudesForChannel[ch][frequency] = magnitude;
                        }
                    }
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

                    // The audacity manual states:
                    // "The spectrum analysis shows the sum of all selected channels,
                    // so shows the sum of left and right channels in a stereo track.
                    // If there are two selected channels with identical audio, the
                    // same peak will be 6 dB higher than if one channel was selected."
                    FrequencyMagnitudes[kvp.Key] += kvp.Value;
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
