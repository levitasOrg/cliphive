using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ClipHive.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Color = System.Windows.Media.Color;
using TextBox = System.Windows.Controls.TextBox;

namespace ClipHive.Views;

/// <summary>
/// Code-behind for the Settings window.
/// Handles hotkey capture (KeyPickerBox behaviour) and wires ViewModel events.
/// </summary>
public partial class SettingsWindow : Window
{
    private SettingsViewModel? _viewModel;
    private bool _capturingHotkey;

    public SettingsWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // ── DataContext wiring ────────────────────────────────────────────────────

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.SaveRequested -= OnSaveRequested;
            _viewModel.CancelRequested -= OnCancelRequested;
        }

        _viewModel = DataContext as SettingsViewModel;

        if (_viewModel is not null)
        {
            _viewModel.SaveRequested += OnSaveRequested;
            _viewModel.CancelRequested += OnCancelRequested;
        }
    }

    private void OnSaveRequested(object? sender, EventArgs e) => Close();
    private void OnCancelRequested(object? sender, EventArgs e) => Close();

    // ── Hotkey Capture ────────────────────────────────────────────────────────

    private void HotkeyBox_GotFocus(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = true;
        HotkeyBox.Background = new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x4A));
        HotkeyBox.Text = "Press a key combination…";
    }

    private void HotkeyBox_LostFocus(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = false;
        HotkeyBox.ClearValue(TextBox.BackgroundProperty);
        // Restore the display string from the ViewModel.
        if (_viewModel is not null)
            HotkeyBox.Text = _viewModel.HotkeyDisplay;
    }

    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturingHotkey) return;

        // Ignore standalone modifier key presses.
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or
                   Key.LeftShift or Key.RightShift or
                   Key.LeftAlt or Key.RightAlt or
                   Key.LWin or Key.RWin)
        {
            e.Handled = true;
            return;
        }

        // Escape cancels capture without saving.
        if (key == Key.Escape)
        {
            HotkeyBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            e.Handled = true;
            return;
        }

        // Build modifier bitmask.
        uint modifiers = 0;
        if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            modifiers |= 0x0002; // MOD_CTRL
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            modifiers |= 0x0004; // MOD_SHIFT
        if (Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt))
            modifiers |= 0x0001; // MOD_ALT

        // Require at least one modifier — bare keys are not valid global hotkeys.
        if (modifiers == 0)
        {
            e.Handled = true;
            return;
        }

        var vkCode = (uint)KeyInterop.VirtualKeyFromKey(key);

        if (_viewModel is not null)
        {
            _viewModel.HotkeyModifiers = modifiers;
            _viewModel.HotkeyVirtualKey = vkCode;
        }

        HotkeyBox.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
        e.Handled = true;
    }
}
