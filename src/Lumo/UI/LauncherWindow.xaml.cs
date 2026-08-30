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
/// staggered result-row entrances, spring-ish scale-in, and the v2.0.1 rim comet
/// (z.ai-style light chasing the border from inside) that dims + pauses whenever the
/// window is hidden or inactive (zero idle CPU).
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
    private readonly ShortcutStore? _shortcuts;      // v1.4
    private readonly MacroRecorder? _recorder;       // v1.5
    private readonly ClipboardHistory? _clips;       // v1.6
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _statusTimer;
    private HotkeyService? _hotkey;
    private bool _sourceReady;                       // v1.7 — glass needs the HWND, applied on SourceInitialized
    private bool _allowClose;
    private IntPtr _hwnd;
    private Brush _themeBorderBrush = Brushes.DimGray;
    private Storyboard? _glowStoryboard;     // v2.0.1 — perimeter comet path storyboard
    private PathGeometry? _glowPath;         // unfrozen — figures swap live as the window resizes
    private bool _glowRunning;               // animation clock currently active
    private bool _glowDimmed;                // window inactive → comet dimmed
    private int _animGen;                    // invalidates stale hide-completion callbacks
    private bool _staggerNext = true;        // v1.4 — stagger only on show/empty→results, not every keystroke
    private string _prevQuery = "";

    private const double GlowDimFactor = 0.4;        // dimmed = 40 % of the active brightness

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

    public LauncherWindow(Settings settings, ShortcutStore? shortcuts = null, MacroRecorder? recorder = null,
                          ClipboardHistory? clips = null)
    {
        InitializeComponent();
        _settings = settings;
        _shortcuts = shortcuts;
        _recorder = recorder;
        _clips = clips;
        _files.MaxEntries = Math.Max(10_000, _settings.MaxIndexedFiles);

        _engine = new SearchEngine(_apps, _files, _settings, _shortcuts, _recorder, _clips);

        _debounce = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(60),   // v1.4 — snappier than 80 ms
        };
        _debounce.Tick += (_, _) => { try { _debounce.Stop(); RunSearch(); } catch (Exception ex) { DiagnosticLogger.LogException("Window.Debounce", ex); } };

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _statusTimer.Tick += (_, _) => { try { UpdateStatusText(); } catch { } };

        // v2.0.1 — track size: rebuild the comet clip + perimeter path as the window
        // height follows the result list (SizeToContent).
        Root.SizeChanged += OnRootSizeChanged;
        ApplyWindowSize();

        ApplyTheme();
        ApplyBorderEffect();
    }

    /// <summary>v2.0.1 — honours the user's launcher width preference (560–900 DIP).</summary>
    private void ApplyWindowSize()
    {
        try { Width = Math.Clamp(_settings.WindowWidth, 560, 900); }
        catch { }
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

            // v2.0 — the HWND now exists: apply DWM chrome (round corners, dark mode),
            // then paint the solid Win11 palette.
            _sourceReady = true;
            GlassBackdrop.Apply(this, _settings.EffectiveDark());
            ApplyTheme();
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
            // v1.6 — remember which window the user was just in, so S/ window
            // commands snap THAT window instead of Lumo itself
            WindowManager.RememberForeground(_hwnd);

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
            GlowClip.Opacity = 0;
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
            GlowClip.BeginAnimation(UIElement.OpacityProperty, glow);

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
        GlowClip.Opacity = CurrentGlowOpacity();
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
            // v1.6 — Raycast-style placement: always top-center of the work area
            Left = area.Left + (area.Width - ActualWidth) / 2;
            Top = area.Top + Math.Max(48, area.Height * 0.12);
        }
        catch { }
    }

    private void OnDeactivated(object sender, EventArgs e)
    {
        try
        {
            if (_settings.HideOnFocusLoss) { Hide(); return; }

            // stays visible → dim the comet + pause the animation (no idle CPU)
            _glowDimmed = true;
            PauseGlow();
            if (_settings.AnimationsEnabled && IsVisible)
                GlowClip.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(GlowClip.Opacity, CurrentGlowOpacity(), TimeSpan.FromMilliseconds(300)));
            else
                GlowClip.Opacity = CurrentGlowOpacity();
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
                GlowClip.BeginAnimation(OpacityProperty,
                    new DoubleAnimation(GlowClip.Opacity, CurrentGlowOpacity(), TimeSpan.FromMilliseconds(300)));
                ResumeGlow();
            }
            else
            {
                GlowClip.Opacity = CurrentGlowOpacity();
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
            // v1.7.1 — never pre-select a section header ("APPS", "CLIPBOARD HISTORY"…):
            // Enter is a no-op there, so the launcher felt dead right after typing.
            // Land on the first actionable row instead.
            if (items.Count > 0)
            {
                int sel = items.FindIndex(i => i.Kind != ResultKind.Header);
                Results.SelectedIndex = sel >= 0 ? sel : 0;
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
        int n = Results.Items.Count;
        int next = Results.SelectedIndex;
        // v1.7.1 — step over section header rows; they are labels, not selectable actions
        do
        {
            next += delta;
            if (next < 0 || next >= n) return;   // hit the end — stay where we are
        }
        while (Results.Items[next] is ResultItem { Kind: ResultKind.Header });
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

                // v1.7.1 — clipboard rows had NO Enter case: pressing Enter on an
                // H/ entry silently did nothing (the subtitle even promised "Enter
                // to copy again"). Route it like the calculator: copy, confirm, hide.
                case ResultKind.Clipboard:
                    _engine.Execute(item); // copies the entry back to the clipboard
                    StatusText.Text = "Copied to clipboard";
                    HideAnimated();
                    break;

                case ResultKind.Header:
                    break;   // section labels are not actions — ignore Enter on them

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

            // v2.0 — DWM chrome only (rounded corners + dark-mode context); no acrylic.
            if (_sourceReady)
                GlassBackdrop.Apply(this, dark);

            var p = Appearance.PaletteFor(dark, _settings.AccentColor);

            Resources["TitleBrush"] = new SolidColorBrush(p.Title);
            Resources["SubtitleBrush"] = new SolidColorBrush(p.Subtitle);
            Resources["HoverBrush"] = new SolidColorBrush(p.Hover);
            Resources["SelectedBrush"] = new SolidColorBrush(p.Selected);
            Resources["AccentBrush"] = new SolidColorBrush(p.Accent);
            Resources["ChipBrush"] = new SolidColorBrush(dark
                ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0D, 0x00, 0x00, 0x00));
            Resources["ChipTextBrush"] = new SolidColorBrush(p.Subtitle);
            Resources["PlaceholderBrush"] = new SolidColorBrush(dark ? FromRgb(0x71, 0x71, 0x71) : FromRgb(0x9D, 0x9D, 0x9D));
            Resources["IconBrush"] = new SolidColorBrush(p.Subtitle);
            Resources["GlyphBoxBrush"] = new SolidColorBrush(p.GlyphBox);
            Resources["BorderLineBrush"] = new SolidColorBrush(p.Border);

            // Win11 rounded corners come from DWM on Windows 11; Windows 10 keeps square
            // corners (an unpainted radius would otherwise expose raw window corners).
            // v2.0.1 — the user can also force square corners from Settings → Appearance.
            bool rounded = !string.Equals(_settings.CornerStyle, "square", StringComparison.OrdinalIgnoreCase);
            float r = GlassBackdrop.IsWin11 && rounded ? 8f : 0f;
            double t = Math.Clamp(_settings.RimThickness, 2.0, 6.0);
            Root.Background = new SolidColorBrush(p.Panel);
            Root.CornerRadius = new CornerRadius(r);
            _themeBorderBrush = new SolidColorBrush(p.Border);

            // v2.0.1 — rim comet geometry follows the theme radius: hairline ring for
            // definition, clipped orbit host, and the cover patch inset by the rim
            // thickness (painted with the SAME opaque panel → the comet shows only
            // in the outer band).
            RimLine.CornerRadius = new CornerRadius(r);
            RimLine.BorderBrush = _themeBorderBrush;
            GlowClip.CornerRadius = new CornerRadius(r);
            GlowCover.CornerRadius = new CornerRadius(Math.Max(0, r - t));
            GlowCover.Margin = new Thickness(t);
            GlowCover.Background = Root.Background;
            Resources["ResultRowPad"] = string.Equals(_settings.RowDensity, "compact", StringComparison.OrdinalIgnoreCase)
                ? new Thickness(10, 3, 10, 3)
                : new Thickness(10, 6, 10, 6);
            if (ActualWidth >= 40 && ActualHeight >= 40)
                UpdateGlowGeometry(new Size(ActualWidth, ActualHeight));
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

    /// <summary>
    /// v2.0 — Win11 text-field focus treatment: show the 2 px accent bar along the
    /// bottom edge of the search field while it holds keyboard focus.
    /// </summary>
    private void OnInputFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        try
        {
            bool focused = Input.IsKeyboardFocusWithin;
            FocusBar.Visibility = focused ? Visibility.Visible : Visibility.Collapsed;
            // v2.0.1 FIX — the Win11 text-field treatment keeps the hairline stroke in
            // BOTH states: the accent lives ONLY in the 2 px bottom bar. (Painting the
            // whole border accent + the bar overlapped two accent layers on one box.)
            SearchField.BorderBrush = (Brush)FindResource("BorderLineBrush");
        }
        catch { }
    }

    private static Color FromRgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    private double CurrentGlowOpacity()
    {
        // v2.0.1 — user-tunable brightness (Settings → Appearance → Glow brightness)
        double active = Math.Clamp(_settings.GlowOpacity, 0.4, 1.0);
        return _glowDimmed ? active * GlowDimFactor : active;
    }

    /// <summary>
    /// The glow effect, v2.0.1 — the z.ai chat-box comet. Two soft light blobs orbit
    /// the true window perimeter (DoubleAnimationUsingPath along a rounded-rect path);
    /// the orbit host is clipped to the window, so the light only ever exists INSIDE
    /// the rim — no outer bleed, no halo, no spinning gradient. The storyboard stays
    /// controllable so it pauses whenever the window is hidden or inactive (zero idle
    /// CPU). "Solid" style = static accent ring, no motion.
    /// </summary>
    public void ApplyBorderEffect()
    {
        try
        {
            StopGlow();
            RimLine.BorderBrush = _themeBorderBrush;

            if (_settings.BorderEffect)
            {
                if (Appearance.IsAnimatedStyle(_settings.BorderStyle))
                {
                    Appearance.BuildCometBrushes(_settings.BorderStyle, _settings.AccentColor, out var head, out var tail);
                    GlowHead.Fill = head;
                    GlowTail.Fill = tail;
                    GlowClip.Visibility = Visibility.Visible;
                    GlowClip.Opacity = CurrentGlowOpacity();

                    _glowPath ??= new PathGeometry();
                    if (ActualWidth >= 40 && ActualHeight >= 40)
                        UpdateGlowGeometry(new Size(ActualWidth, ActualHeight));
                    StartGlowStoryboard();
                }
                else
                {
                    // "Solid" — static accent-coloured ring, zero motion
                    GlowClip.Visibility = Visibility.Collapsed;
                    RimLine.BorderBrush = Appearance.BuildStaticRimBrush(_settings.BorderStyle, _settings.AccentColor);
                }
            }
            else
            {
                GlowClip.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.ApplyBorderEffect", ex);
        }
    }

    private void StartGlowStoryboard()
    {
        try
        {
            _glowStoryboard?.Remove(this);
            _glowStoryboard = null;
            if (_glowPath is null) return;

            // 3.5 s (the v2.0.0 default) was tuned for brush rotation; a blob travelling
            // the real perimeter reads best at a calm pace. Old saved values clamp in.
            double sec = _settings.BorderSpeedSec is <= 0 or double.NaN ? 9.0 : Math.Clamp(_settings.BorderSpeedSec, 4.0, 30.0);
            var dur = TimeSpan.FromSeconds(sec);

            var sb = new Storyboard();
            AddPathAnimations(sb, "GlowHeadPos", dur, TimeSpan.Zero);
            AddPathAnimations(sb, "GlowTailPos", dur, TimeSpan.FromSeconds(-sec * 0.07));   // tail trails the head

            _glowStoryboard = sb;
            sb.Begin(this, isControllable: true);
            _glowRunning = true;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.StartGlow", ex);
        }
    }

    private void AddPathAnimations(Storyboard sb, string targetName, Duration dur, TimeSpan begin)
    {
        foreach (var src in new[] { PathAnimationSource.X, PathAnimationSource.Y })
        {
            var anim = new DoubleAnimationUsingPath
            {
                PathGeometry = _glowPath!,
                Source = src,
                Duration = dur,
                RepeatBehavior = RepeatBehavior.Forever,
                BeginTime = begin,
            };
            Storyboard.SetTargetName(anim, targetName);
            Storyboard.SetTargetProperty(anim, new PropertyPath(
                src == PathAnimationSource.X ? TranslateTransform.XProperty : TranslateTransform.YProperty));
            sb.Children.Add(anim);
        }
    }

    private void OnRootSizeChanged(object sender, SizeChangedEventArgs e)
    {
        try
        {
            if (e.NewSize.Width >= 40 && e.NewSize.Height >= 40)
                UpdateGlowGeometry(e.NewSize);
        }
        catch { }
    }

    /// <summary>
    /// Rebuilds the rounded-rect clip and the perimeter path the comet travels. The
    /// path geometry is mutated IN PLACE (never frozen) so the running storyboard
    /// picks up the new outline without a restart — the comet never snaps back to its
    /// start when the result list changes the window height.
    /// </summary>
    private void UpdateGlowGeometry(Size size)
    {
        bool rounded = !string.Equals(_settings.CornerStyle, "square", StringComparison.OrdinalIgnoreCase);
        double r = GlassBackdrop.IsWin11 && rounded ? 8f : 0f;
        double t = Math.Clamp(_settings.RimThickness, 2.0, 6.0);
        GlowClip.Clip = new RectangleGeometry(new Rect(0, 0, size.Width, size.Height), r, r);

        if (_glowPath is null) return;
        _glowPath.Figures.Clear();
        // the blob CENTRES travel exactly on the middle of the glowing band
        _glowPath.Figures.Add(BuildPerimeterFigure(size.Width, size.Height, t / 2, Math.Max(2, r - t / 2)));
    }

    /// <summary>Closed rounded-rectangle outline (clockwise from the top-left corner).</summary>
    private static PathFigure BuildPerimeterFigure(double w, double h, double inset, double radius)
    {
        double x0 = inset, y0 = inset, x1 = w - inset, y1 = h - inset;
        double r = Math.Max(2, Math.Min(radius, Math.Min(x1 - x0, y1 - y0) / 2));

        var fig = new PathFigure { StartPoint = new Point(x0 + r, y0), IsClosed = true };
        fig.Segments.Add(new LineSegment(new Point(x1 - r, y0), true));
        fig.Segments.Add(new ArcSegment(new Point(x1, y0 + r), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(new Point(x1, y1 - r), true));
        fig.Segments.Add(new ArcSegment(new Point(x1 - r, y1), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(new Point(x0 + r, y1), true));
        fig.Segments.Add(new ArcSegment(new Point(x0, y1 - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        fig.Segments.Add(new LineSegment(new Point(x0, y0 + r), true));
        fig.Segments.Add(new ArcSegment(new Point(x0 + r, y0), new Size(r, r), 0, false, SweepDirection.Clockwise, true));
        return fig;
    }

    private void StopGlow()
    {
        try { _glowStoryboard?.Remove(this); } catch { }
        _glowStoryboard = null;
        _glowRunning = false;
    }

    private void PauseGlow()
    {
        try
        {
            if (_glowStoryboard is { } sb && _glowRunning) { sb.Pause(this); _glowRunning = false; }
        }
        catch { }
    }

    private void ResumeGlow()
    {
        try
        {
            if (_glowStoryboard is { } sb && !_glowRunning) { sb.Resume(this); _glowRunning = true; }
        }
        catch { }
    }

    /// <summary>Re-apply theme + border effect + window size (used live by the settings window).</summary>
    public void RefreshAppearance()
    {
        ApplyTheme();
        ApplyBorderEffect();
        ApplyWindowSize();
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
