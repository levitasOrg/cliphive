using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ClipHive.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ClipHive.Views;

/// <summary>
/// Code-behind for the dark overlay sidebar window.
/// All business logic lives in <see cref="SidebarViewModel"/>;
/// this file handles only keyboard navigation and window lifecycle.
/// </summary>
public partial class SidebarWindow : Window
{
    private SidebarViewModel? _viewModel;
    private bool _closing;

    public SidebarWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // ── DataContext wiring ────────────────────────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.CloseRequested -= OnCloseRequested;

        _viewModel = DataContext as SidebarViewModel;

        if (_viewModel is not null)
            _viewModel.CloseRequested += OnCloseRequested;
    }

    private void OnCloseRequested(object? sender, EventArgs e)
    {
        _closing = true;
        Close();
    }

    // ── Window Lifecycle ──────────────────────────────────────────────────────

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        // Auto-focus the search box so the user can start typing immediately.
        SearchBox.Focus();
        Keyboard.Focus(SearchBox);
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        // Close the window whenever it loses focus (click elsewhere, Alt+Tab, etc.).
        // Guard against re-entrant Close() — Deactivated fires again while closing.
        if (!_closing)
        {
            _closing = true;
            Close();
        }
    }

    // ── Keyboard Navigation ───────────────────────────────────────────────────

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
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
                if (_viewModel?.SelectedItem is { } toDelete)
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
        int index;
        if (current is null)
        {
            index = -1;
        }
        else
        {
            index = -1;
            for (int i = 0; i < items.Count; i++)
            {
                if (ReferenceEquals(items[i], current)) { index = i; break; }
            }
        }

        // Clamp to valid range.
        var newIndex = Math.Clamp(index + delta, 0, items.Count - 1);
        _viewModel.SelectedItem = items[newIndex];

        // Scroll the ListBox to keep the selected item visible.
        ItemsList.ScrollIntoView(_viewModel.SelectedItem);
    }

    // ── Mouse interaction ─────────────────────────────────────────────────────

    private void ItemsList_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Only fire when clicking on an actual clipboard item (not the scrollbar)
        if (e.OriginalSource is FrameworkElement { DataContext: ClipboardItemViewModel item })
        {
            _viewModel?.SelectItemCommand.Execute(item);
            e.Handled = true;
        }
    }
}
