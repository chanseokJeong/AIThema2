using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using AIThemaView2.Models;
using AIThemaView2.Services.Interfaces;
using AIThemaView2.Utils;

namespace AIThemaView2.Services
{
    public class DataCollectionService : IDataCollectionService
    {
        private readonly IEnumerable<IScraperService> _scrapers;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger _logger;
        private readonly RateLimitService _rateLimiter;

        public DataCollectionService(
            IEnumerable<IScraperService> scrapers,
            IServiceProvider serviceProvider,
            RateLimitService rateLimiter,
            ILogger logger)
        {
            _scrapers = scrapers;
            _serviceProvider = serviceProvider;
            _rateLimiter = rateLimiter;
            _logger = logger;
        }

        public async Task<int> CollectTodayEventsAsync()
        {
            return await CollectEventsForDateAsync(DateTime.Today);
        }

        public async Task<int> CollectEventsForDateAsync(DateTime targetDate)
        {
            _logger.Log($"Starting data collection for {targetDate:yyyy-MM-dd}");
            var totalNewEvents = 0;

            foreach (var scraper in _scrapers)
            {
                try
                {
                    // Apply rate limiting
                    await _rateLimiter.WaitAsync();

                    _logger.Log($"Fetching from {scraper.SourceName}...");
                    var events = await scraper.FetchEventsAsync(targetDate);

                    if (events == null || !events.Any())
                    {
                        _logger.Log($"No events found from {scraper.SourceName}");
                        continue;
                    }

                    // Create a new scope for database operations
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var repository = scope.ServiceProvider.GetRequiredService<IEventRepository>();

                        // Filter out duplicates
                        var newEvents = new List<StockEvent>();
                        foreach (var evt in events)
                        {
                            if (!await repository.EventExistsAsync(evt.Hash))
                            {
                                newEvents.Add(evt);
                            }
                        }

                        if (newEvents.Any())
                        {
                            await repository.AddEventsAsync(newEvents);
                            totalNewEvents += newEvents.Count;
                            _logger.Log($"Added {newEvents.Count} new events from {scraper.SourceName}");
                        }
                        else
                        {
                            _logger.Log($"All events from {scraper.SourceName} already exist");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error collecting from {scraper.SourceName}", ex);
                }
            }

            _logger.Log($"Collection complete. Total new events: {totalNewEvents}");
            return totalNewEvents;
        }

        public async Task<List<StockEvent>> GetEventsForDateAsync(DateTime date)
        {
            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IEventRepository>();
            return await repository.GetEventsByDateAsync(date);
        }

        public async Task<List<StockEvent>> SearchEventsAsync(string searchTerm)
        {
            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IEventRepository>();
            return await repository.SearchEventsAsync(searchTerm);
        }

        public async Task CleanupOldDataAsync(int daysToKeep = 30)
        {
            _logger.Log($"Cleaning up events older than {daysToKeep} days");
            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IEventRepository>();
            await repository.CleanupOldEventsAsync(daysToKeep);
            _logger.Log("Cleanup complete");
        }
    }
}
