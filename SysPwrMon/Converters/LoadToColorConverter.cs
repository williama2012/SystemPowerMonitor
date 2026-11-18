using Microsoft.UI;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;

namespace SysPwrMon.Converters;

public sealed class LoadToColorConverter : IValueConverter
{


    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is double progress)
        {
            // Normalize 0-100 to 0.0-1.0
            double ratio = Math.Clamp(progress / 100.0, 0.0, 1.0);

            byte g = ratio < 0.5 ? (byte)255 : (byte)(255 * (1 - (ratio - 0.5) * 2));
            byte r = ratio < 0.5 ? (byte)(255 * (ratio * 2)) : (byte)200;
            
            return new SolidColorBrush(Windows.UI.Color.FromArgb(255, r, g, 0));
        }
        return new SolidColorBrush(Colors.DodgerBlue);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
        => throw new NotImplementedException();
}