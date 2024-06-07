// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OrcanodeMonitor.Core
{
    public class PeriodicTasks : BackgroundService
    {
        const int _defaultFrequencyToPollInMinutes = 5;
        private static TimeSpan FrequencyToPoll
        {
            get
            {
                string? frequencyToPollInMinutesString = Environment.GetEnvironmentVariable("ORCASOUND_POLL_FREQUENCY_IN_MINUTES");
                int frequencyToPollInMinutes = (int.TryParse(frequencyToPollInMinutesString, out var minutes)) ? minutes : _defaultFrequencyToPollInMinutes;
                return TimeSpan.FromMinutes(frequencyToPollInMinutes);
            }
        }

        private readonly ILogger<PeriodicTasks> _logger;

        public PeriodicTasks(ILogger<PeriodicTasks> logger)
        {
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Execute business logic.
                    await ExecuteTask();

                    // Schedule the next execution.
                    await Task.Delay(FrequencyToPoll, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing background task.");
                }
            }
        }

        private new async Task ExecuteTask()
        {
            _logger.LogInformation("Background task executed.");

            EnumerateNodesResult result = await Fetcher.EnumerateOrcasoundNodesAsync();
            if (result.Succeeded)
            {
                foreach (Orcanode node in result.NodeList)
                {
                    await Fetcher.UpdateLatestTimestampAsync(node, result.Timestamp);
                }
            }

            await Fetcher.EnumerateDataplicityNodesAsync(result);

            State.SetLastResult(result);
        }
    }

}

//