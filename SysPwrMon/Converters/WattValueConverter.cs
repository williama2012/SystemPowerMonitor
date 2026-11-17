using Microsoft.UI.Xaml.Data;

namespace SysPwrMon.Converters;

/// Formats a nullable power value (watts) as "43.5 W"; null → "n/a".
public sealed class WattValueConverter : IValueConverter
{
    private const string UnavailableText = "n/a";

    public object Convert(object value, Type targetType, object parameter, string language)
        => value is double d ? $"{d:0.#} W" : UnavailableText;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
