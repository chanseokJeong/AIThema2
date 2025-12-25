using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using AIThemaView2.Models;
using AIThemaView2.Services.Interfaces;
using AIThemaView2.Utils;
using AIThemaView2.Data.Repositories;

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
            var allCollectedEvents = new List<StockEvent>();

            // 1. 모든 스크래퍼에서 이벤트 수집
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

                    _logger.Log($"Found {events.Count} events from {scraper.SourceName}");
                    allCollectedEvents.AddRange(events);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error collecting from {scraper.SourceName}", ex);
                }
            }

            if (!allCollectedEvents.Any())
            {
                _logger.Log("No events collected from any source");
                return 0;
            }

            // 2. 수집된 이벤트 내에서 중복 제거 (NormalizedHash 기준)
            var deduplicatedEvents = DeduplicateEvents(allCollectedEvents);
            _logger.Log($"After deduplication: {deduplicatedEvents.Count} unique events (was {allCollectedEvents.Count})");

            // 3. DB에 저장 (기존 Hash 기준으로 중복 체크)
            var totalNewEvents = 0;
            using (var scope = _serviceProvider.CreateScope())
            {
                var repository = scope.ServiceProvider.GetRequiredService<IEventRepository>();

                // 기존 DB의 이벤트 가져와서 NormalizedHash 기준 중복 체크
                var existingEvents = await repository.GetEventsByDateAsync(targetDate);
                var existingNormalizedHashes = new HashSet<string>(
                    existingEvents.Select(e => e.NormalizedHash));

                var newEvents = new List<StockEvent>();
                foreach (var evt in deduplicatedEvents)
                {
                    // NormalizedHash 기준으로 중복 체크 (소스에 관계없이)
                    if (!existingNormalizedHashes.Contains(evt.NormalizedHash) &&
                        !await repository.EventExistsAsync(evt.Hash))
                    {
                        newEvents.Add(evt);
                        existingNormalizedHashes.Add(evt.NormalizedHash);
                    }
                }

                if (newEvents.Any())
                {
                    await repository.AddEventsAsync(newEvents);
                    totalNewEvents = newEvents.Count;
                    _logger.Log($"Added {newEvents.Count} new events to database");
                }
                else
                {
                    _logger.Log("All events already exist in database");
                }
            }

            _logger.Log($"Collection complete. Total new events: {totalNewEvents}");
            return totalNewEvents;
        }

        /// <summary>
        /// NormalizedHash 기준으로 이벤트 중복 제거
        /// 소스 우선순위: DART > 38커뮤니케이션 > Investing.com > 토스증권 > 기타
        /// </summary>
        private List<StockEvent> DeduplicateEvents(List<StockEvent> events)
        {
            var sourcePriority = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                { "DART", 1 },
                { "38커뮤니케이션", 2 },
                { "Investing.com", 3 },
                { "토스증권", 4 }
            };

            return events
                .GroupBy(e => e.NormalizedHash)
                .Select(g => g
                    .OrderBy(e => sourcePriority.TryGetValue(e.Source, out var priority) ? priority : 99)
                    .ThenBy(e => e.CreatedAt)
                    .First())
                .ToList();
        }

        public async Task<List<StockEvent>> GetEventsForDateAsync(DateTime date)
        {
            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IEventRepository>();
            var events = await repository.GetEventsByDateAsync(date);

            // DB에서 조회한 이벤트도 중복 제거 (이미 저장된 중복 처리)
            var deduplicatedEvents = DeduplicateEvents(events);
            return deduplicatedEvents.OrderBy(e => e.EventTime).ToList();
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
