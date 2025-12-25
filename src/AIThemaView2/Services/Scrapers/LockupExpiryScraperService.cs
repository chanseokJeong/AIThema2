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
    /// 의무보호해제(Lock-up Expiry) 정보 Scraper
    /// 세이브로(Seibro)에서 의무보호예수 해제 일정을 실제로 스크래핑합니다.
    /// </summary>
    public class LockupExpiryScraperService : BaseScraperService
    {
        public override string SourceName => "의무보호해제";

        // 세이브로 모바일 - 의무보호예수 해제물량 조회
        private const string SeibroMobileUrl = "https://m.seibro.or.kr/cnts/company/selectRelease.do";

        // 38커뮤니케이션 - 신규상장 정보 (15일 의무보호해제 계산용)
        private const string NewListingUrl = "https://www.38.co.kr/html/fund/index.htm?o=nw";

        public LockupExpiryScraperService(HttpClient httpClient, ILogger logger)
            : base(httpClient, logger)
        {
        }

        public override async Task<List<StockEvent>> FetchEventsAsync(DateTime targetDate)
        {
            var events = new List<StockEvent>();

            try
            {
                _logger.Log($"[{SourceName}] Fetching lockup expiry information for {targetDate:yyyy-MM-dd}");

                // 1. 세이브로에서 의무보호해제 일정 스크래핑 시도
                var seibroEvents = await FetchFromSeibroAsync(targetDate);
                events.AddRange(seibroEvents);
                _logger.Log($"[{SourceName}] Seibro scraped: {seibroEvents.Count} events");

                // 2. 38.co.kr에서 15일 의무보호해제 계산 (신규상장일 + 15일)
                var fifteenDayEvents = await Fetch15DayLockupFromNewListingsAsync(targetDate);
                events.AddRange(fifteenDayEvents);
                _logger.Log($"[{SourceName}] 15-day lockup events: {fifteenDayEvents.Count} events");

                // 3. 한국 증시 휴장일 체크 (공식 휴장일은 정적 데이터 유지)
                AddKoreanMarketHolidays(events, targetDate);

                _logger.Log($"[{SourceName}] Total fetched: {events.Count} events");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error fetching lockup expiry information", ex);
            }

            return events;
        }

        /// <summary>
        /// 세이브로 모바일에서 의무보호예수 해제 정보 스크래핑
        /// 테이블 구조: 해제일 | 기업명 | 해제주식수 | 예수잔량 | 시장구분
        /// </summary>
        private async Task<List<StockEvent>> FetchFromSeibroAsync(DateTime targetDate)
        {
            var events = new List<StockEvent>();

            try
            {
                _logger.Log($"[{SourceName}] Fetching from Seibro Mobile: {SeibroMobileUrl}");

                var doc = await LoadHtmlDocumentAsync(SeibroMobileUrl);

                // tbody 내의 tr 행 추출
                var rows = doc.DocumentNode.SelectNodes("//table/tbody/tr");

                if (rows != null)
                {
                    _logger.Log($"[{SourceName}] Found {rows.Count} rows in Seibro table");

                    foreach (var row in rows)
                    {
                        try
                        {
                            var cells = row.SelectNodes(".//td");
                            if (cells == null || cells.Count < 5) continue;

                            // 셀 구조: 해제일 | 기업명 | 해제주식수 | 예수잔량 | 시장구분
                            var dateText = CleanText(cells[0].InnerText);
                            var companyName = CleanText(cells[1].InnerText);
                            var sharesText = CleanText(cells[2].InnerText);
                            var remainingText = CleanText(cells[3].InnerText);
                            var marketType = CleanText(cells[4].InnerText);

                            // 날짜 파싱: 2025/12/25 형식
                            var dateMatch = Regex.Match(dateText, @"(\d{4})[/\-.](\d{1,2})[/\-.](\d{1,2})");
                            if (!dateMatch.Success) continue;

                            int year = int.Parse(dateMatch.Groups[1].Value);
                            int month = int.Parse(dateMatch.Groups[2].Value);
                            int day = int.Parse(dateMatch.Groups[3].Value);
                            var eventDate = new DateTime(year, month, day);

                            // 해당 날짜만 처리
                            if (eventDate.Date != targetDate.Date) continue;

                            if (string.IsNullOrEmpty(companyName) || companyName.Length < 2) continue;

                            // 주식수 포맷팅
                            string formattedShares = FormatSharesNumber(sharesText);

                            var title = $"{companyName} 의무보호해제";
                            var description = !string.IsNullOrEmpty(formattedShares)
                                ? $"해제물량: {formattedShares}주"
                                : "보호예수 물량 해제";

                            if (!string.IsNullOrEmpty(marketType))
                                description += $" ({marketType})";

                            var stockEvent = new StockEvent
                            {
                                EventTime = new DateTime(year, month, day, 9, 0, 0),
                                Title = title,
                                Description = description,
                                Source = SourceName,
                                SourceUrl = SeibroMobileUrl,
                                Category = "의무보호해제",
                                IsImportant = true,
                                RelatedStockName = companyName,
                                Hash = GenerateHash(title, eventDate, SourceName)
                            };

                            // 중복 체크 (같은 회사의 여러 해제 건 허용)
                            if (!events.Any(e => e.Hash == stockEvent.Hash))
                            {
                                _logger.Log($"[{SourceName}] Found: {companyName} - {formattedShares}주 ({marketType}) on {eventDate:yyyy-MM-dd}");
                                events.Add(stockEvent);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"[{SourceName}] Error parsing row", ex);
                        }
                    }
                }
                else
                {
                    _logger.Log($"[{SourceName}] No table rows found on Seibro page");
                }

                _logger.Log($"[{SourceName}] Scraped {events.Count} lockup events from Seibro");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error fetching from Seibro", ex);
            }

            return events;
        }

        /// <summary>
        /// 주식수 숫자 포맷팅 (예: 20207259 -> 20,207,259)
        /// </summary>
        private string FormatSharesNumber(string sharesText)
        {
            if (string.IsNullOrWhiteSpace(sharesText)) return "";

            // 숫자만 추출
            var numbersOnly = Regex.Replace(sharesText, @"[^\d]", "");
            if (string.IsNullOrEmpty(numbersOnly)) return sharesText;

            if (long.TryParse(numbersOnly, out long shares))
            {
                return shares.ToString("N0");
            }

            return sharesText;
        }

        /// <summary>
        /// 38.co.kr 신규상장 정보에서 15일 의무보호해제 계산
        /// 상장일 + 15일 = 15일 의무보호해제일
        /// </summary>
        private async Task<List<StockEvent>> Fetch15DayLockupFromNewListingsAsync(DateTime targetDate)
        {
            var events = new List<StockEvent>();

            try
            {
                _logger.Log($"[{SourceName}] Fetching 15-day lockup from new listings: {NewListingUrl}");

                var doc = await LoadHtmlDocumentAsync(NewListingUrl);

                // 신규상장 테이블에서 데이터 추출
                // 테이블 구조: 종목명 | 신규상장일 | 현재가 | 등락률 | 공모가 | ...
                var rows = doc.DocumentNode.SelectNodes("//table//tbody//tr");

                if (rows == null)
                {
                    // tbody가 없을 수 있으므로 직접 tr 찾기
                    rows = doc.DocumentNode.SelectNodes("//table//tr[td]");
                }

                if (rows != null)
                {
                    _logger.Log($"[{SourceName}] Found {rows.Count} rows in new listing table");

                    foreach (var row in rows)
                    {
                        try
                        {
                            var cells = row.SelectNodes(".//td");
                            if (cells == null || cells.Count < 2) continue;

                            // 첫 번째 셀에서 종목명 추출
                            var companyCell = cells[0];
                            var companyLink = companyCell.SelectSingleNode(".//a");
                            var companyName = companyLink != null
                                ? CleanText(companyLink.InnerText)
                                : CleanText(companyCell.InnerText);

                            // 괄호 안의 특수 표시 제거 (예: "(스팩)" 등)
                            companyName = Regex.Replace(companyName, @"\([^)]*\)", "").Trim();

                            if (string.IsNullOrEmpty(companyName) || companyName.Length < 2) continue;

                            // 두 번째 셀에서 상장일 추출 (형식: 2025/12/10)
                            var listingDateText = CleanText(cells[1].InnerText);
                            var dateMatch = Regex.Match(listingDateText, @"(\d{4})[/\-.](\d{1,2})[/\-.](\d{1,2})");

                            if (!dateMatch.Success) continue;

                            int year = int.Parse(dateMatch.Groups[1].Value);
                            int month = int.Parse(dateMatch.Groups[2].Value);
                            int day = int.Parse(dateMatch.Groups[3].Value);
                            var listingDate = new DateTime(year, month, day);

                            // 15일 의무보호해제일 계산
                            var lockupExpiryDate = listingDate.AddDays(15);

                            // 대상 날짜에 해당하는지 확인
                            if (lockupExpiryDate.Date != targetDate.Date) continue;

                            var title = $"{companyName} 15일 의무보호해제";
                            var description = $"상장일({listingDate:MM/dd}) 후 15일 보호예수 해제";

                            var stockEvent = new StockEvent
                            {
                                EventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 9, 0, 0),
                                Title = title,
                                Description = description,
                                Source = SourceName,
                                SourceUrl = NewListingUrl,
                                Category = "의무보호해제",
                                IsImportant = true,
                                RelatedStockName = companyName,
                                Hash = GenerateHash(title, targetDate, SourceName)
                            };

                            if (!events.Any(e => e.RelatedStockName == companyName))
                            {
                                _logger.Log($"[{SourceName}] Found 15-day lockup: {companyName} (listed {listingDate:yyyy-MM-dd})");
                                events.Add(stockEvent);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"[{SourceName}] Error parsing new listing row", ex);
                        }
                    }
                }
                else
                {
                    _logger.Log($"[{SourceName}] No rows found in new listing table");
                }

                _logger.Log($"[{SourceName}] Found {events.Count} 15-day lockup events");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error fetching 15-day lockup from new listings", ex);
            }

            return events;
        }

        private void AddKoreanMarketHolidays(List<StockEvent> events, DateTime targetDate)
        {
            // 2025년~2026년 한국 증시 휴장일
            var holidays = new Dictionary<DateTime, string>
            {
                { new DateTime(2025, 1, 1), "신정" },
                { new DateTime(2025, 1, 28), "설날 연휴" },
                { new DateTime(2025, 1, 29), "설날" },
                { new DateTime(2025, 1, 30), "설날 연휴" },
                { new DateTime(2025, 3, 1), "삼일절" },
                { new DateTime(2025, 5, 5), "어린이날" },
                { new DateTime(2025, 5, 6), "대체공휴일" },
                { new DateTime(2025, 6, 6), "현충일" },
                { new DateTime(2025, 8, 15), "광복절" },
                { new DateTime(2025, 10, 3), "개천절" },
                { new DateTime(2025, 10, 5), "추석 연휴" },
                { new DateTime(2025, 10, 6), "추석" },
                { new DateTime(2025, 10, 7), "추석 연휴" },
                { new DateTime(2025, 10, 8), "대체공휴일" },
                { new DateTime(2025, 10, 9), "한글날" },
                { new DateTime(2025, 12, 25), "크리스마스" },
                { new DateTime(2025, 12, 31), "연말 휴장" },
                { new DateTime(2026, 1, 1), "신정" }
            };

            if (holidays.TryGetValue(targetDate.Date, out string? holidayName))
            {
                var hash = GenerateHash($"KR_Holiday_{holidayName}", targetDate, SourceName);
                if (!events.Any(e => e.Hash == hash))
                {
                    events.Add(new StockEvent
                    {
                        EventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 0, 0, 0),
                        Title = $"국내 증시 휴장 ({holidayName})",
                        Description = $"{holidayName}으로 국내 증시가 휴장합니다.",
                        Source = SourceName,
                        SourceUrl = "https://www.krx.co.kr",
                        Category = "휴장",
                        IsImportant = true,
                        Hash = hash
                    });
                }
            }
        }
    }
}
