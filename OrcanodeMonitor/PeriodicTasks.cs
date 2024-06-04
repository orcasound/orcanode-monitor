using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OrcanodeMonitor
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
                    // Execute your business logic here
                    await ExecuteTask();

                    // Adjust the interval as needed
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error executing background task.");
                }
            }
        }

        private async Task ExecuteTask()
        {
            // Your business logic goes here
            _logger.LogInformation("Background task executed.");
        }
    }

}

//