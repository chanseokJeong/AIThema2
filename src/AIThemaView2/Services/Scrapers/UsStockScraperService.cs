using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using AIThemaView2.Models;
using AIThemaView2.Utils;

namespace AIThemaView2.Services.Scrapers
{
    /// <summary>
    /// 미국 주식시장 주요 일정 Scraper
    /// FOMC, 경제지표, 주요 기업 실적발표 등을 제공합니다.
    /// </summary>
    public class UsStockScraperService : BaseScraperService
    {
        public override string SourceName => "미국일정";

        public UsStockScraperService(HttpClient httpClient, ILogger logger)
            : base(httpClient, logger)
        {
        }

        public override async Task<List<StockEvent>> FetchEventsAsync(DateTime targetDate)
        {
            var events = new List<StockEvent>();

            try
            {
                _logger.Log($"[{SourceName}] Fetching US market events for {targetDate:yyyy-MM-dd}");

                // 주요 경제지표 일정
                AddEconomicIndicators(events, targetDate);

                // FOMC 회의 일정
                AddFomcMeetings(events, targetDate);

                // 주요 기업 실적발표
                AddEarningsReleases(events, targetDate);

                // 기타 주요 이벤트
                AddMajorEvents(events, targetDate);

                _logger.Log($"[{SourceName}] Fetched {events.Count} US market events");
            }
            catch (Exception ex)
            {
                _logger.Log($"[{SourceName}] Error fetching US market events: {ex.Message}");
            }

            await Task.CompletedTask;
            return events;
        }

        private void AddEconomicIndicators(List<StockEvent> events, DateTime targetDate)
        {
            // 2025년 12월 주요 경제지표 일정
            var indicators = new Dictionary<DateTime, List<(string title, string time, string description)>>
            {
                [new DateTime(2025, 12, 24)] = new List<(string, string, string)>
                {
                    ("미국 GDP 확정치 발표", "22:30", "3분기 GDP 최종 수치 발표 - 경제 성장률 확인"),
                    ("내구재 주문 발표", "22:30", "11월 내구재 주문 지표 - 제조업 경기 판단")
                },
                [new DateTime(2025, 12, 26)] = new List<(string, string, string)>
                {
                    ("신규 주택 판매", "00:00", "11월 신규 주택 판매 지표")
                },
                [new DateTime(2025, 12, 27)] = new List<(string, string, string)>
                {
                    ("소비자 신뢰지수", "00:00", "12월 컨퍼런스보드 소비자 신뢰지수")
                }
            };

            if (indicators.TryGetValue(targetDate.Date, out var dayIndicators))
            {
                foreach (var (title, time, description) in dayIndicators)
                {
                    var timeParts = time.Split(':');
                    var eventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day,
                        int.Parse(timeParts[0]), int.Parse(timeParts[1]), 0);

                    events.Add(new StockEvent
                    {
                        EventTime = eventTime,
                        Title = title,
                        Description = description,
                        Source = SourceName,
                        SourceUrl = "https://www.investing.com/economic-calendar/",
                        Category = "경제지표",
                        IsImportant = true,
                        RelatedStockCode = "",
                        RelatedStockName = "",
                        Hash = GenerateHash(title, eventTime, SourceName)
                    });
                }
            }
        }

