using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using HtmlAgilityPack;
using AIThemaView2.Models;
using AIThemaView2.Utils;
using Microsoft.Extensions.Configuration;

namespace AIThemaView2.Services.Scrapers
{
    public class KrxScraperService : BaseScraperService
    {
        public override string SourceName => "KRX";

        private const string KIND_URL = "https://kind.krx.co.kr/disclosure/todaydisclosure.do";
        private readonly IConfiguration _configuration;
        private readonly bool _useOpenAPI;
        private readonly string? _apiKey;
        private readonly string? _openApiUrl;
        private readonly string? _disclosureUrl;

        public KrxScraperService(HttpClient httpClient, ILogger logger, IConfiguration configuration)
            : base(httpClient, logger)
        {
            _configuration = configuration;
            _useOpenAPI = configuration.GetValue<bool>("DataSources:KRX:UseOpenAPI", false);
            _apiKey = configuration["DataSources:KRX:ApiKey"];
            _openApiUrl = configuration["DataSources:KRX:OpenAPIUrl"];
            _disclosureUrl = configuration["DataSources:KRX:DisclosureUrl"];
        }

        public override async Task<List<StockEvent>> FetchEventsAsync(DateTime targetDate)
        {
            var events = new List<StockEvent>();

            try
            {
                _logger.Log($"[{SourceName}] Starting data collection for {targetDate:yyyy-MM-dd}");

                // Use OpenAPI if configured and API key is available
                if (_useOpenAPI && !string.IsNullOrEmpty(_apiKey) && !string.IsNullOrEmpty(_openApiUrl))
                {
                    _logger.Log($"[{SourceName}] Using OpenAPI");
                    return await FetchEventsFromOpenAPIAsync(targetDate);
                }

                // Fallback to web scraping
                _logger.Log($"[{SourceName}] Using web scraping");
                var doc = await LoadHtmlDocumentAsync(KIND_URL);

                // Try multiple possible selectors for KIND disclosure items
                var disclosureNodes = doc.DocumentNode.SelectNodes("//table[@class='type-00']//tbody//tr") ??
                                     doc.DocumentNode.SelectNodes("//table[@class='TB_dis1']//tr") ??
                                     doc.DocumentNode.SelectNodes("//div[@class='AnnTbl']//tr") ??
                                     doc.DocumentNode.SelectNodes("//table//tr[@class='list']");

                if (disclosureNodes == null || !disclosureNodes.Any())
                {
                    _logger.Log($"[{SourceName}] No disclosure nodes found. KIND system may require authentication or have changed structure.");
                    return events;
                }

                foreach (var node in disclosureNodes)
                {
                    try
                    {
                        // KRX KIND typically has columns: Time | Company | Title | Submitter | Code
                        // Try to extract time - usually first column
                        var timeNode = node.SelectSingleNode(".//td[1]") ??
                                      node.SelectSingleNode(".//td[@class='time']");

                        if (timeNode == null) continue;

                        var timeText = CleanText(timeNode.InnerText);
                        if (string.IsNullOrEmpty(timeText)) continue;

                        var eventTime = ParseKoreanDateTime(timeText);

                        // Skip if not from target date
                        if (eventTime.Date != targetDate.Date) continue;

                        // Try to extract company name - usually second column
                        var companyNode = node.SelectSingleNode(".//td[2]") ??
                                         node.SelectSingleNode(".//td[@class='company']");

                        var companyName = companyNode != null ? CleanText(companyNode.InnerText) : "";

                        // Try to extract disclosure title - usually third column
                        var titleNode = node.SelectSingleNode(".//td[3]//a") ??
                                       node.SelectSingleNode(".//td[@class='title']//a") ??
                                       node.SelectSingleNode(".//a");

                        if (titleNode == null) continue;

                        var title = CleanText(titleNode.InnerText);
                        if (string.IsNullOrEmpty(title)) continue;

                        var link = titleNode.GetAttributeValue("href", "");
                        if (!string.IsNullOrEmpty(link))
                        {
                            if (link.StartsWith("/"))
                                link = "https://kind.krx.co.kr" + link;
                            else if (!link.StartsWith("http"))
                                link = "https://kind.krx.co.kr/" + link;
                        }

                        // Try to extract stock code - usually fourth or fifth column
                        var codeNode = node.SelectSingleNode(".//td[4]") ??
                                      node.SelectSingleNode(".//td[5]") ??
                                      node.SelectSingleNode(".//td[@class='code']");

                        var stockCode = codeNode != null ? ExtractStockCode(CleanText(codeNode.InnerText)) : "";

                        var stockEvent = new StockEvent
                        {
                            EventTime = eventTime,
                            Title = !string.IsNullOrEmpty(companyName) ? $"[{companyName}] {title}" : title,
                            Description = !string.IsNullOrEmpty(stockCode) ? $"증권코드: {stockCode}" : "",
                            Source = SourceName,
                            SourceUrl = link,
                            Category = "공시",
                            RelatedStockName = companyName,
                            RelatedStockCode = stockCode,
                            IsImportant = true,
                            Hash = GenerateHash(title, eventTime, SourceName)
                        };

                        events.Add(stockEvent);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"[{SourceName}] Error parsing disclosure node", ex);
                    }
                }

                _logger.Log($"[{SourceName}] Fetched {events.Count} events");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error fetching events. KIND system may require authentication.", ex);
            }

