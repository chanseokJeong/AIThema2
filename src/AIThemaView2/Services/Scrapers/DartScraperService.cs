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

        // 중요 공시만 필터링 - 투자자가 꼭 알아야 할 것만
        private static readonly HashSet<string> ImportantDisclosureKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 핵심 경영사항 (주가에 큰 영향)
            "합병", "분할", "해산", "상장폐지",
            "유상증자", "무상증자", "감자",

            // 실적 발표 (핵심!)
            "잠정실적", "영업실적", "실적공시",
            "매출액또는손익구조",

            // 배당 (투자자 관심)
            "현금배당", "주식배당", "배당결정",

            // 신규상장/공모주
            "신규상장", "기업공개", "상장승인",
            "코스닥시장상장", "유가증권시장상장",

            // 경영진 변경 (중대사항만)
            "대표이사변경", "대표이사선임",

            // 법적 이슈 (중요)
            "소송", "과징금", "제재", "영업정지",

            // M&A
            "인수", "피인수", "영업양수"
        };

        // 제외할 공시 키워드 (너무 일상적이거나 기술적인 것들)
        private static readonly HashSet<string> ExcludeDisclosureKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // 정정/수정 공시
            "정정", "오류정정", "기재정정",

            // 정기보고서 (일상적)
            "사업보고서", "반기보고서", "분기보고서",
            "공시서류", "연결", "전자공시", "첨부", "소액공시",

            // 일상적인 신고서 (투자자에게 불필요)
            "소유주식변동신고", "소유상황보고", "특정증권등소유",
            "임원ㆍ주요주주", "임원·주요주주",

            // 기술적/행정적 공시
            "투자설명서", "일괄신고", "증권신고서",
            "전환청구권", "신주인수권", "교환청구권", "권리행사",
            "주식매수선택권", "스톡옵션",

            // 자회사/계열사 일상 공시
            "자회사의 주요경영사항", "종속회사",

            // 채무보증 (대부분 일상적)
            "채무보증", "담보제공",

            // 기타 불필요
            "공개매수", "자진공시", "풍문", "조회공시",
            "주주총회소집", "기준일", "단일판매",
            "유형자산", "양도결정", "양수결정"
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
