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

                // Get response as bytes to handle different encodings (especially EUC-KR for Korean sites)
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var bytes = await response.Content.ReadAsByteArrayAsync();

                // Try to detect encoding from Content-Type header
                var contentType = response.Content.Headers.ContentType?.CharSet;
                Encoding encoding = Encoding.UTF8;

                // Korean sites often use EUC-KR
                if (!string.IsNullOrEmpty(contentType))
                {
                    try
                    {
                        // Register EUC-KR encoding provider
                        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

                        if (contentType.ToLower().Contains("euc-kr"))
                        {
                            encoding = Encoding.GetEncoding("euc-kr");
                        }
                        else
                        {
                            encoding = Encoding.GetEncoding(contentType);
                        }
                    }
                    catch
                    {
                        // If encoding fails, try EUC-KR for Korean sites
                        if (url.Contains("naver.com") || url.Contains("krx.co.kr"))
                        {
                            try
                            {
                                encoding = Encoding.GetEncoding("euc-kr");
                            }
                            catch
                            {
                                encoding = Encoding.UTF8;
                            }
                        }
                    }
                }
                else if (url.Contains("naver.com") || url.Contains("krx.co.kr"))
                {
                    // Korean sites default to EUC-KR
                    try
                    {
                        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                        encoding = Encoding.GetEncoding("euc-kr");
                    }
                    catch
                    {
                        encoding = Encoding.UTF8;
                    }
                }

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

        protected string GenerateHash(string title, DateTime eventTime, string source)
        {
            var input = $"{title}_{eventTime:yyyyMMddHHmm}_{source}";
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