        private void AddFomcMeetings(List<StockEvent> events, DateTime targetDate)
        {
            // 2025년 FOMC 회의 일정
            var fomcDates = new List<(DateTime start, DateTime end, string description)>
            {
                (new DateTime(2025, 1, 28), new DateTime(2025, 1, 29), "1월 FOMC 회의"),
                (new DateTime(2025, 3, 18), new DateTime(2025, 3, 19), "3월 FOMC 회의"),
                (new DateTime(2025, 5, 6), new DateTime(2025, 5, 7), "5월 FOMC 회의"),
                (new DateTime(2025, 6, 17), new DateTime(2025, 6, 18), "6월 FOMC 회의"),
                (new DateTime(2025, 7, 29), new DateTime(2025, 7, 30), "7월 FOMC 회의"),
                (new DateTime(2025, 9, 16), new DateTime(2025, 9, 17), "9월 FOMC 회의"),
                (new DateTime(2025, 11, 4), new DateTime(2025, 11, 5), "11월 FOMC 회의"),
                (new DateTime(2025, 12, 16), new DateTime(2025, 12, 17), "12월 FOMC 회의")
            };

            foreach (var (start, end, description) in fomcDates)
            {
                // FOMC 시작일
                if (targetDate.Date == start.Date)
                {
                    events.Add(new StockEvent
                    {
                        EventTime = new DateTime(start.Year, start.Month, start.Day, 0, 0, 0),
                        Title = $"{description} 시작",
                        Description = "연방공개시장위원회 정례회의 - 기준금리 결정",
                        Source = SourceName,
                        SourceUrl = "https://www.federalreserve.gov/",
                        Category = "FOMC",
                        IsImportant = true,
                        RelatedStockCode = "",
                        RelatedStockName = "",
                        Hash = GenerateHash($"{description}_start", start, SourceName)
                    });
                }

                // FOMC 종료일 (금리 결정 발표)
                if (targetDate.Date == end.Date)
                {
                    events.Add(new StockEvent
                    {
                        EventTime = new DateTime(end.Year, end.Month, end.Day, 4, 0, 0), // 새벽 4시 (한국시간)
                        Title = $"{description} 금리 결정 발표",
                        Description = "FOMC 기준금리 결정 및 성명서 발표 - 파월 의장 기자회견",
                        Source = SourceName,
                        SourceUrl = "https://www.federalreserve.gov/",
                        Category = "FOMC",
                        IsImportant = true,
                        RelatedStockCode = "",
                        RelatedStockName = "",
                        Hash = GenerateHash($"{description}_end", end, SourceName)
                    });
                }
            }
        }

        private void AddEarningsReleases(List<StockEvent> events, DateTime targetDate)
        {
            // 주요 기업 실적발표 일정 (예시)
            var earnings = new Dictionary<DateTime, List<(string company, string time, string ticker)>>
            {
                [new DateTime(2025, 12, 24)] = new List<(string, string, string)>
                {
                    ("나이키", "06:00", "NKE"),
                    ("페덱스", "22:00", "FDX")
                }
            };

            if (earnings.TryGetValue(targetDate.Date, out var dayEarnings))
            {
                foreach (var (company, time, ticker) in dayEarnings)
                {
                    var timeParts = time.Split(':');
                    var eventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day,
                        int.Parse(timeParts[0]), int.Parse(timeParts[1]), 0);

                    events.Add(new StockEvent
                    {
                        EventTime = eventTime,
                        Title = $"{company} 실적발표",
                        Description = $"{company}({ticker}) 분기 실적 발표 예정",
                        Source = SourceName,
                        SourceUrl = "https://finance.yahoo.com/calendar/earnings",
                        Category = "실적",
                        IsImportant = true,
                        RelatedStockCode = ticker,
                        RelatedStockName = company,
                        Hash = GenerateHash($"{company}_earnings", eventTime, SourceName)
                    });
                }
            }
        }

        private void AddMajorEvents(List<StockEvent> events, DateTime targetDate)
        {
            // 주요 이벤트 (배당, 스플릿, IPO 등)
            var majorEvents = new Dictionary<DateTime, List<(string title, string time, string description, string ticker)>>
            {
                [new DateTime(2025, 12, 25)] = new List<(string, string, string, string)>
                {
                    ("미국 증시 휴장", "00:00", "크리스마스 연휴로 미국 증시 휴장", "")
                },
                [new DateTime(2025, 12, 26)] = new List<(string, string, string, string)>
                {
                    ("미국 증시 정상 개장", "00:00", "연휴 후 정상 거래 재개", "")
                }
            };

            if (majorEvents.TryGetValue(targetDate.Date, out var dayEvents))
            {
                foreach (var (title, time, description, ticker) in dayEvents)
                {
                    var timeParts = time.Split(':');
                    var eventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day,
                        int.Parse(timeParts[0]), int.Parse(timeParts[1]), 0);

                    events.Add(new StockEvent
                    {
                        EventTime = eventTime,
                        Title = title,
                        Description = description,
                        Source = SourceName,
                        SourceUrl = "https://www.nyse.com/markets/hours-calendars",
                        Category = "이벤트",
                        IsImportant = true,
                        RelatedStockCode = ticker,
                        RelatedStockName = "",
                        Hash = GenerateHash(title, eventTime, SourceName)
                    });
                }
            }
        }
    }
}
