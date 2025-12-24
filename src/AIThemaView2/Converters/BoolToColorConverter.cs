using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace AIThemaView2.Converters
{
    public class BoolToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isImportant && isImportant)
            {
                // 중요 일정: 금색
                return new SolidColorBrush(Color.FromRgb(255, 215, 0)); // #FFD700
            }
            // 일반 일정: 흰색
            return new SolidColorBrush(Color.FromRgb(224, 224, 224)); // #E0E0E0
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
