using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ClipHive.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ClipHive.Views;

/// <summary>
/// Code-behind for the dark overlay sidebar window.
/// All business logic lives in <see cref="SidebarViewModel"/>;
/// this file handles keyboard navigation, window lifecycle, and click-outside dismissal.
/// </summary>
public partial class SidebarWindow : Window
{
    private SidebarViewModel? _viewModel;
    private bool _closing;

    // Guard: don't close on deactivation until the window is fully shown.
    // Without this, the hotkey keypress itself can deactivate the window immediately.
    private bool _isReady;

    // Win32 message constants
    private const int WM_ACTIVATEAPP = 0x001C;
    private const int WM_NCACTIVATE  = 0x0086;

    public SidebarWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // ── DataContext wiring ────────────────────────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.CloseRequested   -= OnCloseRequested;
            _viewModel.PropertyChanged  -= OnViewModelPropertyChanged;
        }

        _viewModel = DataContext as SidebarViewModel;

        if (_viewModel is not null)
        {
            _viewModel.CloseRequested   += OnCloseRequested;
            _viewModel.PropertyChanged  += OnViewModelPropertyChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SidebarViewModel.ExpandedItem)) return;

        var item = _viewModel?.ExpandedItem;
        if (item is null)
        {
            DetailPanel.Visibility = Visibility.Collapsed;
            return;
        }

        DetailPanel.Visibility = Visibility.Visible;
        DetailEditor.Text      = item.DecryptedContent;
    }

    private void OnCloseRequested(object? sender, EventArgs e) => DismissWindow();

    // ── Window Lifecycle ──────────────────────────────────────────────────────

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        // Hook Win32 messages on the window's HWND.
        // WPF's Deactivated event is unreliable for WindowStyle=None windows when
        // clicking the desktop, taskbar, or other apps — WM_NCACTIVATE is authoritative.
        var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        src?.AddHook(WndProc);

        TryApplyAcrylicBackdrop();
    }

    private void TryApplyAcrylicBackdrop()
    {
        // Desktop Acrylic is only available on Windows 11 Build 22000+.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;

        var hwnd = new WindowInteropHelper(this).Handle;

        // Apply Desktop Acrylic (transient popup style).
        // The dark #CC1A1A1A border background is kept — acrylic shows subtly
        // through the 20% transparency, adding depth without washing out the theme.
        int backdropType = Win32.DWMSBT_TRANSIENTWINDOW;
        Win32.DwmSetWindowAttribute(hwnd, Win32.DWMWA_SYSTEMBACKDROP_TYPE,
            ref backdropType, sizeof(int));
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam,
                           IntPtr lParam, ref bool handled)
    {
        if (!_isReady) return IntPtr.Zero;

        switch (msg)
        {
            // WM_NCACTIVATE wParam=0  → this window is being deactivated
            case WM_NCACTIVATE when wParam == IntPtr.Zero:
            // WM_ACTIVATEAPP wParam=0 → another app is taking focus
            case WM_ACTIVATEAPP when wParam == IntPtr.Zero:
                DismissWindow();
                break;
        }

        return IntPtr.Zero;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);

        // Enable deactivation-close only after the dispatcher has fully rendered
        // the window. This prevents the hotkey's own keypress from immediately
        // deactivating the window before the user sees it.
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() => _isReady = true));
    }

    // Keep Deactivated as a secondary safety net for edge cases the WndProc misses.
    private void Window_Deactivated(object sender, EventArgs e) => DismissWindow();

    private void DismissWindow()
    {
        if (_closing) return;
        _closing = true;
        DataContext = null; // release ViewModel reference to allow GC
        Close();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect(); // second pass cleans up objects with finalizers
    }

    // ── Keyboard Navigation ───────────────────────────────────────────────────

    // Using PreviewKeyDown (set in XAML) so we intercept Up/Down/Enter/Escape
    // before WPF's keyboard-navigation system or the focused TextBox consumes them.
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                DismissWindow();
                e.Handled = true;
                break;

            case Key.Down:
                MoveSelection(+1);
                e.Handled = true;
                break;

            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;

            case Key.Enter:
                if (_viewModel?.SelectedItem is { } item)
                {
                    _viewModel.SelectItemCommand.Execute(item);
                    e.Handled = true;
                }
                break;

            case Key.Delete:
                // Only intercept Delete when the search box is empty —
                // if the user is editing search text, let the TextBox handle it.
                if (string.IsNullOrEmpty(SearchBox.Text) &&
                    _viewModel?.SelectedItem is { } toDelete)
                {
                    _viewModel.DeleteItemCommand.Execute(toDelete);
                    e.Handled = true;
                }
                break;
        }
    }

    private void MoveSelection(int delta)
    {
        if (_viewModel is null) return;

        var items = _viewModel.FilteredItems;
        if (items.Count == 0) return;

        var current = _viewModel.SelectedItem;
        int index = -1;
        for (int i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], current)) { index = i; break; }
        }

        var newIndex = Math.Clamp(index + delta, 0, items.Count - 1);
        _viewModel.SelectedItem = items[newIndex];
        ItemsList.ScrollIntoView(_viewModel.SelectedItem);
    }

    // ── Mouse interaction ─────────────────────────────────────────────────────

    private void ItemsList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is FrameworkElement { DataContext: ClipboardItemViewModel item })
        {
            _viewModel?.SelectItemCommand.Execute(item);
            e.Handled = true;
        }
    }
}
