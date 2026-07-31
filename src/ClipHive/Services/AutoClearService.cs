namespace ClipHive;

/// <summary>
/// Background service that periodically purges clipboard history according to the
/// user's <see cref="AutoClearPolicy"/>.
///
/// The first tick fires one minute after startup (so stale items from a previous
/// session don't linger for an hour), then hourly. Each pass calls
/// <see cref="IStorageService.DeleteOlderThanAsync(DateTime, bool)"/> with a cutoff
/// derived from the current policy. Pinned items are always preserved.
/// </summary>
public sealed class AutoClearService : IAutoClearService
{
    private static readonly TimeSpan FirstCheckDelay = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly IStorageService _storage;
    private readonly ISettingsService _settings;

    private System.Threading.Timer? _timer;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler? Cleaned;

    public AutoClearService(IStorageService storage, ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(settings);

        _storage = storage;
        _settings = settings;
    }

    /// <summary>
    /// Starts the background timer.
    /// Calling <see cref="Start"/> when already running is a no-op.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_timer is not null)
            return; // already running

        _timer = new System.Threading.Timer(
            callback: _ => _ = RunCleanupSafeAsync(),
            state: null,
            dueTime: FirstCheckDelay,
            period: CheckInterval);
    }

    /// <summary>
    /// Stops the background timer, waiting for any in-flight callback to finish so
    /// shutdown never disposes the storage out from under a running cleanup.
    /// </summary>
    public void Stop()
    {
        var timer = _timer;
        _timer = null;
        if (timer is null) return;

        using var done = new System.Threading.ManualResetEvent(false);
        // Timer.Dispose(WaitHandle) signals after the last queued callback returns.
        if (timer.Dispose(done))
            done.WaitOne(TimeSpan.FromSeconds(2));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    // ── Internal ───────────────────────────────────────────────────────────────

    private async Task RunCleanupSafeAsync()
    {
        try
        {
            await RunCleanupAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // Storage disposed during shutdown — expected race, nothing to do.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ClipHive] Auto-clear pass failed: {ex.Message}");
        }
    }

    internal async Task RunCleanupAsync()
    {
        AppSettings settings = _settings.Load();
        TimeSpan window = PolicyToWindow(settings.AutoClear);

        if (window == TimeSpan.MaxValue)
            return; // AutoClearPolicy.Never — nothing to delete

        DateTime cutoff = DateTime.UtcNow - window;
        await _storage.DeleteOlderThanAsync(cutoff, keepPinned: true).ConfigureAwait(false);

        // Let the app refresh an open sidebar so purged items don't remain visible.
        Cleaned?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Maps an <see cref="AutoClearPolicy"/> to a retention window.
    /// Items older than the window are eligible for deletion.
    /// </summary>
    internal static TimeSpan PolicyToWindow(AutoClearPolicy policy) => policy switch
    {
        AutoClearPolicy.TwoHours     => TimeSpan.FromHours(2),
        AutoClearPolicy.ThreeDays    => TimeSpan.FromHours(72),
        AutoClearPolicy.FifteenDays  => TimeSpan.FromHours(360),
        AutoClearPolicy.OneMonth     => TimeSpan.FromHours(720),
        AutoClearPolicy.Never        => TimeSpan.MaxValue,
        _                            => TimeSpan.MaxValue,
    };
}
