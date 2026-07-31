using Moq;
using Xunit;

namespace ClipHive.Tests.Unit;

public sealed class ContractsTests
{
    [Fact]
    public void ClipboardTextEventArgs_CarriesTextAndSource()
    {
        var args = new ClipboardTextEventArgs("hello", "Notepad");
        Assert.Equal("hello", args.Text);
        Assert.Equal("Notepad", args.SourceApp);
    }

    [Fact]
    public void ClipboardImageEventArgs_CarriesBytesAndNullSource()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var args = new ClipboardImageEventArgs(bytes, null);
        Assert.Same(bytes, args.ImageBytes);
        Assert.Null(args.SourceApp);
    }

    [Fact]
    public async Task AutoClearService_Cleaned_FiresAfterActualCleanup()
    {
        var storage = new Mock<IStorageService>();
        storage.Setup(s => s.DeleteOlderThanAsync(It.IsAny<DateTime>(), true))
               .Returns(Task.CompletedTask);
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Load())
                .Returns(new AppSettings { AutoClear = AutoClearPolicy.TwoHours });

        using var service = new AutoClearService(storage.Object, settings.Object);
        int cleaned = 0;
        service.Cleaned += (_, _) => cleaned++;

        await service.RunCleanupAsync();
        Assert.Equal(1, cleaned);
    }

    [Fact]
    public async Task AutoClearService_Cleaned_DoesNotFireForNeverPolicy()
    {
        var storage = new Mock<IStorageService>();
        var settings = new Mock<ISettingsService>();
        settings.Setup(s => s.Load())
                .Returns(new AppSettings { AutoClear = AutoClearPolicy.Never });

        using var service = new AutoClearService(storage.Object, settings.Object);
        int cleaned = 0;
        service.Cleaned += (_, _) => cleaned++;

        await service.RunCleanupAsync();
        Assert.Equal(0, cleaned);
    }
}
