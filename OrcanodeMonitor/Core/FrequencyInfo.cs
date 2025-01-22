// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using FFMpegCore;
using FFMpegCore.Pipes;
using MathNet.Numerics.IntegralTransforms;
using NAudio.Wave;
using OrcanodeMonitor.Models;
using System.Diagnostics;
using System.Numerics;

namespace OrcanodeMonitor.Core
{
    public class FrequencyInfo
    {
        public FrequencyInfo(float[] data, int sampleRate, int channels, OrcanodeOnlineStatus oldStatus, int? onlyChannel)
        {
            ChannelCount = channels;
            FrequencyMagnitudes = ComputeFrequencyMagnitudes(data, sampleRate, channels);
            Status = GetStatus(oldStatus);
        }

        private static Dictionary<double, double> ComputeFrequencyMagnitudes(float[] data, int sampleRate, int channels)
        {
            var result = new Dictionary<double, double>();
            int n = data.Length / channels;

            // Create an array of complex data for each channel.
            Complex[][] complexData = new Complex[channels][];
            for (int ch = 0; ch < channels; ch++)
            {
                complexData[ch] = new Complex[n];
            }

            // Populate the complex arrays with channel data.
            for (int i = 0; i < n; i++)
            {
                for (int ch = 0; ch < channels; ch++)
                {
                    complexData[ch][i] = new Complex(data[i * channels + ch], 0);
                }
            }

            // Perform Fourier transform for each channel.
            var channelResults = new List<Dictionary<double, double>>();
            for (int ch = 0; ch < channels; ch++)
            {
                Fourier.Forward(complexData[ch], FourierOptions.Matlab);
                var channelResult = new Dictionary<double, double>();
                for (int i = 0; i < n / 2; i++)
                {
                    double magnitude = complexData[ch][i].Magnitude;
                    double frequency = (((double)i) * sampleRate) / n;
                    channelResult[frequency] = magnitude;
                }
                channelResults.Add(channelResult);
            }

            // Combine results from all channels.
            foreach (var channelResult in channelResults)
            {
                foreach (var kvp in channelResult)
                {
                    if (!result.ContainsKey(kvp.Key))
                    {
                        result[kvp.Key] = 0;
                    }
                    if (result[kvp.Key] < kvp.Value)
                    {
                        result[kvp.Key] = kvp.Value;
                    }
                }
            }

            return result;
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

        public Dictionary<double, double> FrequencyMagnitudes { get; }
        public OrcanodeOnlineStatus Status { get; }
        public int ChannelCount { get; private set; } = 0;

        /// <summary>
        /// URL at which the original audio sample can be found.
        /// </summary>
        public string AudioSampleUrl { get; set; } = string.Empty;

        public double MaxMagnitude => FrequencyMagnitudes.Values.Max();

        // Channel-specific methods.
        public double GetMaxMagnitude(int channel) => 0; // TODO
        public double GetMaxNonHumMagnitude(int channel) => 0; // TODO
        public double GetTotalHumMagnitude(int channel) => 0; // TODO
        public double GetTotalNonHumMagnitude(int channel) => 0; // TODO
        public double GetSignalRatio(int channel) => 0; // TODO
        public string GetStatus(int channel) => string.Empty; // TODO

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
        /// Find the maximum magnitude outside the audio hum range.
        /// </summary>
        /// <returns>Magnitude</returns>
        public double GetMaxNonHumMagnitude()
        {
            double maxNonHumMagnitude = 0;
            foreach (var pair in FrequencyMagnitudes)
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
        /// Find the total magnitude outside the audio hum range.
        /// </summary>
        /// <returns>Magnitude</returns>
        public double GetTotalNonHumMagnitude()
        {
            double totalNonHumMagnitude = 0;
            foreach (var pair in FrequencyMagnitudes)
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
        /// Find the total magnitude of the audio hum range.
        /// </summary>
        /// <returns>Magnitude</returns>
        public double GetTotalHumMagnitude()
        {
            double totalHumMagnitude = 0;
            foreach (var pair in FrequencyMagnitudes)
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

        private OrcanodeOnlineStatus GetStatus(OrcanodeOnlineStatus oldStatus)
        {
            double max = MaxMagnitude;
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
            if (GetMaxNonHumMagnitude() < MinNoiseMagnitude)
            {
                // Just silence outside the hum range, no signal.
                return OrcanodeOnlineStatus.Unintelligible;
            }

            double totalNonHumMagnitude = GetTotalNonHumMagnitude();
            double totalHumMagnitude = GetTotalHumMagnitude();
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
