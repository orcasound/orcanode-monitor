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
     public class FfmpegCoreAnalyzer
    {
        /// <summary>
        /// Analyze frequencies.
        /// </summary>
        /// <param name="data"></param>
        /// <param name="sampleRate">Sample rate</param>
        /// <param name="channels">Number of channels</param>
        /// <param name="oldStatus">Old status</param>
        /// <param name="onlyChannel">Channel number, or null for all</param>
        /// <returns>Frequency info</returns>
        private static FrequencyInfo AnalyzeFrequencies(float[] data, int sampleRate, int channels, OrcanodeOnlineStatus oldStatus, int? onlyChannel)
        {
            int n = data.Length;
            FrequencyInfo frequencyInfo = new FrequencyInfo(data, sampleRate, channels, oldStatus, onlyChannel);
            return frequencyInfo;
        }

        /// <summary>
        /// Get the status of the most recent audio stream sample.
        /// </summary>
        /// <param name="args">FFMpeg arguments</param>
        /// <param name="oldStatus">Previous online status</param>
        /// <param name="onlyChannel">Channel number, or null for all</param>
        /// <returns>Status of the most recent audio samples</returns>
        private static async Task<FrequencyInfo> AnalyzeAsync(FFMpegArguments args, OrcanodeOnlineStatus oldStatus, int? onlyChannel)
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

                // Read the entire stream into a byte array.
                byte[] byteBuffer = outputStream.ToArray();

                // Get the number of channels in the WAV file (offset 22, 2 bytes).
                int channels = BitConverter.ToInt16(byteBuffer, 22);

                // Get the sample rate in the WAV file (offset 24, 4 bytes).
                int sampleRate = BitConverter.ToInt32(byteBuffer, 24);

                var waveFormat = new WaveFormat(rate: sampleRate, bits: 16, channels: channels);

                // Compute the duration in seconds.
                double byteRate = waveFormat.SampleRate * waveFormat.Channels * (waveFormat.BitsPerSample / 8.0);
                double durationInSeconds = byteBuffer.Length / byteRate;

                // Convert byte buffer to float buffer.
                var floatBuffer = new float[byteBuffer.Length / sizeof(short)];
                for (int i = 0; i < floatBuffer.Length; i++)
                {
                    floatBuffer[i] = BitConverter.ToInt16(byteBuffer, i * sizeof(short)) / 32768f;
                }

                // Perform FFT and analyze frequencies.
                var status = AnalyzeFrequencies(floatBuffer, waveFormat.SampleRate, waveFormat.Channels, oldStatus, onlyChannel);
                return status;
            }
        }

        public static async Task<FrequencyInfo> AnalyzeFileAsync(string filename, OrcanodeOnlineStatus oldStatus)
        {
            var args = FFMpegArguments.FromFileInput(filename);
            return await AnalyzeAsync(args, oldStatus, null);
        }

        public static async Task<FrequencyInfo> AnalyzeAudioStreamAsync(Stream stream, OrcanodeOnlineStatus oldStatus)
        {
            StreamPipeSource streamPipeSource = new StreamPipeSource(stream);
            var args = FFMpegArguments.FromPipeInput(streamPipeSource);
            return await AnalyzeAsync(args, oldStatus, null);
        }
    }
}
