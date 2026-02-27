using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Tech901.IdPhoto.Core.Enums;

namespace Tech901.IdPhoto.App.Converters;

public class SpeechStateToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SpeechState state || parameter is not string param)
            return Visibility.Collapsed;

        var targets = param.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var target in targets)
        {
            if (Enum.TryParse<SpeechState>(target, ignoreCase: true, out var parsed) && parsed == state)
                return Visibility.Visible;
        }

        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
