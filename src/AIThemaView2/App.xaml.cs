using System;
using System.IO;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using AIThemaView2.Data;
using AIThemaView2.Data.Repositories;
using AIThemaView2.Services;
using AIThemaView2.Services.Interfaces;
using AIThemaView2.Services.Scrapers;
using AIThemaView2.ViewModels;
using AIThemaView2.Utils;

namespace AIThemaView2
{
    public partial class App : Application
    {
        private IHost? _host;

        public App()
        {
            try
            {
                _host = Host.CreateDefaultBuilder()
                    .ConfigureAppConfiguration((context, config) =>
                    {
                        config.SetBasePath(AppContext.BaseDirectory);
                        config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
                    })
                    .ConfigureServices((context, services) =>
                    {
                        ConfigureServices(context.Configuration, services);
                    })
                    .Build();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"애플리케이션 초기화 실패:\n\n{ex.Message}\n\n{ex.StackTrace}",
                    "초기화 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                Environment.Exit(1);
            }
        }

        private void ConfigureServices(IConfiguration configuration, IServiceCollection services)
        {
            // Configuration
            services.AddSingleton(configuration);

            // Database
            services.AddDbContext<StockEventContext>(options =>
            {
                var connectionString = configuration["Database:ConnectionString"];
                options.UseSqlite(connectionString);
            });

            // Repositories
            services.AddScoped<IEventRepository, EventRepository>();

            // HttpClient
            services.AddHttpClient();

            // Utilities
            services.AddSingleton<ILogger>(sp =>
            {
                var logPath = configuration["Logging:FilePath"] ?? "logs/app.log";
                return new Logger(logPath);
            });

            services.AddSingleton<RateLimitService>(sp =>
            {
                var maxConcurrent = configuration.GetValue<int>("DataCollection:MaxConcurrentRequests", 3);
                var delay = configuration.GetValue<int>("DataCollection:RequestDelayMs", 1000);
                return new RateLimitService(maxConcurrent, delay);
            });

            // Scrapers - Real data sources
            if (configuration.GetValue<bool>("DataSources:DART:Enabled", false))
            {
                services.AddSingleton<IScraperService, DartScraperService>();
            }

            if (configuration.GetValue<bool>("DataSources:RssNews:Enabled", false))
            {
                services.AddSingleton<IScraperService, RssNewsScraperService>();
            }

            if (configuration.GetValue<bool>("DataSources:IPO:Enabled", false))
            {
                services.AddSingleton<IScraperService, IpoScraperService>();
            }

            if (configuration.GetValue<bool>("DataSources:UsStock:Enabled", false))
            {
                services.AddSingleton<IScraperService, UsStockScraperService>();
            }

            // Mock data for testing
            if (configuration.GetValue<bool>("DataSources:MockData:Enabled", false))
            {
                services.AddSingleton<IScraperService, MockDataScraperService>();
            }

            if (configuration.GetValue<bool>("DataSources:NaverFinance:Enabled", false))
            {
                services.AddSingleton<IScraperService, NaverFinanceScraperService>();
            }

            if (configuration.GetValue<bool>("DataSources:KRX:Enabled", false))
            {
                services.AddSingleton<IScraperService, KrxScraperService>();
            }

            // Services
            services.AddSingleton<IDataCollectionService, DataCollectionService>();

            services.AddSingleton<ISchedulerService>(sp =>
            {
                var dataService = sp.GetRequiredService<IDataCollectionService>();
                var logger = sp.GetRequiredService<ILogger>();
                var interval = configuration.GetValue<int>("DataCollection:IntervalMinutes", 5);
                return new SchedulerService(dataService, logger, interval);
            });

            // ViewModels
            services.AddTransient<MainViewModel>();

            // Windows
            services.AddTransient<MainWindow>();
        }

        private async void OnStartup(object sender, StartupEventArgs e)
        {
            try
            {
                if (_host == null)
                {
                    MessageBox.Show(
                        "애플리케이션 호스트 초기화에 실패했습니다.\n설정을 확인하고 다시 시도해주세요.",
                        "시작 오류",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    Shutdown(1);
                    return;
                }

                await _host.StartAsync();

                // Initialize database
                using (var scope = _host.Services.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<StockEventContext>();
                    await context.Database.EnsureCreatedAsync();
                }

                // Show main window
                var mainWindow = _host.Services.GetRequiredService<MainWindow>();
                var viewModel = _host.Services.GetRequiredService<MainViewModel>();
                mainWindow.DataContext = viewModel;
                mainWindow.Show();

                // Log successful startup
                var logger = _host.Services.GetRequiredService<ILogger>();
                logger.Log("Application started successfully");
            }
            catch (Exception ex)
            {
                var errorMessage = $"애플리케이션 시작 실패:\n\n{ex.Message}";

                if (ex.InnerException != null)
                {
                    errorMessage += $"\n\n내부 예외: {ex.InnerException.Message}";
                }

                errorMessage += $"\n\n스택 추적:\n{ex.StackTrace}";

                MessageBox.Show(
                    errorMessage,
                    "치명적 시작 오류",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                // Try to log the error
                try
                {
                    if (_host != null)
                    {
                        var logger = _host.Services.GetService<ILogger>();
                        logger?.LogError("Application startup failed", ex);
                    }
                }
                catch
                {
                    // Ignore logging errors
                }

                Shutdown(1);
            }
        }

        private async void OnExit(object sender, ExitEventArgs e)
        {
            if (_host != null)
            {
                using (_host)
                {
                    await _host.StopAsync(TimeSpan.FromSeconds(5));
                }
            }
        }
    }
}
