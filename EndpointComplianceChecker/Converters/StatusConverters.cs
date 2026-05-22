using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using EndpointComplianceChecker.ViewModels;

namespace EndpointComplianceChecker.Converters;

public class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is CheckStatus status)
        {
            return status switch
            {
                CheckStatus.Pass    => new SolidColorBrush(Color.FromRgb(0x27, 0xAE, 0x60)),
                CheckStatus.Warning => new SolidColorBrush(Color.FromRgb(0xF3, 0x9C, 0x12)),
                CheckStatus.Fail    => new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C)),
                CheckStatus.Running => new SolidColorBrush(Color.FromRgb(0x2E, 0x86, 0xAB)),
                CheckStatus.Skipped => new SolidColorBrush(Color.FromRgb(0x71, 0x80, 0x96)),
                _                   => new SolidColorBrush(Color.FromRgb(0xA0, 0xAE, 0xC0))
            };
        }
        return Binding.DoNothing;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>Colors log lines by severity keyword: [WARN] → yellow, [ERROR] → red, default → light grey.</summary>
public class StringToColorConverter : IValueConverter
{
    private static readonly SolidColorBrush BrushWarn  = new(Color.FromRgb(0xFF, 0xD7, 0x6E));
    private static readonly SolidColorBrush BrushError = new(Color.FromRgb(0xFF, 0x6B, 0x6B));
    private static readonly SolidColorBrush BrushInfo  = new(Color.FromRgb(0x79, 0xC7, 0xE3));
    private static readonly SolidColorBrush BrushDefault = new(Color.FromRgb(0xC9, 0xD1, 0xD9));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string line)
        {
            if (line.Contains("[ERROR]", StringComparison.OrdinalIgnoreCase)) return BrushError;
            if (line.Contains("[WARN]",  StringComparison.OrdinalIgnoreCase)) return BrushWarn;
            if (line.Contains("[INFO]",  StringComparison.OrdinalIgnoreCase)) return BrushInfo;
        }
        return BrushDefault;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
