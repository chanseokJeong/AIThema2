using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using PuppeteerSharp;
using AIThemaView2.Models;
using AIThemaView2.Utils;

namespace AIThemaView2.Services.Scrapers
{
    /// <summary>
    /// 토스증권 캘린더 스크래퍼
    /// Headless browser를 사용하여 JavaScript 렌더링 후 DOM을 스크래핑합니다.
    /// </summary>
    public class TossCalendarScraperService : BaseScraperService
    {
        public override string SourceName => "토스증권";

        private const string TossCalendarUrl = "https://www.tossinvest.com/calendar";
        private static bool _browserDownloaded = false;
        private static readonly object _downloadLock = new object();

        public TossCalendarScraperService(HttpClient httpClient, ILogger logger)
            : base(httpClient, logger)
        {
        }

        public override async Task<List<StockEvent>> FetchEventsAsync(DateTime targetDate)
        {
            var events = new List<StockEvent>();

            try
            {
                _logger.Log($"[{SourceName}] Starting Toss calendar scraping for {targetDate:yyyy-MM-dd}");

                // 브라우저 다운로드 (최초 1회)
                await EnsureBrowserDownloadedAsync();

                // Headless browser로 페이지 로드
                var htmlContent = await LoadPageWithBrowserAsync(targetDate);

                if (string.IsNullOrEmpty(htmlContent))
                {
                    _logger.Log($"[{SourceName}] Failed to load page content");
                    return events;
                }

                // HTML 파싱
                events = ParseCalendarEvents(htmlContent, targetDate);

                _logger.Log($"[{SourceName}] Fetched {events.Count} events from Toss calendar");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error fetching Toss calendar", ex);
            }

            return events;
        }

        private async Task EnsureBrowserDownloadedAsync()
        {
            if (_browserDownloaded) return;

            try
            {
                _logger.Log($"[{SourceName}] Downloading browser...");
                var browserFetcher = new BrowserFetcher();
                await browserFetcher.DownloadAsync();
                _browserDownloaded = true;
                _logger.Log($"[{SourceName}] Browser downloaded successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Failed to download browser", ex);
                throw;
            }
        }

        private async Task<string> LoadPageWithBrowserAsync(DateTime targetDate)
        {
            IBrowser? browser = null;
            try
            {
                // Headless 브라우저 실행
                browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = true,
                    Args = new[] {
                        "--no-sandbox",
                        "--disable-setuid-sandbox",
                        "--disable-dev-shm-usage",
                        "--disable-gpu"
                    }
                });

                using var page = await browser.NewPageAsync();

                // User-Agent 설정
                await page.SetUserAgentAsync("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                // 뷰포트 설정
                await page.SetViewportAsync(new ViewPortOptions
                {
                    Width = 1920,
                    Height = 1080
                });

                // 페이지 로드 (날짜 파라미터가 있다면 URL에 추가)
                var url = TossCalendarUrl;
                _logger.Log($"[{SourceName}] Loading page: {url}");

                await page.GoToAsync(url, new NavigationOptions
                {
                    WaitUntil = new[] { WaitUntilNavigation.Networkidle2 },
                    Timeout = 30000
                });

                // 특정 날짜 선택 (필요시)
                // 캘린더에서 해당 날짜 클릭
                var daySelector = $"button[data-contents-label='{targetDate.Day}']";
                try
                {
                    await page.WaitForSelectorAsync(daySelector, new WaitForSelectorOptions { Timeout = 5000 });
                    await page.ClickAsync(daySelector);

                    // 데이터 로딩 대기
                    await Task.Delay(3000);
                }
                catch
                {
                    _logger.Log($"[{SourceName}] Could not click on date {targetDate.Day}, using current view");
                }

                // 추가 대기 (동적 콘텐츠 로딩)
                await Task.Delay(2000);

                // 완성된 HTML 가져오기
                var content = await page.GetContentAsync();
                return content;
            }
            finally
            {
                if (browser != null)
                {
                    await browser.CloseAsync();
                }
            }
        }

        private List<StockEvent> ParseCalendarEvents(string html, DateTime targetDate)
        {
            var events = new List<StockEvent>();

            try
            {
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // 이벤트 목록 파싱 - 토스증권 캘린더의 실제 구조에 맞게 조정 필요
                // 일반적인 이벤트 아이템들을 찾음
                var eventNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'event')]")
                    ?? doc.DocumentNode.SelectNodes("//li[contains(@class, 'schedule')]")
                    ?? doc.DocumentNode.SelectNodes("//div[contains(@class, 'schedule')]")
                    ?? doc.DocumentNode.SelectNodes("//div[contains(@class, 'calendar-item')]");

