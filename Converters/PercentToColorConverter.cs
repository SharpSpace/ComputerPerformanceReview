using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace ComputerPerformanceReview.Converters;

/// <summary>
/// Converts a percentage (0-100) to a color brush (green/yellow/red).
/// ConverterParameter can override thresholds as "warn,critical" e.g. "70,90".
/// </summary>
public class PercentToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double percent = System.Convert.ToDouble(value);
        double warn = 70, critical = 90;

        if (parameter is string p)
        {
            var parts = p.Split(',');
            if (parts.Length == 2)
            {
                double.TryParse(parts[0], CultureInfo.InvariantCulture, out warn);
                double.TryParse(parts[1], CultureInfo.InvariantCulture, out critical);
            }
        }

        var app = Application.Current;
        if (percent > critical)
            return app.FindResource("CriticalBrush");
        if (percent > warn)
            return app.FindResource("WarningBrush");
        return app.FindResource("OkBrush");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a severity string ("Critical"/"Warning") to a color brush.
/// </summary>
public class SeverityToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var app = Application.Current;
        return value?.ToString() switch
        {
            "Critical" => app.FindResource("CriticalBrush"),
            "Warning" => app.FindResource("WarningBrush"),
            _ => app.FindResource("OkBrush")
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts bytes (long) to a human-readable string like "3.5 GB".
/// </summary>
public class BytesToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        long bytes = System.Convert.ToInt64(value);
        return FormatHelper.FormatBytes(bytes);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts a number to a compact string (1.1M, 46.5K, etc.)
/// </summary>
public class NumberToCompactConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double num = System.Convert.ToDouble(value);
        return FormatHelper.FormatNumber((long)num);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns Visibility.Visible if value is true, else Collapsed.
/// ConverterParameter options:
///   "invert" – show when value is false/null/0
///   "empty"  – show when value is 0, null, false, or empty string (e.g. for "no items" messages)
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = parameter is string { } p
            ? p switch
            {
                "invert" => value is not true,
                "empty"  => value is null or false or 0 or 0L or 0.0 or "",
                _        => value is true,
            }
            : value is true;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns Visibility.Visible if value is a non-null non-empty string, else Collapsed.
/// </summary>
public class StringNotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Converts an ActionStep difficulty string to a badge background color.
/// "Easy" → green, "Medium" → orange, "Hard" → red, else gray.
/// </summary>
public class DifficultyToColorConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value?.ToString() switch
        {
            "Easy"   => new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x2D, 0xA4, 0x4E)), // green
            "Medium" => new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0xBF, 0x87, 0x00)), // amber
            "Hard"   => new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0xCF, 0x22, 0x2E)), // red
            _        => new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(0x57, 0x60, 0x6A)), // gray
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