            return events;
        }

        private async Task<List<StockEvent>> FetchEventsFromOpenAPIAsync(DateTime targetDate)
        {
            var events = new List<StockEvent>();

            try
            {
                // Use disclosure URL if available, otherwise use main API URL
                var apiUrl = !string.IsNullOrEmpty(_disclosureUrl) ? _disclosureUrl : _openApiUrl;

                if (string.IsNullOrEmpty(apiUrl))
                {
                    _logger.Log($"[{SourceName}] No API URL configured");
                    return events;
                }

                // Build API request URL with query parameters
                var requestUrl = $"{apiUrl}?apikey={_apiKey}&basDt={targetDate:yyyyMMdd}";

                _logger.Log($"[{SourceName}] Calling API: {apiUrl.Split('?')[0]}");

                var response = await _httpClient.GetAsync(requestUrl);

                // Log status code
                _logger.Log($"[{SourceName}] API Response Status: {response.StatusCode}");

                if (!response.IsSuccessStatusCode)
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError($"[{SourceName}] API Error Response: {errorContent}");
                    return events;
                }

                var jsonString = await response.Content.ReadAsStringAsync();
                _logger.Log($"[{SourceName}] OpenAPI response received, length: {jsonString.Length}");

                // Log first 500 characters of response for debugging
                if (jsonString.Length > 0)
                {
                    var preview = jsonString.Length > 500 ? jsonString.Substring(0, 500) + "..." : jsonString;
                    _logger.Log($"[{SourceName}] Response preview: {preview}");
                }

                // Parse JSON response
                using var jsonDoc = JsonDocument.Parse(jsonString);
                var root = jsonDoc.RootElement;

                // Try different possible response structures
                if (root.TryGetProperty("result", out var result))
                {
                    // KRX data-dbg API format: { "result": [...] }
                    events = ParseKrxApiResponse(result, targetDate);
                }
                else if (root.TryGetProperty("OutBlock_1", out var outBlock))
                {
                    // Alternative format
                    events = ParseKrxApiResponse(outBlock, targetDate);
                }
                else if (root.TryGetProperty("output", out var output))
                {
                    // Another alternative format
                    events = ParseKrxApiResponse(output, targetDate);
                }
                else if (root.ValueKind == JsonValueKind.Array)
                {
                    // Response is directly an array
                    events = ParseKrxApiResponse(root, targetDate);
                }
                else
                {
                    _logger.Log($"[{SourceName}] Unknown API response format. Root properties: {string.Join(", ", root.EnumerateObject().Select(p => p.Name))}");
                }

                _logger.Log($"[{SourceName}] Fetched {events.Count} events from OpenAPI");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error fetching from OpenAPI", ex);
            }

