using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lumo.Native;
using Lumo.Services;

namespace Lumo.UI;

/// <summary>
/// v3.0.0-alpha.5 — the App Deck as its OWN window (user request: "The Deck tab
/// should be independent, not inside the AI tab, and it can be launched via a
/// button click and can be adjusted in settings").
///
/// The hub's AI window keeps only the chat; its rail deck button raises
/// <see cref="DeckLaunchRequested"/> and App.OpenDeck() shows this window
/// (singleton per app lifetime). The deck surface itself is the
/// AppDeckView — same store, editor, tutorial and global-hotkey plumbing;
/// its two events are re-exposed so App wires them exactly once.
///
/// Keyboard: numpad 1–9 (and the digit row) launch the matching slot whenever
/// the user is not typing in a field; Esc backs out of the slot editor /
/// tutorial first, then closes the window.
/// v3.0.0-alpha.6 — Ctrl+Tab / Ctrl+Shift+Tab cycle pages, Ctrl+1…9 jumps to a
/// page, and the window's size/position survive restarts (Settings.DeckWin*).
/// </summary>
public partial class AppDeckWindow : Window
{
    private readonly Settings _settings;
    private AppDeckView? _view;

    /// <summary>The deck surface asked for Lumo Settings (tutorial link) — App routes it.</summary>
    public event Action? SettingsRequested;

    /// <summary>The tutorial toggled global numpad hotkeys — App re-registers them on the launcher.</summary>
    public event Action? GlobalHotkeysChanged;

    private bool _sourceReady;
    private bool _geometryRestored;

    public AppDeckWindow(Settings settings, UsageStore? usage = null)
    {
        InitializeComponent();
        _settings = settings;

        // v3.0.0-alpha.4 doctrine — the view is built ORPHANED (before it joins
        // the tree), so its theme tokens must survive without a live window:
        // TryFindResource + the resolved palette, never a throwing FindResource.
        var view = new AppDeckView(_settings, usage);
        view.GlobalHotkeysChanged += () => GlobalHotkeysChanged?.Invoke();
        view.SettingsRequested += () => SettingsRequested?.Invoke();
        DeckHost.Content = view;
        _view = view;

        ApplySelfTheme();

        Closed += (_, _) =>
        {
            SaveGeometry();
            try { _view = null; DeckHost.Content = null; } catch { }
        };
        Loaded += (_, _) => TryFocusDeck();
    }

