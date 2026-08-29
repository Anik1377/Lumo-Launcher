using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Lumo.Core;
using Lumo.Native;
using Lumo.Services;
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

    /// <summary>Human-readable description of the hotkey that actually registered.</summary>
    public string? ActiveHotkeyDescription => _hotkey?.ActiveDescription;

    public LauncherWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;

        _engine = new SearchEngine(_apps, _files, _settings);

        _debounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(80),
        };
        _debounce.Tick += (_, _) => { try { _debounce.Stop(); RunSearch(); } catch (Exception ex) { DiagnosticLogger.LogException("Window.Debounce", ex); } };

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => { try { UpdateStatusText(); } catch { } };

        ApplyTheme();
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
            if (!IsVisible) { CenterNearCursor(); Show(); }
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

            Color panel = dark ? FromHex("#FF1E1F26") : Colors.White;
            Color border = dark ? FromHex("#FF33364A") : FromHex("#FFE2E4EC");
            Color title = dark ? FromHex("#FFF2F3F7") : FromHex("#FF1B1D27");
            Color subtitle = dark ? FromHex("#FF8A8FA3") : FromHex("#FF7A7F92");
            Color hover = dark ? FromHex("#FF2E3140") : FromHex("#FFF0F1F7");
            Color selected = dark ? FromHex("#FF3A3E52") : FromHex("#FFE4E6FB");
            Color glyphBox = dark ? FromHex("#FF2E3140") : FromHex("#FFEFF0F8");
            Color accent = FromHex("#FF7C6CFF");
            Color separator = dark ? FromHex("#FF2A2C38") : FromHex("#FFECECF2");

            Resources["TitleBrush"] = new SolidColorBrush(title);
            Resources["SubtitleBrush"] = new SolidColorBrush(subtitle);
            Resources["HoverBrush"] = new SolidColorBrush(hover);
            Resources["SelectedBrush"] = new SolidColorBrush(selected);
            Resources["GlyphBoxBrush"] = new SolidColorBrush(glyphBox);
            Resources["AccentBrush"] = new SolidColorBrush(accent);

            Root.Background = new SolidColorBrush(panel);
            Root.BorderBrush = new SolidColorBrush(border);
            Input.Foreground = new SolidColorBrush(title);
            Input.CaretBrush = new SolidColorBrush(accent);
            Separator.Background = new SolidColorBrush(separator);
            PrefixBadge.Foreground = new SolidColorBrush(accent);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.ApplyTheme", ex);
        }
    }

    private static Color FromHex(string hex) =>
        (Color)ColorConverter.ConvertFromString(hex);
}
