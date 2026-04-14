using Moq;
using Xunit;

namespace ClipHive.Tests.Unit;

public class AutoClearServiceTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private static Mock<IStorageService> StorageMock() => new(MockBehavior.Strict);

    private static Mock<ISettingsService> SettingsMock(AutoClearPolicy policy)
    {
        var mock = new Mock<ISettingsService>(MockBehavior.Strict);
        mock.Setup(s => s.Load()).Returns(new AppSettings { AutoClear = policy });
        return mock;
    }

    // ── Constructor ────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullStorage_Throws()
    {
        var settings = new Mock<ISettingsService>().Object;
        Assert.Throws<ArgumentNullException>(() => new AutoClearService(null!, settings));
    }

    [Fact]
    public void Constructor_NullSettings_Throws()
    {
        var storage = new Mock<IStorageService>().Object;
        Assert.Throws<ArgumentNullException>(() => new AutoClearService(storage, null!));
    }

    // ── PolicyToWindow ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AutoClearPolicy.TwoHours,    2)]
    [InlineData(AutoClearPolicy.ThreeDays,   72)]
    [InlineData(AutoClearPolicy.FifteenDays, 360)]
    [InlineData(AutoClearPolicy.OneMonth,    720)]
    public void PolicyToWindow_ReturnsCorrectHours(AutoClearPolicy policy, int expectedHours)
    {
        TimeSpan result = AutoClearService.PolicyToWindow(policy);
        Assert.Equal(TimeSpan.FromHours(expectedHours), result);
    }

    [Fact]
    public void PolicyToWindow_Never_ReturnsMaxValue()
    {
        TimeSpan result = AutoClearService.PolicyToWindow(AutoClearPolicy.Never);
        Assert.Equal(TimeSpan.MaxValue, result);
    }

    // ── RunCleanupAsync ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(AutoClearPolicy.TwoHours,    2)]
    [InlineData(AutoClearPolicy.ThreeDays,   72)]
    [InlineData(AutoClearPolicy.FifteenDays, 360)]
    [InlineData(AutoClearPolicy.OneMonth,    720)]
    public async Task RunCleanup_CallsDeleteOlderThanWithCorrectCutoff(
        AutoClearPolicy policy, int windowHours)
    {
        var storageMock = StorageMock();
        DateTime? capturedCutoff = null;
        bool? capturedKeepPinned = null;

        storageMock
            .Setup(s => s.DeleteOlderThanAsync(It.IsAny<DateTime>(), It.IsAny<bool>()))
            .Callback<DateTime, bool>((cutoff, keepPinned) =>
            {
                capturedCutoff = cutoff;
                capturedKeepPinned = keepPinned;
            })
            .Returns(Task.CompletedTask);

        var settingsMock = SettingsMock(policy);

        using var svc = new AutoClearService(storageMock.Object, settingsMock.Object);

        DateTime before = DateTime.UtcNow;
        await svc.RunCleanupAsync();
        DateTime after = DateTime.UtcNow;

        storageMock.Verify(
            s => s.DeleteOlderThanAsync(It.IsAny<DateTime>(), true),
            Times.Once);

        Assert.NotNull(capturedCutoff);
        Assert.True(capturedKeepPinned);

        // Cutoff should be approximately UtcNow − window
        DateTime expectedMin = before - TimeSpan.FromHours(windowHours);
        DateTime expectedMax = after  - TimeSpan.FromHours(windowHours);
        Assert.InRange(capturedCutoff!.Value, expectedMin, expectedMax);
    }

    [Fact]
    public async Task RunCleanup_NeverPolicy_DoesNotCallDelete()
    {
        var storageMock = new Mock<IStorageService>(MockBehavior.Strict);
        // No setup for DeleteOlderThanAsync — strict mock will throw if called
        var settingsMock = SettingsMock(AutoClearPolicy.Never);

        using var svc = new AutoClearService(storageMock.Object, settingsMock.Object);
        await svc.RunCleanupAsync();

        // If we reach here without exception, delete was never called — pass
        storageMock.VerifyNoOtherCalls();
    }

    // ── Start / Stop ───────────────────────────────────────────────────────────

    [Fact]
    public void Start_ThenStop_DoesNotThrow()
    {
        var storageMock = new Mock<IStorageService>();
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.Load()).Returns(new AppSettings());

        using var svc = new AutoClearService(storageMock.Object, settingsMock.Object);
        svc.Start();
        svc.Stop(); // should not throw
    }

    [Fact]
    public void Start_CalledTwice_IsNoOp()
    {
        var storageMock = new Mock<IStorageService>();
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.Load()).Returns(new AppSettings());

        using var svc = new AutoClearService(storageMock.Object, settingsMock.Object);
        svc.Start();
        svc.Start(); // second call should not throw or double-register
        svc.Stop();
    }

    // ── Dispose ────────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_CancelsTimerWithoutException()
    {
        var storageMock = new Mock<IStorageService>();
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.Load()).Returns(new AppSettings());

        var svc = new AutoClearService(storageMock.Object, settingsMock.Object);
        svc.Start();
        svc.Dispose(); // should not throw
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var storageMock = new Mock<IStorageService>();
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.Load()).Returns(new AppSettings());

        var svc = new AutoClearService(storageMock.Object, settingsMock.Object);
        svc.Dispose();
        svc.Dispose(); // idempotent
    }

    [Fact]
    public void Start_AfterDispose_ThrowsObjectDisposedException()
    {
        var storageMock = new Mock<IStorageService>();
        var settingsMock = new Mock<ISettingsService>();
        settingsMock.Setup(s => s.Load()).Returns(new AppSettings());

        var svc = new AutoClearService(storageMock.Object, settingsMock.Object);
        svc.Dispose();

        Assert.Throws<ObjectDisposedException>(() => svc.Start());
    }
}
