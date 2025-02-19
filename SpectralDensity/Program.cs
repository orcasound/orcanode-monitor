using FFMpegCore;
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
using System.Reflection.Emit;

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

        public static async Task Main(string[] args)
        {
            string filePath = "C:\\Users\\dthal\\git\\orcasound\\orcanode-monitor\\OrcanodeMonitor\\output.wav";
            FrequencyInfo frequencyInfo = await FfmpegCoreAnalyzer.AnalyzeFileAsync(filePath, OrcanodeOnlineStatus.Online);
            
            for (int channel = 0; channel < frequencyInfo.ChannelCount; channel++)
            {
                var dictionary = frequencyInfo.GetFrequencyMagnitudes(channel);
                double keyWithHighestValue = dictionary.Aggregate((x, y) => x.Value > y.Value ? x : y).Key;
                Console.WriteLine(keyWithHighestValue);

                var summaryLabels = new List<string>();
                var summaryMaxBucketMagnitude = new List<double>();
                FillInGraphPoints(frequencyInfo, summaryLabels, summaryMaxBucketMagnitude, channel);
                Console.WriteLine("Done");
            }
        }
    }
}
