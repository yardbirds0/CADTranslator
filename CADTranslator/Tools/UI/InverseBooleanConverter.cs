using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

// 确保命名空间一字不差
namespace CADTranslator.Tools.UI
    {
    // 确保类名一字不差
    public class BooleanToVisibilityConverter : IValueConverter
        {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
            return (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;
            }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
            throw new NotImplementedException();
            }
        }

    public class InverseBooleanToVisibilityConverter : IValueConverter
        {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
            return (value is bool b && !b) ? Visibility.Visible : Visibility.Collapsed;
            }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
            throw new NotImplementedException();
            }
        }
    }