using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace MkvAudioSwap.App;

/// <summary>把 ViewModel 里的 #RRGGBB 字符串变成画刷。空值或解析失败时退回正文色。</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s ? Palette.FromHex(s) : Palette.Text;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
