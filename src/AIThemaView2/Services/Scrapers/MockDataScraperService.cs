using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using AIThemaView2.Models;
using AIThemaView2.Utils;

namespace AIThemaView2.Services.Scrapers
{
    /// <summary>
    /// Mock data scraper for testing - generates realistic sample stock market events
    /// </summary>
    public class MockDataScraperService : BaseScraperService
    {
        public override string SourceName => "Sample";

        private static readonly string[] Companies =
        {
            "삼성전자", "SK하이닉스", "현대차", "LG전자", "POSCO", "네이버", "카카오",
            "삼성바이오로직스", "셀트리온", "기아", "삼성SDI", "현대모비스", "LG화학"
        };

        private static readonly string[] NewsTemplates =
        {
            "{0}, 실적 발표 예정",
            "{0}, 신규 투자 계획 발표",
            "{0}, 배당금 지급 공시",
            "{0}, 주요 임원 인사",
            "{0}, 신제품 출시 발표",
            "{0}, 해외 시장 진출 발표",
            "{0}, 설비 투자 확대",
            "{0}, 연구개발 성과 공개",
            "{0}, 분기 실적 호조",
            "{0}, 주주총회 개최 안내",
            "증시 분석: {0} 목표가 상향",
            "{0}, 주가 급등세",
            "{0}, 거래량 급증",
            "{0}, 외국인 매수 증가"
        };

        public MockDataScraperService(HttpClient httpClient, ILogger logger)
            : base(httpClient, logger)
        {
        }

        public override async Task<List<StockEvent>> FetchEventsAsync(DateTime targetDate)
        {
            _logger.Log($"[{SourceName}] Generating sample data for {targetDate:yyyy-MM-dd}");

            var events = new List<StockEvent>();
            var random = new Random(targetDate.GetHashCode()); // Consistent random for same date

            // Generate 20-30 events throughout the day
            var eventCount = random.Next(20, 31);

            for (int i = 0; i < eventCount; i++)
            {
                var hour = random.Next(9, 16); // Market hours 9:00 - 15:59
                var minute = random.Next(0, 60);
                var eventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, hour, minute, 0);

                var company = Companies[random.Next(Companies.Length)];
                var template = NewsTemplates[random.Next(NewsTemplates.Length)];
                var title = string.Format(template, company);

                var categories = new[] { "뉴스", "공시", "증권뉴스" };
                var category = categories[random.Next(categories.Length)];

                var stockEvent = new StockEvent
                {
                    EventTime = eventTime,
                    Title = title,
                    Description = $"샘플 데이터 - 실제 수집 시스템 연결 필요",
                    Source = SourceName,
                    SourceUrl = "https://example.com",
                    Category = category,
                    RelatedStockName = company,
                    RelatedStockCode = GenerateMockStockCode(company, random),
                    IsImportant = random.Next(100) > 70, // 30% are important
                    Hash = GenerateHash(title, eventTime, SourceName)
                };

                events.Add(stockEvent);
            }

            // Sort by time
            events = events.OrderBy(e => e.EventTime).ToList();

            _logger.Log($"[{SourceName}] Generated {events.Count} sample events");

            // Simulate network delay
            await Task.Delay(500);

            return events;
        }

        private string GenerateMockStockCode(string company, Random random)
        {
            // Generate realistic looking stock codes
            var codes = new Dictionary<string, string>
            {
                { "삼성전자", "005930" },
                { "SK하이닉스", "000660" },
                { "현대차", "005380" },
                { "LG전자", "066570" },
                { "POSCO", "005490" },
                { "네이버", "035420" },
                { "카카오", "035720" },
                { "삼성바이오로직스", "207940" },
                { "셀트리온", "068270" },
                { "기아", "000270" },
                { "삼성SDI", "006400" },
                { "현대모비스", "012330" },
                { "LG화학", "051910" }
            };

            return codes.ContainsKey(company) ? codes[company] : random.Next(100000, 999999).ToString();
        }
    }
}
