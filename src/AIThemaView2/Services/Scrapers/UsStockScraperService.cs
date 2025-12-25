using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using HtmlAgilityPack;
using AIThemaView2.Models;
using AIThemaView2.Utils;

namespace AIThemaView2.Services.Scrapers
{
    /// <summary>
    /// 미국 주식시장 주요 일정 Scraper
    /// Investing.com API에서 실제 경제지표, FOMC, 실적발표 일정을 가져옵니다.
    /// </summary>
    public class UsStockScraperService : BaseScraperService
    {
        public override string SourceName => "미국일정";

        private const string EconomicCalendarApiUrl = "https://www.investing.com/economic-calendar/Service/getCalendarFilteredData";
        private const string EconomicCalendarUrl = "https://www.investing.com/economic-calendar/";

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

                // Investing.com API에서 경제 캘린더 데이터 가져오기
                var calendarEvents = await FetchEconomicCalendarApiAsync(targetDate);
                events.AddRange(calendarEvents);

                // 주요 미국 휴장일 체크
                AddUsMarketHolidays(events, targetDate);

                // 미국 조기폐장일 체크 (Early Close)
                AddUsEarlyCloseEvents(events, targetDate);

                // 주요 FOMC 일정 추가 (고정 일정)
                AddFomcSchedule(events, targetDate);

                // 2025년 12월 주요 경제지표 일정 추가
                AddDecember2025Events(events, targetDate);

                _logger.Log($"[{SourceName}] Fetched {events.Count} US market events");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error fetching US market events", ex);
            }

