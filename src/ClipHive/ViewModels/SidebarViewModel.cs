using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace ClipHive.ViewModels;

/// <summary>
/// ViewModel for the sidebar overlay.
/// Manages the observable clipboard history list, live search filtering,
/// and commands for selecting, deleting, pinning, and clearing items.
/// </summary>
public sealed class SidebarViewModel : INotifyPropertyChanged
{
    private readonly IStorageService _storage;
    private readonly IPasteService _paste;

    private string _searchText = string.Empty;
    private ClipboardItemViewModel? _selectedItem;
    private ClipboardItemViewModel? _expandedItem;
    private IReadOnlyList<ClipboardItemViewModel> _filteredItems = Array.Empty<ClipboardItemViewModel>();

    // Raised by commands to signal the View that the window should close.
    public event EventHandler? CloseRequested;

    public SidebarViewModel(IStorageService storage, IPasteService paste)
    {
        _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        _paste = paste ?? throw new ArgumentNullException(nameof(paste));

        Items = new ObservableCollection<ClipboardItemViewModel>();
        Items.CollectionChanged += (_, _) => RefreshFilteredItems();

        SelectItemCommand  = new RelayCommand<ClipboardItemViewModel>(ExecuteSelectItem, _ => true);
        DeleteItemCommand  = new RelayCommand<ClipboardItemViewModel>(ExecuteDeleteItem, _ => true);
        PinItemCommand     = new RelayCommand<ClipboardItemViewModel>(ExecutePinItem, _ => true);
        ClearAllCommand    = new RelayCommand(ExecuteClearAll);
        ExpandItemCommand  = new RelayCommand<ClipboardItemViewModel>(item =>
        {
            ExpandedItem = ReferenceEquals(ExpandedItem, item) ? null : item;
        });
    }

    // ── Observable Properties ────────────────────────────────────────────────

