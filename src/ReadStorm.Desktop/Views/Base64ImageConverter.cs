using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace ReadStorm.Desktop.Views;

/// <summary>
/// 将封面数据转换为 Avalonia Bitmap。
/// 支持 byte[]（BLOB）与 Base64 字符串；空值或解析失败时返回 null。
/// </summary>
public sealed class Base64ImageConverter : IValueConverter
{
    public static readonly Base64ImageConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is byte[] blob && blob.Length > 0)
        {
            try
            {
                using var stream = new MemoryStream(blob);
                return new Bitmap(stream);
            }
            catch
            {
                // blob 解析失败
            }
        }

        if (value is string base64 && !string.IsNullOrWhiteSpace(base64))
        {
            try
            {
                var bytes = System.Convert.FromBase64String(base64);
                using var stream = new MemoryStream(bytes);
                return new Bitmap(stream);
            }
            catch
            {
                // base64 解码失败
            }
        }
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