            return events;
        }

        private async Task<List<StockEvent>> FetchEconomicCalendarApiAsync(DateTime targetDate)
        {
            var events = new List<StockEvent>();

            try
            {
                // Investing.com AJAX API 호출 - 미국 + 한국 경제지표
                var dateFrom = targetDate.ToString("yyyy-MM-dd");
                var dateTo = targetDate.ToString("yyyy-MM-dd");

                // country codes: 5 = United States, 11 = South Korea
                var formContent = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("country[]", "5"), // United States
                    new KeyValuePair<string, string>("country[]", "11"), // South Korea
                    new KeyValuePair<string, string>("dateFrom", dateFrom),
                    new KeyValuePair<string, string>("dateTo", dateTo),
                    new KeyValuePair<string, string>("timeZone", "88"), // Seoul timezone
                    new KeyValuePair<string, string>("timeFilter", "timeRemain"),
                    new KeyValuePair<string, string>("currentTab", "custom"),
                    new KeyValuePair<string, string>("limit_from", "0")
                });

                var request = new HttpRequestMessage(HttpMethod.Post, EconomicCalendarApiUrl);
                request.Content = formContent;
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Accept-Language", "ko-KR,ko;q=0.9,en-US;q=0.8,en;q=0.7");
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");
                request.Headers.Add("Referer", EconomicCalendarUrl);

                var response = await _httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.Log($"[{SourceName}] API failed with status: {response.StatusCode}, using fallback");
                    return GetFallbackEconomicEvents(targetDate);
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();

                // JSON 파싱
                using var doc = JsonDocument.Parse(jsonResponse);
                var root = doc.RootElement;

                if (root.TryGetProperty("data", out var dataElement))
                {
                    var htmlData = dataElement.GetString();
                    if (!string.IsNullOrEmpty(htmlData))
                    {
                        events = ParseEconomicCalendarHtml(htmlData, targetDate);
                    }
                }

                if (events.Count == 0)
                {
                    _logger.Log($"[{SourceName}] No events from API, using fallback");
                    return GetFallbackEconomicEvents(targetDate);
                }

                // 이름 기반 중복 제거 (같은 지표가 여러 번 나오는 경우)
                events = RemoveDuplicatesByName(events);
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error calling economic calendar API", ex);
                return GetFallbackEconomicEvents(targetDate);
            }

            return events;
        }

        /// <summary>
        /// 이름 기반으로 중복 경제지표 제거 (같은 지표가 여러 시간에 나오면 첫 번째만 유지)
        /// </summary>
        private List<StockEvent> RemoveDuplicatesByName(List<StockEvent> events)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<StockEvent>();

            foreach (var evt in events.OrderBy(e => e.EventTime))
            {
                // 제목에서 핵심 키워드 추출 (번역된 한글 제목 기준)
                var normalizedKey = NormalizeIndicatorTitle(evt.Title);

                if (!seen.Contains(normalizedKey))
                {
                    seen.Add(normalizedKey);
                    result.Add(evt);
                }
            }

            return result;
        }

        /// <summary>
        /// 경제지표 제목 정규화 - 유사한 이름의 지표를 같은 것으로 처리
        /// (예: "CPI (MoM)"과 "CPI (YoY)"를 동일 그룹으로 처리)
        /// </summary>
        private string NormalizeIndicatorTitle(string title)
        {
            // 공백, 특수문자 제거하고 소문자로 변환
            var normalized = title.ToLower()
                .Replace(" ", "")
                .Replace("-", "")
                .Replace("_", "")
                .Replace("(", "")
                .Replace(")", "")
                .Replace("q1", "")
                .Replace("q2", "")
                .Replace("q3", "")
                .Replace("q4", "")
                .Replace("mom", "")
                .Replace("yoy", "")
                .Replace("전월대비", "")
                .Replace("전년대비", "");

            return normalized;
        }

        private List<StockEvent> ParseEconomicCalendarHtml(string html, DateTime targetDate)
        {
            var events = new List<StockEvent>();

            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var rows = doc.DocumentNode.SelectNodes("//tr[contains(@class, 'js-event-item')]");
                if (rows == null) return events;

                foreach (var row in rows)
                {
                    try
                    {
                        // 시간 추출
                        var timeNode = row.SelectSingleNode(".//td[contains(@class, 'time')]");
                        var timeText = timeNode != null ? CleanText(timeNode.InnerText) : "";

                        // 국가 추출 (국기 아이콘의 title 속성에서)
                        var flagNode = row.SelectSingleNode(".//td[contains(@class, 'flagCur')]//span[contains(@class, 'cemark')]")
                            ?? row.SelectSingleNode(".//td[contains(@class, 'left')]//span[@title]");
                        var countryTitle = flagNode?.GetAttributeValue("title", "") ?? "";

                        // 국가 코드 판별
                        bool isKorea = countryTitle.Contains("Korea", StringComparison.OrdinalIgnoreCase) ||
                                      countryTitle.Contains("한국", StringComparison.OrdinalIgnoreCase) ||
                                      row.OuterHtml.Contains("South Korea");
                        bool isUS = countryTitle.Contains("United States", StringComparison.OrdinalIgnoreCase) ||
                                   countryTitle.Contains("미국", StringComparison.OrdinalIgnoreCase) ||
                                   row.OuterHtml.Contains("United States");

                        // 이벤트명 추출
                        var eventNode = row.SelectSingleNode(".//td[contains(@class, 'event')]//a")
                            ?? row.SelectSingleNode(".//td[contains(@class, 'event')]");
                        var eventName = eventNode != null ? CleanText(eventNode.InnerText) : "";

                        if (string.IsNullOrEmpty(eventName)) continue;

                        // 불필요한 경제지표 필터링 (채권 경매 등)
                        if (ShouldFilterOut(eventName)) continue;

                        // 중요도 (별 개수)
                        var bullIcons = row.SelectNodes(".//td[contains(@class, 'sentiment')]//i[contains(@class, 'grayFullBullishIcon')]");
                        int importance = bullIcons?.Count ?? 0;

                        // 중요도가 낮은 지표는 제외 (별 2개 미만)
                        if (importance < 2) continue;

                        // 실제값, 예측값, 이전값
                        var actualNode = row.SelectSingleNode(".//td[contains(@class, 'act')]");
                        var forecastNode = row.SelectSingleNode(".//td[contains(@class, 'fore')]");
                        var previousNode = row.SelectSingleNode(".//td[contains(@class, 'prev')]");

                        var actual = actualNode != null ? CleanText(actualNode.InnerText) : "";
                        var forecast = forecastNode != null ? CleanText(forecastNode.InnerText) : "";
                        var previous = previousNode != null ? CleanText(previousNode.InnerText) : "";

                        // 시간 파싱
                        DateTime eventTime = targetDate.Date;
                        if (!string.IsNullOrEmpty(timeText) && timeText.Contains(":"))
                        {
                            var timeParts = timeText.Split(':');
                            if (timeParts.Length >= 2 && int.TryParse(timeParts[0], out int hour) && int.TryParse(timeParts[1], out int minute))
                            {
                                eventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, hour, minute, 0);
                            }
                        }

                        string description = BuildDescription(eventName, actual, forecast, previous);
                        string translatedName = TranslateEventName(eventName);

                        // 국가 접두사 추가 (원본 이름에 국가 정보가 없는 경우)
                        string countryPrefix = "";
                        if (isKorea && !translatedName.StartsWith("한국"))
                            countryPrefix = "🇰🇷 ";
                        else if (isUS && !translatedName.StartsWith("미국"))
                            countryPrefix = "🇺🇸 ";

                        var stockEvent = new StockEvent
                        {
                            EventTime = eventTime,
                            Title = countryPrefix + translatedName,
                            Description = description,
                            Source = SourceName,
                            SourceUrl = EconomicCalendarUrl,
                            Category = DetermineCategory(eventName),
                            IsImportant = importance >= 2,
                            Hash = GenerateHash(eventName, eventTime, SourceName)
                        };

                        if (!events.Any(e => e.Hash == stockEvent.Hash))
                        {
                            events.Add(stockEvent);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[{SourceName}] Error parsing row", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error parsing economic calendar HTML", ex);
            }

            return events;
        }

        /// <summary>
        /// 불필요한 경제지표 필터링 (채권 경매, 중요도 낮은 지표 등)
        /// </summary>
        private bool ShouldFilterOut(string eventName)
        {
            var lowerName = eventName.ToLower();

            // 채권 경매 관련 - 일반 투자자에게 불필요
            var auctionKeywords = new[]
            {
                "bill auction", "note auction", "bond auction", "tips auction",
                "t-bill", "t-note", "t-bond", "treasury auction",
                "week bill", "year note", "year bond",
                "coupon equivalent", "high rate", "bid-to-cover"
            };

            if (auctionKeywords.Any(keyword => lowerName.Contains(keyword)))
                return true;

            // 기타 중요도 낮은 지표들
            var lowPriorityKeywords = new[]
            {
                "redbook", "api weekly", "baker hughes", "rig count",
                "mortgage market index", "mba mortgage", "mba purchase",
                "chain store", "icsc", "johnson redbook",
                "foreign buying", "tic", "treasury international capital",
                "federal budget", "monthly budget", "budget balance",
                "consumer credit", "credit card", "installment credit"
            };

            if (lowPriorityKeywords.Any(keyword => lowerName.Contains(keyword)))
                return true;

            return false;
        }

        private List<StockEvent> GetFallbackEconomicEvents(DateTime targetDate)
        {
            var events = new List<StockEvent>();
            var dayOfWeek = targetDate.DayOfWeek;

            // 주말 제외
            if (dayOfWeek == DayOfWeek.Saturday || dayOfWeek == DayOfWeek.Sunday)
                return events;

            // 매월 첫째 금요일: 비농업 고용지표 (NFP) - 가장 중요한 지표
            if (dayOfWeek == DayOfWeek.Friday && targetDate.Day <= 7)
            {
                events.Add(new StockEvent
                {
                    EventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 22, 30, 0),
                    Title = "미국 비농업 고용지표 (NFP)",
                    Description = "월간 비농업 고용지표 발표 - 미국 고용시장 핵심 지표",
                    Source = SourceName,
                    SourceUrl = EconomicCalendarUrl,
                    Category = "경제지표",
                    IsImportant = true,
                    Hash = GenerateHash("Nonfarm Payrolls", targetDate, SourceName)
                });
            }

            return events;
        }

        private void AddDecember2025Events(List<StockEvent> events, DateTime targetDate)
        {
            // 2025년 12월 주요 경제 일정 (핵심 지표만)
            var december2025Events = new Dictionary<DateTime, List<(string title, string time, string description, string category, bool important)>>
            {
                [new DateTime(2025, 12, 5)] = new()
                {
                    ("비농업 고용지표 (NFP)", "22:30", "11월 비농업 고용지표 발표 - 미국 고용시장 핵심 지표", "경제지표", true)
                },
                [new DateTime(2025, 12, 11)] = new()
                {
                    ("소비자물가지수 (CPI)", "22:30", "11월 소비자물가지수 발표 - 인플레이션 핵심 지표", "경제지표", true)
                },
                [new DateTime(2025, 12, 12)] = new()
                {
                    ("생산자물가지수 (PPI)", "22:30", "11월 생산자물가지수 발표 - 인플레이션 선행 지표", "경제지표", true)
                },
                [new DateTime(2025, 12, 17)] = new()
                {
                    ("소매판매", "22:30", "11월 소매판매 발표 - 소비 동향 지표", "경제지표", true)
                },
                [new DateTime(2025, 12, 19)] = new()
                {
                    ("근원 PCE 물가지수", "22:30", "11월 근원 PCE 물가지수 발표 - 연준 선호 인플레이션 지표", "경제지표", true)
                },
                [new DateTime(2025, 12, 24)] = new()
                {
                    ("GDP 확정치", "22:30", "3분기 GDP 최종 수치 발표", "경제지표", true)
                }
            };

            if (december2025Events.TryGetValue(targetDate.Date, out var dayEvents))
            {
                foreach (var (title, time, description, category, important) in dayEvents)
                {
                    var timeParts = time.Split(':');
                    var eventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day,
                        int.Parse(timeParts[0]), int.Parse(timeParts[1]), 0);

                    var hash = GenerateHash($"Dec2025_{title}", eventTime, SourceName);

                    // 중복 체크
                    if (!events.Any(e => e.Hash == hash || e.Title.Contains(title.Split(' ')[0])))
                    {
                        events.Add(new StockEvent
                        {
                            EventTime = eventTime,
                            Title = $"미국 {title}",
                            Description = description,
                            Source = SourceName,
                            SourceUrl = EconomicCalendarUrl,
                            Category = category,
                            IsImportant = important,
                            Hash = hash
                        });
                    }
                }
            }
        }

        private void AddUsMarketHolidays(List<StockEvent> events, DateTime targetDate)
        {
            // 2025년 미국 증시 휴장일
            var holidays = new Dictionary<DateTime, string>
            {
                { new DateTime(2025, 1, 1), "신년" },
                { new DateTime(2025, 1, 20), "마틴 루터 킹 데이" },
                { new DateTime(2025, 2, 17), "대통령의 날" },
                { new DateTime(2025, 4, 18), "성금요일" },
                { new DateTime(2025, 5, 26), "현충일" },
                { new DateTime(2025, 6, 19), "준틴스 데이" },
                { new DateTime(2025, 7, 4), "독립기념일" },
                { new DateTime(2025, 9, 1), "노동절" },
                { new DateTime(2025, 11, 27), "추수감사절" },
                { new DateTime(2025, 12, 25), "크리스마스" }
            };

            if (holidays.TryGetValue(targetDate.Date, out string? holidayName))
            {
                var hash = GenerateHash($"US_Holiday_{holidayName}", targetDate, SourceName);
                if (!events.Any(e => e.Hash == hash))
                {
                    events.Add(new StockEvent
                    {
                        EventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 0, 0, 0),
                        Title = $"미국 증시 휴장 ({holidayName})",
                        Description = $"{holidayName} 연휴로 미국 증시가 휴장합니다.",
                        Source = SourceName,
                        SourceUrl = "https://www.nyse.com/markets/hours-calendars",
                        Category = "휴장",
                        IsImportant = true,
                        Hash = hash
                    });
                }
            }
        }

        private void AddUsEarlyCloseEvents(List<StockEvent> events, DateTime targetDate)
        {
            // 미국 증시 조기폐장일 (오후 1시 폐장, 한국시간 새벽 3시)
            var earlyCloseDates = new Dictionary<DateTime, string>
            {
                // 2025년 조기폐장일
                { new DateTime(2025, 7, 3), "독립기념일 전날" },
                { new DateTime(2025, 11, 28), "추수감사절 다음날" },
                { new DateTime(2025, 12, 24), "크리스마스 이브" },
                // 2026년 조기폐장일
                { new DateTime(2026, 7, 3), "독립기념일 전날" },
                { new DateTime(2026, 11, 27), "추수감사절 다음날" },
                { new DateTime(2026, 12, 24), "크리스마스 이브" }
            };

            if (earlyCloseDates.TryGetValue(targetDate.Date, out string? reason))
            {
                var hash = GenerateHash($"US_EarlyClose_{reason}", targetDate, SourceName);
                if (!events.Any(e => e.Hash == hash))
                {
                    events.Add(new StockEvent
                    {
                        EventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 3, 0, 0), // 한국시간 새벽 3시 (미국 오후 1시)
                        Title = $"미국 증시 조기폐장 ({reason})",
                        Description = $"{reason}로 미국 증시가 오후 1시(현지시간)에 조기폐장합니다. 한국시간 새벽 3시.",
                        Source = SourceName,
                        SourceUrl = "https://www.nyse.com/markets/hours-calendars",
                        Category = "휴장",
                        IsImportant = true,
                        Hash = hash
                    });
                }
            }
        }

        private void AddFomcSchedule(List<StockEvent> events, DateTime targetDate)
        {
            // 2025년 FOMC 회의 일정 (결정 발표일)
            var fomcDates = new List<DateTime>
            {
                new DateTime(2025, 1, 29),
                new DateTime(2025, 3, 19),
                new DateTime(2025, 5, 7),
                new DateTime(2025, 6, 18),
                new DateTime(2025, 7, 30),
                new DateTime(2025, 9, 17),
                new DateTime(2025, 11, 5),
                new DateTime(2025, 12, 17)
            };

            if (fomcDates.Contains(targetDate.Date))
            {
                var hash = GenerateHash("FOMC_Decision", targetDate, SourceName);
                if (!events.Any(e => e.Hash == hash))
                {
                    events.Add(new StockEvent
                    {
                        EventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 4, 0, 0),
                        Title = "FOMC 금리 결정 발표",
                        Description = "연방공개시장위원회 기준금리 결정 및 성명서 발표. 파월 의장 기자회견 예정.",
                        Source = SourceName,
                        SourceUrl = "https://www.federalreserve.gov/",
                        Category = "FOMC",
                        IsImportant = true,
                        Hash = hash
                    });
                }
            }

            // FOMC 회의 시작일 (결정 발표 전날)
            var fomcStartDates = fomcDates.Select(d => d.AddDays(-1)).ToList();
            if (fomcStartDates.Contains(targetDate.Date))
            {
                var hash = GenerateHash("FOMC_Start", targetDate, SourceName);
                if (!events.Any(e => e.Hash == hash))
                {
                    events.Add(new StockEvent
                    {
                        EventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 0, 0, 0),
                        Title = "FOMC 회의 시작",
                        Description = "연방공개시장위원회 정례회의 시작. 내일 금리 결정 발표 예정.",
                        Source = SourceName,
                        SourceUrl = "https://www.federalreserve.gov/",
                        Category = "FOMC",
                        IsImportant = true,
                        Hash = hash
                    });
                }
            }
        }

        private string BuildDescription(string eventName, string actual, string forecast, string previous)
        {
            var parts = new List<string> { TranslateEventName(eventName) };

            if (!string.IsNullOrEmpty(actual) && actual != "&nbsp;" && actual.Trim() != "")
                parts.Add($"실제: {actual}");
            if (!string.IsNullOrEmpty(forecast) && forecast != "&nbsp;" && forecast.Trim() != "")
                parts.Add($"예측: {forecast}");
            if (!string.IsNullOrEmpty(previous) && previous != "&nbsp;" && previous.Trim() != "")
                parts.Add($"이전: {previous}");

            return string.Join(" | ", parts);
        }

        private string TranslateEventName(string eventName)
        {
            var translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "Initial Jobless Claims", "신규 실업수당 청구건수" },
                { "Continuing Jobless Claims", "계속 실업수당 청구건수" },
                { "Nonfarm Payrolls", "비농업 고용지표" },
                { "Non-Farm Employment Change", "비농업 고용 변화" },
                { "Unemployment Rate", "실업률" },
                { "CPI", "소비자물가지수" },
                { "Consumer Price Index", "소비자물가지수" },
                { "Core CPI", "근원 소비자물가지수" },
                { "PPI", "생산자물가지수" },
                { "Producer Price Index", "생산자물가지수" },
                { "Core PPI", "근원 생산자물가지수" },
                { "GDP", "GDP 성장률" },
                { "Gross Domestic Product", "GDP 성장률" },
                { "Retail Sales", "소매판매" },
                { "Industrial Production", "산업생산" },
                { "Housing Starts", "주택착공건수" },
                { "Building Permits", "건축허가건수" },
                { "Existing Home Sales", "기존주택판매" },
                { "New Home Sales", "신규주택판매" },
                { "Consumer Confidence", "소비자신뢰지수" },
                { "CB Consumer Confidence", "CB 소비자신뢰지수" },
                { "Michigan Consumer Sentiment", "미시간 소비자심리지수" },
                { "UoM Consumer Sentiment", "미시간 소비자심리지수" },
                { "ISM Manufacturing PMI", "ISM 제조업 PMI" },
                { "ISM Services PMI", "ISM 서비스업 PMI" },
                { "ISM Non-Manufacturing PMI", "ISM 비제조업 PMI" },
                { "Durable Goods Orders", "내구재 주문" },
                { "Core Durable Goods Orders", "근원 내구재 주문" },
                { "Trade Balance", "무역수지" },
                { "FOMC", "연준 금리결정" },
                { "Fed Interest Rate Decision", "연준 금리결정" },
                { "Federal Funds Rate", "연방기금금리" },
                { "MBA Mortgage Applications", "MBA 모기지 신청건수" },
                { "Crude Oil Inventories", "원유재고" },
                { "EIA Crude Oil Inventories", "EIA 원유재고" },
                { "Natural Gas Storage", "천연가스 재고" },
                { "PCE Price Index", "PCE 물가지수" },
                { "Core PCE Price Index", "근원 PCE 물가지수" },
                { "Personal Spending", "개인소비지출" },
                { "Personal Income", "개인소득" },
                { "ADP Employment Change", "ADP 민간고용 변화" },
                { "JOLTs Job Openings", "JOLTS 구인건수" },
                { "S&P Global Manufacturing PMI", "S&P 제조업 PMI" },
                { "S&P Global Services PMI", "S&P 서비스업 PMI" },
                { "Philadelphia Fed Manufacturing Index", "필라델피아 연준 제조업지수" },
                { "Empire State Manufacturing Index", "뉴욕 엠파이어스테이트 제조업지수" },
                { "Chicago PMI", "시카고 PMI" },
                // 한국 경제지표
                { "South Korea Interest Rate", "한국 기준금리 결정" },
                { "BoK Interest Rate Decision", "한국은행 기준금리 결정" },
                { "South Korea GDP", "한국 GDP 성장률" },
                { "South Korea CPI", "한국 소비자물가지수" },
                { "South Korea Trade Balance", "한국 무역수지" },
                { "South Korea Exports", "한국 수출" },
                { "South Korea Imports", "한국 수입" },
                { "South Korea Industrial Production", "한국 산업생산" },
                { "South Korea Unemployment Rate", "한국 실업률" },
                { "South Korea Manufacturing PMI", "한국 제조업 PMI" },
                { "South Korea Current Account", "한국 경상수지" },
                { "South Korea Retail Sales", "한국 소매판매" },
                { "South Korea Consumer Confidence", "한국 소비자신뢰지수" },
                { "South Korea PPI", "한국 생산자물가지수" }
            };

            foreach (var kvp in translations)
            {
                if (eventName.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    return kvp.Value;
            }

            return eventName;
        }

        private string DetermineCategory(string eventName)
        {
            var lowerName = eventName.ToLower();

            if (lowerName.Contains("fomc") || lowerName.Contains("fed") || lowerName.Contains("interest rate"))
                return "FOMC";
            if (lowerName.Contains("earnings") || lowerName.Contains("실적"))
                return "실적";
            if (lowerName.Contains("holiday") || lowerName.Contains("closed"))
                return "휴장";

            return "경제지표";
        }
    }
}
