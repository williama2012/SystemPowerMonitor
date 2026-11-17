using Microsoft.UI.Xaml.Data;

namespace SysPwrMon.Converters;

/// Formats a nullable memory value (mebibytes, LHM's unit) as "15,747 MiB";
/// null → "n/a".
public sealed class MebibyteValueConverter : IValueConverter
{
    private const string UnavailableText = "n/a";

    public object Convert(object value, Type targetType, object parameter, string language)
        => value is double d ? $"{d:0.#} MiB" : UnavailableText;

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotSupportedException();
}
