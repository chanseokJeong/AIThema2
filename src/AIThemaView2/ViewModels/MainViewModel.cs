using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using AIThemaView2.Models;
using AIThemaView2.Services.Interfaces;

namespace AIThemaView2.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly IDataCollectionService _dataCollectionService;
        private readonly ISchedulerService _schedulerService;
        private DateTime _selectedDate;
        private string _searchText = string.Empty;
        private bool _isLoading;
        private string _statusMessage = "Ready";
        private ObservableCollection<TimelineGroupViewModel> _timelineGroups;
        private string? _selectedCategory;

        public MainViewModel(
            IDataCollectionService dataCollectionService,
            ISchedulerService schedulerService)
        {
            _dataCollectionService = dataCollectionService;
            _schedulerService = schedulerService;

            SelectedDate = DateTime.Today;
            _timelineGroups = new ObservableCollection<TimelineGroupViewModel>();

            // Commands
            RefreshCommand = new RelayCommand(async _ => await RefreshDataAsync());
            SearchCommand = new RelayCommand(async _ => await SearchAsync());
            ChangeDateCommand = new RelayCommand(async _ => await LoadEventsForSelectedDateAsync());
            FilterByCategoryCommand = new RelayCommand(FilterByCategory);
            ClearFilterCommand = new RelayCommand(_ => ClearFilter());

            // Initialize
            _ = InitializeAsync();
        }

        #region Properties

        public DateTime SelectedDate
        {
            get => _selectedDate;
            set
            {
                if (SetProperty(ref _selectedDate, value))
                {
                    _ = LoadEventsForSelectedDateAsync();
                }
            }
        }

        public string SearchText
        {
            get => _searchText;
            set => SetProperty(ref _searchText, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public ObservableCollection<TimelineGroupViewModel> TimelineGroups
        {
            get => _timelineGroups;
            set => SetProperty(ref _timelineGroups, value);
        }

        public string? SelectedCategory
        {
            get => _selectedCategory;
            set => SetProperty(ref _selectedCategory, value);
        }

        #endregion

        #region Commands

        public ICommand RefreshCommand { get; }
        public ICommand SearchCommand { get; }
        public ICommand ChangeDateCommand { get; }
        public ICommand FilterByCategoryCommand { get; }
        public ICommand ClearFilterCommand { get; }

        #endregion

        #region Methods

        private async Task InitializeAsync()
        {
            StatusMessage = "Initializing...";
            _schedulerService.StartScheduler();

            // Collect initial data for today
            IsLoading = true;
            StatusMessage = "Collecting initial data...";
            try
            {
                var newEventsCount = await _dataCollectionService.CollectTodayEventsAsync();
                StatusMessage = $"Collected {newEventsCount} events";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Initial collection error: {ex.Message}";
            }

            // Load events for selected date
            await LoadEventsForSelectedDateAsync();
        }

        public async Task RefreshDataAsync()
        {
            IsLoading = true;
            StatusMessage = $"Collecting data for {SelectedDate:yyyy-MM-dd}...";

            try
            {
                var newEventsCount = await _dataCollectionService.CollectEventsForDateAsync(SelectedDate);
                await LoadEventsForSelectedDateAsync();
                StatusMessage = $"Refreshed. {newEventsCount} new events found.";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadEventsForSelectedDateAsync()
        {
            IsLoading = true;
            StatusMessage = $"Loading events for {SelectedDate:yyyy-MM-dd}...";

            try
            {
                var events = await _dataCollectionService.GetEventsForDateAsync(SelectedDate);
                UpdateTimelineGroups(events);
                StatusMessage = $"Loaded {events.Count} events";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task SearchAsync()
        {
            if (string.IsNullOrWhiteSpace(SearchText))
            {
                await LoadEventsForSelectedDateAsync();
                return;
            }

            IsLoading = true;
            StatusMessage = $"Searching for '{SearchText}'...";

            try
            {
                var events = await _dataCollectionService.SearchEventsAsync(SearchText);
                UpdateTimelineGroups(events);
                StatusMessage = $"Found {events.Count} events";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void FilterByCategory(object? parameter)
        {
            if (parameter is string category)
            {
                SelectedCategory = category;
                _ = LoadEventsForSelectedDateAsync();
            }
        }

        private void ClearFilter()
        {
            SelectedCategory = null;
            _ = LoadEventsForSelectedDateAsync();
        }

        private void UpdateTimelineGroups(List<StockEvent> events)
        {
            // Ensure UI updates happen on the UI thread
            Application.Current?.Dispatcher.Invoke(() =>
            {
                // Filter by category if selected
                if (!string.IsNullOrEmpty(SelectedCategory))
                {
                    events = events.Where(e => e.Category == SelectedCategory).ToList();
                }

                // Group events by time (hour:minute)
                var grouped = events
                    .GroupBy(e => new { e.EventTime.Hour, e.EventTime.Minute })
                    .OrderBy(g => g.Key.Hour)
                    .ThenBy(g => g.Key.Minute)
                    .Select(g => new TimelineGroupViewModel
                    {
                        TimeDisplay = $"{g.Key.Hour:D2}:{g.Key.Minute:D2}",
                        Events = new ObservableCollection<TimelineItemViewModel>(
                            g.Select(e => new TimelineItemViewModel(e))
                        )
                    })
                    .ToList();

                TimelineGroups = new ObservableCollection<TimelineGroupViewModel>(grouped);
            });
        }

        #endregion
    }

    public class TimelineGroupViewModel
    {
        public string TimeDisplay { get; set; } = string.Empty;
        public ObservableCollection<TimelineItemViewModel> Events { get; set; } = new();
    }
}
