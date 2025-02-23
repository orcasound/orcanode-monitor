// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using FFMpegCore;
using FFMpegCore.Pipes;
using OrcanodeMonitor.Models;
using System.Runtime.InteropServices;
using System.Text;

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
        /// <returns>Frequency info</returns>
        private static FrequencyInfo AnalyzeFrequencies(float[] data, uint sampleRate, int channels, OrcanodeOnlineStatus oldStatus)
        {
            FrequencyInfo frequencyInfo = new FrequencyInfo(data, sampleRate, channels, oldStatus);
            return frequencyInfo;
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
            public uint ChunkSize;      // Size following the ChunkSize.
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

                // Read the entire stream into a byte array.
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
                var floatBuffer = new float[dataSize / sizeof(short)];
                for (int i = 0; i < floatBuffer.Length; i++)
                {
                    floatBuffer[i] = BitConverter.ToInt16(byteBuffer, dataOffset + i * sizeof(short)) / 32768f;
                }

                // Perform FFT and analyze frequencies.
                var status = AnalyzeFrequencies(floatBuffer, fmtChunk.SampleRate, fmtChunk.NumChannels, oldStatus);
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
