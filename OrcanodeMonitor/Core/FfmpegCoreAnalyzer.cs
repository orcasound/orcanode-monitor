// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using FFMpegCore;
using FFMpegCore.Pipes;
using MathNet.Numerics.IntegralTransforms;
using NAudio.Wave;
using OrcanodeMonitor.Models;
using System.Numerics;

namespace OrcanodeMonitor.Core
{
    public class FfmpegCoreAnalyzer
    {
        // We consider anything below this amplitude as silence.
        const double MaxSilenceAmplitude = 20.0;

        // Microphone audio hum typically falls within the 50 Hz to 60 Hz
        // range. This hum is often caused by electrical interference from
        // power lines and other electronic devices.
        const double MinHumFrequency = 50.0; // Hz
        const double MaxHumFrequency = 60.0; // Hz

        private static OrcanodeOnlineStatus AnalyzeFrequencies(float[] data, int sampleRate)
        {
            int n = data.Length;
            Complex[] complexData = data.Select(d => new Complex(d, 0)).ToArray();
            Fourier.Forward(complexData, FourierOptions.Matlab);
            double[] amplitudes = new double[n / 2];
            for (int i = 0; i < n / 2; i++)
            {
                amplitudes[i] = complexData[i].Magnitude;
            }

            double max = amplitudes.Max();
            if (max < MaxSilenceAmplitude)
            {
                // File contains mostly silence across all frequencies.
                return OrcanodeOnlineStatus.Unintelligible;
            }

            // Look for signal in frequencies other than the audio hum range.
            double halfOfMax = amplitudes.Max() / 2.0;
            var majorOtherIndices = new List<int>();
            for (int i = 0; i < amplitudes.Length; i++)
            {
                if (amplitudes[i] > halfOfMax)
                {
                    double frequency = (((double)i) * sampleRate) / n;
                    if (frequency < MinHumFrequency || frequency > MaxHumFrequency)
                    {
                        majorOtherIndices.Add(i);
                    }
                }
            }

            if (majorOtherIndices.Count == 0)
            {
                // Essentially just silence outside the hum range, no signal.
                return OrcanodeOnlineStatus.Unintelligible;
            }

            return OrcanodeOnlineStatus.Online;
        }

        /// <summary>
        /// Get the status of the most recent audio stream sample.
        /// </summary>
        /// <param name="args">FFMpeg arguments</param>
        /// <returns>Status of the most recent audio samples</returns>
        private static async Task<OrcanodeOnlineStatus> AnalyzeAsync(FFMpegArguments args)
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

            // Perform FFT and analyze frequencies
            var status = AnalyzeFrequencies(floatBuffer, waveFormat.SampleRate);
            return status;
        }

        public static async Task<OrcanodeOnlineStatus> AnalyzeFileAsync(string filename)
        {
            var args = FFMpegArguments.FromFileInput(filename);
            return await AnalyzeAsync(args);
        }

        public static async Task<OrcanodeOnlineStatus> AnalyzeAudioStreamAsync(Stream stream)
        {
            StreamPipeSource streamPipeSource = new StreamPipeSource(stream);
            var args = FFMpegArguments.FromPipeInput(streamPipeSource);
            return await AnalyzeAsync(args);
        }
    }
}