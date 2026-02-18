using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace ReadStorm.Desktop.Views;

/// <summary>
/// 当章节索引等于当前阅读索引时，返回高亮背景色；否则返回透明。
/// MultiBinding values: [0]=item index (int), [1]=current chapter index (int)
/// </summary>
public class TocHighlightConverter : IMultiValueConverter
{
    public static readonly TocHighlightConverter Instance = new();

    private static readonly IBrush HighlightBrush = new SolidColorBrush(Color.FromArgb(40, 59, 130, 246)); // 浅蓝高亮
    private static readonly IBrush TransparentBrush = Brushes.Transparent;

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2
            && values[0] is int itemIndex
            && values[1] is int currentIndex)
        {
            return itemIndex == currentIndex ? HighlightBrush : TransparentBrush;
        }
        return TransparentBrush;
    }
}

/// <summary>
/// 当章节索引等于当前阅读索引时，返回 Bold；否则返回 Normal。
/// </summary>
public class TocFontWeightConverter : IMultiValueConverter
{
    public static readonly TocFontWeightConverter Instance = new();

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count >= 2
            && values[0] is int itemIndex
            && values[1] is int currentIndex)
        {
            return itemIndex == currentIndex ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal;
        }
        return Avalonia.Media.FontWeight.Normal;
    }
}
