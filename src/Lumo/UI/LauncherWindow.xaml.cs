using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lumo.Core;
using Lumo.Native;
using Lumo.Services;
using Appearance = Lumo.Services.Appearance;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Lumo.UI;

/// <summary>
/// The launcher window.
///
/// FIX (v1.1) — "typing any character freezes then crashes": the search pipeline is now
/// fully synchronous, in-memory and bounded; results arrive via an 80 ms debounce; every
/// event handler is wrapped in try/catch that logs instead of throwing; there are no
/// blocking waits (.Result/.Wait), no cross-thread UI access and no recursion in matching.
/// Any residual dispatcher exception is caught in App and logged — the app no longer dies.
/// </summary>
public partial class LauncherWindow : Window
{
    private readonly Settings _settings;
    private readonly AppIndex _apps = new();
    private readonly FileIndex _files = new();
    private readonly SearchEngine _engine;
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _statusTimer;
    private HotkeyService? _hotkey;
    private bool _allowClose;
    private IntPtr _hwnd;
    private Brush _themeBorderBrush = Brushes.DimGray;
    private RotateTransform? _rotBorder;
    private RotateTransform? _rotHalo;

    /// <summary>Human-readable description of the hotkey that actually registered.</summary>
    public string? ActiveHotkeyDescription => _hotkey?.ActiveDescription;

    /// <summary>Raised when the user asks for the settings window (gear, Settings row).</summary>
    public event Action? SettingsRequested;

    public LauncherWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;
        _files.MaxEntries = Math.Max(10_000, _settings.MaxIndexedFiles);

        _engine = new SearchEngine(_apps, _files, _settings);

