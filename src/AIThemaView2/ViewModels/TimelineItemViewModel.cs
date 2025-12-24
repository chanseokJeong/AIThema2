using System.Diagnostics;
using System.Windows.Input;
using AIThemaView2.Models;

namespace AIThemaView2.ViewModels
{
    public class TimelineItemViewModel : ViewModelBase
    {
        private readonly StockEvent _stockEvent;

        public TimelineItemViewModel(StockEvent stockEvent)
        {
            _stockEvent = stockEvent;
            OpenLinkCommand = new RelayCommand(_ => OpenLink(), _ => !string.IsNullOrEmpty(SourceUrl));
        }

        public string Title => _stockEvent.Title;
        public string? Description => _stockEvent.Description;
        public string Category => _stockEvent.Category;
        public string CategoryColor => _stockEvent.CategoryColor;
        public bool IsImportant => _stockEvent.IsImportant;
        public string Source => _stockEvent.Source;
        public string? SourceUrl => _stockEvent.SourceUrl;
        public string? RelatedStockName => _stockEvent.RelatedStockName;
        public string? RelatedStockCode => _stockEvent.RelatedStockCode;
        public string TimeDisplay => _stockEvent.TimeDisplay;

        public ICommand OpenLinkCommand { get; }

        private void OpenLink()
        {
            if (!string.IsNullOrEmpty(SourceUrl))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = SourceUrl,
                        UseShellExecute = true
                    });
                }
                catch
                {
                    // Handle error silently
                }
            }
        }
    }
}
