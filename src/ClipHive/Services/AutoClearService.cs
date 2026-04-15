namespace ClipHive;

/// <summary>
/// Background service that periodically purges clipboard history according to the
/// user's <see cref="AutoClearPolicy"/>.
///
/// The timer fires every hour and calls
/// <see cref="IStorageService.DeleteOlderThanAsync(DateTime, bool)"/> with a cutoff
/// derived from the current policy. Pinned items are always preserved.
/// </summary>
public sealed class AutoClearService : IAutoClearService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

    private readonly IStorageService _storage;
    private readonly ISettingsService _settings;

    private System.Threading.Timer? _timer;
    private bool _disposed;

    public AutoClearService(IStorageService storage, ISettingsService settings)
    {
        ArgumentNullException.ThrowIfNull(storage);
        ArgumentNullException.ThrowIfNull(settings);

        _storage = storage;
        _settings = settings;
    }

    /// <summary>
    /// Starts the background timer. The first tick fires after one hour.
    /// Calling <see cref="Start"/> when already running is a no-op.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_timer is not null)
            return; // already running

        _timer = new System.Threading.Timer(
            callback: _ => _ = RunCleanupAsync(),
            state: null,
            dueTime: CheckInterval,
            period: CheckInterval);
    }

    /// <summary>
    /// Stops the background timer without disposing the service.
    /// </summary>
    public void Stop()
    {
        _timer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        _timer?.Dispose();
        _timer = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }

    // ── Internal ───────────────────────────────────────────────────────────────

    internal async Task RunCleanupAsync()
    {
        AppSettings settings = _settings.Load();
        TimeSpan window = PolicyToWindow(settings.AutoClear);

        if (window == TimeSpan.MaxValue)
            return; // AutoClearPolicy.Never — nothing to delete

        DateTime cutoff = DateTime.UtcNow - window;
        await _storage.DeleteOlderThanAsync(cutoff, keepPinned: true).ConfigureAwait(false);
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
