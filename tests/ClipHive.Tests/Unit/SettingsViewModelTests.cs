using ClipHive.ViewModels;
using Moq;
using Xunit;

namespace ClipHive.Tests.Unit;

public sealed class SettingsViewModelTests
{
    private static (SettingsViewModel vm, Mock<ISettingsService> service, Func<AppSettings?> saved)
        CreateSut(AppSettings? loaded = null)
    {
        var settings = loaded ?? new AppSettings();
        AppSettings? captured = null;

        var service = new Mock<ISettingsService>();
        service.Setup(s => s.Load()).Returns(settings);
        service.Setup(s => s.Save(It.IsAny<AppSettings>()))
               .Callback<AppSettings>(s => captured = s);

        var vm = new SettingsViewModel(service.Object);
        return (vm, service, () => captured);
    }

    [Fact]
    public void Save_PreservesSettingsTheDialogDoesNotEdit()
    {
        // Regression: Save used to construct a fresh AppSettings, silently resetting
        // the plain-text hotkey and ignore lists to defaults.
        var loaded = new AppSettings
        {
            PlainTextHotkeyModifiers = 0x0008, // Win
            PlainTextHotkeyVirtualKey = 0x42,  // 'B'
            IgnoredApps = { "KeePass" },
            IgnorePatterns = { "^ghp_" },
        };
        var (vm, _, saved) = CreateSut(loaded);

        vm.MaxHistoryCount = 300;
        vm.SaveCommand.Execute(null);

        var result = saved();
        Assert.NotNull(result);
        Assert.Equal(0x0008u, result!.PlainTextHotkeyModifiers);
        Assert.Equal(0x42u, result.PlainTextHotkeyVirtualKey);
        Assert.Contains("KeePass", result.IgnoredApps);
        Assert.Contains("^ghp_", result.IgnorePatterns);
        Assert.Equal(300, result.MaxHistoryCount);
    }

    [Theory]
    [InlineData(0, SettingsViewModel.MinHistoryCount)]
    [InlineData(-5, SettingsViewModel.MinHistoryCount)]
    [InlineData(999_999, SettingsViewModel.MaxHistoryCountLimit)]
    [InlineData(500, 500)]
    public void Save_ClampsMaxHistoryCount(int input, int expected)
    {
        // 0 or negative would silently disable history purging entirely.
        var (vm, _, saved) = CreateSut();

        vm.MaxHistoryCount = input;
        vm.SaveCommand.Execute(null);

        Assert.Equal(expected, saved()!.MaxHistoryCount);
    }

    [Fact]
    public void Save_WithoutModifier_IsRejected()
    {
        var (vm, service, _) = CreateSut();

        vm.HotkeyModifiers = 0;
        vm.SaveCommand.Execute(null);

        service.Verify(s => s.Save(It.IsAny<AppSettings>()), Times.Never);
    }
}
