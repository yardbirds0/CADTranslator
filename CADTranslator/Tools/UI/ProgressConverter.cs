// 文件路径: CADTranslator/Tools/UI/ProgressConverter.cs
// 【请用此代码完整替换】

using System;
using System.Globalization;
using System.Windows.Data;

namespace CADTranslator.Tools.UI
    {
    public class ProgressConverter : IValueConverter
        {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
            try
                {
                // 使用 System.Convert.ToDouble 来安全地处理任何数字类型 (int, double, etc.)
                double progressValue = System.Convert.ToDouble(value);

                // 将 0-100 的范围转换为 0.0-1.0 的范围
                return progressValue / 100.0;
                }
            catch
                {
                // 如果转换失败，返回一个安全的默认值 0
                return 0;
                }
            }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
            // 我们不需要反向转换，所以这里不需要实现
            throw new NotImplementedException();
            }
        }
    }