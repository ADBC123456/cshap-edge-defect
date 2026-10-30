using System;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using DiscSorter.Core.Models;

namespace DiscSorter.App.Converters
{
    /// <summary>
    /// 把类别（Q/H/K/W/NG）转成对应的颜色，XAML 里这样用：
    ///   Foreground="{Binding Category, Converter={StaticResource CategoryBrush}}"
    /// 各类别的颜色在 Styles.xaml 里设置。
    /// </summary>
    public class CategoryBrushConverter : IValueConverter
    {
        public Brush QBrush { get; set; }

        public Brush HBrush { get; set; }

        public Brush KBrush { get; set; }

        public Brush WBrush { get; set; }

        public Brush NGBrush { get; set; }

        /// <summary>没有类别（空库位）时用的颜色。</summary>
        public Brush EmptyBrush { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (!(value is DefectCategory))
            {
                return EmptyBrush;
            }

            DefectCategory category = (DefectCategory)value;
            switch (category)
            {
                case DefectCategory.Q:
                    return QBrush;
                case DefectCategory.H:
                    return HBrush;
                case DefectCategory.K:
                    return KBrush;
                case DefectCategory.W:
                    return WBrush;
                case DefectCategory.NG:
                    return NGBrush;
                default:
                    return EmptyBrush;
            }
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
