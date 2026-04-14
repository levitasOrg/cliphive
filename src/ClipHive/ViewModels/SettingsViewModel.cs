using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ClipHive.ViewModels;

/// <summary>
/// ViewModel for the Settings window.
/// Binds to <see cref="AppSettings"/> via <see cref="ISettingsService"/>.
/// </summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly ISettingsService _settingsService;

    private uint _hotkeyModifiers;
    private uint _hotkeyVirtualKey;
    private AutoClearPolicy _autoClearPolicy;
    private bool _startWithWindows;
    private int _maxHistoryCount;
    private bool _hideFromTray;
    private string _hotkeyDisplay = string.Empty;

    // Raised when Save is executed so the View can close.
    public event EventHandler? SaveRequested;

    // Raised when Cancel is executed so the View can close.
    public event EventHandler? CancelRequested;

    public SettingsViewModel(ISettingsService settings)
    {
        _settingsService = settings ?? throw new ArgumentNullException(nameof(settings));

        SaveCommand = new RelayCommand(ExecuteSave);
        CancelCommand = new RelayCommand(ExecuteCancel);

        LoadFromService();
    }

    // ── Bound Properties ─────────────────────────────────────────────────────

    /// <summary>
    /// Display string shown in the hotkey picker, e.g. "Ctrl + Shift + V".
    /// The View sets this when the user presses a new key combination.
    /// </summary>
    public string HotkeyDisplay
    {
        get => _hotkeyDisplay;
        set
        {
            if (_hotkeyDisplay == value) return;
            _hotkeyDisplay = value;
            OnPropertyChanged();
        }
    }

    public uint HotkeyModifiers
    {
        get => _hotkeyModifiers;
        set
        {
            if (_hotkeyModifiers == value) return;
            _hotkeyModifiers = value;
            OnPropertyChanged();
            UpdateHotkeyDisplay();
        }
    }

    public uint HotkeyVirtualKey
    {
        get => _hotkeyVirtualKey;
        set
        {
            if (_hotkeyVirtualKey == value) return;
            _hotkeyVirtualKey = value;
            OnPropertyChanged();
            UpdateHotkeyDisplay();
        }
    }

    public AutoClearPolicy AutoClearPolicy
    {
        get => _autoClearPolicy;
        set
        {
            if (_autoClearPolicy == value) return;
            _autoClearPolicy = value;
            OnPropertyChanged();
        }
    }

    public bool StartWithWindows
    {
        get => _startWithWindows;
        set
        {
            if (_startWithWindows == value) return;
            _startWithWindows = value;
            OnPropertyChanged();
        }
    }

    public int MaxHistoryCount
    {
        get => _maxHistoryCount;
        set
        {
            if (_maxHistoryCount == value) return;
            _maxHistoryCount = value;
            OnPropertyChanged();
        }
    }

    public bool HideFromTray
    {
        get => _hideFromTray;
        set
        {
            if (_hideFromTray == value) return;
            _hideFromTray = value;
            OnPropertyChanged();
        }
    }

    /// <summary>All values for the auto-clear policy combo box.</summary>
    public IReadOnlyList<AutoClearPolicy> AutoClearPolicies { get; } =
        Enum.GetValues<AutoClearPolicy>();

    // ── Commands ─────────────────────────────────────────────────────────────

    public ICommand SaveCommand { get; }
    public ICommand CancelCommand { get; }

    // ── Private Helpers ──────────────────────────────────────────────────────

    private void LoadFromService()
    {
        var s = _settingsService.Load();
        _hotkeyModifiers = s.HotkeyModifiers;
        _hotkeyVirtualKey = s.HotkeyVirtualKey;
        _autoClearPolicy = s.AutoClear;
        _startWithWindows = s.StartWithWindows;
        _maxHistoryCount = s.MaxHistoryCount;
        _hideFromTray = s.HideFromTray;
        UpdateHotkeyDisplay();
    }

    private void ExecuteSave()
    {
        // Validation: require at least one modifier.
        if (_hotkeyModifiers == 0) return;

        var updated = new AppSettings
        {
            HotkeyModifiers = _hotkeyModifiers,
            HotkeyVirtualKey = _hotkeyVirtualKey,
            AutoClear = _autoClearPolicy,
            StartWithWindows = _startWithWindows,
            MaxHistoryCount = _maxHistoryCount,
            HideFromTray = _hideFromTray
        };

        _settingsService.Save(updated);
        SaveRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ExecuteCancel() =>
        CancelRequested?.Invoke(this, EventArgs.Empty);

    private void UpdateHotkeyDisplay()
    {
        var parts = new System.Collections.Generic.List<string>();

        // MOD_CTRL = 0x0002, MOD_SHIFT = 0x0004, MOD_ALT = 0x0001, MOD_WIN = 0x0008
        if ((_hotkeyModifiers & 0x0002) != 0) parts.Add("Ctrl");
        if ((_hotkeyModifiers & 0x0004) != 0) parts.Add("Shift");
        if ((_hotkeyModifiers & 0x0001) != 0) parts.Add("Alt");
        if ((_hotkeyModifiers & 0x0008) != 0) parts.Add("Win");

        var keyChar = _hotkeyVirtualKey is >= 0x41 and <= 0x5A
            ? ((char)_hotkeyVirtualKey).ToString()
            : $"0x{_hotkeyVirtualKey:X2}";

        parts.Add(keyChar);
        HotkeyDisplay = string.Join(" + ", parts);
    }

    // ── INotifyPropertyChanged ───────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
