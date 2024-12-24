// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using FFMpegCore;
using FFMpegCore.Pipes;
using MathNet.Numerics.IntegralTransforms;
using NAudio.Wave;
using OrcanodeMonitor.Models;
using System.Diagnostics;
using System.IO;
using System.Numerics;

namespace OrcanodeMonitor.Core
{
    public class FrequencyInfo
    {
        public FrequencyInfo(float[] data, int sampleRate, OrcanodeOnlineStatus oldStatus)
        {
            FrequencyMagnitudes = ComputeFrequencyMagnitudes(data, sampleRate);
            Status = GetStatus(oldStatus);
        }

        private static Dictionary<double, double> ComputeFrequencyMagnitudes(float[] data, int sampleRate)
        {
            var result = new Dictionary<double, double>();
            int n = data.Length;
            Complex[] complexData = data.Select(d => new Complex(d, 0)).ToArray();
            Fourier.Forward(complexData, FourierOptions.Matlab);
            for (int i = 0; i < n / 2; i++)
            {
                double magnitude = complexData[i].Magnitude;
                double frequency = (((double)i) * sampleRate) / n;
                result[frequency] = magnitude;
            }
            return result;
        }

        // We consider anything above this average magnitude as not silence.
        const double _defaultMaxSilenceMagnitude = 20.0;
        private static double MaxSilenceMagnitude
        {
            get
            {
                string? maxSilenceMagnitudeString = Environment.GetEnvironmentVariable("ORCASOUND_MAX_SILENCE_AMPLITUDE");
                double maxSilenceMagnitude = double.TryParse(maxSilenceMagnitudeString, out var magnitude) ? magnitude : _defaultMaxSilenceMagnitude;
                return maxSilenceMagnitude;
            }
        }

        // We consider anything below this average magnitude as silence.
        const double _defaultMinNoiseMagnitude = 15.0;
        private static double MinNoiseMagnitude
        {
            get
            {
                string? minNoiseMagnitudeString = Environment.GetEnvironmentVariable("ORCASOUND_MIN_NOISE_AMPLITUDE");
                double minNoiseMagnitude = double.TryParse(minNoiseMagnitudeString, out var magnitude) ? magnitude : _defaultMinNoiseMagnitude;
                return minNoiseMagnitude;
            }
        }

        // Minimum ratio of magnitude outside the hum range to magnitude
        // within the hum range.  So far the max in a known-unintelligible
        // sample is 21% and the min in a known-good sample is 50%.
        const double _defaultMinSignalPercent = 30;
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
        public double MaxMagnitude => FrequencyMagnitudes.Values.Max();

        // Microphone audio hum typically falls within the 50 Hz to 60 Hz
        // range. This hum is often caused by electrical interference from
        // power lines and other electronic devices.
        const double MinHumFrequency = 50.0; // Hz
        const double MaxHumFrequency = 60.0; // Hz

        private static bool IsHumFrequency(double frequency) => (frequency >= MinHumFrequency && frequency <= MaxHumFrequency);

        /// <summary>
        /// Find the maximum mangnitude outside the audio hum range.
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

            // Find the maximum magnitude outside the audio hum range.
            double maxNonHumMagnitude = GetMaxNonHumMagnitude();

            if (maxNonHumMagnitude / max < MinSignalRatio)
            {
                // Essentially just silence outside the hum range, no signal.
                return OrcanodeOnlineStatus.Unintelligible;
            }

            // Signal outside the hum range.
            return OrcanodeOnlineStatus.Online;
        }
    }

    public class FfmpegCoreAnalyzer
    {
        private static FrequencyInfo AnalyzeFrequencies(float[] data, int sampleRate, OrcanodeOnlineStatus oldStatus)
        {
            int n = data.Length;
            FrequencyInfo frequencyInfo = new FrequencyInfo(data, sampleRate, oldStatus);
            return frequencyInfo;
        }

        /// <summary>
        /// Get the status of the most recent audio stream sample.
        /// </summary>
        /// <param name="args">FFMpeg arguments</param>
        /// <param name="oldStatus">Previous online status</param>
        /// <returns>Status of the most recent audio samples</returns>
        private static async Task<FrequencyInfo> AnalyzeAsync(FFMpegArguments args, OrcanodeOnlineStatus oldStatus)
        {
            using (var outputStream = new MemoryStream())
            {
                // Create an output stream (e.g., MemoryStream).
                var pipeSink = new StreamPipeSink(outputStream);
                GlobalFFOptions.Configure(options => options.BinaryFolder = FFMpegInstaller.InstallationDirectory);

                bool ok = await args
                    .OutputToPipe(pipeSink, options => options
                    .WithAudioCodec("pcm_s16le")
                    .ForceFormat("wav"))
                    .ProcessAsynchronously();
                if (!ok)
                {
                    throw new Exception("FFMpeg processing failed.");
                }

                // Get the number of channels in the WAV file, which is encoded
                // in a 2-byte field at offset 22.
                outputStream.Seek(22, SeekOrigin.Begin);
                int channels;
                using (BinaryReader reader = new BinaryReader(outputStream, System.Text.Encoding.Default, true))
                {
                    channels = reader.ReadInt16();
                }

                var waveFormat = new WaveFormat(rate: 44100, bits: 16, channels: channels);
                var rawStream = new RawSourceWaveStream(outputStream, waveFormat);

                // Reset the position to the beginning.
                rawStream.Seek(0, SeekOrigin.Begin);

                // Compute the duration in seconds.
                long totalBytes = rawStream.Length;
                double byteRate = waveFormat.SampleRate * waveFormat.Channels * (waveFormat.BitsPerSample / 8.0);
                double durationInSeconds = totalBytes / byteRate;

                // Read the audio data into a byte buffer.
                var byteBuffer = new byte[rawStream.Length];
                int bytesRead = rawStream.Read(byteBuffer, 0, byteBuffer.Length);

                // Convert byte buffer to float buffer.
                var floatBuffer = new float[byteBuffer.Length / sizeof(short)];
                for (int i = 0; i < floatBuffer.Length; i++)
                {
                    floatBuffer[i] = BitConverter.ToInt16(byteBuffer, i * sizeof(short)) / 32768f;
                }

                // Perform FFT and analyze frequencies.
                var status = AnalyzeFrequencies(floatBuffer, waveFormat.SampleRate, oldStatus);
                return status;
            }
        }

        public static async Task<FrequencyInfo> AnalyzeFileAsync(string filename, OrcanodeOnlineStatus oldStatus)
        {
            var args = FFMpegArguments.FromFileInput(filename);
            return await AnalyzeAsync(args, oldStatus);
        }

        public static async Task<FrequencyInfo> AnalyzeAudioStreamAsync(Stream stream, OrcanodeOnlineStatus oldStatus)
        {
            StreamPipeSource streamPipeSource = new StreamPipeSource(stream);
            var args = FFMpegArguments.FromPipeInput(streamPipeSource);
            return await AnalyzeAsync(args, oldStatus);
        }
    }
}
