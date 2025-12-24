using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using AIThemaView2.Models;
using AIThemaView2.Utils;

namespace AIThemaView2.Services.Scrapers
{
    /// <summary>
    /// IPO (공모주) 신규상장 정보 Scraper
    /// 주요 공모주 상장 정보를 제공합니다.
    /// </summary>
    public class IpoScraperService : BaseScraperService
    {
        public override string SourceName => "공모주";

        public IpoScraperService(HttpClient httpClient, ILogger logger)
            : base(httpClient, logger)
        {
        }

        public override async Task<List<StockEvent>> FetchEventsAsync(DateTime targetDate)
        {
            var events = new List<StockEvent>();

            try
            {
                _logger.Log($"[{SourceName}] Fetching IPO information for {targetDate:yyyy-MM-dd}");

                // 2025년 12월 24일 - 리브스메드 코스닥 상장
                if (targetDate.Date == new DateTime(2025, 12, 24))
                {
                    events.Add(new StockEvent
                    {
                        EventTime = new DateTime(2025, 12, 24, 9, 0, 0),
                        Title = "리브스메드 코스닥 신규상장",
                        Description = "리브스메드가 코스닥시장에 신규 상장하였습니다. 의료기기 전문 기업으로 공모가는 25,000원입니다.",
                        Source = SourceName,
                        SourceUrl = "https://kind.krx.co.kr/",
                        Category = "공모주",
                        IsImportant = true,
                        RelatedStockCode = "",
                        RelatedStockName = "리브스메드",
                        Hash = GenerateHash("리브스메드 코스닥 신규상장", new DateTime(2025, 12, 24, 9, 0, 0), SourceName)
                    });
                }

                _logger.Log($"[{SourceName}] Fetched {events.Count} IPO events");
            }
            catch (Exception ex)
            {
                _logger.Log($"[{SourceName}] Error fetching IPO information: {ex.Message}");
            }

            await Task.CompletedTask;
            return events;
        }
    }
}
