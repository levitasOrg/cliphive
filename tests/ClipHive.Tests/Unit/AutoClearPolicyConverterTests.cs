using System.Globalization;
using ClipHive.Helpers;
using Xunit;

namespace ClipHive.Tests.Unit;

public sealed class AutoClearPolicyConverterTests
{
    private readonly AutoClearPolicyConverter _converter = new();

    [Theory]
    [InlineData(AutoClearPolicy.TwoHours, "2 Hours")]
    [InlineData(AutoClearPolicy.ThreeDays, "3 Days")]
    [InlineData(AutoClearPolicy.FifteenDays, "15 Days")]
    [InlineData(AutoClearPolicy.OneMonth, "1 Month")]
    [InlineData(AutoClearPolicy.Never, "Never")]
    public void Convert_MapsPolicyToDisplayString(AutoClearPolicy policy, string expected)
    {
        Assert.Equal(expected,
            _converter.Convert(policy, typeof(string), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void ConvertBack_IsNotSupported()
    {
        Assert.Throws<NotImplementedException>(() =>
            _converter.ConvertBack("Never", typeof(AutoClearPolicy), null!, CultureInfo.InvariantCulture));
    }
}
