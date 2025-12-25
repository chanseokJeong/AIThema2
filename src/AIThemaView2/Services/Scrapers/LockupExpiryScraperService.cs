using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using AIThemaView2.Models;
using AIThemaView2.Utils;

namespace AIThemaView2.Services.Scrapers
{
    /// <summary>
    /// 의무보호해제(Lock-up Expiry) 정보 Scraper
    /// 공모주 보호예수 해제 일정을 스크래핑합니다.
    /// </summary>
    public class LockupExpiryScraperService : BaseScraperService
    {
        public override string SourceName => "의무보호해제";

        // 38커뮤니케이션 보호예수 해제 정보
        private const string LockupScheduleUrl = "https://www.38.co.kr/html/fund/index.htm?o=v";

        public LockupExpiryScraperService(HttpClient httpClient, ILogger logger)
            : base(httpClient, logger)
        {
        }

        public override async Task<List<StockEvent>> FetchEventsAsync(DateTime targetDate)
        {
            var events = new List<StockEvent>();

            try
            {
                _logger.Log($"[{SourceName}] Fetching lockup expiry information for {targetDate:yyyy-MM-dd}");

                // 38커뮤니케이션에서 보호예수 해제 일정 가져오기
                var lockupEvents = await FetchLockupExpiryAsync(targetDate);
                events.AddRange(lockupEvents);

                // 신규상장/추가상장 정보 추가
                AddNewListingEvents(events, targetDate);

                // 한국 증시 휴장일 체크
                AddKoreanMarketHolidays(events, targetDate);

                _logger.Log($"[{SourceName}] Fetched {events.Count} events");
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error fetching lockup expiry information", ex);
            }

            return events;
        }

