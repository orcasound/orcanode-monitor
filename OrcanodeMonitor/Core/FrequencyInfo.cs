// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using FftSharp;
using OrcanodeMonitor.Models;
using System.Diagnostics;
using System.Numerics;

namespace OrcanodeMonitor.Core
{
    public class FrequencyInfo
    {
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
        private static bool IsHumFrequency(double frequency, double humFrequency)
        {
            if (frequency == 0.0)
            {
                return false;
            }
            Debug.Assert(frequency > 0.0);
            Debug.Assert(humFrequency >= 0.0);
            const double tolerance = 1.0;
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
                if (!IsHumFrequency(frequency))
                {
                    totalNonHumMagnitude += magnitude;
                    count++;
                }
            }
            return totalNonHumMagnitude / count;
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
                if (!IsHumFrequency(frequency))
                {
                    double deviation = magnitude - average;
                    totalNonHumDifference += deviation * deviation;
                    count++;
                }
            }
            double averageNonHumDifference = totalNonHumDifference / count;
            double standardDeviation = Math.Sqrt(averageNonHumDifference);
            return standardDeviation;
        }

        /// <summary>
        /// Find the average magnitude inside the audio hum range among a set of frequency magnitudes.
        /// </summary>
        /// <returns>Magnitude</returns>
        private double GetAverageHumMagnitude(Dictionary<double, double> frequencyMagnitudes)
        {
            double totalNonHumMagnitude = 0;
            int count = 0;
            foreach (var pair in frequencyMagnitudes)
            {
                double frequency = pair.Key;
                double magnitude = pair.Value;
                if (IsHumFrequency(frequency))
                {
                    totalNonHumMagnitude += magnitude;
                    count++;
                }
            }
            return totalNonHumMagnitude / count;
        }

        /// <summary>
        /// Find the maximum magnitude outside the audio hum range.
        /// </summary>
        /// <param name="channel">Channel, or null for all</param>
        /// <returns>Magnitude</returns>
        public double GetMaxNonHumMagnitude(int? channel = null) => GetMaxNonHumMagnitude(GetFrequencyMagnitudes(channel));

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

        /// <summary>
        /// Find the average decibels outside the audio hum range.
        /// </summary>
        /// <param name="channel">Channel, or null for all</param>
        /// <returns>Decibels</returns>
        public double GetAverageNonHumDecibels(int? channel = null) => MagnitudeToDecibels(GetAverageNonHumMagnitude(channel));

        /// <summary>
        /// Find the decibel deviation outside the audio hum range.
        /// Note: This is not currently used, but might be in the future.
        /// </summary>
        /// <param name="channel">Channel, or null for all</param>
        /// <returns>Decibels</returns>
        public double GetDeviationNonHumDecibels(int? channel = null) => MagnitudeToDecibels(GetDeviationNonHumMagnitude(channel));

        /// <summary>
        /// Find the average decibels inside the audio hum range.
        /// </summary>
        /// <param name="channel">Channel, or null for all</param>
        /// <returns>Decibels</returns>
        public double GetAverageHumDecibels(int? channel = null) => MagnitudeToDecibels(GetAverageHumMagnitude(channel));

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
