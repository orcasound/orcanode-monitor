// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;
using static System.Formats.Asn1.AsnWriter;

namespace OrcanodeMonitor.Core
{
    public class PeriodicTasks : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
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

        public PeriodicTasks(IServiceScopeFactory scopeFactory, ILogger<PeriodicTasks> logger)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
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

            using var scope = _scopeFactory.CreateScope();
            OrcanodeMonitorContext context = scope.ServiceProvider.GetRequiredService<OrcanodeMonitorContext>();

            await Fetcher.UpdateDataplicityDataAsync(context);

            await Fetcher.UpdateOrcasoundDataAsync(context);

            await Fetcher.UpdateMezmoDataAsync(context);

            await Fetcher.UpdateS3DataAsync(context);

            await Fetcher.UpdateOrcaHelloDataAsync(context);
        }
    }
}

//