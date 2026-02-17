// Copyright (c) Orcanode Monitor contributors
// SPDX-License-Identifier: MIT
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrcanodeMonitor.Data;
using OrcanodeMonitor.Models;

namespace OrcanodeMonitor.Core
{
    public class PeriodicTasks : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly OrcaHelloFetcher _orcaHelloFetcher;
        const int _defaultFrequencyToPollInMinutes = 5;
        public static TimeSpan FrequencyToPoll
        {
            get
            {
                string? frequencyToPollInMinutesString = Fetcher.GetConfig("ORCASOUND_POLL_FREQUENCY_IN_MINUTES");
                int frequencyToPollInMinutes = (int.TryParse(frequencyToPollInMinutesString, out var minutes)) ? minutes : _defaultFrequencyToPollInMinutes;
                return TimeSpan.FromMinutes(frequencyToPollInMinutes);
            }
        }
        public static double PollsPerDay
        {
            get
            {
                TimeSpan timeSpan = FrequencyToPoll;
                TimeSpan oneDay = TimeSpan.FromDays(1);
                return oneDay.TotalMinutes / timeSpan.TotalMinutes;
            }
        }

        private readonly ILogger<PeriodicTasks> _logger;

        public PeriodicTasks(IServiceScopeFactory scopeFactory, ILogger<PeriodicTasks> logger, OrcaHelloFetcher orcaHelloFetcher)
        {
            _logger = logger;
            _scopeFactory = scopeFactory;
            _orcaHelloFetcher = orcaHelloFetcher;
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

            await DataplicityFetcher.UpdateDataplicityDataAsync(context, _logger);

            await Fetcher.UpdateOrcasoundDataAsync(context, _logger);

            await MezmoFetcher.UpdateMezmoDataAsync(context, _logger);

            await Fetcher.UpdateS3DataAsync(context, _logger);

            await _orcaHelloFetcher.UpdateOrcaHelloDataAsync(context, _logger);

            await DataplicityFetcher.CheckForRebootsNeededAsync(context, _logger);
        }
    }
}