using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HtmlAgilityPack;
using AIThemaView2.Models;
using AIThemaView2.Services.Interfaces;
using AIThemaView2.Utils;

namespace AIThemaView2.Services.Scrapers
{
    public abstract class BaseScraperService : IScraperService
    {
        protected readonly HttpClient _httpClient;
        protected readonly HtmlWeb _htmlWeb;
        protected readonly ILogger _logger;

        public abstract string SourceName { get; }

        protected BaseScraperService(HttpClient httpClient, ILogger logger)
        {
            _httpClient = httpClient;
            _logger = logger;
            _htmlWeb = new HtmlWeb();

            // Configure HttpClient
            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,*/*;q=0.8");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "ko-KR,ko;q=0.9,en;q=0.8");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public abstract Task<List<StockEvent>> FetchEventsAsync(DateTime targetDate);

        protected async Task<HtmlDocument> LoadHtmlDocumentAsync(string url)
        {
            try
            {
                _logger.Log($"[{SourceName}] Loading URL: {url}");

                // Register encoding provider for EUC-KR support
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                // Get response as bytes to handle different encodings
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var bytes = await response.Content.ReadAsByteArrayAsync();

                // Detect encoding
                Encoding encoding = DetectEncoding(url, bytes, response.Content.Headers.ContentType?.CharSet);

                var html = encoding.GetString(bytes);
                var doc = new HtmlDocument();
                doc.LoadHtml(html);
                return doc;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error loading HTML from {url}", ex);
                throw;
            }
        }

        private Encoding DetectEncoding(string url, byte[] bytes, string? contentTypeCharset)
        {
            // Korean sites that typically use EUC-KR
            var eucKrSites = new[] { "38.co.kr", "naver.com", "krx.co.kr", "kind.krx.co.kr" };

            // Check if it's a known EUC-KR site
            bool isKoreanSite = eucKrSites.Any(site => url.Contains(site));

            // Try to detect from Content-Type header
            if (!string.IsNullOrEmpty(contentTypeCharset))
            {
                try
                {
                    var charset = contentTypeCharset.ToLower().Trim();
                    if (charset.Contains("euc-kr") || charset.Contains("euc_kr"))
                    {
                        return Encoding.GetEncoding("euc-kr");
                    }
                    if (charset.Contains("utf-8") || charset.Contains("utf8"))
                    {
                        return Encoding.UTF8;
                    }
                    return Encoding.GetEncoding(contentTypeCharset);
                }
                catch
                {
                    // Fall through to other detection methods
                }
            }

            // Try to detect from HTML meta tag
            var htmlPreview = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 2048));

            // Check for charset in meta tag
            var charsetMatch = System.Text.RegularExpressions.Regex.Match(
                htmlPreview,
                @"charset\s*=\s*[""']?([^""'\s>]+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (charsetMatch.Success)
            {
                var detectedCharset = charsetMatch.Groups[1].Value.ToLower().Trim();
                try
                {
                    if (detectedCharset.Contains("euc-kr") || detectedCharset.Contains("euc_kr"))
                    {
                        return Encoding.GetEncoding("euc-kr");
                    }
                    if (detectedCharset.Contains("utf-8") || detectedCharset.Contains("utf8"))
                    {
                        return Encoding.UTF8;
                    }
                    return Encoding.GetEncoding(detectedCharset);
                }
                catch
                {
                    // Fall through
                }
            }

            // Default for known Korean sites
            if (isKoreanSite)
            {
                try
                {
                    return Encoding.GetEncoding("euc-kr");
                }
                catch
                {
                    return Encoding.UTF8;
                }
            }

            return Encoding.UTF8;
        }

        protected string GenerateHash(string title, DateTime eventTime, string source)
        {
            var input = $"{title}_{eventTime:yyyyMMddHHmm}_{source}";
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hashBytes);
        }

        /// <summary>
        /// 제목에서 국가 이모지, 국가명 접두사 등을 제거하여 정규화
        /// 서로 다른 소스에서 온 동일 이벤트를 식별하기 위함
        /// </summary>
        protected string NormalizeTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            var normalized = title;

            // 국가 이모지 제거
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[\U0001F1E0-\U0001F1FF]{2}", "");

            // 일반적인 국가명 접두사 제거
            var prefixesToRemove = new[]
            {
                "🇺🇸 ", "🇰🇷 ", "🇯🇵 ", "🇨🇳 ", "🇪🇺 ",
                "미국 ", "한국 ", "일본 ", "중국 ", "유럽 ",
                "US ", "USA ", "Korea ", "KR ", "JP ", "CN ", "EU ",
                "[미국] ", "[한국] ", "[US] ", "[KR] ",
                "(미국) ", "(한국) ", "(US) ", "(KR) "
            };

            foreach (var prefix in prefixesToRemove)
            {
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring(prefix.Length);
                }
            }

            // 소스명 접두사 제거 (예: "[Investing.com]", "[토스증권]")
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"^\[[^\]]+\]\s*", "");

            // 공백 정규화
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\s+", " ").Trim();

            // 소문자로 변환하여 대소문자 무시
            normalized = normalized.ToLowerInvariant();

            return normalized;
        }

        /// <summary>
        /// 소스를 제외한 정규화된 해시 생성 - 중복 제거용
        /// </summary>
        protected string GenerateNormalizedHash(string title, DateTime eventTime)
        {
            var normalizedTitle = NormalizeTitle(title);
            var input = $"{normalizedTitle}_{eventTime:yyyyMMdd}"; // 시간 제외, 날짜만 사용
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hashBytes);
        }

        protected DateTime ParseKoreanDateTime(string dateTimeStr)
        {
            // Parse Korean datetime formats like "2025.12.24 12:21"
            try
            {
                // Remove any extra whitespace
                dateTimeStr = dateTimeStr.Trim();

                // Common formats
                string[] formats = {
                    "yyyy.MM.dd HH:mm",
                    "yyyy-MM-dd HH:mm",
                    "yyyy.MM.dd HH:mm:ss",
                    "MM.dd HH:mm",
                    "MM-dd HH:mm",
                    "yyyy/MM/dd HH:mm",
                    "MM/dd HH:mm"
                };

                foreach (var format in formats)
                {
                    if (DateTime.TryParseExact(dateTimeStr, format, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out DateTime result))
                    {
                        // If year is missing, add current year
                        if (format.StartsWith("MM"))
                        {
                            result = new DateTime(DateTime.Now.Year, result.Month, result.Day,
                                result.Hour, result.Minute, result.Second);
                        }
                        return result;
                    }
                }

                // Fallback to standard parsing
                if (DateTime.TryParse(dateTimeStr, out DateTime fallbackResult))
                {
                    return fallbackResult;
                }

                // If all parsing fails, return current time
                _logger.LogError($"[{SourceName}] Failed to parse datetime: '{dateTimeStr}'");
                return DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error parsing datetime '{dateTimeStr}'", ex);
                return DateTime.Now;
            }
        }

        protected string CleanText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text.Trim()
                .Replace("\r\n", " ")
                .Replace("\n", " ")
                .Replace("\t", " ")
                .Replace("  ", " ")
                .Trim();
        }

        protected string ExtractStockCode(string text)
        {
            // Extract stock code from text (usually 6 digits)
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var match = System.Text.RegularExpressions.Regex.Match(text, @"\b\d{6}\b");
            return match.Success ? match.Value : string.Empty;
        }
    }
}
