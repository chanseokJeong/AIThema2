using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using HtmlAgilityPack;
using AIThemaView2.Models;
using AIThemaView2.Utils;

namespace AIThemaView2.Services.Scrapers
{
    public class NaverFinanceScraperService : BaseScraperService
    {
        public override string SourceName => "Naver";

        public NaverFinanceScraperService(HttpClient httpClient, ILogger logger)
            : base(httpClient, logger)
        {
        }

        public override async Task<List<StockEvent>> FetchEventsAsync(DateTime targetDate)
        {
            var events = new List<StockEvent>();

            try
            {
                _logger.Log($"[{SourceName}] Starting data collection for {targetDate:yyyy-MM-dd}");

                // Fetch realtime news
                var realtimeEvents = await FetchRealtimeNewsAsync(targetDate);
                events.AddRange(realtimeEvents);

                // Fetch stock news
                var stockEvents = await FetchStockNewsAsync(targetDate);
                events.AddRange(stockEvents);

                // Fetch disclosure news
                var disclosureEvents = await FetchDisclosureNewsAsync(targetDate);
                events.AddRange(disclosureEvents);

                _logger.Log($"[{SourceName}] Fetched {events.Count} total events");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error fetching events", ex);
            }

            return events;
        }

        private async Task<List<StockEvent>> FetchRealtimeNewsAsync(DateTime targetDate)
        {
            var events = new List<StockEvent>();
            var url = "https://finance.naver.com/news/news_list.naver?mode=RANK";

            try
            {
                _logger.Log($"[{SourceName}] Fetching realtime news from: {url}");
                var doc = await LoadHtmlDocumentAsync(url);

                // Look for news items in the list
                var newsNodes = doc.DocumentNode.SelectNodes("//div[@class='newsList']//dd[@class='articleSubject']") ??
                               doc.DocumentNode.SelectNodes("//dl[@class='newsList']//dd[@class='articleSubject']") ??
                               doc.DocumentNode.SelectNodes("//table[@class='type5']//tr");

                if (newsNodes == null || !newsNodes.Any())
                {
                    _logger.Log($"[{SourceName}] No realtime news nodes found");
                    return events;
                }

                foreach (var node in newsNodes.Take(50)) // Limit to latest 50
                {
                    try
                    {
                        var titleNode = node.SelectSingleNode(".//a") ?? node.SelectSingleNode("./a");
                        if (titleNode == null) continue;

                        var title = CleanText(titleNode.InnerText);
                        if (string.IsNullOrEmpty(title)) continue;

                        var link = titleNode.GetAttributeValue("href", "");
                        if (!string.IsNullOrEmpty(link) && link.StartsWith("/"))
                            link = "https://finance.naver.com" + link;

                        // Get time from next sibling or parent
                        var timeNode = node.SelectSingleNode(".//span[@class='wdate']") ??
                                      node.SelectSingleNode("./following-sibling::dd[@class='articleSummary']//span[@class='wdate']") ??
                                      node.SelectSingleNode(".//td[@class='date']");

                        var timeText = timeNode != null ? CleanText(timeNode.InnerText) : "";
                        var eventTime = !string.IsNullOrEmpty(timeText)
                            ? ParseKoreanDateTime(timeText)
                            : DateTime.Now;

                        // Accept today or if parsing failed (will be Today)
                        if (eventTime.Date != targetDate.Date)
                        {
                            // Try with just time (might be HH:mm format for today's news)
                            if (timeText.Contains(":") && !timeText.Contains(".") && !timeText.Contains("-"))
                            {
                                // It's probably today's time only
                                eventTime = DateTime.Parse($"{targetDate:yyyy-MM-dd} {timeText}");
                            }
                            else
                            {
                                continue; // Skip if not today
                            }
                        }

                        var stockEvent = new StockEvent
                        {
                            EventTime = eventTime,
                            Title = title,
                            Description = "",
                            Source = SourceName,
                            SourceUrl = link,
                            Category = "뉴스",
                            IsImportant = false,
                            Hash = GenerateHash(title, eventTime, SourceName)
                        };

                        events.Add(stockEvent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[{SourceName}] Error parsing realtime news node", ex);
                    }
                }

                _logger.Log($"[{SourceName}] Fetched {events.Count} realtime news events");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error fetching realtime news", ex);
            }

            return events;
        }

        private async Task<List<StockEvent>> FetchStockNewsAsync(DateTime targetDate)
        {
            var events = new List<StockEvent>();
            var url = "https://finance.naver.com/news/news_list.naver?mode=LSS2D&section_id=101&section_id2=258";

            try
            {
                _logger.Log($"[{SourceName}] Fetching stock news from: {url}");
                var doc = await LoadHtmlDocumentAsync(url);

                var newsNodes = doc.DocumentNode.SelectNodes("//div[@class='newsList']//dd[@class='articleSubject']") ??
                               doc.DocumentNode.SelectNodes("//dl[@class='newsList']//dd[@class='articleSubject']");

                if (newsNodes == null || !newsNodes.Any())
                {
                    _logger.Log($"[{SourceName}] No stock news nodes found");
                    return events;
                }

                foreach (var node in newsNodes.Take(50))
                {
                    try
                    {
                        var titleNode = node.SelectSingleNode(".//a");
                        if (titleNode == null) continue;

                        var title = CleanText(titleNode.InnerText);
                        if (string.IsNullOrEmpty(title)) continue;

                        var link = titleNode.GetAttributeValue("href", "");
                        if (!string.IsNullOrEmpty(link) && link.StartsWith("/"))
                            link = "https://finance.naver.com" + link;

                        var timeNode = node.SelectSingleNode("./following-sibling::dd[@class='articleSummary']//span[@class='wdate']");
                        var timeText = timeNode != null ? CleanText(timeNode.InnerText) : "";

                        var eventTime = !string.IsNullOrEmpty(timeText)
                            ? ParseKoreanDateTime(timeText)
                            : DateTime.Now;

                        // Parse time-only format for today
                        if (timeText.Contains(":") && !timeText.Contains("."))
                        {
                            eventTime = DateTime.Parse($"{targetDate:yyyy-MM-dd} {timeText}");
                        }
                        else if (eventTime.Date != targetDate.Date)
                        {
                            continue;
                        }

                        var stockEvent = new StockEvent
                        {
                            EventTime = eventTime,
                            Title = title,
                            Description = "",
                            Source = SourceName,
                            SourceUrl = link,
                            Category = "증권뉴스",
                            IsImportant = false,
                            Hash = GenerateHash(title, eventTime, SourceName)
                        };

                        events.Add(stockEvent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[{SourceName}] Error parsing stock news node", ex);
                    }
                }

                _logger.Log($"[{SourceName}] Fetched {events.Count} stock news events");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error fetching stock news", ex);
            }

            return events;
        }

        private async Task<List<StockEvent>> FetchDisclosureNewsAsync(DateTime targetDate)
        {
            var events = new List<StockEvent>();
            var url = "https://finance.naver.com/news/news_list.naver?mode=LSS2D&section_id=101&section_id2=259";

            try
            {
                _logger.Log($"[{SourceName}] Fetching disclosure news from: {url}");
                var doc = await LoadHtmlDocumentAsync(url);

                var newsNodes = doc.DocumentNode.SelectNodes("//div[@class='newsList']//dd[@class='articleSubject']") ??
                               doc.DocumentNode.SelectNodes("//dl[@class='newsList']//dd[@class='articleSubject']");

                if (newsNodes == null || !newsNodes.Any())
                {
                    _logger.Log($"[{SourceName}] No disclosure news nodes found");
                    return events;
                }

                foreach (var node in newsNodes.Take(50))
                {
                    try
                    {
                        var titleNode = node.SelectSingleNode(".//a");
                        if (titleNode == null) continue;

                        var title = CleanText(titleNode.InnerText);
                        if (string.IsNullOrEmpty(title)) continue;

                        var link = titleNode.GetAttributeValue("href", "");
                        if (!string.IsNullOrEmpty(link) && link.StartsWith("/"))
                            link = "https://finance.naver.com" + link;

                        var timeNode = node.SelectSingleNode("./following-sibling::dd[@class='articleSummary']//span[@class='wdate']");
                        var timeText = timeNode != null ? CleanText(timeNode.InnerText) : "";

                        var eventTime = !string.IsNullOrEmpty(timeText)
                            ? ParseKoreanDateTime(timeText)
                            : DateTime.Now;

                        // Parse time-only format
                        if (timeText.Contains(":") && !timeText.Contains("."))
                        {
                            eventTime = DateTime.Parse($"{targetDate:yyyy-MM-dd} {timeText}");
                        }
                        else if (eventTime.Date != targetDate.Date)
                        {
                            continue;
                        }

                        var stockEvent = new StockEvent
                        {
                            EventTime = eventTime,
                            Title = title,
                            Description = "",
                            Source = SourceName,
                            SourceUrl = link,
                            Category = "공시",
                            IsImportant = true,
                            Hash = GenerateHash(title, eventTime, SourceName)
                        };

                        events.Add(stockEvent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[{SourceName}] Error parsing disclosure news node", ex);
                    }
                }

                _logger.Log($"[{SourceName}] Fetched {events.Count} disclosure news events");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error fetching disclosure news", ex);
            }

            return events;
        }
    }
}
