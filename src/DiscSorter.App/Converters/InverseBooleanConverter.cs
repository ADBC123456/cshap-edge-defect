using System;
using System.Globalization;
using System.Windows.Data;

namespace DiscSorter.App.Converters
{
    /// <summary>bool 取反：true → false，false → true。两个单选按钮绑同一个 bool 属性时用。</summary>
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool)
            {
                return !(bool)value;
            }
            return false;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool)
            {
                return !(bool)value;
            }
            return false;
        }
    }
}
