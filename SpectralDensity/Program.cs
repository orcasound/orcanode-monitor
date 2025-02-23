using FFMpegCore;
using FFMpegCore.Pipes;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using MathNet.Numerics.Random;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Azure.Cosmos;
using Microsoft.Data.SqlClient.DataClassification;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json.Linq;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using System;
using System.Collections;
using System.Numerics;
using System.Reflection.Emit;
using System.Runtime.InteropServices;
using System.Text;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace OrcanodeMonitor
{
    public class Program
    {
        /// <summary>
        /// Maximum frequency to analyze in Hz.
        /// Typical human hearing range is up to 20kHz.
        /// Orca calls are up to 40kHz.
        /// </summary>
        private const int MAX_FREQUENCY = 24000;

        /// <summary>
        /// Number of points to plot on the graph. 1000 points provides a good balance
        /// between resolution and performance.
        /// </summary>
        private const int POINT_COUNT = 1000;

        private static void FillInGraphPoints(FrequencyInfo frequencyInfo, List<string> labels, List<double> maxBucketMagnitudeList, int? channel = null)
        {
            if (frequencyInfo == null)
            {
                return;
            }

            // Compute the logarithmic base needed to get PointCount points.
            double b = Math.Pow(MAX_FREQUENCY, 1.0 / POINT_COUNT);
            double logb = Math.Log(b);

            var maxBucketMagnitude = new double[POINT_COUNT];
            var maxBucketFrequency = new int[POINT_COUNT];
            foreach (var pair in frequencyInfo.GetFrequencyMagnitudes(channel))
            {
                double frequency = pair.Key;
                double magnitude = pair.Value;
                int bucket = (frequency < 1) ? 0 : Math.Min(POINT_COUNT - 1, (int)(Math.Log(frequency) / logb));
                if (maxBucketMagnitude[bucket] < magnitude)
                {
                    maxBucketMagnitude[bucket] = magnitude;
                    maxBucketFrequency[bucket] = (int)Math.Round(frequency);
                }
            }
            for (int i = 0; i < POINT_COUNT; i++)
            {
                if (maxBucketMagnitude[i] > 0)
                {
                    labels.Add(maxBucketFrequency[i].ToString());
                    maxBucketMagnitudeList.Add(maxBucketMagnitude[i]);
                }
            }
        }

        // Convert to decibels
        // But do it safely; -Inf is nobody's friend
        public static double MagnitudeToDecibels(double magnitude, int windowSize, int windowCount)
        {
            double temp = magnitude / windowSize / windowCount;
            if (temp > 0.0)
            {
                return 10 * Math.Log10(temp);
            }
            else
            {
                return 0;
            }
        }

        public static T ByteArrayToStructure<T>(byte[] bytes, int offset) where T : struct
        {
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try
            {
                IntPtr ptr = (IntPtr)(handle.AddrOfPinnedObject().ToInt64() + offset);
                return (T)Marshal.PtrToStructure(ptr, typeof(T));
            }
            finally
            {
                handle.Free();
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct RiffHeader
        {
            public uint ChunkId;        // 'RIFF'
            public uint ChunkSize;      // File size minus 8 bytes for 'RIFF' and 'WAVE'
            public uint Format;         // 'WAVE'
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct FmtChunk
        {
            public uint ChunkID;
            public uint ChunkSize;
            public short AudioFormat;
            public short NumChannels;
            public uint SampleRate;
            public uint ByteRate;
            public short BlockAlign;
            public short BitsPerSample;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct ListChunk
        {
            public uint ChunkID;
            public uint ChunkSize;
            public uint TypeID;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct InfoChunk
        {
            public uint ChunkID;
            public uint ChunkSize;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 1)]
            public byte[] SubchunkData;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        public struct DataChunk
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            private byte[] subchunk2Id;
            public uint Subchunk2Size;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 0)]
            public byte[] Data;

            public string Subchunk2Id
            {
                get => Encoding.ASCII.GetString(subchunk2Id);
                set => subchunk2Id = Encoding.ASCII.GetBytes(value.PadRight(4).Substring(0, 4));
            }
        }

        public static async Task Analyzer(string filePath)
        {
            GlobalFFOptions.Configure(options => options.BinaryFolder = FFMpegInstaller.InstallationDirectory);
            var args = FFMpegArguments.FromFileInput(filePath);
            var outputStream = new MemoryStream();
            var pipeSink = new StreamPipeSink(outputStream);
            bool ok = await args
                .OutputToPipe(pipeSink, options => options
                .WithAudioCodec("pcm_s16le")
                .ForceFormat("wav"))
                .ProcessAsynchronously();
            if (!ok)
            {
                throw new Exception("FFMpeg processing failed.");
            }
            byte[] byteBuffer = outputStream.ToArray();

            // Process the 12-byte RIFF header.
            int offset = 0;
            string headerType = Encoding.ASCII.GetString(byteBuffer, offset, 4);
            if (headerType != "RIFF")
            {
                throw new ArgumentException("Malformed data", nameof(headerType));
            }
            RiffHeader riffHeader = ByteArrayToStructure<RiffHeader>(byteBuffer, offset);
            offset += 12;

            FmtChunk fmtChunk = default(FmtChunk);
            ListChunk listChunk = default(ListChunk);
            DataChunk dataChunk = default(DataChunk);
            int subchunkSize = 0;
            int dataOffset = 0;
            int dataSize = 0;

            // Process the next chunk.
            do
            {
                headerType = Encoding.ASCII.GetString(byteBuffer, offset, 4);
                subchunkSize = BitConverter.ToInt32(byteBuffer, offset + 4);
                if (subchunkSize == -1)
                {
                    subchunkSize = byteBuffer.Length - offset - 8;
                }
                switch (headerType)
                {
                    case "fmt ":
                        fmtChunk = ByteArrayToStructure<FmtChunk>(byteBuffer, offset);
                        if (fmtChunk.SampleRate <= 0)
                        {
                            throw new ArgumentException("Sample rate must be positive", nameof(fmtChunk.SampleRate));
                        }
                        break;
                    case "LIST":
                        listChunk = ByteArrayToStructure<ListChunk>(byteBuffer, offset);
                        break;
                    case "data":
                        dataOffset = offset + 8;
                        dataSize = byteBuffer.Length - dataOffset;
                        break;
                }
                offset += 8 + subchunkSize;
            } while (offset < byteBuffer.Length);

            if (dataSize == 0)
            {
                throw new ArgumentException("Malformed data", nameof(headerType));
            }

            // Convert byte buffer to float buffer.
            var data = new float[dataSize / sizeof(short)];
            if (data == null || data.Length == 0)
            {
                throw new ArgumentException("Audio data cannot be null or empty", nameof(data));
            }
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = BitConverter.ToInt16(byteBuffer, dataOffset + i * sizeof(short)) / 32768f;
            }
            int windowSize = 8192;
            int overlap = windowSize / 2; // 50% overlap
            int totalSamples = data.Length / fmtChunk.NumChannels;
            const double maxPossibleValue = 32767.0; // Maximum possible value for 16-bit PCM audio
            List<double> magnitudes = new List<double>();

            // Create an array of complex data for each channel.
            Complex[][] complexData = new Complex[fmtChunk.NumChannels][];
            for (int ch = 0; ch < fmtChunk.NumChannels; ch++)
            {
                complexData[ch] = new Complex[windowSize];
                //FrequencyMagnitudesForChannel[ch] = new Dictionary<double, double>();
            }

            for (int windowIndex = 0; windowIndex <= totalSamples - windowSize; windowIndex += overlap)
            {
                for (int ch = 0; ch < fmtChunk.NumChannels; ch++)
                {
                    // Form complexData from data while applying a Hann window.
                    for (int i = 0; i < windowSize; i++)
                    {
                        int dataIndex = windowIndex + i;
                        double multiplier = 2 * Math.PI / windowSize;
                        double coeff0 = 0.5;
                        double coeff1 = -0.5;
                        double window = coeff0 + coeff1 * Math.Cos(i * multiplier); // Apply Hann window
                        complexData[ch][i] = new Complex(data[dataIndex] * window, 0);
                    }

                    // Perform the FFT on the current window.
                    Fourier.Forward(complexData[ch], FourierOptions.Matlab); // Use Matlab option for FFT

                    // Collect magnitudes.
                    for (int i = 0; i < windowSize / 2; i++)
                    {
                        double magnitude = complexData[ch][i].Magnitude;
                        magnitudes.Add(magnitude);
                    }
                }
            }

            // Average magnitudes.
            int numWindows = (totalSamples - windowSize) / overlap + 1;
            double[] avgMagnitudes = new double[windowSize / 2];
            for (int i = 0; i < windowSize / 2; i++)
            {
                avgMagnitudes[i] = magnitudes.Skip(i * numWindows).Take(numWindows).Average();
            }

            Console.WriteLine("Frequency (Hz)  Level (dB) <== analyze");
            for (int i = 0; i < avgMagnitudes.Length; i++)
            {
                double frequency = i * 1.0 * fmtChunk.SampleRate / windowSize;
                double temp = avgMagnitudes[i] / windowSize / numWindows;
                double dB = temp > 0 ? 10 * Math.Log10(temp) : 0;
                //double dB = 20 * Math.Log10(avgMagnitudes[i] / maxPossibleValue);
                //double dB = MagnitudeToDecibels(avgMagnitudes[i] / maxPossibleValue, windowSize, numWindows);

                // Print frequency and dB with specified column widths
                Console.WriteLine($"{frequency,-15:F6} {dB,-15:F6}");
            }
        }

        public static async Task Main(string[] args)
        {
            string filePath = "C:\\Users\\dthal\\git\\orcasound\\orcanode-monitor\\OrcanodeMonitor\\output.wav";
            await Analyzer(filePath);
            return;


            FrequencyInfo frequencyInfo = await FfmpegCoreAnalyzer.AnalyzeFileAsync(filePath, OrcanodeOnlineStatus.Online);
            
            for (int channel = 0; channel < frequencyInfo.ChannelCount; channel++)
            {
                var frequencyMagnitudes = frequencyInfo.GetFrequencyMagnitudes(channel);

                double maxPossibleValue = 32767.0; // Maximum possible value for 16-bit PCM audio

                Console.WriteLine("Frequency (Hz)  Level (dB) <== program");
                foreach (var item in frequencyMagnitudes)
                {
                    double frequency = item.Key;
                    double magnitude = item.Value;

                    // Normalize magnitude using the maximum possible value
                    double normalizedMagnitude = magnitude / maxPossibleValue;

                    // Convert to dB
                    double dB = 20 * Math.Log10(normalizedMagnitude);

                    Console.WriteLine($"{frequency, -15:F6} {dB, -15:F6}");
                }
#if false

                // Compute the Hann window scaling factor
                double HannWindow(int i, int N) => 0.5 * (1 - Math.Cos(2 * Math.PI * i / (N - 1)));
                int windowSize = 8192;
                double scalingFactor = 2.0 / Enumerable.Range(0, windowSize).Sum(i => HannWindow(i, windowSize));

                //double maxAmplitude = 32767.0; // Maximum amplitude for 16-bit audio
                double maxMagnitude = dictionary.Values.Max(); // Find the maximum magnitude in the dataset
                foreach (var dictionaryEntry in dictionary)
                {
                    double frequency = dictionaryEntry.Key;
                    double magnitude = dictionaryEntry.Value;
                    double normalizedMagnitude = magnitude / maxMagnitude;
                    double dB = 20 * Math.Log10(normalizedMagnitude);

                    Console.WriteLine($"Frequency: {frequency}, Magnitude: {magnitude}, dB: {dB}");
                }
                double keyWithHighestValue = dictionary.Aggregate((x, y) => x.Value > y.Value ? x : y).Key;
                Console.WriteLine(keyWithHighestValue);

                var summaryLabels = new List<string>();
                var summaryMaxBucketMagnitude = new List<double>();
                FillInGraphPoints(frequencyInfo, summaryLabels, summaryMaxBucketMagnitude, channel);
                Console.WriteLine("Done");
#endif
            }
        }
    }
}
