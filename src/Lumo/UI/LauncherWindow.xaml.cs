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
/// The launcher window — v1.3 "Apple-clean" refresh.
///
/// Design language: iOS system greys, accent-tinted selection, soft layered shadow,
/// staggered result-row entrances, spring-ish scale-in, and the rotating glow border
/// that dims + pauses whenever the window is hidden or inactive (zero idle CPU).
///
/// FIX (v1.1, kept) — the search pipeline is fully synchronous, in-memory and bounded;
/// results arrive via an 80 ms debounce; every event handler is wrapped in try/catch
/// that logs instead of throwing.
/// </summary>
public partial class LauncherWindow : Window
{
    private readonly Settings _settings;
    private readonly AppIndex _apps = new();
    private readonly FileIndex _files = new();
    private readonly SearchEngine _engine;
    private readonly ShortcutStore? _shortcuts;   // v1.4
    private readonly MacroRecorder? _recorder;    // v1.5
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _statusTimer;
    private HotkeyService? _hotkey;
    private bool _allowClose;
    private IntPtr _hwnd;
    private Brush _themeBorderBrush = Brushes.DimGray;
    private RotateTransform? _rotBorder;
    private RotateTransform? _rotHalo;
    private Storyboard? _borderStoryboard;
    private bool _glowRunning;               // animation clock currently active
    private bool _glowDimmed;                // window inactive → halo dimmed
    private int _animGen;                    // invalidates stale hide-completion callbacks
    private bool _staggerNext = true;        // v1.4 — stagger only on show/empty→results, not every keystroke
    private string _prevQuery = "";

    private const double GlowActiveOpacity = 0.50;
    private const double GlowDimOpacity = 0.16;

    /// <summary>Human-readable description of the hotkey that actually registered.</summary>
    public string? ActiveHotkeyDescription => _hotkey?.ActiveDescription;

    /// <summary>Raised when the user asks for the settings window (gear, Settings row).</summary>
    public event Action? SettingsRequested;

    /// <summary>v1.4 — user asked to create/edit a shortcut (row in /sc mode).</summary>
    public event Action<string?>? ShortcutEditorRequested;

    /// <summary>v1.4 — user asked to manage shortcuts (opens settings → Shortcuts).</summary>
    public event Action? ManageShortcutsRequested;

    /// <summary>v1.5 — recording finished; App opens the builder with the captured steps.</summary>
    public event Action<List<MacroStep>, string?>? RecordFinishRequested;

    public LauncherWindow(Settings settings, ShortcutStore? shortcuts = null, MacroRecorder? recorder = null)
    {
        InitializeComponent();
        _settings = settings;
        _shortcuts = shortcuts;
        _recorder = recorder;
        _files.MaxEntries = Math.Max(10_000, _settings.MaxIndexedFiles);

        _engine = new SearchEngine(_apps, _files, _settings, _shortcuts, _recorder);

        _debounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(60),   // v1.4 — snappier than 80 ms
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
            try { HideAnimated(); } catch { }
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
            _animGen++; // cancel any pending hide-completion
            Root.BeginAnimation(OpacityProperty, null);

            if (!IsVisible) { CenterNearCursor(); AnimateShow(); }
            else if (_settings.AnimationsEnabled) { RestoreVisualState(); }

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

    /// <summary>Fade + slide + gentle scale-in — the launcher springs open, Spotlight-style.</summary>
    private void AnimateShow()
    {
        try
        {
            Show();

            if (!_settings.AnimationsEnabled)
            {
                RestoreVisualState();
                ResumeGlow();
                return;
            }

            Root.Opacity = 0;
            RootScale.ScaleX = RootScale.ScaleY = 0.94;
            RootShift.Y = 14;
            GlowHalo.Opacity = 0;
            _staggerNext = true;   // v1.4 — cascade the fresh result list on (re)open

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease };
            var scale = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(230)) { EasingFunction = ease };
            var slide = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(230)) { EasingFunction = ease };
            var glow = new DoubleAnimation(0, CurrentGlowOpacity(), TimeSpan.FromMilliseconds(420)) { EasingFunction = ease };