    // ---------------------------------------------------------------- theme

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _sourceReady = true;
        try { ApplySelfTheme(); } catch (Exception ex) { DiagnosticLogger.LogException("DeckWindow.Theme", ex); }
        RestoreGeometry();
    }

    // ---------------------------------------------------------------- geometry

    /// <summary>v3.0.0-alpha.6 — put the deck back where the user left it (0 = unset
    /// → centered), then clamp into the nearest monitor's work area so a monitor
    /// change can never strand the window off-screen.</summary>
    private void RestoreGeometry()
    {
        if (_geometryRestored) return;
        _geometryRestored = true;
        try
        {
            if (_settings.DeckWinWidth > 200 && _settings.DeckWinHeight > 150 &&
                _settings.DeckWinWidth < 4000 && _settings.DeckWinHeight < 3000)
            {
                Width = _settings.DeckWinWidth;
                Height = _settings.DeckWinHeight;
            }
            if (_settings.DeckWinLeft != 0 || _settings.DeckWinTop != 0)
            {
                Left = _settings.DeckWinLeft;
                Top = _settings.DeckWinTop;
                ClampIntoNearestWorkArea();
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("DeckWindow.RestoreGeometry", ex); }
    }

    private void ClampIntoNearestWorkArea()
    {
        try
        {
            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
            if (dpiX <= 0 || dpiY <= 0) return;
            var px = new System.Drawing.Point(
                (int)Math.Round((Left + Width / 2) * dpiX),
                (int)Math.Round((Top + Height / 2) * dpiY));
            var area = System.Windows.Forms.Screen.FromPoint(px).WorkingArea;
            double workLeft = area.Left / dpiX, workTop = area.Top / dpiY;
            double workRight = (area.Left + area.Width) / dpiX;
            double workBottom = (area.Top + area.Height) / dpiY;
            Left = Math.Clamp(Left, workLeft, Math.Max(workLeft, workRight - Width));
            Top = Math.Clamp(Top, workTop, Math.Max(workTop, workBottom - Height));
        }
        catch { /* cosmetic — a stray position beats a crash */ }
    }

    private void SaveGeometry()
    {
        try
        {
            if (WindowState == WindowState.Normal)
            {
                _settings.DeckWinLeft = Left;
                _settings.DeckWinTop = Top;
                _settings.DeckWinWidth = Width;
                _settings.DeckWinHeight = Height;
            }
            _settings.Save();
        }
        catch (Exception ex) { DiagnosticLogger.LogException("DeckWindow.SaveGeometry", ex); }
    }

    /// <summary>The same Fluent token set as the hub — the deck is part of the family.</summary>
    private void ApplySelfTheme()
    {
        try
        {
            var t = ThemeService.Apply(this, _settings);
            if (_sourceReady) GlassBackdrop.Apply(this, t.Dark);

            bool rounded = !string.Equals(_settings.CornerStyle, "square", StringComparison.OrdinalIgnoreCase);
            float r = GlassBackdrop.IsWin11 && rounded ? 8f : 0f;
            RootCard.CornerRadius = new CornerRadius(r);
            CaptionBar.CornerRadius = new CornerRadius(r, r, 0, 0);
        }
        catch (Exception ex) { DiagnosticLogger.LogException("DeckWindow.ApplyTheme", ex); }
    }

    // ---------------------------------------------------------------- chrome

    private void OnDragWindow(object sender, MouseButtonEventArgs e)
    {
        try { if (e.ButtonState == MouseButtonState.Pressed) DragMove(); }
        catch { }
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        try { WindowState = WindowState.Minimized; }
        catch (Exception ex) { DiagnosticLogger.LogException("DeckWindow.Minimize", ex); }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        try { Close(); } catch { }
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        try { SettingsRequested?.Invoke(); }
        catch (Exception ex) { DiagnosticLogger.LogException("DeckWindow.Settings", ex); }
    }

    // ---------------------------------------------------------------- keyboard

    private void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (_view is null || !_view.TryCloseEditor()) Close();
                return;
            }

            bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);

            // v3.0.0-alpha.6 — page navigation: Ctrl+Tab cycles (Shift reverses),
            // Ctrl+1…9 jumps straight to a page. Skipped while an overlay owns the
            // surface — switching pages under the editor would be confusing.
            if (ctrl && !(_view?.IsOverlayOpen ?? true))
            {
                if (e.Key == Key.Tab)
                {
                    e.Handled = true;
                    bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
                    _view?.CyclePage(shift ? -1 : 1);
                    return;
                }
                int page = e.Key switch
                {
                    Key.D1 or Key.NumPad1 => 1,
                    Key.D2 or Key.NumPad2 => 2,
                    Key.D3 or Key.NumPad3 => 3,
                    Key.D4 or Key.NumPad4 => 4,
                    Key.D5 or Key.NumPad5 => 5,
                    Key.D6 or Key.NumPad6 => 6,
                    Key.D7 or Key.NumPad7 => 7,
                    Key.D8 or Key.NumPad8 => 8,
                    Key.D9 or Key.NumPad9 => 9,
                    _ => -1,
                };
                if (page > 0)
                {
                    e.Handled = _view?.JumpToPage(page) == true;
                    if (e.Handled) return;
                }
            }

            // numpad 1–9 (and the digit row) launch slots — unless the user is
            // typing in the slot editor, where the digits belong to the fields
            if (Keyboard.FocusedElement is TextBox) return;
            if (ctrl) return;   // Ctrl+<other> belongs to the control, not the deck
            int slot = e.Key switch
            {
                Key.D1 or Key.NumPad1 => 0,
                Key.D2 or Key.NumPad2 => 1,
                Key.D3 or Key.NumPad3 => 2,
                Key.D4 or Key.NumPad4 => 3,
                Key.D5 or Key.NumPad5 => 4,
                Key.D6 or Key.NumPad6 => 5,
                Key.D7 or Key.NumPad7 => 6,
                Key.D8 or Key.NumPad8 => 7,
                Key.D9 or Key.NumPad9 => 8,
                _ => -1,
            };
            if (slot >= 0)
            {
                e.Handled = true;
                _view?.LaunchSlot(slot);
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("DeckWindow.Key", ex); }
    }

    private void TryFocusDeck()
    {
        try { Keyboard.Focus(DeckHost); }
        catch { /* cosmetic */ }
    }
}
