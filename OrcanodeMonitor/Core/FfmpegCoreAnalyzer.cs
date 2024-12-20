﻿// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using FFMpegCore;
using FFMpegCore.Pipes;
using MathNet.Numerics.IntegralTransforms;
using NAudio.Wave;
using OrcanodeMonitor.Models;
using System.Numerics;

namespace OrcanodeMonitor.Core
{
    public class FrequencyInfo
    {
        public Dictionary<double, double> FrequencyAmplitudes { get; set; }
        public OrcanodeOnlineStatus Status { get; set; }
    }

    public class FfmpegCoreAnalyzer
    {
        // We consider anything above this average amplitude as not silence.
        const double _defaultMaxSilenceAmplitude = 20.0;
        private static double MaxSilenceAmplitude
        {
            get
            {
                string? maxSilenceAmplitudeString = Environment.GetEnvironmentVariable("ORCASOUND_MAX_SILENCE_AMPLITUDE");
                double maxSilenceAmplitude = double.TryParse(maxSilenceAmplitudeString, out var amplitude) ? amplitude : _defaultMaxSilenceAmplitude;
                return maxSilenceAmplitude;
            }
        }

        // We consider anything below this average amplitude as silence.
        const double _defaultMinNoiseAmplitude = 15.0;
        private static double MinNoiseAmplitude
        {
            get
            {
                string? minNoiseAmplitudeString = Environment.GetEnvironmentVariable("ORCASOUND_MIN_NOISE_AMPLITUDE");
                double minNoiseAmplitude = double.TryParse(minNoiseAmplitudeString, out var amplitude) ? amplitude : _defaultMinNoiseAmplitude;
                return minNoiseAmplitude;
            }
        }

        // Minimum ratio of amplitude outside the hum range to amplitude
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

        // Microphone audio hum typically falls within the 50 Hz to 60 Hz
        // range. This hum is often caused by electrical interference from
        // power lines and other electronic devices.
        const double MinHumFrequency = 50.0; // Hz
        const double MaxHumFrequency = 60.0; // Hz

        private static bool IsHumFrequency(double frequency) => (frequency >= MinHumFrequency && frequency <= MaxHumFrequency);

        public static Dictionary<double, double> ComputeFrequencyAmplitudes(float[] data, int sampleRate)
        {
            var result = new Dictionary<double, double>();
            int n = data.Length;
            Complex[] complexData = data.Select(d => new Complex(d, 0)).ToArray();
            Fourier.Forward(complexData, FourierOptions.Matlab);
            for (int i = 0; i < n / 2; i++)
            {
                double amplitude = complexData[i].Magnitude;
                double frequency = (((double)i) * sampleRate) / n;
                result[frequency] = amplitude;
            }
            return result;
        }

        private static FrequencyInfo AnalyzeFrequencies(float[] data, int sampleRate, OrcanodeOnlineStatus oldStatus)
        {
            int n = data.Length;
            FrequencyInfo frequencyInfo = new FrequencyInfo();
            frequencyInfo.FrequencyAmplitudes = ComputeFrequencyAmplitudes(data, sampleRate);

            double max = frequencyInfo.FrequencyAmplitudes.Values.Max();
            if (max < MinNoiseAmplitude)
            {
                // File contains mostly silence across all frequencies.
                frequencyInfo.Status = OrcanodeOnlineStatus.Silent;
                return frequencyInfo;
            }

            if ((max <= MaxSilenceAmplitude) && (oldStatus == OrcanodeOnlineStatus.Silent))
            {
                // In between the min and max silence range, so keep previous status.
                frequencyInfo.Status = oldStatus;
                return frequencyInfo;
            }

            // Find the maximum amplitude outside the audio hum range.
            double maxNonHumAmplitude = 0;
            foreach (var pair in frequencyInfo.FrequencyAmplitudes) {
                double frequency = pair.Key;
                double amplitude = pair.Value;
                if (!IsHumFrequency(frequency))
                {
                    if (maxNonHumAmplitude < amplitude)
                    {
                        maxNonHumAmplitude = amplitude;
                    }
                }
            }

            if (maxNonHumAmplitude / max < MinSignalRatio)
            {
                // Essentially just silence outside the hum range, no signal.
                frequencyInfo.Status = OrcanodeOnlineStatus.Unintelligible;
                return frequencyInfo;
            }

            // Signal outside the hum range.
            frequencyInfo.Status = OrcanodeOnlineStatus.Online;
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
            var outputStream = new MemoryStream(); // Create an output stream (e.g., MemoryStream)
            var pipeSink = new StreamPipeSink(outputStream);

            GlobalFFOptions.Configure(options => options.BinaryFolder = FFMpegInstaller.InstallationDirectory);

            bool ok = await args
                .OutputToPipe(pipeSink, options => options
                .WithAudioCodec("pcm_s16le")
                .ForceFormat("wav"))
                .ProcessAsynchronously();

            var waveFormat = new WaveFormat(rate: 44100, bits: 16, channels: 1);
            var rawStream = new RawSourceWaveStream(outputStream, waveFormat);

            // Reset the position to the beginning
            rawStream.Seek(0, SeekOrigin.Begin);

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
