using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace AIThemaView2.Utils
{
    /// <summary>
    /// 이벤트 제목 정규화 및 중복 제거용 해시 생성 유틸리티
    /// </summary>
    public static class TitleNormalizer
    {
        private static readonly string[] PrefixesToRemove = new[]
        {
            "🇺🇸 ", "🇰🇷 ", "🇯🇵 ", "🇨🇳 ", "🇪🇺 ",
            "미국 ", "한국 ", "일본 ", "중국 ", "유럽 ",
            "US ", "USA ", "Korea ", "KR ", "JP ", "CN ", "EU ",
            "[미국] ", "[한국] ", "[US] ", "[KR] ",
            "(미국) ", "(한국) ", "(US) ", "(KR) ",
            "[Investing.com] ", "[토스증권] ", "[DART] ", "[38커뮤니케이션] "
        };

        /// <summary>
        /// 제목에서 국가 이모지, 국가명 접두사 등을 제거하여 정규화
        /// 서로 다른 소스에서 온 동일 이벤트를 식별하기 위함
        /// </summary>
        public static string NormalizeTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            var normalized = title;

            // 국가 이모지 제거 (유니코드 국기 이모지)
            normalized = Regex.Replace(normalized, @"[\U0001F1E0-\U0001F1FF]{2}", "");

            // 일반적인 국가명/소스명 접두사 제거
            foreach (var prefix in PrefixesToRemove)
            {
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring(prefix.Length);
                }
            }

            // 대괄호로 둘러싸인 소스명 접두사 제거 (예: "[Investing.com]", "[토스증권]")
            normalized = Regex.Replace(normalized, @"^\[[^\]]+\]\s*", "");

            // 괄호 안 내용 정규화 (예: "(MoM)", "(YoY)" 등)
            normalized = Regex.Replace(normalized, @"\s*\((MoM|YoY|QoQ|예비치|속보|확정)\)\s*", " ");

            // 특수 문자 정규화
            normalized = normalized.Replace("'", "'").Replace("'", "'");
            normalized = normalized.Replace(""", "\"").Replace(""", "\"");

            // 공백 정규화
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            // 소문자로 변환하여 대소문자 무시
            normalized = normalized.ToLowerInvariant();

            return normalized;
        }

        /// <summary>
        /// 소스를 제외한 정규화된 해시 생성 - 중복 제거용
        /// 같은 이벤트가 여러 소스에서 수집되어도 동일 해시 생성
        /// </summary>
        public static string GenerateNormalizedHash(string title, DateTime eventTime)
        {
            var normalizedTitle = NormalizeTitleForHash(title);
            // 날짜만 사용 (시간 무시) - 다른 소스에서 다른 시간으로 등록될 수 있으므로
            var input = $"{normalizedTitle}_{eventTime:yyyyMMdd}";
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
            return Convert.ToBase64String(hashBytes);
        }

        /// <summary>
        /// 해시 생성용 정규화 - 국가 정보와 소스 정보를 정규화하여 동일 이벤트 식별
        /// </summary>
        private static string NormalizeTitleForHash(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;

            var normalized = title;

            // 1. 국가 이모지를 텍스트로 변환
            normalized = normalized.Replace("🇺🇸", "미국");
            normalized = normalized.Replace("🇰🇷", "한국");
            normalized = normalized.Replace("🇯🇵", "일본");
            normalized = normalized.Replace("🇨🇳", "중국");
            normalized = normalized.Replace("🇪🇺", "유럽");

            // 2. 영문 국가명을 한글로 정규화
            normalized = Regex.Replace(normalized, @"\bUS\b|\bUSA\b|\bUnited States\b", "미국", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bKorea\b|\bKR\b|\bSouth Korea\b", "한국", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bJapan\b|\bJP\b", "일본", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bChina\b|\bCN\b", "중국", RegexOptions.IgnoreCase);
            normalized = Regex.Replace(normalized, @"\bEU\b|\bEurope\b", "유럽", RegexOptions.IgnoreCase);

            // 3. 소스명 접두사 제거
            var sourcePrefixesToRemove = new[]
            {
                "[Investing.com] ", "[토스증권] ", "[DART] ", "[38커뮤니케이션] ",
                "[Investing.com]", "[토스증권]", "[DART]", "[38커뮤니케이션]"
            };

            foreach (var prefix in sourcePrefixesToRemove)
            {
                if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    normalized = normalized.Substring(prefix.Length).TrimStart();
                }
            }

            // 4. 대괄호로 둘러싸인 소스명 접두사 제거
            normalized = Regex.Replace(normalized, @"^\[(Investing\.com|토스증권|DART|38커뮤니케이션)\]\s*", "", RegexOptions.IgnoreCase);

            // 5. 괄호 안 부가 정보 제거 (MoM, YoY, QoQ, 예비치, 확정 등)
            normalized = Regex.Replace(normalized, @"\s*\([^)]*\)\s*", " ");

            // 6. 경제지표 영문/한글 동의어 정규화
            var synonyms = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "GDP", "국내총생산" },
                { "CPI", "소비자물가지수" },
                { "PPI", "생산자물가지수" },
                { "PMI", "구매관리자지수" },
                { "PCE", "개인소비지출" },
                { "ISM", "공급관리협회" },
                { "FOMC", "연방공개시장위원회" },
                { "Nonfarm Payrolls", "비농업고용" },
                { "Unemployment Rate", "실업률" },
                { "Retail Sales", "소매판매" },
                { "Industrial Production", "산업생산" },
                { "Consumer Confidence", "소비자신뢰" },
                { "Trade Balance", "무역수지" }
            };

            foreach (var pair in synonyms)
            {
                normalized = Regex.Replace(normalized, $@"\b{Regex.Escape(pair.Key)}\b", pair.Value, RegexOptions.IgnoreCase);
            }

            // 7. 공백 정규화
            normalized = Regex.Replace(normalized, @"\s+", " ").Trim();

            // 8. 소문자로 변환
            normalized = normalized.ToLowerInvariant();

            return normalized;
        }

        /// <summary>
        /// 두 제목이 동일한 이벤트인지 확인
        /// </summary>
        public static bool AreSameEvent(string title1, string title2, DateTime date1, DateTime date2)
        {
            // 날짜가 다르면 다른 이벤트
            if (date1.Date != date2.Date)
                return false;

            var normalized1 = NormalizeTitle(title1);
            var normalized2 = NormalizeTitle(title2);

            // 완전 일치
            if (normalized1 == normalized2)
                return true;

            // 한쪽이 다른 쪽을 포함하는 경우 (긴 제목이 짧은 제목을 포함)
            if (normalized1.Length > 5 && normalized2.Length > 5)
            {
                if (normalized1.Contains(normalized2) || normalized2.Contains(normalized1))
                    return true;
            }

            return false;
        }
    }
}
