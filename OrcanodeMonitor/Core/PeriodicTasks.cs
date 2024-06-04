﻿using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OrcanodeMonitor.Core
{
    public class PeriodicTasks : BackgroundService
    {
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
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing background task.");
                }
            }
        }

        private new async Task ExecuteTask()
        {
            // Your business logic goes here
            _logger.LogInformation("Background task executed.");

            EnumerateNodesResult result = await Fetcher.EnumerateNodesAsync();
            if (!result.Succeeded)
            {
                return;
            }

            foreach (Orcanode node in result.NodeList)
            {
                await Fetcher.UpdateLatestTimestampAsync(node);
            }
        }
    }

}

//