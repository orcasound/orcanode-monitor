// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OrcanodeMonitor.Core;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using static OrcanodeMonitor.Core.Fetcher;

namespace OrcanodeMonitor.Pages
{
    public class SpectralDensityModel : PageModel
    {
        private readonly OrcanodeMonitorContext _databaseContext;
        private readonly ILogger<NodeEventsModel> _logger;
        private string _nodeId;
        public string Id => _nodeId;
        private List<string> _labels;
        private List<int> _data;
        public List<string> Labels => _labels;
        public List<int> Data => _data;

        public SpectralDensityModel(OrcanodeMonitorContext context, ILogger<NodeEventsModel> logger)
        {
            _databaseContext = context;
            _logger = logger;
            _nodeId = string.Empty;
        }

        private async Task UpdateFrequencyDataAsync()
        {
            _labels = new List<string> { };
            _data = new List<int> { };
            Orcanode node = _databaseContext.Orcanodes.Where(n => n.ID == _nodeId).First();
            TimestampResult? result = await GetLatestS3TimestampAsync(node, false, _logger);
            if (result != null)
            {
                FrequencyInfo? frequencyInfo = await Fetcher.GetLatestAudioSampleAsync(node, result.UnixTimestampString, false, _logger);
                if (frequencyInfo != null)
                {
                    const int MaxFrequency = 23000;
                    const int PointCount = 1000;

                    // Compute the logarithmic base needed to get PointCount points.
                    double b = Math.Pow(MaxFrequency, 1.0 / PointCount);
                    double logb = Math.Log(b);

                    double maxAmplitude = frequencyInfo.FrequencyAmplitudes.Values.Max();
                    var sums = new double[PointCount];
                    var count = new int[PointCount];

                    foreach (var pair in frequencyInfo.FrequencyAmplitudes)
                    {
                        double frequency = pair.Key;
                        double amplitude = pair.Value;
                        int bucket = (frequency < 1) ? 0 : (int)(Math.Log(frequency) / logb);
                        count[bucket]++;
                        sums[bucket] += amplitude;
                    }

                    // Fill in graph points.
                    for (int i = 0; i < PointCount; i++)
                    {
                        if (count[i] > 0)
                        {
                            int frequency = (int)Math.Pow(b, i);
                            int amplitude = (int)(sums[i] / count[i]);
                            _labels.Add(frequency.ToString());
                            _data.Add(amplitude);
                        }
                    }
                }
            }
        }

        public async Task OnGetAsync(string id)
        {
            _nodeId = id;
            await UpdateFrequencyDataAsync();
        }
    }
}
