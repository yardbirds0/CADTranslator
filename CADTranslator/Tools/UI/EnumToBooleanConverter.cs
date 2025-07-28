using System;
using System.Globalization;
using System.Windows.Data;

namespace CADTranslator.Tools.UI
    {
    public class EnumToBooleanConverter : IValueConverter
        {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
            if (value == null || parameter == null)
                return false;
            return value.ToString().Equals(parameter.ToString(), StringComparison.InvariantCultureIgnoreCase);
            }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
            if (value == null || parameter == null)
                return null;
            if ((bool)value)
                return Enum.Parse(targetType, parameter.ToString(), true);
            return Binding.DoNothing;
            }
        }
    }