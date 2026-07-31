using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using ClipHive.Views;
using ClipHive.ViewModels;

namespace ClipHive;

/// <summary>
/// Application entry point — wires all services, owns the tray icon, and routes
/// clipboard changes / hotkey presses to the SidebarViewModel.
/// </summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage] // WPF application bootstrap: tray icon, HwndSource, MessageBox recovery — desktop-only
public partial class App : System.Windows.Application
{
    // ── Services ──────────────────────────────────────────────────────────────
    private SettingsService?          _settingsService;
    private EncryptionHelper?         _encryption;
    private StorageService?           _storage;
    private PasteService?             _paste;
    private ClipboardMonitorService?  _clipboardMonitor;
    private HotkeyService?            _hotkeyService;
    private AutoClearService?         _autoClear;
    private SidebarViewModel?         _sidebarVm;

    // ── Tray ──────────────────────────────────────────────────────────────────
    private System.Windows.Forms.NotifyIcon? _trayIcon;

    // ── Hidden message window for hotkey WM_HOTKEY messages ──────────────────
    private HwndSource? _msgWindow;

    // ── Active sidebar instance (null when closed) ────────────────────────────
    private SidebarWindow? _sidebar;

    // ── Single-instance mutex ─────────────────────────────────────────────────
    private Mutex? _singleInstanceMutex;
    // Matches the AppId in ClipHive.iss — guarantees uniqueness across all users.
    private const string MutexName = "Global\\ClipHive-{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}";

    // ─────────────────────────────────────────────────────────────────────────