            Root.BeginAnimation(UIElement.OpacityProperty, fade);
            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale.Clone());
            RootShift.BeginAnimation(TranslateTransform.YProperty, slide);
            GlowHalo.BeginAnimation(UIElement.OpacityProperty, glow);

            ResumeGlow();
        }
        catch
        {
            try { RestoreVisualState(); Show(); } catch { }
        }
    }

    private void RestoreVisualState()
    {
        Root.Opacity = 1;
        RootScale.ScaleX = RootScale.ScaleY = 1;
        RootShift.Y = 0;
        GlowHalo.Opacity = _glowDimmed ? GlowDimOpacity : CurrentGlowOpacity();
    }

    /// <summary>Quick fade-out, then really hide. Safe against rapid toggle races.</summary>
    private void HideAnimated()
    {
        try
        {
            PauseGlow();
            if (!_settings.AnimationsEnabled || !IsVisible) { Hide(); return; }

            int gen = ++_animGen;
            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(90))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn },
            };
            fade.Completed += (_, _) =>
            {
                if (gen != _animGen) return; // user re-summoned mid-fade
                try { Hide(); } catch { }
            };
            Root.BeginAnimation(UIElement.OpacityProperty, fade);
        }
        catch
        {
            try { Hide(); } catch { }
        }
    }

    public void ToggleLauncher()
    {
        try
        {
            if (IsVisible && NativeMethods.GetForegroundWindow() == _hwnd)
                HideAnimated();
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
        try
        {
            if (_settings.HideOnFocusLoss) { Hide(); return; }

            // stays visible → dim the glow + pause the border animation (no idle CPU)
            _glowDimmed = true;
            PauseGlow();
            if (_settings.AnimationsEnabled && IsVisible)
                GlowHalo.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(GlowHalo.Opacity, GlowDimOpacity, TimeSpan.FromMilliseconds(300)));
            else
                GlowHalo.Opacity = GlowDimOpacity;
        }
        catch { }
    }

    private void OnWindowActivated(object sender, EventArgs e)
    {
        try
        {
            _glowDimmed = false;
            if (_settings.AnimationsEnabled && IsVisible)
            {
                GlowHalo.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(GlowHalo.Opacity, CurrentGlowOpacity(), TimeSpan.FromMilliseconds(300)));
                ResumeGlow();
            }
            else
            {
                GlowHalo.Opacity = CurrentGlowOpacity();
                ResumeGlow();
            }
        }
        catch { }
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
                    HideAnimated();
                    e.Handled = true;
                    break;
                case Key.Back when Keyboard.Modifiers == ModifierKeys.Control && Input.Text.Length > 0:
                    Input.Clear();
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
            if (e.Key == Key.Escape)
            {
                // v1.5.1 — Escape during a recording cancels it first.
                // Before, the window hid but the recorder stayed live invisibly.
                if (_recorder is { Active: true })
                {
                    _recorder.Cancel();
                    UpdateStatusText();
                    RunSearch();
                    StatusText.Text = "Recording cancelled";
                }
                HideAnimated();
                e.Handled = true;
            }
        }
        catch { }
    }

    private void OnClearClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            Input.Clear();
            Input.Focus();
        }
        catch { }
    }

    private void UpdatePrefixBadge()
    {
        try
        {
            var t = Input.Text.Trim();
            bool letterPrefix = t.Length >= 2 && t[1] == '/' && char.IsLetter(t[0]);
            bool slashMode = t.StartsWith("/");   // v1.4 — shortcut mode
            PrefixBadge.Text = letterPrefix ? char.ToUpperInvariant(t[0]).ToString() :
                               slashMode ? "⚡" : "L";
            PrefixBadge.Visibility = letterPrefix || slashMode ? Visibility.Visible : Visibility.Collapsed;
            MagnifierIcon.Visibility = letterPrefix || slashMode ? Visibility.Collapsed : Visibility.Visible;
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

            // v1.4 smoothing — cascade in only when the view *changes shape*
            // (window just opened, or left the default rows). Typing updates bind
            // instantly, which feels snappier than re-animating every keystroke.
            string q = Input.Text.Trim();
            bool stagger = _staggerNext || _prevQuery.Length == 0 || q.Length == 0;
            _staggerNext = false;
            _prevQuery = q;
            if (stagger) PlayStaggeredEntrance();
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.BindResults", ex);
        }
    }

    /// <summary>
    /// v1.3 — results cascade in: each row fades + slides up with a 22 ms stagger,
    /// the Raycast/Spotlight feel. Runs on the dispatcher after containers exist.
    /// </summary>
    private void PlayStaggeredEntrance()
    {
        if (!_settings.AnimationsEnabled) return;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            try
            {
                int n = Math.Min(Results.Items.Count, 10);
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var dur = TimeSpan.FromMilliseconds(170);
                for (int i = 0; i < n; i++)
                {
                    if (Results.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement fe) continue;

                    var tt = new TranslateTransform(0, 8);
                    fe.RenderTransform = tt;
                    var delay = TimeSpan.FromMilliseconds(i * 22);
                    tt.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(8, 0, dur) { EasingFunction = ease, BeginTime = delay });
                    fe.BeginAnimation(UIElement.OpacityProperty,
                        new DoubleAnimation(0, 1, dur) { EasingFunction = ease, BeginTime = delay });
                }
            }
            catch (Exception ex) { DiagnosticLogger.LogException("Window.Stagger", ex); }
        });
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

            // open the in-app settings window (not the settings folder)
            if (item.RunArgument == "cmd:app-settings")
            {
                SettingsRequested?.Invoke();
                return;
            }

            // v1.4 — shortcut management rows in /sc mode
            if (item.RunArgument == "cmd:new-shortcut" || item.RunArgument.StartsWith("cmd:new-shortcut:"))
            {
                string? preset = item.RunArgument.StartsWith("cmd:new-shortcut:")
                    ? item.RunArgument["cmd:new-shortcut:".Length..].Trim() : null;
                HideAnimated();
                ShortcutEditorRequested?.Invoke(string.IsNullOrWhiteSpace(preset) ? null : preset);
                return;
            }
            if (item.RunArgument == "cmd:manage-shortcuts")
            {
                HideAnimated();
                ManageShortcutsRequested?.Invoke();
                return;
            }

            // v1.5 — macro recording flow (v1.5.1: always hand focus back to the
            // input box — clicking a row moved keyboard focus to the list, so the
            // keyboard appeared dead right after starting a recording)
            if (item.RunArgument == "cmd:record-macro")
            {
                if (_recorder is { Active: false }) _recorder.Start();
                Input.Clear();
                UpdateStatusText();
                RunSearch();
                StatusText.Text = "● Recording — type to open an app / file / URL; every launch is captured";
                FocusInput();
                return;
            }
            if (item.RunArgument == "cmd:record-stop")
            {
                var steps = _recorder?.Stop() ?? new List<MacroStep>();
                string? name = _recorder?.Name;
                UpdateStatusText();
                Input.Clear();
                RunSearch();
                if (steps.Count == 0)
                {
                    StatusText.Text = "Nothing recorded — launch apps, files or URLs while recording";
                    FocusInput();
                    return;
                }
                HideAnimated();
                RecordFinishRequested?.Invoke(steps, name);
                return;
            }
            if (item.RunArgument == "cmd:record-cancel")
            {
                _recorder?.Cancel();
                UpdateStatusText();
                Input.Clear();
                RunSearch();
                StatusText.Text = "Recording cancelled";
                FocusInput();
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
                    HideAnimated();
                    break;

                case ResultKind.App:
                case ResultKind.File:
                case ResultKind.Web:
                case ResultKind.Image:
                case ResultKind.Tool:
                case ResultKind.Shortcut:
                    PauseGlow();
                    var error = _engine.Execute(item); // launch first, then hide on success
                    if (error is null)
                    {
                        if (_recorder is { Active: true })
                        {
                            // v1.5.1 — during a recording the launcher stays open, clears
                            // the used-up query and hands the keyboard back, so the next
                            // launch is one keystroke away
                            Input.Clear();
                            StatusText.Text = $"● Recorded {_recorder.Count} — launch more, or “Stop & save”";
                            RunSearch();
                            FocusInput();
                        }
                        else Hide();
                    }
                    else
                    {
                        StatusText.Text = error; // stay open and tell the user what failed
                        ResumeGlow();
                    }
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
            if (_recorder is { Active: true })
            {
                StatusText.Text = $"● REC — {_recorder.Count} step{(_recorder.Count == 1 ? "" : "s")} captured · launch apps / files / URLs";
                StatusText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5A));
                return;
            }
            StatusText.Foreground = (Brush)FindResource("SubtitleBrush");

            string hotkey = _hotkey?.ActiveDescription ?? "—";
            string fallback = _hotkey is { UsedFallback: true } ? " (fallback)" : "";
            string files = _files.Ready
                ? $"index {_files.Entries.Count:N0} files"
                : $"indexing… {_files.IndexedCount:N0} files";

            StatusText.Text = $"Hotkey {hotkey}{fallback} · {files} · apps {_apps.Entries.Count:N0}";
        }
        catch { }
    }

    /// <summary>v1.5 — App started a recording (e.g. from Settings): refresh status + rows.</summary>
    public void RefreshStatusForRecording()
    {
        try { UpdateStatusText(); RunSearch(); } catch { }
    }

    public void ApplyTheme()
    {
        try
        {
            bool dark = _settings.EffectiveDark();
            var p = Appearance.PaletteFor(dark, _settings.AccentColor);

            Resources["TitleBrush"] = new SolidColorBrush(p.Title);
            Resources["SubtitleBrush"] = new SolidColorBrush(p.Subtitle);
            Resources["HoverBrush"] = new SolidColorBrush(p.Hover);
            Resources["SelectedBrush"] = new SolidColorBrush(p.Selected);
            Resources["GlyphBoxBrush"] = new SolidColorBrush(p.GlyphBox);
            Resources["AccentBrush"] = new SolidColorBrush(p.Accent);
            Resources["ChipBrush"] = new SolidColorBrush(dark ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0D, 0x00, 0x00, 0x00));
            Resources["ChipTextBrush"] = new SolidColorBrush(p.Subtitle);
            Resources["PlaceholderBrush"] = new SolidColorBrush(dark ? FromRgb(0x63, 0x63, 0x66) : FromRgb(0xC7, 0xC7, 0xCC));
            Resources["IconBrush"] = new SolidColorBrush(p.Subtitle);
            Resources["BorderLineBrush"] = new SolidColorBrush(p.Border);

            Root.Background = new SolidColorBrush(p.Panel);
            _themeBorderBrush = new SolidColorBrush(p.Border);
            Input.Foreground = new SolidColorBrush(p.Title);
            Input.CaretBrush = new SolidColorBrush(p.Accent);
            Input.SelectionBrush = new SolidColorBrush(Appearance.Tint(p.Accent, 0x55));
            Input.SelectionTextBrush = new SolidColorBrush(p.Title);
            Separator.Background = new SolidColorBrush(p.Separator);
            PrefixBadge.Foreground = new SolidColorBrush(p.Accent);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.ApplyTheme", ex);
        }
    }

    private static Color FromRgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private double CurrentGlowOpacity() => _glowDimmed ? GlowDimOpacity : GlowActiveOpacity;

    /// <summary>
    /// The "chat box" glow border: a rotating multi-colour gradient stroke on the card
    /// plus a blurred halo copy behind the window. v1.3 runs it through a controllable
    /// storyboard so it can pause whenever the window is hidden or inactive.
    /// </summary>
    public void ApplyBorderEffect()
    {
        try
        {
            _borderStoryboard?.Remove(this);
            _borderStoryboard = null;
            _glowRunning = false;

            if (_settings.BorderEffect)
            {
                var borderBrush = Appearance.BuildBorderBrush(_settings.BorderStyle, _settings.AccentColor, out var rotBorder);
                var haloBrush = Appearance.BuildHaloBrush(_settings.BorderStyle, _settings.AccentColor, out var rotHalo);

                Root.BorderBrush = borderBrush;
                GlowHalo.Background = haloBrush;
                GlowHalo.Visibility = Visibility.Visible;

                double sec = _settings.BorderSpeedSec is <= 0 or double.NaN ? 3.5 : _settings.BorderSpeedSec;
                sec = Math.Clamp(sec, 1.0, 12.0);

                var sb = new Storyboard();
                var anim = new DoubleAnimation(0, 360, TimeSpan.FromSeconds(sec)) { RepeatBehavior = RepeatBehavior.Forever };
                Storyboard.SetTarget(anim, rotBorder);
                Storyboard.SetTargetProperty(anim, new PropertyPath(RotateTransform.AngleProperty));
                sb.Children.Add(anim);

                if (rotHalo is not null)
                {
                    var anim2 = anim.Clone();
                    Storyboard.SetTarget(anim2, rotHalo);
                    Storyboard.SetTargetProperty(anim2, new PropertyPath(RotateTransform.AngleProperty));
                    sb.Children.Add(anim2);
                }

                _rotBorder = rotBorder;
                _rotHalo = rotHalo;
                _borderStoryboard = sb;
                sb.Begin(this, true); // isControllable → pause/resume supported
                _glowRunning = true;
                GlowHalo.Opacity = CurrentGlowOpacity();
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
            _borderStoryboard?.Remove(this);
            _rotBorder?.BeginAnimation(RotateTransform.AngleProperty, null);
            _rotHalo?.BeginAnimation(RotateTransform.AngleProperty, null);
        }
        catch { }
        _rotBorder = null;
        _rotHalo = null;
        _borderStoryboard = null;
        _glowRunning = false;
    }

    private void PauseGlow()
    {
        try
        {
            if (_borderStoryboard is { } sb && _glowRunning) { sb.Pause(this); _glowRunning = false; }
        }
        catch { }
    }

    private void ResumeGlow()
    {
        try
        {
            if (_borderStoryboard is { } sb && !_glowRunning) { sb.Resume(this); _glowRunning = true; }
        }
        catch { }
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
}