                if (eventNodes == null)
                {
                    // 대안: 텍스트 기반 파싱
                    _logger.Log($"[{SourceName}] No event nodes found, trying text-based parsing");
                    return ParseEventsFromText(doc, targetDate);
                }

                foreach (var node in eventNodes)
                {
                    try
                    {
                        var title = CleanText(node.InnerText);
                        if (string.IsNullOrEmpty(title) || title.Length < 3)
                            continue;

                        // 카테고리 추출
                        var category = DetermineCategory(title);

                        var stockEvent = new StockEvent
                        {
                            EventTime = targetDate,
                            Title = title,
                            Description = "",
                            Source = SourceName,
                            SourceUrl = TossCalendarUrl,
                            Category = category,
                            IsImportant = true,
                            Hash = GenerateHash(title, targetDate, SourceName)
                        };

                        if (!events.Any(e => e.Hash == stockEvent.Hash))
                        {
                            events.Add(stockEvent);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[{SourceName}] Error parsing event node", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error parsing calendar HTML", ex);
            }

            return events;
        }

        private List<StockEvent> ParseEventsFromText(HtmlDocument doc, DateTime targetDate)
        {
            var events = new List<StockEvent>();

            try
            {
                // 주식 관련 이벤트 패턴만 (경제지표는 Investing.com에서 가져오므로 제외)
                var stockPatterns = new[]
                {
                    "상장", "공모", "청약", "배당", "실적", "증자",
                    "분할", "합병", "보호예수", "락업", "유상증자", "무상증자",
                    "결산", "주주총회", "IR", "컨퍼런스콜"
                };

                // 제외할 패턴 (경제지표 - Investing.com에서 이미 수집)
                var excludePatterns = new[]
                {
                    "GDP", "CPI", "PPI", "PMI", "PCE", "ISM", "FOMC",
                    "고용", "실업", "금리", "소매판매", "무역수지", "산업생산",
                    "소비자물가", "생산자물가", "비농업", "주택", "신규주문",
                    "경제지표", "발표", "지수"
                };

                // span 태그들에서 이벤트 추출
                var spanNodes = doc.DocumentNode.SelectNodes("//span");
                if (spanNodes != null)
                {
                    foreach (var span in spanNodes)
                    {
                        var text = CleanText(span.InnerText);
                        if (string.IsNullOrEmpty(text) || text.Length < 4 || text.Length > 100)
                            continue;

                        // 경제지표 관련 키워드가 포함되어 있으면 제외
                        if (excludePatterns.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase)))
                            continue;

                        // 주식 이벤트 키워드 포함 여부 확인
                        bool isRelevant = stockPatterns.Any(p => text.Contains(p, StringComparison.OrdinalIgnoreCase));

                        if (isRelevant)
                        {
                            var category = DetermineCategory(text);
                            var stockEvent = new StockEvent
                            {
                                EventTime = targetDate,
                                Title = text,
                                Description = "",
                                Source = SourceName,
                                SourceUrl = TossCalendarUrl,
                                Category = category,
                                IsImportant = true,
                                Hash = GenerateHash(text, targetDate, SourceName)
                            };

                            if (!events.Any(e => e.Title == stockEvent.Title))
                            {
                                events.Add(stockEvent);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error parsing events from text", ex);
            }

            return events;
        }

        private string DetermineCategory(string text)
        {
            var lowerText = text.ToLower();

            // 경제지표
            if (lowerText.Contains("gdp") || lowerText.Contains("cpi") || lowerText.Contains("ppi") ||
                lowerText.Contains("고용") || lowerText.Contains("실업") || lowerText.Contains("pmi") ||
                lowerText.Contains("소매") || lowerText.Contains("무역") || lowerText.Contains("경제"))
                return "경제지표";

            // 금리/FOMC
            if (lowerText.Contains("금리") || lowerText.Contains("fomc") || lowerText.Contains("연준") ||
                lowerText.Contains("한은") || lowerText.Contains("기준금리"))
                return "FOMC";

            // 실적
            if (lowerText.Contains("실적") || lowerText.Contains("earnings") || lowerText.Contains("분기"))
                return "실적";

            // 배당
            if (lowerText.Contains("배당"))
                return "배당";

            // IPO/상장
            if (lowerText.Contains("상장") || lowerText.Contains("공모") || lowerText.Contains("청약") ||
                lowerText.Contains("ipo"))
                return "IPO";

            // 휴장
            if (lowerText.Contains("휴장") || lowerText.Contains("휴일") || lowerText.Contains("휴무"))
                return "휴장";

            return "일정";
        }
    }
}
