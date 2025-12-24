# AI Thema View 2 - Korean Stock Market Information Collector

한국 주식시장의 일자별 이슈, 행사, 공시 정보를 자동으로 수집하여 타임라인 형태로 보여주는 C# WPF 데스크톱 애플리케이션

## Features

- 네이버 증권, KRX KIND 등 여러 소스에서 주식 관련 정보 자동 수집
- 시간별 타임라인 뷰로 이벤트 표시
- 카테고리별 필터링 (공시, 뉴스, 이벤트, 실적, 공모주)
- SQLite 데이터베이스를 사용한 로컬 데이터 저장
- 자동 스케줄링 (기본 5분 간격)
- 다크 테마 UI

## Requirements

- Windows 10 or later
- .NET 8.0 SDK
- Visual Studio 2022 (권장)

## Installation

1. Clone the repository
```bash
git clone https://github.com/yourusername/AIThemaView2.git
cd AIThemaView2
```

2. Restore NuGet packages
```bash
dotnet restore
```

3. Build the project
```bash
dotnet build
```

4. Run the application
```bash
dotnet run --project src\AIThemaView2\AIThemaView2.csproj
```

## Configuration

`appsettings.json` 파일에서 다음 설정을 변경할 수 있습니다:

- `DataCollection:IntervalMinutes`: 자동 수집 간격 (분)
- `DataCollection:MaxConcurrentRequests`: 최대 동시 요청 수
- `DataCollection:RequestDelayMs`: 요청 간 지연 시간 (밀리초)
- `DataSources`: 데이터 소스 활성화/비활성화

## Technology Stack

- **.NET 8.0**: 최신 .NET 플랫폼
- **WPF**: Windows Presentation Foundation for UI
- **Entity Framework Core 8.0**: SQLite 데이터베이스 ORM
- **HtmlAgilityPack**: 웹 스크래핑
- **MVVM Pattern**: Model-View-ViewModel 아키텍처
- **Dependency Injection**: Microsoft.Extensions.DependencyInjection

## Project Structure

```
AIThemaView2/
├── src/AIThemaView2/
│   ├── Models/           # 데이터 모델
│   ├── ViewModels/       # MVVM ViewModels
│   ├── Views/            # WPF Views
│   ├── Services/         # 비즈니스 로직
│   ├── Data/             # 데이터베이스 및 Repository
│   └── Utils/            # 유틸리티
└── tests/                # 테스트 프로젝트
```

## Usage

1. 애플리케이션 실행
2. 날짜 선택기에서 조회할 날짜 선택
3. 카테고리 필터로 원하는 정보 필터링
4. 이벤트 클릭 시 원본 링크로 이동
5. 새로고침 버튼으로 최신 정보 수집

## License

MIT License

## Author

Created with Claude Code - 2025-12-24
