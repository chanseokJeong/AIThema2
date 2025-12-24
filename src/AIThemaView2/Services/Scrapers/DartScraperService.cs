using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using AIThemaView2.Models;
using AIThemaView2.Utils;
using Microsoft.Extensions.Configuration;

namespace AIThemaView2.Services.Scrapers
{
    /// <summary>
    /// DART (전자공시시스템) Open API Scraper
    /// 금융감독원 공식 공시 정보를 가져옵니다.
    /// API Key 필요: https://opendart.fss.or.kr/
    /// </summary>
    public class DartScraperService : BaseScraperService
    {
        public override string SourceName => "DART";

        private readonly IConfiguration _configuration;
        private readonly string? _apiKey;
        private const string DART_API_URL = "https://opendart.fss.or.kr/api/list.json";

        // 중요 공시만 필터링 (불필요한 공시 제외)
        private static readonly HashSet<string> ImportantDisclosureKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 주요 경영사항
            "주요사항보고",
            "합병", "분할", "해산",
            "유상증자", "무상증자", "감자",
            "주식배당", "현금배당", "배당",
            "자기주식", "소각",

            // 실적 및 재무
            "잠정실적", "영업실적", "매출액",
            "당기순이익",

            // 임원 및 지배구조
            "대표이사", "임원", "감사",
            "최대주주", "주요주주",

            // 투자 및 거래
            "타법인", "출자", "투자",
            "자산양수도", "영업양수도",
            "채무보증", "담보제공",

            // 기타 중요사항
            "특수관계인", "거래", "소송",
            "영업정지", "과징금", "제재",
            "CB", "BW", "EB", "전환사채", "신주인수권",

            // 공모주 및 신규상장 (IPO)
            "공모", "공개모집", "신규상장", "기업공개",
            "유가증권시장신규상장", "코스닥시장신규상장", "코넥스시장신규상장",
            "신규상장승인", "상장예비심사", "공모가", "청약",
            "주관회사"
        };

        // 제외할 공시 키워드 (너무 일상적이거나 기술적인 것들)
        private static readonly HashSet<string> ExcludeDisclosureKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "정정", "오류정정", "기재정정",
            "사업보고서", "반기보고서", "분기보고서",
            "공시서류", "연결",
            "전자공시", "첨부",
            "소액공시"
        };

        public DartScraperService(HttpClient httpClient, ILogger logger, IConfiguration configuration)
            : base(httpClient, logger)
        {
            _configuration = configuration;
            _apiKey = configuration["DataSources:DART:ApiKey"];
        }

        public override async Task<List<StockEvent>> FetchEventsAsync(DateTime targetDate)
        {
            var events = new List<StockEvent>();

            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.Log($"[{SourceName}] API Key not configured. Get one from https://opendart.fss.or.kr/");
                return events;
            }

            try
            {
                _logger.Log($"[{SourceName}] Fetching disclosures for {targetDate:yyyy-MM-dd}");

                // DART API parameters
                var beginDate = targetDate.ToString("yyyyMMdd");
                var endDate = targetDate.ToString("yyyyMMdd");

                var requestUrl = $"{DART_API_URL}?crtfc_key={_apiKey}&bgn_de={beginDate}&end_de={endDate}&page_count=100";

                _logger.Log($"[{SourceName}] Calling DART API");

                var response = await _httpClient.GetAsync(requestUrl);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"[{SourceName}] API request failed with status: {response.StatusCode}");
                    return events;
                }

                var jsonString = await response.Content.ReadAsStringAsync();

                using var jsonDoc = JsonDocument.Parse(jsonString);
                var root = jsonDoc.RootElement;

                // Check status
                if (root.TryGetProperty("status", out var status))
                {
                    var statusCode = status.GetString();
                    if (statusCode != "000")
                    {
                        var message = root.TryGetProperty("message", out var msg) ? msg.GetString() : "Unknown error";
                        _logger.LogError($"[{SourceName}] API Error: {statusCode} - {message}");
                        return events;
                    }
                }

                // Parse list
                if (root.TryGetProperty("list", out var list) && list.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in list.EnumerateArray())
                    {
                        try
                        {
                            var corpName = GetJsonProperty(item, "corp_name");
                            var reportNm = GetJsonProperty(item, "report_nm");
                            var rcept_no = GetJsonProperty(item, "rcept_no");
                            var rcept_dt = GetJsonProperty(item, "rcept_dt"); // yyyyMMdd format
                            var stock_code = GetJsonProperty(item, "stock_code");

                            if (string.IsNullOrEmpty(reportNm))
                                continue;

                            // 필터링: 중요하지 않은 공시 제외
                            if (!IsImportantDisclosure(reportNm))
                            {
                                continue;
                            }

                            // Parse date and time
                            DateTime eventTime;
                            if (!string.IsNullOrEmpty(rcept_dt) && rcept_dt.Length >= 8)
                            {
                                var year = int.Parse(rcept_dt.Substring(0, 4));
                                var month = int.Parse(rcept_dt.Substring(4, 2));
                                var day = int.Parse(rcept_dt.Substring(6, 2));

                                // DART doesn't provide exact time, use current time for today's disclosures
                                if (year == targetDate.Year && month == targetDate.Month && day == targetDate.Day)
                                {
                                    eventTime = new DateTime(year, month, day, DateTime.Now.Hour, DateTime.Now.Minute, 0);
                                }
                                else
                                {
                                    continue; // Skip if not target date
                                }
                            }
                            else
                            {
                                continue;
                            }

                            var title = !string.IsNullOrEmpty(corpName) ? $"[{corpName}] {reportNm}" : reportNm;
                            var url = !string.IsNullOrEmpty(rcept_no)
                                ? $"https://dart.fss.or.kr/dsaf001/main.do?rcpNo={rcept_no}"
                                : "";

                            // Use receipt number for unique hash (receipt number is always unique)
                            var uniqueId = $"{title}_{rcept_no}_{SourceName}";

                            var stockEvent = new StockEvent
                            {
                                EventTime = eventTime,
                                Title = title,
                                Description = $"공시번호: {rcept_no}",
                                Source = SourceName,
                                SourceUrl = url,
                                Category = "공시",
                                RelatedStockName = corpName,
                                RelatedStockCode = stock_code,
                                IsImportant = true,
                                Hash = GenerateHash(uniqueId, DateTime.MinValue, "")
                            };

                            events.Add(stockEvent);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"[{SourceName}] Error parsing disclosure item", ex);
                        }
                    }
                }

                _logger.Log($"[{SourceName}] Fetched {events.Count} disclosures");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error fetching from DART API", ex);
            }

            return events;
        }

        private string GetJsonProperty(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
            return string.Empty;
        }

        private bool IsImportantDisclosure(string reportName)
        {
            // 제외 키워드가 포함되어 있으면 제외
            foreach (var excludeKeyword in ExcludeDisclosureKeywords)
            {
                if (reportName.Contains(excludeKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            // 중요 키워드가 포함되어 있으면 포함
            foreach (var importantKeyword in ImportantDisclosureKeywords)
            {
                if (reportName.Contains(importantKeyword, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            // 기본적으로 제외 (중요 키워드가 없으면 불필요한 공시로 간주)
            return false;
        }
    }
}