        _debounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(80),
        };
        _debounce.Tick += (_, _) => { try { _debounce.Stop(); RunSearch(); } catch (Exception ex) { DiagnosticLogger.LogException("Window.Debounce", ex); } };

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => { try { UpdateStatusText(); } catch { } };

        ApplyTheme();
        ApplyBorderEffect();
    }

    // ---------------------------------------------------------------- lifecycle

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            var source = HwndSource.FromHwnd(_hwnd);
            source?.AddHook(WndProc);

            // FIX: register a hotkey that actually works (Alt+Space default, fallback chain).
            _hotkey = new HotkeyService(_hwnd, _settings);
            if (_hotkey.TryRegister(out var active))
            {
                if (_hotkey.UsedFallback)
                    DiagnosticLogger.Log("Window", $"Configured hotkey unavailable — fallback '{active}' active");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.OnSourceInitialized", ex);
        }
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        try
        {
            _apps.BeginIndexInBackground();
            _files.BeginIndexInBackground();
            _statusTimer.Start();
            FocusInput();
            RunSearch(); // seed with default rows
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.OnContentRendered", ex);
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HotkeyService.HotkeyId)
        {
            handled = true;
            try { ToggleLauncher(); } catch (Exception ex) { DiagnosticLogger.LogException("Window.Hotkey", ex); }
        }
        return IntPtr.Zero;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        // Esc / Alt+F4 hide the window; real exit goes through the tray menu.
        if (!_allowClose)
        {
            e.Cancel = true;
            try { Hide(); } catch { }
            return;
        }

        base.OnClosing(e);
        try { _hotkey?.Dispose(); _statusTimer.Stop(); _debounce.Stop(); } catch { }
    }

    /// <summary>Called by App right before Shutdown so the window can really close.</summary>
    public void PrepareForExit() => _allowClose = true;

    // ---------------------------------------------------------------- show / hide

    /// <summary>Show + focus the launcher (hotkey, tray click, or second-instance signal).</summary>
    public void ActivateLauncher()
    {
        try
        {
            if (!IsVisible) { CenterNearCursor(); AnimateShow(); }
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

            Activate();
            if (_hwnd != IntPtr.Zero) NativeMethods.ForceForeground(_hwnd);
            FocusInput();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.ActivateLauncher", ex);
        }
    }

    /// <summary>Small fade + slide-in animation — the launcher feels light, not like a popup.</summary>
    private void AnimateShow()
    {
        try
        {
            Root.Opacity = 0;
            RootShift.Y = -12;
            Show();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(130)) { EasingFunction = ease };
            var slide = new DoubleAnimation(-12, 0, TimeSpan.FromMilliseconds(150)) { EasingFunction = ease };
            Root.BeginAnimation(UIElement.OpacityProperty, fade);
            RootShift.BeginAnimation(TranslateTransform.YProperty, slide);
        }
        catch
        {
            try { Root.Opacity = 1; RootShift.Y = 0; Show(); } catch { }
        }
    }

    public void ToggleLauncher()
    {
        try
        {
            if (IsVisible && NativeMethods.GetForegroundWindow() == _hwnd)
                Hide();
            else
                ActivateLauncher();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.ToggleLauncher", ex);
            try { ActivateLauncher(); } catch { }
        }
    }

    private void FocusInput()
    {
        try
        {
            Input.Focus();
            Keyboard.Focus(Input);
            Input.SelectAll();
        }
        catch { }
    }

    private void CenterNearCursor()
    {
        try
        {
            var area = SystemParameters.WorkArea;
            // Center on the primary work area (simple + reliable).
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + Math.Max(60, area.Height * 0.28);
        }
        catch { }
    }

    private void OnDeactivated(object sender, EventArgs e)
    {
        if (!_settings.HideOnFocusLoss) return;
        try { Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(Hide)); } catch { }
    }

    // ---------------------------------------------------------------- input & search

    private void OnInputTextChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            UpdatePrefixBadge();
            _debounce.Stop();
            _debounce.Start();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.TextChanged", ex);
        }
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            switch (e.Key)
            {
                case Key.Down:
                    MoveSelection(1);
                    e.Handled = true;
                    break;
                case Key.Up:
                    MoveSelection(-1);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    ExecuteSelected();
                    e.Handled = true;
                    break;
                case Key.Escape:
                    Hide();
                    e.Handled = true;
                    break;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.InputKeyDown", ex);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        try
        {
            if (e.Key == Key.Escape) { Hide(); e.Handled = true; }
        }
        catch { }
    }

    private void UpdatePrefixBadge()
    {
        try
        {
            var t = Input.Text.Trim();
            PrefixBadge.Text = t.Length >= 2 && t[1] == '/' && char.IsLetter(t[0])
                ? char.ToUpperInvariant(t[0]).ToString()
                : "L";
        }
        catch { }
    }

    private void RunSearch()
    {
        List<ResultItem> items;
        try
        {
            items = _engine.Search(Input.Text);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.RunSearch", ex);
            items = new List<ResultItem>
            {
                new() { Title = "Search error (logged to log.txt)", Subtitle = ex.Message, Glyph = "!", Kind = ResultKind.Error }
            };
        }

        try
        {
            Results.ItemsSource = items;
            if (items.Count > 0)
            {
                Results.SelectedIndex = 0;
                Results.ScrollIntoView(Results.SelectedItem);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.BindResults", ex);
        }
    }

    private void MoveSelection(int delta)
    {
        if (Results.Items.Count == 0) return;
        int next = Math.Clamp(Results.SelectedIndex + delta, 0, Results.Items.Count - 1);
        Results.SelectedIndex = next;
        Results.ScrollIntoView(Results.SelectedItem);
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        try { ExecuteSelected(); } catch (Exception ex) { DiagnosticLogger.LogException("Window.ResultClick", ex); }
    }

    private void ExecuteSelected()
    {
        try
        {
            if (Results.SelectedItem is not ResultItem item) return;

            // v1.2: open the in-app settings window (not the settings folder)
            if (item.RunArgument == "cmd:app-settings")
            {
                SettingsRequested?.Invoke();
                return;
            }

            switch (item.Kind)
            {
                case ResultKind.Hint:
                    if (!string.IsNullOrEmpty(item.RunArgument))
                    {
                        Input.Text = item.RunArgument;
                        Input.CaretIndex = Input.Text.Length;
                        Input.Focus();
                    }
                    break;

                case ResultKind.Calculator:
                    _engine.Execute(item); // copies result
                    StatusText.Text = "Copied: " + item.RunArgument;
                    Hide();
                    break;

                case ResultKind.App:
                case ResultKind.File:
                case ResultKind.Web:
                case ResultKind.Image:
                case ResultKind.Tool:
                    Hide();               // hide first so focus returns before the launched app takes over
                    _engine.Execute(item);
                    break;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.ExecuteSelected", ex);
        }
    }

    // ---------------------------------------------------------------- status & theme

    private void UpdateStatusText()
    {
        try
        {
            string hotkey = _hotkey?.ActiveDescription ?? "—";
            string fallback = _hotkey is { UsedFallback: true } ? " (fallback)" : "";
            string files = _files.Ready
                ? $"index {_files.Entries.Count:N0} files"
                : $"indexing… {_files.IndexedCount:N0} files";

            StatusText.Text = $"Hotkey {hotkey}{fallback} · {files} · apps {_apps.Entries.Count:N0}";
        }
        catch { }
    }

    public void ApplyTheme()
    {
        try
        {
            bool dark = !string.Equals(_settings.Theme, "light", StringComparison.OrdinalIgnoreCase);
            var p = Appearance.PaletteFor(dark, _settings.AccentColor);

            Resources["TitleBrush"] = new SolidColorBrush(p.Title);
            Resources["SubtitleBrush"] = new SolidColorBrush(p.Subtitle);
            Resources["HoverBrush"] = new SolidColorBrush(p.Hover);
            Resources["SelectedBrush"] = new SolidColorBrush(p.Selected);
            Resources["GlyphBoxBrush"] = new SolidColorBrush(p.GlyphBox);
            Resources["AccentBrush"] = new SolidColorBrush(p.Accent);

            Root.Background = new SolidColorBrush(p.Panel);
            _themeBorderBrush = new SolidColorBrush(p.Border);
            Input.Foreground = new SolidColorBrush(p.Title);
            Input.CaretBrush = new SolidColorBrush(p.Accent);
            Separator.Background = new SolidColorBrush(p.Separator);
            PrefixBadge.Foreground = new SolidColorBrush(p.Accent);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.ApplyTheme", ex);
        }
    }

    /// <summary>
    /// v1.2 — the "chat box" glow border: a rotating multi-colour gradient stroke on the
    /// card plus a blurred copy of the same gradient glowing out behind the window.
    /// Style / speed / on-off all come from Settings; when off we fall back to the
    /// classic solid theme border.
    /// </summary>
    public void ApplyBorderEffect()
    {
        try
        {
            if (_settings.BorderEffect)
            {
                var borderBrush = Appearance.BuildBorderBrush(_settings.BorderStyle, _settings.AccentColor, out var rotBorder);
                var haloBrush = Appearance.BuildHaloBrush(_settings.BorderStyle, _settings.AccentColor, out var rotHalo);

                Root.BorderBrush = borderBrush;
                GlowHalo.Background = haloBrush;
                GlowHalo.Visibility = Visibility.Visible;

                double sec = _settings.BorderSpeedSec is <= 0 or double.NaN ? 3.5 : _settings.BorderSpeedSec;
                sec = Math.Clamp(sec, 1.0, 12.0);
                var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(sec))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                };

                _rotBorder = rotBorder;
                _rotHalo = rotHalo;
                rotBorder?.BeginAnimation(RotateTransform.AngleProperty, anim);
                rotHalo?.BeginAnimation(RotateTransform.AngleProperty, anim.Clone());
            }
            else
            {
                StopBorderAnimation();
                Root.BorderBrush = _themeBorderBrush;
                GlowHalo.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.ApplyBorderEffect", ex);
        }
    }

    private void StopBorderAnimation()
    {
        try
        {
            _rotBorder?.BeginAnimation(RotateTransform.AngleProperty, null);
            _rotHalo?.BeginAnimation(RotateTransform.AngleProperty, null);
        }
        catch { }
        _rotBorder = null;
        _rotHalo = null;
    }

    /// <summary>Re-apply theme + border effect (used live by the settings window).</summary>
    public void RefreshAppearance()
    {
        ApplyTheme();
        ApplyBorderEffect();
    }

    /// <summary>Re-register the global hotkey after the user changed it in Settings.</summary>
    public string ReapplyHotkey()
    {
        try
        {
            if (_hotkey is null) return "(none)";
            bool ok = _hotkey.TryRegister(out var active);
            UpdateStatusText();
            return ok ? active : "(none)";
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.ReapplyHotkey", ex);
            return "(none)";
        }
    }

    /// <summary>Re-crawl the file system with the current index cap (Settings → Search).</summary>
    public void RebuildIndex()
    {
        try
        {
            _files.MaxEntries = Math.Max(10_000, _settings.MaxIndexedFiles);
            _files.BeginIndexInBackground();
            UpdateStatusText();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.RebuildIndex", ex);
        }
    }

    private void OnGearClick(object sender, MouseButtonEventArgs e)
    {
        try { SettingsRequested?.Invoke(); }
        catch (Exception ex) { DiagnosticLogger.LogException("Window.Gear", ex); }
    }

    private static Color FromHex(string hex) =>
        (Color)ColorConverter.ConvertFromString(hex);
}