    public ObservableCollection<ClipboardItemViewModel> Items { get; }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (_searchText == value) return;
            _searchText = value;
            OnPropertyChanged();
            RefreshFilteredItems();
        }
    }

    public IReadOnlyList<ClipboardItemViewModel> FilteredItems
    {
        get => _filteredItems;
        private set
        {
            _filteredItems = value;
            OnPropertyChanged();
        }
    }

    public ClipboardItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (_selectedItem == value) return;
            _selectedItem = value;
            OnPropertyChanged();
        }
    }

    public ClipboardItemViewModel? ExpandedItem
    {
        get => _expandedItem;
        private set
        {
            _expandedItem = value;
            OnPropertyChanged();
        }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    public ICommand SelectItemCommand  { get; }
    public ICommand DeleteItemCommand  { get; }
    public ICommand PinItemCommand     { get; }
    public ICommand ClearAllCommand    { get; }
    public ICommand ExpandItemCommand  { get; }

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>Loads all clipboard history from storage and populates <see cref="Items"/>.</summary>
    public async Task LoadAsync()
    {
        var records = await _storage.GetAllAsync().ConfigureAwait(false);

        // Run collection mutations on the UI thread when available (no-op in tests).
        RunOnUiThread(() =>
        {
            ExpandedItem = null;
            Items.Clear();
            foreach (var item in records)
            {
                // The StorageService already decrypts content; the decrypted text
                // is stored in EncryptedContent field after decryption (per StorageService contract).
                Items.Add(new ClipboardItemViewModel(item, item.EncryptedContent));
            }
        });
    }

    /// <summary>
    /// Called by App when the clipboard monitor fires.
    /// Triggers a lightweight reload so that the new item carries its real DB Id.
    /// Using a transient Id=0 item was eliminated (Fix #5): a user who deleted an
    /// Id=0 item before LoadAsync refreshed would silently leave the DB row behind.
    /// </summary>
    /// <summary>
    /// Set to true while the sidebar window is visible so that clipboard events
    /// trigger a live reload. When false, reloads are skipped — LoadAsync is called
    /// on open instead, preventing unnecessary decrypt+thumbnail work in the background.
    /// </summary>
    public bool IsVisible { get; set; }

    public void OnClipboardChanged(string newContent)
    {
        if (string.IsNullOrEmpty(newContent)) return;
        // Only reload if the sidebar is currently open. When closed, LoadAsync is
        // called on ShowSidebar() instead, avoiding decrypt + image thumbnail work
        // on every clipboard copy regardless of whether the UI is visible.
        if (IsVisible)
            _ = LoadAsync();
    }

    // ── Private Helpers ──────────────────────────────────────────────────────

    private void RefreshFilteredItems()
    {
        var query = _searchText.Trim();
        IEnumerable<ClipboardItemViewModel> source = Items;

        if (!string.IsNullOrEmpty(query))
        {
            source = source.Where(i =>
                i.Preview.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.DecryptedContent.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        // Pinned items always first, then by insertion order (index in Items).
        FilteredItems = source
            .OrderByDescending(i => i.IsPinned)
            .ToList();
    }

    private void ExecuteSelectItem(ClipboardItemViewModel? item)
    {
        if (item is null) return;
        // Fire-and-forget: paste then close.
        _ = PasteAndCloseAsync(item);
    }

    private async Task PasteAndCloseAsync(ClipboardItemViewModel item)
    {
        // Close FIRST so the target window regains focus before we send Ctrl+V.
        // If we paste while the sidebar is still active, SendInput routes to the
        // sidebar instead of the intended target application.
        RunOnUiThread(() => CloseRequested?.Invoke(this, EventArgs.Empty));

        // Give Windows time to transfer focus to the previously active window.
        await Task.Delay(150).ConfigureAwait(false);

        if (item.IsImage && item.ImageData != null)
            await _paste.PasteImageAsync(item.ImageData).ConfigureAwait(false);
        else
            await _paste.PasteAsync(item.DecryptedContent).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs <paramref name="action"/> on the WPF dispatcher when a WPF Application is
    /// present (production), or synchronously on the calling thread (unit tests).
    /// </summary>
    private static void RunOnUiThread(Action action)
    {
        var app = System.Windows.Application.Current;
        if (app is null)
        {
            action();
        }
        else
        {
            app.Dispatcher.Invoke(action);
        }
    }

    private void ExecuteDeleteItem(ClipboardItemViewModel? item)
    {
        if (item is null) return;
        Items.Remove(item);
        if (item.Id > 0)
            _ = _storage.DeleteAsync(item.Id);
    }

    private void ExecutePinItem(ClipboardItemViewModel? item)
    {
        if (item is null) return;
        if (item.Id > 0)
            _ = _storage.SetPinnedAsync(item.Id, !item.IsPinned);

        // Reload to reflect the new pinned state from storage.
        _ = LoadAsync();
    }

    private void ExecuteClearAll()
    {
        _ = _storage.DeleteAllAsync(keepPinned: true);
        // Remove non-pinned items from the local collection immediately.
        var toRemove = Items.Where(i => !i.IsPinned).ToList();
        foreach (var item in toRemove)
            Items.Remove(item);
    }

    // ── INotifyPropertyChanged ───────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

// ── Minimal ICommand implementations ──────────────────────────────────────────

/// <summary>Simple parameterless relay command.</summary>
internal sealed class RelayCommand : ICommand
{
    private readonly Action _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => System.Windows.Input.CommandManager.RequerySuggested += value;
        remove => System.Windows.Input.CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter) => _execute();
}

/// <summary>Generic relay command with a typed parameter.</summary>
internal sealed class RelayCommand<T> : ICommand
{
    private readonly Action<T?> _execute;
    private readonly Func<T?, bool>? _canExecute;

    public RelayCommand(Action<T?> execute, Func<T?, bool>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => System.Windows.Input.CommandManager.RequerySuggested += value;
        remove => System.Windows.Input.CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) =>
        _canExecute?.Invoke(parameter is T t ? t : default) ?? true;

    public void Execute(object? parameter) =>
        _execute(parameter is T t ? t : default);
}