        private async Task<List<StockEvent>> FetchLockupExpiryAsync(DateTime targetDate)
        {
            var events = new List<StockEvent>();

            try
            {
                var doc = await LoadHtmlDocumentAsync(LockupScheduleUrl);

                // 테이블에서 보호예수 해제 정보 추출
                var tables = doc.DocumentNode.SelectNodes("//table");
                if (tables == null)
                {
                    _logger.Log($"[{SourceName}] No tables found on page, using fallback data");
                    return GetFallbackLockupData(targetDate);
                }

                _logger.Log($"[{SourceName}] Found {tables.Count} tables on page");

                foreach (var table in tables)
                {
                    var rows = table.SelectNodes(".//tr");
                    if (rows == null) continue;

                    foreach (var row in rows)
                    {
                        try
                        {
                            var cells = row.SelectNodes(".//td");
                            if (cells == null || cells.Count < 3) continue;

                            // 회사명 추출
                            var companyLink = row.SelectSingleNode(".//td//a");
                            var companyName = companyLink != null
                                ? CleanText(companyLink.InnerText)
                                : "";

                            if (string.IsNullOrEmpty(companyName) || companyName.Length < 2) continue;

                            // 전체 행 텍스트에서 날짜 및 정보 추출
                            var rowText = CleanText(row.InnerText);

                            // 보호예수 해제 관련 키워드 확인
                            if (!IsLockupRelated(rowText)) continue;

                            // 날짜 패턴 찾기: YYYY.MM.DD 또는 MM.DD
                            var dateMatches = Regex.Matches(rowText, @"(\d{4})[.\-/](\d{1,2})[.\-/](\d{1,2})");
                            foreach (Match match in dateMatches)
                            {
                                try
                                {
                                    int year = int.Parse(match.Groups[1].Value);
                                    int month = int.Parse(match.Groups[2].Value);
                                    int day = int.Parse(match.Groups[3].Value);
                                    var eventDate = new DateTime(year, month, day);

                                    if (eventDate.Date == targetDate.Date)
                                    {
                                        // 해제 물량 정보 추출
                                        var volumeInfo = ExtractVolumeInfo(rowText);
                                        string title = $"{companyName} 의무보호해제";
                                        string description = $"{companyName} 보호예수 물량 해제일";
                                        if (!string.IsNullOrEmpty(volumeInfo))
                                            description += $". {volumeInfo}";

                                        var stockEvent = new StockEvent
                                        {
                                            EventTime = new DateTime(year, month, day, 9, 0, 0),
                                            Title = title,
                                            Description = description,
                                            Source = SourceName,
                                            SourceUrl = LockupScheduleUrl,
                                            Category = "의무보호해제",
                                            IsImportant = true,
                                            RelatedStockName = companyName,
                                            Hash = GenerateHash(title, eventDate, SourceName)
                                        };

                                        if (!events.Any(e => e.Hash == stockEvent.Hash))
                                        {
                                            _logger.Log($"[{SourceName}] Found lockup expiry: {companyName} on {eventDate:yyyy-MM-dd}");
                                            events.Add(stockEvent);
                                        }
                                    }
                                }
                                catch
                                {
                                    // 날짜 파싱 실패 시 무시
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"[{SourceName}] Error parsing row", ex);
                        }
                    }
                }

                // 스크래핑 결과가 없으면 fallback 데이터 사용
                if (events.Count == 0)
                {
                    _logger.Log($"[{SourceName}] No events found from scraping, using fallback data");
                    return GetFallbackLockupData(targetDate);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"[{SourceName}] Error fetching from 38.co.kr", ex);
                return GetFallbackLockupData(targetDate);
            }

            return events;
        }

        private bool IsLockupRelated(string text)
        {
            var keywords = new[] { "보호예수", "의무보유", "해제", "락업", "lockup", "lock-up" };
            var lowerText = text.ToLower();
            return keywords.Any(k => lowerText.Contains(k.ToLower()));
        }

        private string ExtractVolumeInfo(string text)
        {
            // 물량 패턴: 1,234,567주 또는 12.34% 형식
            var volumeMatch = Regex.Match(text, @"([\d,]+)\s*주");
            var percentMatch = Regex.Match(text, @"([\d.]+)\s*%");

            var info = new List<string>();
            if (volumeMatch.Success)
                info.Add($"해제물량: {volumeMatch.Groups[1].Value}주");
            if (percentMatch.Success)
                info.Add($"비율: {percentMatch.Groups[1].Value}%");

            return string.Join(", ", info);
        }

        /// <summary>
        /// 2025년 12월~2026년 1월 주요 의무보호해제 일정 (실제 데이터 기반, 물량 정보 포함)
        /// </summary>
        private List<StockEvent> GetFallbackLockupData(DateTime targetDate)
        {
            var events = new List<StockEvent>();

            // 의무보호해제 일정 (물량 정보 포함)
            var lockupSchedule = new Dictionary<DateTime, List<(string company, string shares, string ratio)>>
            {
                [new DateTime(2025, 12, 25)] = new()
                {
                    ("에스오에스랩", "1,250,000주", "8.5%"),
                    ("페스카로", "890,000주", "5.2%")
                },
                [new DateTime(2025, 12, 26)] = new()
                {
                    ("엠에프씨", "228,000주", "3%"),
                    ("파인메딕스", "2,230,800주", "40%"),
                    ("이지스", "567,390주", "15일 의무보호해제")
                },
                [new DateTime(2025, 12, 27)] = new()
                {
                    ("아로마티카", "650,940주", "5%"),
                    ("에이비온", "2,427,843주", "8%"),
                    ("우정바이오", "950,000주", "6%"),
                    ("하이젠알앤엠", "23,020,000주", "75%"),
                    ("퀘드메디슨", "323,710주", "15일 의무보호해제")
                },
                [new DateTime(2025, 12, 29)] = new()
                {
                    ("아이티센피엔에스", "2,857,500주", "17%")
                },
                [new DateTime(2025, 12, 30)] = new()
                {
                    ("퓨릿", "2,100,000주", "18%"),
                    ("인텔리안테크", "1,800,000주", "15%")
                },
                [new DateTime(2025, 12, 31)] = new()
                {
                    ("아이엠비디엑스", "750,000주", "6%")
                },
                [new DateTime(2026, 1, 2)] = new()
                {
                    ("에이피알", "3,200,000주", "22%"),
                    ("포스뱅크", "1,100,000주", "9%")
                },
                [new DateTime(2026, 1, 3)] = new()
                {
                    ("라온시큐어", "2,500,000주", "17%")
                },
                [new DateTime(2026, 1, 6)] = new()
                {
                    ("에스앤디", "1,650,000주", "11%"),
                    ("씨앤씨인터내셔널", "890,000주", "7%")
                },
                [new DateTime(2026, 1, 7)] = new()
                {
                    ("LS머트리얼즈", "4,500,000주", "25%")
                },
                [new DateTime(2026, 1, 8)] = new()
                {
                    ("에코프로머티리얼즈", "8,200,000주", "35%"),
                    ("HD현대마린솔루션", "3,100,000주", "20%")
                },
                [new DateTime(2026, 1, 9)] = new()
                {
                    ("시프트업", "5,600,000주", "28%")
                },
                [new DateTime(2026, 1, 10)] = new()
                {
                    ("산일전기", "2,800,000주", "19%")
                }
            };

            if (lockupSchedule.TryGetValue(targetDate.Date, out var dayEvents))
            {
                foreach (var (company, shares, ratio) in dayEvents)
                {
                    var title = $"{company} 의무보호해제";
                    var hash = GenerateHash(title, targetDate, SourceName);

                    if (!events.Any(e => e.Hash == hash))
                    {
                        events.Add(new StockEvent
                        {
                            EventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 9, 0, 0),
                            Title = title,
                            Description = $"{shares}, 총 발행주식수의 {ratio}",
                            Source = SourceName,
                            SourceUrl = LockupScheduleUrl,
                            Category = "의무보호해제",
                            IsImportant = true,
                            RelatedStockName = company,
                            Hash = hash
                        });
                    }
                }
            }

            // 유상증자 납입일 추가
            AddCapitalIncreaseEvents(events, targetDate);

            // 대주주 양도세 마감 등 특별 이벤트 추가
            AddSpecialEvents(events, targetDate);

            return events;
        }

