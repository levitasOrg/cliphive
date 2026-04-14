using System.Globalization;
using System.Windows.Data;

namespace ClipHive.Helpers;

/// <summary>Converts <see cref="AutoClearPolicy"/> enum values to human-readable strings for display.</summary>
[ValueConversion(typeof(AutoClearPolicy), typeof(string))]
public sealed class AutoClearPolicyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (AutoClearPolicy)value switch
        {
            AutoClearPolicy.TwoHours    => "2 Hours",
            AutoClearPolicy.ThreeDays   => "3 Days",
            AutoClearPolicy.FifteenDays => "15 Days",
            AutoClearPolicy.OneMonth    => "1 Month",
            AutoClearPolicy.Never       => "Never",
            _                           => value.ToString()!,
        };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
