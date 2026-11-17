using Microsoft.UI.Xaml.Data;

namespace SysPwrMon.Converters;

/// Formats a nullable percentage (0-100) as "12.5 %"; a null value (sensor
/// absent) renders "n/a" — a log/monitor must show absence, not guess it.
/// One-directional by design: these display converters never write back.
public sealed class PercentValueConverter : IValueConverter
{
    private const string UnavailableText = "n/a";

    public object Convert(object value, Type targetType, object parameter, string language)
        => value is double d ? $"{d:0.#} %" : UnavailableText;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
