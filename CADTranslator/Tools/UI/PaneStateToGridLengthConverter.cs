// 文件路径: CADTranslator/Tools/UI/PaneStateToGridLengthConverter.cs
using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace CADTranslator.Tools.UI
    {
    public class PaneStateToGridLengthConverter : IValueConverter
        {
        // 这是展开时侧边栏的宽度
        public double ExpandedWidth { get; set; } = 320;

        // 这是收起时侧边栏的宽度
        public double CompactWidth { get; set; } = 48;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
            // 检查传入的值是不是一个布尔值 (bool)
            if (value is bool isPaneOpen)
                {
                // 如果是展开状态 (true)，就返回展开宽度；否则返回紧凑宽度
                return new GridLength(isPaneOpen ? ExpandedWidth : CompactWidth);
                }

            // 如果传入的不是布尔值，返回一个默认的宽度
            return new GridLength(ExpandedWidth);
            }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
            // 我们不需要反向转换，所以这里直接抛出异常
            throw new NotImplementedException();
            }
        }
    }