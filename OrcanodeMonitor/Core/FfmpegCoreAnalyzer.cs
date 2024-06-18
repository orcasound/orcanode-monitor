using FFMpegCore;
using FFMpegCore.Pipes;
using Microsoft.EntityFrameworkCore.SqlServer.Query.Internal;
using NAudio.Wave;

namespace OrcanodeMonitor.Core
{
    public class FfmpegCoreAnalyzer
    {
        /// <summary>
        /// Get the Std.Dev. of the most recent audio samples.
        /// </summary>
        /// <param name="args">FFMpeg arguments</param>
        /// <returns>Std.Dev. of the most recent audio samples</returns>
        private static async Task<double> AnalyzeAsync(FFMpegArguments args)
        {
            var outputStream = new MemoryStream(); // Create an output stream (e.g., MemoryStream)
            var pipeSink = new StreamPipeSink(outputStream);

            bool ok = await args
                .OutputToPipe(pipeSink, options => options
                .WithAudioCodec("pcm_s16le")
                .ForceFormat("wav"))
            .ProcessAsynchronously();

            var waveFormat = new WaveFormat(rate: 44100, bits: 16, channels: 1);
            var rawStream = new RawSourceWaveStream(outputStream, waveFormat);

            byte[] buffer = new byte[4096]; // Adjust buffer size as needed
            int bytesRead;

            // Reset the position to the beginning
            rawStream.Seek(0, SeekOrigin.Begin);

            var variance = new WelfordVariance();

            while ((bytesRead = rawStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (int i = 0; i < bytesRead; i += 2) // Assuming 16-bit samples
                {
                    short sample = BitConverter.ToInt16(buffer, i);
                    variance.Add(sample);
                }
            }
            return variance.StandardDeviation;
        }

        public static async Task<double> AnalyzeFileAsync(string filename)
        {
#if false
            var mediaInfo = await FFProbe.AnalyseAsync(filename);
            if (mediaInfo == null)
            {
                return false;
            }
#endif
            var args = FFMpegArguments.FromFileInput(filename);
            return await AnalyzeAsync(args);
        }

        public static async Task<double> AnalyzeAudioStreamAsync(Stream stream)
        {
            StreamPipeSource streamPipeSource = new StreamPipeSource(stream);
            var args = FFMpegArguments.FromPipeInput(streamPipeSource);
            return await AnalyzeAsync(args);
        }
    }
}

