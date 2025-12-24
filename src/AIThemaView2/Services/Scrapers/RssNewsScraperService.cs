using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using AIThemaView2.Models;
using AIThemaView2.Utils;

namespace AIThemaView2.Services.Scrapers
{
    /// <summary>
    /// RSS Feed scraper for Korean financial news
    /// </summary>
    public class RssNewsScraperService : BaseScraperService
    {
        public override string SourceName => "뉴스RSS";

        private static readonly string[] RSS_FEEDS = new[]
        {
            "https://www.mk.co.kr/rss/30100041/", // 매일경제 증권
            "https://www.hankyung.com/feed/economy", // 한국경제 경제
            "https://www.edaily.co.kr/rss/rss_news.xml?sec_cd=E02", // 이데일리 증권
        };

        public RssNewsScraperService(HttpClient httpClient, ILogger logger)
            : base(httpClient, logger)
        {
        }

        public override async Task<List<StockEvent>> FetchEventsAsync(DateTime targetDate)
        {
            var events = new List<StockEvent>();

            _logger.Log($"[{SourceName}] Fetching news from RSS feeds for {targetDate:yyyy-MM-dd}");

            foreach (var feedUrl in RSS_FEEDS)
            {
                try
                {
                    var feedEvents = await FetchRssFeedAsync(feedUrl, targetDate);
                    events.AddRange(feedEvents);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[{SourceName}] Error fetching RSS feed: {feedUrl}", ex);
                }

                // Delay between feeds
                await Task.Delay(500);
            }

            _logger.Log($"[{SourceName}] Fetched {events.Count} news items from RSS feeds");

            return events;
        }

        private async Task<List<StockEvent>> FetchRssFeedAsync(string feedUrl, DateTime targetDate)
        {
            var events = new List<StockEvent>();

            try
            {
                _logger.Log($"[{SourceName}] Loading RSS feed: {feedUrl}");

                var response = await _httpClient.GetAsync(feedUrl);
                response.EnsureSuccessStatusCode();

                var xmlContent = await response.Content.ReadAsStringAsync();
                var xmlDoc = XDocument.Parse(xmlContent);

                // RSS 2.0 format
                var items = xmlDoc.Descendants("item");

                foreach (var item in items.Take(20)) // Limit per feed
                {
                    try
                    {
                        var title = item.Element("title")?.Value;
                        var link = item.Element("link")?.Value;
                        var pubDateStr = item.Element("pubDate")?.Value;
                        var description = item.Element("description")?.Value;

                        if (string.IsNullOrEmpty(title))
                            continue;

                        // Parse publication date
                        DateTime eventTime = DateTime.Now;
                        if (!string.IsNullOrEmpty(pubDateStr))
                        {
                            // Try RFC 822 format (common in RSS)
                            if (DateTime.TryParse(pubDateStr, out var parsedDate))
                            {
                                eventTime = parsedDate;
                            }
                        }

                        // Only include today's news
                        if (eventTime.Date != targetDate.Date)
                            continue;

                        // Clean title and description
                        title = CleanHtmlTags(title);
                        description = CleanHtmlTags(description ?? "");

                        var stockEvent = new StockEvent
                        {
                            EventTime = eventTime,
                            Title = title,
                            Description = description.Length > 200 ? description.Substring(0, 200) + "..." : description,
                            Source = SourceName,
                            SourceUrl = link ?? "",
                            Category = "뉴스",
                            IsImportant = false,
                            Hash = GenerateHash(title, eventTime, SourceName)
                        };

                        events.Add(stockEvent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[{SourceName}] Error parsing RSS item", ex);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error loading RSS feed: {feedUrl}", ex);
            }

            return events;
        }

        private string CleanHtmlTags(string html)
        {
            if (string.IsNullOrEmpty(html))
                return string.Empty;

            // Remove HTML tags
            var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", "");

            // Decode HTML entities
            text = System.Net.WebUtility.HtmlDecode(text);

            // Clean whitespace
            text = CleanText(text);

            return text;
        }
    }
}
