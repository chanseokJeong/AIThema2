using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using AIThemaView2.Models;
using AIThemaView2.Utils;

namespace AIThemaView2.Services.Scrapers
{
    /// <summary>
    /// IPO (공모주) 신규상장 정보 Scraper
    /// 38커뮤니케이션에서 실제 공모주 일정을 스크래핑합니다.
    /// </summary>
    public class IpoScraperService : BaseScraperService
    {
        public override string SourceName => "공모주";

        private const string IpoScheduleUrl = "https://www.38.co.kr/html/fund/index.htm?o=k";

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

                // 38커뮤니케이션에서 공모주 일정 가져오기
                var ipoEvents = await FetchFromA38Async(targetDate);
                events.AddRange(ipoEvents);

                _logger.Log($"[{SourceName}] Fetched {events.Count} IPO events");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error fetching IPO information", ex);
            }

            return events;
        }

        private async Task<List<StockEvent>> FetchFromA38Async(DateTime targetDate)
        {
            var events = new List<StockEvent>();

            try
            {
                var doc = await LoadHtmlDocumentAsync(IpoScheduleUrl);

                // 청약 일정 테이블 찾기
                var tables = doc.DocumentNode.SelectNodes("//table");
                if (tables == null)
                {
                    _logger.Log($"[{SourceName}] No tables found on page");
                    return events;
                }

                _logger.Log($"[{SourceName}] Found {tables.Count} tables on page");

                foreach (var table in tables)
                {
                    var rows = table.SelectNodes(".//tr");
                    if (rows == null) continue;

                    foreach (var row in rows)
                    {
                        try
                        {
                            var cells = row.SelectNodes(".//td");
                            if (cells == null || cells.Count < 2) continue;

                            // 회사명 추출 (첫번째 셀에서 링크 찾기)
                            var companyLink = row.SelectSingleNode(".//td//a");
                            var companyName = companyLink != null
                                ? CleanText(companyLink.InnerText)
                                : "";

                            if (string.IsNullOrEmpty(companyName) || companyName.Length < 2) continue;

                            // 전체 행 텍스트에서 날짜 패턴 찾기
                            var rowText = CleanText(row.InnerText);

                            // 날짜 패턴: 2025.12.24 또는 12.24~12.25 형식
                            // 패턴1: YYYY.MM.DD~MM.DD
                            var dateRangeMatch = Regex.Match(rowText, @"(\d{4})\.(\d{1,2})\.(\d{1,2})~(\d{1,2})\.(\d{1,2})");
                            if (dateRangeMatch.Success)
                            {
                                int year = int.Parse(dateRangeMatch.Groups[1].Value);
                                int startMonth = int.Parse(dateRangeMatch.Groups[2].Value);
                                int startDay = int.Parse(dateRangeMatch.Groups[3].Value);
                                int endMonth = int.Parse(dateRangeMatch.Groups[4].Value);
                                int endDay = int.Parse(dateRangeMatch.Groups[5].Value);

                                var startDate = new DateTime(year, startMonth, startDay);
                                var endDate = new DateTime(year, endMonth, endDay);

                                // 대상 날짜가 청약 기간에 포함되는지 확인
                                if (targetDate.Date >= startDate.Date && targetDate.Date <= endDate.Date)
                                {
                                    string priceInfo = ExtractPriceInfo(rowText);
                                    string title = $"{companyName} 청약";
                                    string description = $"{companyName} 청약 진행 중 ({startDate:MM.dd}~{endDate:MM.dd})";
                                    if (!string.IsNullOrEmpty(priceInfo))
                                        description += $". {priceInfo}";

                                    var stockEvent = new StockEvent
                                    {
                                        EventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 9, 0, 0),
                                        Title = title,
                                        Description = description,
                                        Source = SourceName,
                                        SourceUrl = IpoScheduleUrl,
                                        Category = "공모주",
                                        IsImportant = true,
                                        RelatedStockName = companyName,
                                        Hash = GenerateHash(title, targetDate, SourceName)
                                    };

                                    if (!events.Any(e => e.Hash == stockEvent.Hash))
                                    {
                                        _logger.Log($"[{SourceName}] Found IPO: {companyName} ({startDate:MM.dd}~{endDate:MM.dd})");
                                        events.Add(stockEvent);
                                    }
                                }
                                continue;
                            }

                            // 패턴2: 단일 날짜 YYYY.MM.DD (상장일 등)
                            var singleDateMatches = Regex.Matches(rowText, @"(\d{4})\.(\d{1,2})\.(\d{1,2})");
                            foreach (Match match in singleDateMatches)
                            {
                                try
                                {
                                    int year = int.Parse(match.Groups[1].Value);
                                    int month = int.Parse(match.Groups[2].Value);
                                    int day = int.Parse(match.Groups[3].Value);
                                    var eventDate = new DateTime(year, month, day);

                                    if (eventDate.Date == targetDate.Date)
                                    {
                                        string eventType = DetermineEventType(rowText);
                                        string title = $"{companyName} {eventType}";
                                        string priceInfo = ExtractPriceInfo(rowText);
                                        string description = $"{companyName} {eventType}";
                                        if (!string.IsNullOrEmpty(priceInfo))
                                            description += $". {priceInfo}";

                                        var stockEvent = new StockEvent
                                        {
                                            EventTime = new DateTime(year, month, day, 9, 0, 0),
                                            Title = title,
                                            Description = description,
                                            Source = SourceName,
                                            SourceUrl = IpoScheduleUrl,
                                            Category = "공모주",
                                            IsImportant = true,
                                            RelatedStockName = companyName,
                                            Hash = GenerateHash(title, eventDate, SourceName)
                                        };

                                        if (!events.Any(e => e.Hash == stockEvent.Hash))
                                        {
                                            _logger.Log($"[{SourceName}] Found IPO event: {title}");
                                            events.Add(stockEvent);
                                        }
                                    }
                                }
                                catch
                                {
                                    // 날짜 파싱 실패 시 무시
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"[{SourceName}] Error parsing row", ex);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error fetching from 38.co.kr", ex);
            }

            return events;
        }

        private string DetermineEventType(string rowText)
        {
            var lowerText = rowText.ToLower();

            if (lowerText.Contains("상장"))
                return "신규상장";
            if (lowerText.Contains("청약"))
                return "청약";
            if (lowerText.Contains("환불"))
                return "환불일";
            if (lowerText.Contains("납입"))
                return "납입일";
            if (lowerText.Contains("배정"))
                return "배정일";

            return "공모주 일정";
        }

        private string ExtractPriceInfo(string text)
        {
            // 가격 패턴: 10,000~12,000 또는 15,000 형식
            var priceRangeMatch = Regex.Match(text, @"([\d,]+)~([\d,]+)");
            if (priceRangeMatch.Success)
            {
                var price1 = priceRangeMatch.Groups[1].Value.Replace(",", "");
                var price2 = priceRangeMatch.Groups[2].Value.Replace(",", "");
                if (int.TryParse(price1, out int p1) && int.TryParse(price2, out int p2))
                {
                    if (p1 >= 1000 && p1 <= 500000 && p2 >= 1000 && p2 <= 500000)
                    {
                        return $"공모가 {priceRangeMatch.Groups[1].Value}~{priceRangeMatch.Groups[2].Value}원";
                    }
                }
            }

            // 단일 가격 패턴
            var singlePriceMatch = Regex.Match(text, @"(\d{1,3}(,\d{3})+)원?");
            if (singlePriceMatch.Success)
            {
                var price = singlePriceMatch.Groups[1].Value.Replace(",", "");
                if (int.TryParse(price, out int p) && p >= 1000 && p <= 500000)
                {
                    return $"공모가 {singlePriceMatch.Groups[1].Value}원";
                }
            }

            return "";
        }
    }
}
