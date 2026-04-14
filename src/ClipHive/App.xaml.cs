using System.Reflection;
using System.Windows;
using System.Windows.Interop;
using ClipHive.Views;
using ClipHive.ViewModels;

namespace ClipHive;

/// <summary>
/// Application entry point — wires all services, owns the tray icon, and routes
/// clipboard changes / hotkey presses to the SidebarViewModel.
/// </summary>
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

    // ─────────────────────────────────────────────────────────────────────────

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Keep the process alive even with no open windows.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // ── Instantiate services ──────────────────────────────────────────────
        _settingsService  = new SettingsService();
        _encryption       = new EncryptionHelper();
        _storage          = new StorageService(_encryption);
        _paste            = new PasteService();
        _clipboardMonitor = new ClipboardMonitorService(_paste);
        _hotkeyService    = new HotkeyService();
        _autoClear        = new AutoClearService(_storage, _settingsService);

        AppSettings settings = _settingsService.Load();
        _storage.MaxHistoryCount = settings.MaxHistoryCount;

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

        // ── Register hotkey (Ctrl+Shift+V by default) ────────────────────────
        _hotkeyService.Register(_msgWindow.Handle, settings.HotkeyModifiers, settings.HotkeyVirtualKey);
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;

        // ── Start clipboard monitoring ────────────────────────────────────────
        _clipboardMonitor.ClipboardChanged      += OnClipboardChanged;
        _clipboardMonitor.ClipboardImageChanged += OnClipboardImageChanged;
        _clipboardMonitor.Start();

        // ── Start auto-clear background service ──────────────────────────────
        _autoClear.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _autoClear?.Stop();
        _clipboardMonitor?.Stop();
        _clipboardMonitor?.Dispose();
        _hotkeyService?.Dispose();
        _msgWindow?.Dispose();
        _storage?.Dispose();

        _trayIcon?.Dispose();

        base.OnExit(e);
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

    private void OnClipboardChanged(object? sender, string content)
    {
        _ = _storage!.AddAsync(content);
        _sidebarVm?.OnClipboardChanged(content);
    }

    private void OnClipboardImageChanged(object? sender, byte[] imageBytes)
    {
        _ = _storage!.AddImageAsync(imageBytes);
        // Reload sidebar to show the new image item.
        _sidebarVm?.OnClipboardChanged("__image__");
    }

    // ── Sidebar window ────────────────────────────────────────────────────────

    private void ShowSidebar()
    {
        if (_sidebar is not null)
        {
            // Already open — bring to front.
            _sidebar.Activate();
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

        _sidebar.Closed += (_, _) => _sidebar = null;
        _sidebar.Show();
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
        _hotkeyService!.Register(_msgWindow!.Handle, updated.HotkeyModifiers, updated.HotkeyVirtualKey);

        // Apply tray visibility preference.
        _trayIcon!.Visible = !updated.HideFromTray;

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
