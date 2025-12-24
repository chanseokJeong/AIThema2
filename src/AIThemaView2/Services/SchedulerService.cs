using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using AIThemaView2.Services.Interfaces;
using AIThemaView2.Utils;

namespace AIThemaView2.Services
{
    public class SchedulerService : BackgroundService, ISchedulerService
    {
        private readonly IDataCollectionService _dataCollectionService;
        private readonly ILogger _logger;
        private readonly int _intervalMinutes;
        private PeriodicTimer? _timer;
        private bool _isRunning;

        public SchedulerService(
            IDataCollectionService dataCollectionService,
            ILogger logger,
            int intervalMinutes = 5)
        {
            _dataCollectionService = dataCollectionService;
            _logger = logger;
            _intervalMinutes = intervalMinutes;
        }

        public void StartScheduler()
        {
            if (!_isRunning)
            {
                _isRunning = true;
                _logger.Log($"Scheduler started with {_intervalMinutes} minute interval");
            }
        }

        public void StopScheduler()
        {
            _isRunning = false;
            _logger.Log("Scheduler stopped");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _timer = new PeriodicTimer(TimeSpan.FromMinutes(_intervalMinutes));
            _isRunning = true;

            // Initial collection
            _logger.Log("Performing initial data collection...");
            await CollectDataAsync();

            // Periodic collection
            try
            {
                while (await _timer.WaitForNextTickAsync(stoppingToken) && !stoppingToken.IsCancellationRequested)
                {
                    if (_isRunning)
                    {
                        await CollectDataAsync();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.Log("Scheduler cancelled");
            }
        }

        private async Task CollectDataAsync()
        {
            try
            {
                _logger.Log("Scheduled data collection started");
                var newEventsCount = await _dataCollectionService.CollectTodayEventsAsync();
                _logger.Log($"Scheduled collection complete. {newEventsCount} new events added");

                // Cleanup old data weekly (check if today is Sunday)
                if (DateTime.Now.DayOfWeek == DayOfWeek.Sunday)
                {
                    await _dataCollectionService.CleanupOldDataAsync(30);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Error in scheduled collection", ex);
            }
        }

        public override void Dispose()
        {
            _timer?.Dispose();
            base.Dispose();
        }
    }
}