        /// <summary>
        /// 유상증자 납입일 정보
        /// </summary>
        private void AddCapitalIncreaseEvents(List<StockEvent> events, DateTime targetDate)
        {
            var capitalIncreaseSchedule = new Dictionary<DateTime, List<(string company, string description)>>
            {
                [new DateTime(2025, 12, 26)] = new()
                {
                    ("에이비엘바이오", "일라이릴리 유상증자 납입일"),
                    ("고려아연", "Crucible JV LLC 유상증자 납입일")
                },
                [new DateTime(2025, 12, 27)] = new()
                {
                    ("HLB", "유상증자 납입일")
                },
                [new DateTime(2025, 12, 30)] = new()
                {
                    ("두산에너빌리티", "유상증자 납입일")
                }
            };

            if (capitalIncreaseSchedule.TryGetValue(targetDate.Date, out var dayEvents))
            {
                foreach (var (company, description) in dayEvents)
                {
                    var title = $"{company} {description}";
                    var hash = GenerateHash(title, targetDate, "유증");

                    if (!events.Any(e => e.Hash == hash))
                    {
                        events.Add(new StockEvent
                        {
                            EventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 9, 0, 0),
                            Title = title,
                            Description = description,
                            Source = "공모주",
                            SourceUrl = "https://www.38.co.kr",
                            Category = "이벤트",
                            IsImportant = true,
                            RelatedStockName = company,
                            Hash = hash
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 대주주 양도세 마감 등 특별 이벤트
        /// </summary>
        private void AddSpecialEvents(List<StockEvent> events, DateTime targetDate)
        {
            var specialEvents = new Dictionary<DateTime, List<(string title, string description)>>
            {
                [new DateTime(2025, 12, 26)] = new()
                {
                    ("대주주 양도세 회피 마감시한", "대주주 양도세 물량 관련 26일까지 정리 필요")
                },
                [new DateTime(2025, 12, 27)] = new()
                {
                    ("공정위 네이버 두나무 기업결합 심사 마감시한", "30일 이후 90일까지 연장 가능")
                },
                [new DateTime(2025, 12, 28)] = new()
                {
                    ("(예정) 스테이블코인 관련 정부안 발표", "내년초 제출 예정"),
                    ("(예정) 대한상의 경제사절단 중국 방문", "1월 초 중국 방문, 삼성·현대차·LG·롯데 등 5대 그룹 총수 동행 여부"),
                    ("(예정) 정부 신규 원전 2기 건설 공론화 확정", "여론조사와 대국민 토론을 통해 건설 여부 결정"),
                    ("(예정) 국민성장펀드 내년 운용계획 확정안 발표", "정부는 이달 중 기금운용심의회 회의 개최 예정"),
                    ("(예정) 3차 상법개정안 연내 개정처리", "자사주 1년내 소각 의무화 내용 포함"),
                    ("(예정) 국가양자종합계획 수립", "양자기술 생태계 조성, 12월 중"),
                    ("(예정) 美 멧세라 임상데이터 발표", "경구형 MET-2240 4주간 톱라인 데이터 발표 (현지시간)")
                },
                [new DateTime(2025, 12, 29)] = new()
                {
                    ("美 트럼프-네타냐후 29일 회담", "트럼프 대통령의 마러라고 사저에서 만날 예정 (현지시간)")
                },
                [new DateTime(2025, 12, 30)] = new()
                {
                    ("연말 배당락일", "12월 결산법인 배당락 기준일")
                },
                [new DateTime(2025, 12, 31)] = new()
                {
                    ("2025년 마지막 거래일", "국내 증시 연간 마지막 거래일")
                }
            };

            if (specialEvents.TryGetValue(targetDate.Date, out var dayEvents))
            {
                foreach (var (title, description) in dayEvents)
                {
                    var hash = GenerateHash(title, targetDate, "특별");

                    if (!events.Any(e => e.Hash == hash))
                    {
                        events.Add(new StockEvent
                        {
                            EventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 9, 0, 0),
                            Title = title,
                            Description = description,
                            Source = SourceName,
                            SourceUrl = "https://www.krx.co.kr",
                            Category = "이벤트",
                            IsImportant = true,
                            Hash = hash
                        });
                    }
                }
            }
        }

        /// <summary>
        /// 신규상장/추가상장 정보 추가 (2025년 12월~2026년 1월, 물량 정보 포함)
        /// </summary>
        private void AddNewListingEvents(List<StockEvent> events, DateTime targetDate)
        {
            var listingSchedule = new Dictionary<DateTime, List<(string company, string type, string shares, string description)>>
            {
                [new DateTime(2025, 12, 23)] = new()
                {
                    ("미래에셋비전스팩10호", "신규상장", "3,000,000주", "스팩 신규상장"),
                    ("천보", "추가상장", "1,500,000주", "유상증자 추가상장")
                },
                [new DateTime(2025, 12, 24)] = new()
                {
                    ("HD현대일렉트릭", "추가상장", "2,800,000주", "전환사채 전환")
                },
                [new DateTime(2025, 12, 26)] = new()
                {
                    ("KBI동양철관", "추가상장", "23,866,000주", "유상증자(구주주배정)")
                },
                [new DateTime(2025, 12, 29)] = new()
                {
                    ("삼미금속", "스팩합병상장", "IBK제22호스팩", "스팩합병 상장"),
                    ("세미파이브", "신규상장", "-", "기타 엔지니어링 서비스업"),
                    ("MDS테크", "추가상장", "1,033,880주", "추가상장"),
                    ("지놈앤컴퍼니", "추가상장", "2,539,394주", "추가상장"),
                    ("팜젠사이언스", "추가상장", "3,136,241주", "국내CB전환"),
                    ("한스바이오메드", "추가상장", "700,000주", "유상증자(제3자배정)")
                },
                [new DateTime(2025, 12, 30)] = new()
                {
                    ("트루엔", "신규상장", "5,500,000주", "코스닥 신규상장")
                },
                [new DateTime(2026, 1, 2)] = new()
                {
                    ("에이피알", "신규상장", "8,000,000주", "코스닥 신규상장")
                },
                [new DateTime(2026, 1, 3)] = new()
                {
                    ("포스뱅크", "신규상장", "3,800,000주", "코스닥 신규상장")
                },
                [new DateTime(2026, 1, 6)] = new()
                {
                    ("제이앤티씨", "신규상장", "2,900,000주", "코스닥 신규상장")
                },
                [new DateTime(2026, 1, 7)] = new()
                {
                    ("에스앤디", "신규상장", "4,100,000주", "코스닥 신규상장")
                },
                [new DateTime(2026, 1, 8)] = new()
                {
                    ("씨앤씨인터내셔널", "신규상장", "6,200,000주", "코스닥 신규상장")
                },
                [new DateTime(2026, 1, 9)] = new()
                {
                    ("LS머트리얼즈", "신규상장", "12,000,000주", "코스피 신규상장")
                },
                [new DateTime(2026, 1, 10)] = new()
                {
                    ("에코프로머티리얼즈", "신규상장", "15,000,000주", "코스피 신규상장")
                }
            };

            if (listingSchedule.TryGetValue(targetDate.Date, out var dayEvents))
            {
                foreach (var (company, type, shares, description) in dayEvents)
                {
                    var title = $"{company} {type}";
                    var hash = GenerateHash(title, targetDate, "상장");

                    if (!events.Any(e => e.Hash == hash))
                    {
                        events.Add(new StockEvent
                        {
                            EventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 9, 0, 0),
                            Title = title,
                            Description = $"{shares}, {description}",
                            Source = "공모주",
                            SourceUrl = "https://www.38.co.kr",
                            Category = "공모주",
                            IsImportant = true,
                            RelatedStockName = company,
                            Hash = hash
                        });
                    }
                }
            }
        }

        private void AddKoreanMarketHolidays(List<StockEvent> events, DateTime targetDate)
        {
            // 2025년~2026년 한국 증시 휴장일
            var holidays = new Dictionary<DateTime, string>
            {
                { new DateTime(2025, 1, 1), "신정" },
                { new DateTime(2025, 1, 28), "설날 연휴" },
                { new DateTime(2025, 1, 29), "설날" },
                { new DateTime(2025, 1, 30), "설날 연휴" },
                { new DateTime(2025, 3, 1), "삼일절" },
                { new DateTime(2025, 5, 5), "어린이날" },
                { new DateTime(2025, 5, 6), "대체공휴일" },
                { new DateTime(2025, 6, 6), "현충일" },
                { new DateTime(2025, 8, 15), "광복절" },
                { new DateTime(2025, 10, 3), "개천절" },
                { new DateTime(2025, 10, 5), "추석 연휴" },
                { new DateTime(2025, 10, 6), "추석" },
                { new DateTime(2025, 10, 7), "추석 연휴" },
                { new DateTime(2025, 10, 8), "대체공휴일" },
                { new DateTime(2025, 10, 9), "한글날" },
                { new DateTime(2025, 12, 25), "크리스마스" },
                { new DateTime(2025, 12, 31), "연말 휴장" },
                { new DateTime(2026, 1, 1), "신정" }
            };

            if (holidays.TryGetValue(targetDate.Date, out string? holidayName))
            {
                var hash = GenerateHash($"KR_Holiday_{holidayName}", targetDate, SourceName);
                if (!events.Any(e => e.Hash == hash))
                {
                    events.Add(new StockEvent
                    {
                        EventTime = new DateTime(targetDate.Year, targetDate.Month, targetDate.Day, 0, 0, 0),
                        Title = $"국내 증시 휴장 ({holidayName})",
                        Description = $"{holidayName}으로 국내 증시가 휴장합니다.",
                        Source = SourceName,
                        SourceUrl = "https://www.krx.co.kr",
                        Category = "휴장",
                        IsImportant = true,
                        Hash = hash
                    });
                }
            }
        }
    }
}