            return events;
        }

        private List<StockEvent> ParseKrxApiResponse(JsonElement dataArray, DateTime targetDate)
        {
            var events = new List<StockEvent>();

            if (dataArray.ValueKind != JsonValueKind.Array)
            {
                _logger.Log($"[{SourceName}] Data is not an array, type: {dataArray.ValueKind}");
                return events;
            }

            var itemCount = 0;
            foreach (var item in dataArray.EnumerateArray())
            {
                itemCount++;
                try
                {
                    // Log first item for debugging
                    if (itemCount == 1)
                    {
                        _logger.Log($"[{SourceName}] First item properties: {string.Join(", ", item.EnumerateObject().Select(p => p.Name))}");
                    }

                    // Extract fields (field names may vary depending on API endpoint)
                    // Try multiple possible field names for each value
                    var companyName = GetJsonStringValue(item,
                        "ISU_ABBRV", "ISU_NM", "CORP_NAME", "corpName", "companyName", "isuAbbrv");

                    var title = GetJsonStringValue(item,
                        "TITLE", "DSC_TITLE", "REPORT_NM", "title", "disclTitle", "reportNm");

                    var stockCode = GetJsonStringValue(item,
                        "ISU_CD", "STK_CODE", "ISU_SRT_CD", "isuCd", "stkCode", "isuSrtCd");

                    var timeStr = GetJsonStringValue(item,
                        "DSPL_TIME", "TRD_DD", "REG_DT", "BAS_DD", "dsplTime", "basDd", "regDt", "trdDd");

                    // If no title found, try to construct from available data
                    if (string.IsNullOrEmpty(title))
                    {
                        // Log what fields are available
                        var availableFields = string.Join(", ", item.EnumerateObject().Select(p => $"{p.Name}={p.Value}"));
                        _logger.Log($"[{SourceName}] No title found in item. Available fields: {availableFields}");
                        continue;
                    }

                    // Parse time - if no time string, use target date
                    DateTime eventTime;
                    if (!string.IsNullOrEmpty(timeStr))
                    {
                        eventTime = ParseKoreanDateTime(timeStr);
                    }
                    else
                    {
                        // If no time info, use target date with current time
                        eventTime = targetDate.Date.AddHours(DateTime.Now.Hour).AddMinutes(DateTime.Now.Minute);
                        _logger.Log($"[{SourceName}] No time found, using target date");
                    }

                    // Only include if from target date (unless we have no date filter)
                    if (eventTime.Date != targetDate.Date)
                    {
                        _logger.Log($"[{SourceName}] Skipping item from different date: {eventTime:yyyy-MM-dd}");
                        continue;
                    }

                    var stockEvent = new StockEvent
                    {
                        EventTime = eventTime,
                        Title = !string.IsNullOrEmpty(companyName) ? $"[{companyName}] {title}" : title,
                        Description = !string.IsNullOrEmpty(stockCode) ? $"증권코드: {stockCode}" : "",
                        Source = SourceName,
                        SourceUrl = "https://kind.krx.co.kr",
                        Category = "공시",
                        RelatedStockName = companyName,
                        RelatedStockCode = ExtractStockCode(stockCode),
                        IsImportant = true,
                        Hash = GenerateHash(title, eventTime, SourceName)
                    };

                    events.Add(stockEvent);
                }
                catch (Exception ex)
                {
                    _logger.LogError($"[{SourceName}] Error parsing API item #{itemCount}", ex);
                }
            }

            _logger.Log($"[{SourceName}] Parsed {events.Count} events from {itemCount} items");
            return events;
        }

        private string GetJsonStringValue(JsonElement element, params string[] possibleKeys)
        {
            foreach (var key in possibleKeys)
            {
                if (element.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String)
                {
                    return value.GetString() ?? string.Empty;
                }
            }
            return string.Empty;
        }
    }
}