    protected override async void OnStartup(StartupEventArgs e)
    {
        // ── Single-instance guard ─────────────────────────────────────────────
        _singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out bool isFirstInstance);
        if (!isFirstInstance)
        {
            // Another instance is already in the tray — just exit silently.
            _singleInstanceMutex.Dispose();
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Keep the process alive even with no open windows.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Last-resort handler: log + notify instead of the silent WPF crash exit.
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // ── Encryption key (recoverable failure path) ─────────────────────────
        _encryption = CreateEncryptionWithRecovery();
        if (_encryption is null)
        {
            Shutdown();
            return;
        }

        // ── Instantiate services ──────────────────────────────────────────────
        _settingsService  = new SettingsService();
        _storage          = new StorageService(_encryption);
        _paste            = new PasteService();
        _clipboardMonitor = new ClipboardMonitorService(_paste);
        _hotkeyService    = new HotkeyService();
        _autoClear        = new AutoClearService(_storage, _settingsService);

        AppSettings settings = _settingsService.Load();
        SyncStartupSetting(settings);
        _storage.MaxHistoryCount = settings.MaxHistoryCount;
        _clipboardMonitor.Filter.Update(settings.IgnoredApps, settings.IgnorePatterns);

        // ── ViewModel ─────────────────────────────────────────────────────────
        _sidebarVm = new SidebarViewModel(_storage, _paste);
        await _sidebarVm.LoadAsync();

        // ── Tray icon ─────────────────────────────────────────────────────────
        BuildTrayIcon();

        // ── Hidden HwndSource for hotkey messages ─────────────────────────────
        var parameters = new HwndSourceParameters("ClipHive-HotkeyWindow")
        {
            Width               = 0,
            Height              = 0,
            WindowStyle         = 0,
            ExtendedWindowStyle = 0x00000080, // WS_EX_TOOLWINDOW
            ParentWindow        = IntPtr.Zero,
        };
        _msgWindow = new HwndSource(parameters);
        _msgWindow.AddHook(WndProc);

        // ── Register hotkeys ─────────────────────────────────────────────────
        bool mainHotkeyOk = _hotkeyService.Register(
            _msgWindow.Handle, settings.HotkeyModifiers, settings.HotkeyVirtualKey);
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        bool plainHotkeyOk = _hotkeyService.RegisterPlainText(_msgWindow.Handle,
            settings.PlainTextHotkeyModifiers, settings.PlainTextHotkeyVirtualKey);
        _hotkeyService.PlainTextHotkeyPressed += OnPlainTextHotkeyPressed;

        // A failed registration (another clipboard tool owns the combo) must be
        // surfaced, or the app looks installed-but-dead; with a hidden tray icon it
        // would be completely unreachable — so keep the tray visible in that case.
        if (!mainHotkeyOk || !plainHotkeyOk)
        {
            _trayIcon!.Visible = true;
            NotifyHotkeyFailure(mainHotkeyOk, plainHotkeyOk);
        }
        else
        {
            _trayIcon!.Visible = !settings.HideFromTray;
        }

        // ── Start clipboard monitoring ────────────────────────────────────────
        _clipboardMonitor.ClipboardChanged      += OnClipboardChanged;
        _clipboardMonitor.ClipboardImageChanged += OnClipboardImageChanged;
        _clipboardMonitor.Start();

        // ── Start auto-clear background service ──────────────────────────────
        _autoClear.Cleaned += OnAutoClearCleaned;
        _autoClear.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Order matters: stop the producers first (monitor, hotkeys, auto-clear —
        // Stop() waits for an in-flight cleanup), THEN dispose the storage they
        // write to (its Dispose also waits briefly for in-flight operations).
        _clipboardMonitor?.Stop();
        _clipboardMonitor?.Dispose();
        _hotkeyService?.Dispose();
        _autoClear?.Stop();
        _msgWindow?.Dispose();
        _storage?.Dispose();

        _trayIcon?.Dispose();

        // Release single-instance mutex so a fresh launch can succeed.
        try { _singleInstanceMutex?.ReleaseMutex(); } catch (ApplicationException) { }
        _singleInstanceMutex?.Dispose();

        base.OnExit(e);
    }

    // ── Startup helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Loads the encryption key, offering a reset when the key blob is corrupted
    /// instead of crashing on every launch. Returns null when the user declines
    /// (the app then exits cleanly).
    /// </summary>
    private static EncryptionHelper? CreateEncryptionWithRecovery()
    {
        try
        {
            return new EncryptionHelper();
        }
        catch (EncryptionKeyException ex)
        {
            var choice = System.Windows.MessageBox.Show(
                "ClipHive cannot load its encryption key — the key file may be corrupted.\n\n" +
                "Without the key, the existing clipboard history cannot be decrypted.\n\n" +
                "Reset now? This deletes the stored history and creates a fresh key " +
                "(choose No to exit without changing anything).\n\n" +
                $"Details: {ex.InnerException?.Message ?? ex.Message}",
                "ClipHive — encryption key problem",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (choice != MessageBoxResult.Yes)
                return null;

            try
            {
                EncryptionHelper.ResetKeyAndData();
                return new EncryptionHelper();
            }
            catch (Exception resetEx)
            {
                System.Windows.MessageBox.Show(
                    "The key reset failed: " + resetEx.Message + "\n\n" +
                    "Delete %LOCALAPPDATA%\\ClipHive\\key.dat manually and restart ClipHive.",
                    "ClipHive", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }
    }

    /// <summary>
    /// Reconciles the StartWithWindows setting with the actual HKCU Run entry, which
    /// the installer's "start with Windows" task writes without the app knowing.
    /// Registry state wins on first sight (it reflects the user's installer choice);
    /// afterwards the in-app toggle keeps both in sync.
    /// </summary>
    private void SyncStartupSetting(AppSettings settings)
    {
        try
        {
            bool registryEnabled = StartupHelper.IsStartupEnabled();
            if (registryEnabled != settings.StartWithWindows)
            {
                settings.StartWithWindows = registryEnabled;
                _settingsService!.Save(settings);
            }
        }
        catch (Exception) { /* registry unavailable — non-critical */ }
    }

    private void NotifyHotkeyFailure(bool mainOk, bool plainOk)
    {
        string which = (mainOk, plainOk) switch
        {
            (false, false) => "The sidebar and plain-text paste hotkeys are",
            (false, true)  => "The sidebar hotkey is",
            _              => "The plain-text paste hotkey is",
        };
        _trayIcon!.ShowBalloonTip(10_000, "ClipHive hotkey unavailable",
            $"{which} already in use by another application. " +
            "Open Settings from the tray icon to choose a different combination.",
            System.Windows.Forms.ToolTipIcon.Warning);
    }

    private void OnDispatcherUnhandledException(object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            string logPath = Path.Combine(EncryptionHelper.DataDirectory, "error.log");
            File.AppendAllText(logPath,
                $"[{DateTime.Now:O}] {e.Exception}\n\n");
        }
        catch (Exception) { /* logging must never crash the handler */ }

        _trayIcon?.ShowBalloonTip(10_000, "ClipHive error",
            "An unexpected error occurred and was logged to " +
            "%LOCALAPPDATA%\\ClipHive\\error.log. ClipHive keeps running.",
            System.Windows.Forms.ToolTipIcon.Error);
        e.Handled = true;
    }

    // ── Tray icon ─────────────────────────────────────────────────────────────

    private void BuildTrayIcon()
    {
        var contextMenu = new System.Windows.Forms.ContextMenuStrip();
        contextMenu.Items.Add("Open ClipHive",    null, (_, _) => ShowSidebar());
        contextMenu.Items.Add("Settings",          null, (_, _) => OpenSettings());
        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        contextMenu.Items.Add("Exit",              null, (_, _) => ExitApp());
        contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        var credit = new System.Windows.Forms.ToolStripMenuItem("crafted by gmv ♥")
        {
            Enabled   = false,
            ForeColor = System.Drawing.Color.FromArgb(160, 120, 200),
            Font      = new System.Drawing.Font("Segoe UI", 8f, System.Drawing.FontStyle.Italic),
        };
        contextMenu.Items.Add(credit);

        _trayIcon = new System.Windows.Forms.NotifyIcon
        {
            Text             = "ClipHive — Clipboard Manager",
            Icon             = LoadAppIcon(),
            ContextMenuStrip = contextMenu,
            Visible          = true,
        };

        _trayIcon.DoubleClick += (_, _) => ShowSidebar();
    }

    /// <summary>
    /// Loads the ClipHive icon from the embedded resource.
    /// Falls back to the generic application icon if the resource is unavailable.
    /// </summary>
    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("ClipHive.ico");
            if (stream is not null)
                return new System.Drawing.Icon(stream);
        }
        catch { /* fall through to default */ }
        return System.Drawing.SystemIcons.Application;
    }

    // ── Hotkey / clipboard callbacks ──────────────────────────────────────────

    private void OnHotkeyPressed(object? sender, EventArgs e) =>
        Dispatcher.Invoke(ShowSidebar);

    private void OnPlainTextHotkeyPressed(object? sender, EventArgs e) =>
        SafeFireAndForget(_paste!.PastePlainTextFromClipboardAsync());

    private void OnClipboardChanged(object? sender, ClipboardTextEventArgs e)
    {
        SafeFireAndForget(_storage!.AddAsync(e.Text, e.SourceApp));
        _sidebarVm?.OnClipboardChanged(e.Text);
    }

    private async void OnClipboardImageChanged(object? sender, ClipboardImageEventArgs e)
    {
        string? ocrText = null;
        try { ocrText = await OcrService.RecognizeTextAsync(e.ImageBytes).ConfigureAwait(false); }
        catch { /* OCR failure is non-fatal */ }

        try
        {
            await _storage!.AddImageAsync(e.ImageBytes, e.SourceApp, ocrText).ConfigureAwait(false);
        }
        catch (ObjectDisposedException) { return; } // shutdown race — drop the item

        // Reload sidebar to show the new image item.
        _sidebarVm?.OnClipboardChanged("__image__");
    }

    private void OnAutoClearCleaned(object? sender, EventArgs e)
    {
        // Purged rows must not remain visible (and pastable) in an open sidebar.
        var vm = _sidebarVm;
        if (vm is { IsVisible: true })
            SafeFireAndForget(vm.LoadAsync());
    }

    /// <summary>
    /// Observes a fire-and-forget task: shutdown races surface as
    /// ObjectDisposedException (expected, dropped); anything else is logged instead
    /// of becoming an unobserved-task exception.
    /// </summary>
    private static void SafeFireAndForget(Task task) =>
        task.ContinueWith(
            t => System.Diagnostics.Debug.WriteLine(
                $"[ClipHive] Background task failed: {t.Exception?.GetBaseException().Message}"),
            TaskContinuationOptions.OnlyOnFaulted);

    // ── Sidebar window ────────────────────────────────────────────────────────

    private void ShowSidebar()
    {
        if (_sidebar is not null)
        {
            // Already open — toggle: pressing the hotkey again closes it.
            _sidebar.Close();
            return;
        }

        _sidebar = new SidebarWindow
        {
            DataContext = _sidebarVm
        };

        // Position: top-right corner of the primary screen.
        var screen = System.Windows.SystemParameters.WorkArea;
        _sidebar.Left = screen.Right  - _sidebar.Width  - 12;
        _sidebar.Top  = screen.Top    + 12;

        _sidebar.Closed += (_, _) =>
        {
            _sidebarVm!.IsVisible = false;
            _sidebar = null;
        };

        // Mark visible and reload fresh history before showing.
        _sidebarVm!.IsVisible = true;
        SafeFireAndForget(_sidebarVm.LoadAsync());

        _sidebar.Show();
        // Explicitly steal foreground — Show() alone does not move keyboard focus
        // from the previous app because the hotkey fires on a hidden HWND, not on
        // a visible foreground window.
        _sidebar.Activate();
    }

    private void OpenSettings()
    {
        var settingsVm = new SettingsViewModel(_settingsService!);
        var win = new SettingsWindow { DataContext = settingsVm };

        settingsVm.SaveRequested  += (_, _) => win.Close();
        settingsVm.CancelRequested += (_, _) => win.Close();

        win.ShowDialog();

        // Re-read settings in case hotkey or MaxHistoryCount changed.
        AppSettings updated = _settingsService!.Load();
        _storage!.MaxHistoryCount = updated.MaxHistoryCount;
        _clipboardMonitor!.Filter.Update(updated.IgnoredApps, updated.IgnorePatterns);

        bool mainOk = _hotkeyService!.Register(_msgWindow!.Handle, updated.HotkeyModifiers, updated.HotkeyVirtualKey);
        bool plainOk = _hotkeyService!.RegisterPlainText(_msgWindow!.Handle, updated.PlainTextHotkeyModifiers, updated.PlainTextHotkeyVirtualKey);

        // Apply tray visibility preference — but never hide the tray icon when a
        // hotkey failed to register, or the app becomes unreachable.
        _trayIcon!.Visible = !updated.HideFromTray || !mainOk || !plainOk;
        if (!mainOk || !plainOk)
            NotifyHotkeyFailure(mainOk, plainOk);

        // Sync startup registry entry.
        try { StartupHelper.SetStartup(updated.StartWithWindows); }
        catch (Exception) { /* non-critical */ }
    }

    private void ExitApp()
    {
        _trayIcon!.Visible = false;
        Shutdown();
    }

    // ── Hidden window procedure (receives WM_HOTKEY) ──────────────────────────

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;
        if (msg == WM_HOTKEY)
        {
            _hotkeyService?.OnWmHotkey(wParam.ToInt32());
            handled = true;
        }
        return IntPtr.Zero;
    }
}
