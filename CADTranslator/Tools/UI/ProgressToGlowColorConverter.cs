using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace CADTranslator.Tools.UI
    {
    public class ProgressToGlowColorConverter : IValueConverter
        {
        // 定义渐变的起始和结束颜色
        public Color StartColor { get; set; } = (Color)ColorConverter.ConvertFromString("#2de4ea"); // 蓝色
        public Color EndColor { get; set; } = (Color)ColorConverter.ConvertFromString("#76ff03");   // 绿色

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
            if (value is double progress)
                {
                // 线性插值计算当前进度下的颜色
                byte r = (byte)(StartColor.R + (EndColor.R - StartColor.R) * progress);
                byte g = (byte)(StartColor.G + (EndColor.G - StartColor.G) * progress);
                byte b = (byte)(StartColor.B + (EndColor.B - StartColor.B) * progress);
                return new SolidColorBrush(Color.FromRgb(r, g, b));
                }
            return new SolidColorBrush(StartColor);
            }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
            throw new NotImplementedException();
            }
        }
    }