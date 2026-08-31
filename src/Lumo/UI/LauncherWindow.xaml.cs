using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
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
    private readonly Favourites _favs;               // v2.2 — pinned favourites
    private readonly AiService _ai = new();          // v2.3 — ? answers (cache + in-flight dedupe)

    /// <summary>v2.3.0-alpha.3 — the shared AI service, reused by the chat window (one cache, one log).</summary>
    public AiService Ai => _ai;
    private readonly BookmarkIndex _bookmarks = new(); // v2.3 — B/ Chrome & Edge bookmarks
    private readonly PluginStore? _plugins;            // v2.5 — Task 4.2 JSON plugin commands

    /// <summary>v2.5 (Task 4.3) — hotkey id → shortcut id for the per-shortcut global hotkeys.</summary>
    private readonly Dictionary<int, string> _shortcutHotkeyMap = new();
    private readonly DispatcherTimer _debounce;
    private readonly DispatcherTimer _statusTimer;
    private HotkeyService? _hotkey;
    private bool _sourceReady;                       // v1.7 — glass needs the HWND, applied on SourceInitialized
    private bool _allowClose;
    private IntPtr _hwnd;
    private Brush _themeBorderBrush = Brushes.DimGray;
    // v2.0.2 — the comet is driven by CompositionTarget.Rendering + a Stopwatch: the
    // position is a pure function of elapsed time (elapsed % lap), so the loop is
    // seamless BY CONSTRUCTION and can never stall like timeline clocks can.
    private readonly Stopwatch _glowWatch = Stopwatch.StartNew();
    private Point[] _glowSamples = Array.Empty<Point>();   // precomputed perimeter samples
    private bool _glowClockAttached;
    private bool _glowDimmed;                // window inactive → comet dimmed
    private int _animGen;                    // invalidates stale hide-completion callbacks
    private bool _staggerNext = true;        // v1.4 — stagger only on show/empty→results, not every keystroke
    private string _prevQuery = "";

    // v2.2 (DEV_PLAN Task 2.3) — preview pane state
    private bool _previewOpen;
    private int _previewGen;                 // invalidates stale preview reads
    private readonly DispatcherTimer _previewDebounce;   // 120 ms selection debounce

    private const double GlowDimFactor = 0.4;        // dimmed = 40 % of the active brightness

    /// <summary>Human-readable description of the hotkey that actually registered.</summary>
    public string? ActiveHotkeyDescription => _hotkey?.ActiveDescription;

    /// <summary>Raised when the user asks for the settings window (gear, Settings row).</summary>
    public event Action? SettingsRequested;

    /// <summary>v1.4 — user asked to create/edit a shortcut (row in /sc mode).</summary>
    public event Action<string?>? ShortcutEditorRequested;

    /// <summary>v1.4 — user asked to manage shortcuts (opens settings → Shortcuts).</summary>
    public event Action? ManageShortcutsRequested;

    /// <summary>
    /// v2.3.0-alpha.2 — open settings on a specific page (0 = default).
    /// Raised by the AI setup row ("cmd:ai-setup") so a dead-end ? query can land
    /// straight on Settings → AI with the one-click Ollama installer.
    /// </summary>
    public event Action<int>? SettingsPageRequested;

    /// <summary>
    /// v2.3.0-alpha.3 — open (or focus) the dedicated AI chat window; the payload
    /// is the question to auto-send ("AI/why is the sky blue") or null/empty to
    /// just open the empty chat. Raised by the AI/ rows ("cmd:ai-chat").
    /// </summary>
    public event Action<string?>? AiChatRequested;

    /// <summary>v1.5 — recording finished; App opens the builder with the captured steps.</summary>
    public event Action<List<MacroStep>, string?>? RecordFinishRequested;

    public LauncherWindow(Settings settings, ShortcutStore? shortcuts = null, MacroRecorder? recorder = null,
                          ClipboardHistory? clips = null, UsageStore? usage = null, Favourites? favourites = null,
                          PluginStore? plugins = null)
    {
        InitializeComponent();
        _settings = settings;
        _shortcuts = shortcuts;
        _recorder = recorder;
        _clips = clips;
        _favs = favourites ?? new Favourites();
        _plugins = plugins;
        _files.MaxEntries = Math.Max(10_000, _settings.MaxIndexedFiles);

        _engine = new SearchEngine(_apps, _files, _settings, _shortcuts, _recorder, _clips, usage, _favs, _ai, _bookmarks, _plugins);

        // v2.2 — placeholder so the ContextMenuOpening routed event always fires;
        // BuildRowMenu swaps in the real menu (or null) before WPF auto-opens it.
        Results.ContextMenu = new ContextMenu();

        _previewDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };   // v2.2
        _previewDebounce.Tick += (_, _) =>
        {
            try { _previewDebounce.Stop(); RenderPreview(); }
            catch (Exception ex) { DiagnosticLogger.LogException("Window.PreviewDebounce", ex); }
        };

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

    /// <summary>
    /// Apple “Designing Fluid Interfaces” §14 — reduced motion. Motion plays only when
    /// the user allowed it in Lumo AND Windows itself has “show animations” enabled;
    /// otherwise the UI cross-fades / snaps instead of sliding and scaling.
    /// </summary>
    private bool MotionOk() => _settings.AnimationsEnabled && SystemParameters.ClientAreaAnimation;

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

            // v2.5 (DEV_PLAN Task 4.3) — register every shortcut's global hotkey too
            ReapplyShortcutHotkeys();

            // v2.0 → v2.4.0-alpha.2 — the HWND now exists; ApplyTheme applies DWM
            // chrome (round corners, dark mode) and the material (acrylic or solid)
            // in one place.
            _sourceReady = true;
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
            _bookmarks.BeginLoadInBackground();   // v2.3 — Chrome/Edge bookmarks for B/
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
        if (msg == NativeMethods.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == HotkeyService.HotkeyId)
            {
                handled = true;
                try { ToggleLauncher(); } catch (Exception ex) { DiagnosticLogger.LogException("Window.Hotkey", ex); }
            }
            else if (_shortcutHotkeyMap.TryGetValue(id, out var shortcutId))
            {
                // v2.5 (DEV_PLAN Task 4.3) — a per-shortcut hotkey fired: run the
                // shortcut whether or not the launcher is visible (that's the point).
                handled = true;
                try
                {
                    string? err = _engine.Execute(new ResultItem { Title = shortcutId, Glyph = "⚡", Kind = ResultKind.Shortcut, RunArgument = shortcutId });
                    if (err is not null)
                    {
                        DiagnosticLogger.Log("Hotkey", $"Shortcut hotkey '{shortcutId}' failed: {err}");
                        if (IsVisible) StatusText.Text = err;
                    }
                    else
                    {
                        DiagnosticLogger.Log("Hotkey", $"Shortcut hotkey ran '{shortcutId}'");
                    }
                }
                catch (Exception ex) { DiagnosticLogger.LogException("Window.ShortcutHotkey", ex); }
            }
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
            else if (MotionOk()) { RestoreVisualState(); }

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

    /// <summary>Fade + slide + gentle scale-in — the launcher springs open, Spotlight-style.
    /// Critically damped (ease-out, no overshoot): motion that informs, never distracts.</summary>
    private void AnimateShow()
    {
        try
        {
            Show();

            if (!MotionOk())
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

    /// <summary>
    /// Quick fade-out, then really hide. Safe against rapid toggle races.
    /// Apple §7 Spatial consistency — “if something disappears one way, we expect it
    /// to emerge from where it came”: the exit mirrors the entrance path (scale down
    /// + settle back down 10 px), with the mirrored EaseIn curve.
    /// </summary>
    private void HideAnimated()
    {
        try
        {
            if (_previewOpen) ClosePreview();   // v2.2 — fresh open next time
            PauseGlow();
            if (!MotionOk() || !IsVisible) { Hide(); return; }

            int gen = ++_animGen;
            var dur = TimeSpan.FromMilliseconds(130);
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

            var fade = new DoubleAnimation(1, 0, dur) { EasingFunction = ease };
            var scale = new DoubleAnimation(1, 0.965, dur) { EasingFunction = ease };
            var slide = new DoubleAnimation(0, 10, dur) { EasingFunction = ease };
            var glow = new DoubleAnimation(CurrentGlowOpacity(), 0, dur) { EasingFunction = ease };

            fade.Completed += (_, _) =>
            {
                if (gen != _animGen) return; // user re-summoned mid-fade
                try
                {
                    // Drop every hold-end animation so the next show starts clean.
                    Root.BeginAnimation(UIElement.OpacityProperty, null);
                    RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                    RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                    RootShift.BeginAnimation(TranslateTransform.YProperty, null);
                    GlowClip.BeginAnimation(UIElement.OpacityProperty, null);
                    RestoreVisualState();
                    Hide();
                }
                catch { }
            };
            Root.BeginAnimation(UIElement.OpacityProperty, fade);
            RootScale.BeginAnimation(ScaleTransform.ScaleXProperty, scale);
            RootScale.BeginAnimation(ScaleTransform.ScaleYProperty, scale.Clone());
            RootShift.BeginAnimation(TranslateTransform.YProperty, slide);
            GlowClip.BeginAnimation(UIElement.OpacityProperty, glow);
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

                // v2.2 (DEV_PLAN Task 2.3) — Tab (or Ctrl+Tab) toggles the preview pane
                case Key.Tab:
                    TogglePreview();
                    e.Handled = true;
                    break;

                // v2.2 (DEV_PLAN Task 2.1) — Ctrl+→ opens the row's quick-action menu
                case Key.Right when Keyboard.Modifiers == ModifierKeys.Control:
                    OpenRowMenuKeyboard();
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
                // v2.2 — Escape closes the preview pane first; a second Esc hides the window
                if (_previewOpen)
                {
                    ClosePreview();
                    e.Handled = true;
                    return;
                }

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

            MaybeAskAi();   // v2.3 — ? queries auto-ask once; the answer row appears when the reply lands
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.BindResults", ex);
        }
    }

    // ---------------------------------------------------------------- v2.3 — AI ask flow

    private int _aiGen;   // invalidates stale AI replies (query moved on before the answer landed)

    /// <summary>
    /// ? queries auto-ask: fire ONE bounded request per prompt (deduped inside
    /// AiService), and when the reply lands while the query is unchanged, re-run
    /// the search so the cached Answer row appears. The synchronous pipeline is
    /// never blocked — this is all Task.Run + dispatcher, per agent rule 1.
    /// </summary>
    private void MaybeAskAi(bool force = false)
    {
        try
        {
            string t = Input.Text.TrimStart();
            if (!t.StartsWith("?") || t.Length < 4) return;   // "?" + at least 2 chars
            if (!_settings.AiEnabled) return;

            // v2.3.0-alpha.2 — keep the Ollama probe fresh (background, never the
            // search thread): the AI rows consult OllamaManager.Current to decide
            // between "asking…" and the one-click setup row. Re-render once the
            // probe lands so the right row appears without further typing.
            if (!AiProviders.IsAnthropic(_settings.AiStyle, _settings.AiEndpoint) &&
                OllamaManager.IsLocalEndpoint(_settings.AiEndpoint) &&
                (OllamaManager.Current.Stale || !OllamaManager.Current.Probed))
            {
                string endpoint = _settings.AiEndpoint;
                int genAtStart = _aiGen;
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await OllamaManager.RefreshStatusAsync(endpoint).ConfigureAwait(true);
                        await Dispatcher.InvokeAsync(() =>
                        {
                            try { if (genAtStart == _aiGen) RunSearch(); } catch { }
                        });
                    }
                    catch (Exception ex) { DiagnosticLogger.LogException("Launcher.OllamaProbe", ex); }
                });
            }

            string prompt = t[1..].Trim();
            if (prompt.Length == 0) return;
            if (!force && _ai.HasCached(prompt)) return;      // cached → the row already shows the answer

            int gen = ++_aiGen;
            StatusText.Text = $"Asking {_settings.AiModel}…";
            _ = Task.Run(async () =>
            {
                var reply = await _ai.AskAsync(_settings, prompt).ConfigureAwait(true);
                await Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        if (gen != _aiGen) return;   // stale — the user asked something else
                        if (reply.Ok)
                        {
                            StatusText.Text = "AI answer ready — Enter copies it";
                            RunSearch();             // re-render: the cached Answer row now exists
                        }
                        else
                        {
                            StatusText.Text = "AI — " + reply.Error;
                        }
                    }
                    catch (Exception ex) { DiagnosticLogger.LogException("Window.AiApply", ex); }
                });
            });
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.MaybeAskAi", ex);
        }
    }

    /// <summary>
    /// v1.3 — results cascade in: each row fades + slides up 6 px on a 20 ms stagger
    /// (tightened in alpha.5), the Raycast/Spotlight feel. Runs after containers exist.
    /// </summary>
    private void PlayStaggeredEntrance()
    {
        if (!MotionOk()) return;
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

                    var tt = new TranslateTransform(0, 6);
                    fe.RenderTransform = tt;
                    var delay = TimeSpan.FromMilliseconds(i * 20);
                    tt.BeginAnimation(TranslateTransform.YProperty,
                        new DoubleAnimation(6, 0, dur) { EasingFunction = ease, BeginTime = delay });
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
        if (Results.SelectedItem is ResultItem item) ExecuteItem(item);
    }

    /// <summary>
    /// v2.2.0-alpha.2 rework — the one true execute path. Enter, double-click and the
    /// quick-action menu's "Open" all funnel through here, so the menu's primary
    /// action can never drift from what pressing Enter does.
    /// </summary>
    private void ExecuteItem(ResultItem item)
    {
        try
        {
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

            // v2.5 (DEV_PLAN Task 4.2) — plugin management lands on Settings → Plugins
            if (item.RunArgument == "cmd:plugins-manage")
            {
                HideAnimated();
                SettingsPageRequested?.Invoke(7);
                return;
            }

            // v2.6.0-alpha.2 — first-party plugin catalog, same page
            if (item.RunArgument == "cmd:plugins-get")
            {
                HideAnimated();
                SettingsPageRequested?.Invoke(7);
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

            // v2.3 — "Ask …" row: force the request for the current ? query (the
            // launcher stays open — the answer arrives on the same view).
            if (item.RunArgument == "cmd:ai-ask")
            {
                MaybeAskAi(force: true);
                return;
            }

            // v2.3.0-alpha.2 — the setup row: Ollama is missing or not serving.
            // Land the user on Settings → AI where install / start / pull-model
            // actions live. The launcher hides; the settings window takes over.
            if (item.RunArgument == "cmd:ai-setup")
            {
                HideAnimated();
                SettingsPageRequested?.Invoke(6);
                return;
            }

            // v2.3.0-alpha.3 — the AI/ rows: open the dedicated chat window and let
            // it auto-send the typed question (ForwardText), or just open empty.
            if (item.RunArgument == "cmd:ai-chat")
            {
                HideAnimated();
                AiChatRequested?.Invoke(item.ForwardText);
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

                // v2.3 — AI answers copy the FULL multi-line text, like the calculator.
                case ResultKind.Answer:
                    _engine.Execute(item);
                    StatusText.Text = "Answer copied to clipboard";
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
                case ResultKind.Plugin:   // v2.5 — Task 4.2 plugin commands run like any launch
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

    // ------------------------------------------------- v2.2 row quick actions (Task 2.1)

    /// <summary>Right-click selects the row under the cursor first, so the menu
    /// describes what the pointer is actually on — standard list behaviour. Rows that
    /// cannot carry actions (headers/hints/errors) clear the selection instead, so no
    /// menu ever describes the wrong row.</summary>
    private void OnResultsRightDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (e.OriginalSource is not DependencyObject d) return;
            if (ItemsControl.ContainerFromElement(Results, d) is not ListBoxItem lbi) return;
            Results.SelectedItem = lbi.Content is ResultItem
            {
                Kind: not ResultKind.Header and not ResultKind.Hint and not ResultKind.Error
            } item ? item : null;
        }
        catch { }
    }

    /// <summary>
    /// The menu is rebuilt for the selected row every time it opens. When the row
    /// carries no actions we set e.Handled: merely nulling the ContextMenu inside
    /// this handler does NOT stop WPF from opening the (empty) menu it already
    /// captured at event-raise time — right-clicking a header used to flash an
    /// empty menu shell.
    /// </summary>
    private void OnResultsContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        try
        {
            if (!BuildRowMenu()) e.Handled = true;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Window.RowMenuOpening", ex); }
    }

    /// <summary>Ctrl+→ toggles the quick-action menu under the selected row.</summary>
    private void OpenRowMenuKeyboard()
    {
        try
        {
            // v2.2.0-alpha.2 — the gesture is a toggle, not a spigot
            if (Results.ContextMenu is ContextMenu { IsOpen: true })
            {
                Results.ContextMenu.IsOpen = false;
                return;
            }
            if (!BuildRowMenu() || Results.ContextMenu is not ContextMenu menu) return;
            if (Results.ItemContainerGenerator.ContainerFromItem(Results.SelectedItem) is ListBoxItem lbi)
            {
                menu.PlacementTarget = lbi;
                menu.Placement = PlacementMode.Bottom;
            }
            menu.IsOpen = true;
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Window.RowMenuKeyboard", ex); }
    }

    /// <summary>
    /// Rebuilds the menu for the selected row. Returns false when there is nothing
    /// to show — ContextMenu is left null so no stale menu can ever open.
    /// </summary>
    private bool BuildRowMenu()
    {
        if (Results.SelectedItem is not ResultItem item)
        {
            Results.ContextMenu = null;
            return false;
        }

        var actions = RowActions.For(item, _favs.IsPinned(item.RunArgument));
        if (actions.Count == 0)
        {
            Results.ContextMenu = null;
            return false;
        }

        var menu = new ContextMenu();
        foreach (var a in actions)
        {
            // v2.2.0-alpha.2 — the pin/unpin pair sits apart from the work actions
            if (a is RowAction.Pin or RowAction.Unpin && menu.Items.Count > 0)
            {
                var sep = new Border { Height = 1, Margin = new Thickness(7, 5, 7, 4), Opacity = 0.6 };
                sep.SetResourceReference(Border.BackgroundProperty, "BorderLineBrush");
                menu.Items.Add(sep);
            }

            var mi = new MenuItem { Header = RowActions.Label(a) };
            if (a == RowAction.Open) mi.InputGestureText = "Enter";
            var captured = a;
            mi.Click += (_, _) =>
            {
                try { ExecuteRowAction(item, captured); }
                catch (Exception ex) { DiagnosticLogger.LogException("Window.RowActionClick", ex); }
            };
            menu.Items.Add(mi);
        }
        Results.ContextMenu = menu;
        return true;
    }

    /// <summary>Runs one quick action. The launcher stays open — these are helpers
    /// you fire without losing your place (Apple §4: the work happens in place).</summary>
    private void ExecuteRowAction(ResultItem item, RowAction action)
    {
        switch (action)
        {
            // v2.2.0-alpha.2 — the menu's primary action is the exact Enter path
            case RowAction.Open:
                ExecuteItem(item);
                break;

            case RowAction.OpenContainingFolder:
                OpenContainingFolder(item.RunArgument);
                break;

            case RowAction.CopyPath:
                TryClipboardText(item.RunArgument);
                // web rows carry their URL as the "path" — say so
                StatusText.Text = item.RunArgument.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? "Copied URL" : "Copied path";
                break;

            case RowAction.CopyName:
                TryClipboardText(item.Title);
                StatusText.Text = "Copied name";
                break;

            case RowAction.OpenTerminal:
                OpenTerminalHere(item.RunArgument);
                break;

            case RowAction.RunAsAdmin:
                RunElevated(item.RunArgument);
                break;

            // v2.2.0-alpha.2 — one Toggle call covers both directions
            case RowAction.Pin:
            case RowAction.Unpin:
                bool nowPinned = _favs.Toggle(item.RunArgument, item.Title, item.Subtitle, item.Glyph, item.Kind.ToString());
                StatusText.Text = nowPinned ? "★ Pinned to favourites" : "Unpinned";
                RunSearch();   // FAVOURITES section / star state refresh immediately
                break;
        }
    }

    /// <summary>
    /// v2.2.0-alpha.2 — the hover star: pin/unpin without opening any menu. The star
    /// is only rendered on rows that CanPin (SearchEngine.Annotate stamps it); marking
    /// the mouse-down handled keeps the ListBox from also treating the click as row
    /// selection. Pin/unpin triggers a fresh search, which re-stamps every row — so
    /// the star can never show stale state.
    /// </summary>
    private void OnPinStarDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            e.Handled = true;
            if (sender is FrameworkElement { DataContext: ResultItem { CanPin: true } item })
            {
                bool nowPinned = _favs.Toggle(item.RunArgument, item.Title, item.Subtitle, item.Glyph, item.Kind.ToString());
                StatusText.Text = nowPinned ? "★ Pinned to favourites" : "Unpinned";
                RunSearch();
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Window.PinStar", ex); }
    }

    private static void TryClipboardText(string? s)
    {
        if (string.IsNullOrEmpty(s)) return;
        try { Clipboard.SetText(s); } catch { /* clipboard can be locked */ }
    }

    private static void OpenContainingFolder(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            // web rows have no folder — open the URL itself
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            return;
        }

        string full = Environment.ExpandEnvironmentVariables(path);
        if (Directory.Exists(full))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{full}\"") { UseShellExecute = true });
        else if (File.Exists(full))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{full}\"") { UseShellExecute = true });
    }

    /// <summary>Windows Terminal first, classic cmd fallback — both start IN the folder.</summary>
    private void OpenTerminalHere(string path)
    {
        try
        {
            string dir = "";
            if (!string.IsNullOrWhiteSpace(path) && !path.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                string full = Environment.ExpandEnvironmentVariables(path);
                dir = Directory.Exists(full) ? full : Path.GetDirectoryName(full) ?? "";
            }
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            {
                StatusText.Text = "No folder to open a terminal in";
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo("wt.exe", $"-d \"{dir}\"") { UseShellExecute = true });
            }
            catch
            {
                Process.Start(new ProcessStartInfo("cmd.exe", $"/K cd /d \"{dir}\"") { UseShellExecute = true });
            }
            StatusText.Text = "Terminal opened";
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.OpenTerminal", ex);
            StatusText.Text = "Couldn't open a terminal";
        }
    }

    private void RunElevated(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(Environment.ExpandEnvironmentVariables(path)))
            {
                StatusText.Text = "File not found";
                return;
            }
            Process.Start(new ProcessStartInfo
            {
                FileName = Environment.ExpandEnvironmentVariables(path),
                UseShellExecute = true,
                Verb = "runas",
            });
            StatusText.Text = "Launched as administrator";
        }
        catch (System.ComponentModel.Win32Exception w) when (w.NativeErrorCode == 1223)
        {
            StatusText.Text = "Elevation cancelled";   // UAC prompt dismissed — not an error
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.RunAsAdmin", ex);
            StatusText.Text = "Couldn't elevate: " + ex.Message;
        }
    }

    // ------------------------------------------------- v2.2 preview pane (Task 2.3)

    private const long PreviewMaxBytes = 512 * 1024;   // never read more than half a MB
    private const int PreviewMaxLines = 200;           // head of the file only

    private void OnResultsSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        try
        {
            if (!_previewOpen) return;
            _previewDebounce.Stop();
            _previewDebounce.Start();   // 120 ms debounce — fast arrow-key walks don't thrash reads
        }
        catch { }
    }

    private void TogglePreview()
    {
        try
        {
            _previewOpen = !_previewOpen;
            PreviewPane.Visibility = _previewOpen ? Visibility.Visible : Visibility.Collapsed;
            if (_previewOpen)
            {
                _previewDebounce.Stop();
                RenderPreview();
            }
            else
            {
                _previewGen++;   // invalidate any in-flight read
                ClearPreview();
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Window.TogglePreview", ex); }
    }

    private void ClosePreview()
    {
        if (_previewOpen) TogglePreview();
    }

    private void ClearPreview()
    {
        PreviewText.Text = "";
        PreviewText.Visibility = Visibility.Collapsed;
        PreviewImage.Source = null;
        PreviewImage.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Fills the pane for the current selection. File contents are read on a worker
    /// thread (rule 1 — never block the UI thread) and guarded by a generation
    /// counter, so a slow read for row 12 can never land after the user has already
    /// moved to row 14.
    /// </summary>
    private void RenderPreview()
    {
        try
        {
            int gen = ++_previewGen;
            ClearPreview();
            if (Results.SelectedItem is not ResultItem item) return;

            PreviewTitle.Text = item.Title;
            PreviewMeta.Text = "";

            switch (item.Kind)
            {
                case ResultKind.Header:
                case ResultKind.Hint:
                case ResultKind.Error:
                    PreviewMeta.Text = "nothing to preview";
                    return;

                case ResultKind.Web:
                case ResultKind.Image:
                    PreviewMeta.Text = "web result";
                    PreviewText.Text = item.RunArgument;
                    PreviewText.Visibility = Visibility.Visible;
                    return;

                case ResultKind.Calculator:
                    PreviewMeta.Text = "calculator";
                    PreviewText.Text = item.RunArgument + "  (Enter copies)";
                    PreviewText.Visibility = Visibility.Visible;
                    return;

                case ResultKind.Clipboard:
                    if (_clips?.Find(item.RunArgument) is not { } ce)
                    {
                        PreviewMeta.Text = "entry no longer in history";
                        return;
                    }
                    PreviewMeta.Text = $"{ce.Text.Length:N0} chars";
                    PreviewText.Text = TruncateHead(ce.Text.Replace("\r", ""), PreviewMaxLines);
                    PreviewText.Visibility = Visibility.Visible;
                    return;

                case ResultKind.Shortcut:
                    if (_shortcuts?.Find(item.RunArgument) is not { } def)
                    {
                        PreviewMeta.Text = "shortcut missing";
                        return;
                    }
                    PreviewMeta.Text = def.Describe();
                    if (def.IsSnippet)
                    {
                        PreviewText.Text = TruncateHead(def.Target.Replace("\r", ""), PreviewMaxLines);
                        PreviewText.Visibility = Visibility.Visible;
                    }
                    else if (File.Exists(Environment.ExpandEnvironmentVariables(def.Target)))
                    {
                        PreviewPathAsync(gen, Environment.ExpandEnvironmentVariables(def.Target));
                    }
                    else
                    {
                        PreviewText.Text = def.Target;
                        PreviewText.Visibility = Visibility.Visible;
                    }
                    return;

                case ResultKind.Tool:
                    PreviewMeta.Text = "command";
                    PreviewText.Text = item.RunArgument;
                    PreviewText.Visibility = Visibility.Visible;
                    return;

                case ResultKind.App:
                case ResultKind.File:
                    PreviewPathAsync(gen, Environment.ExpandEnvironmentVariables(item.RunArgument));
                    return;
            }
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Window.RenderPreview", ex); }
    }

    private void PreviewPathAsync(int gen, string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                PreviewMeta.Text = "folder";
                PreviewText.Text = path;
                PreviewText.Visibility = Visibility.Visible;
                return;
            }
            if (!File.Exists(path))
            {
                PreviewMeta.Text = "file not found";
                return;
            }

            var fi = new FileInfo(path);
            long len = fi.Length;

            if (IsImageExtension(fi.Extension))
            {
                PreviewMeta.Text = $"{len / 1024d:N0} KB · image";
                string p = path;
                Task.Run(() =>
                {
                    try
                    {
                        if (len > 48 * 1024 * 1024) throw new IOException("image too large to preview");
                        byte[] bytes = File.ReadAllBytes(p);
                        Dispatcher.BeginInvoke(() =>
                        {
                            if (gen != _previewGen || !_previewOpen) return;   // stale — drop
                            try
                            {
                                var img = new BitmapImage();
                                using var ms = new MemoryStream(bytes);
                                img.BeginInit();
                                img.DecodePixelWidth = 240;   // thumbnail decode — cheap on RAM
                                img.CacheOption = BitmapCacheOption.OnLoad;
                                img.StreamSource = ms;
                                img.EndInit();
                                img.Freeze();
                                PreviewImage.Source = img;
                                PreviewImage.Visibility = Visibility.Visible;
                            }
                            catch
                            {
                                PreviewText.Text = "(couldn't decode image)";
                                PreviewText.Visibility = Visibility.Visible;
                            }
                        });
                    }
                    catch (Exception ex) { DiagnosticLogger.LogException("Preview.Image", ex); }
                });
                return;
            }

            PreviewMeta.Text = $"{len / 1024d:N0} KB" + (len > PreviewMaxBytes ? " · head only" : "");
            string file = path;
            Task.Run(() =>
            {
                try
                {
                    string head = ReadTextHead(file, PreviewMaxBytes, PreviewMaxLines);
                    Dispatcher.BeginInvoke(() =>
                    {
                        if (gen != _previewGen || !_previewOpen) return;   // stale — drop
                        PreviewText.Text = head;
                        PreviewText.Visibility = Visibility.Visible;
                    });
                }
                catch (Exception ex) { DiagnosticLogger.LogException("Preview.Text", ex); }
            });
        }
        catch (Exception ex) { DiagnosticLogger.LogException("Window.PreviewPath", ex); }
    }

    /// <summary>Reads at most <paramref name="maxBytes"/> bytes / <paramref name="maxLines"/>
    /// lines of a file, sharing read/write so files open in other apps don't block us.</summary>
    private static string ReadTextHead(string path, long maxBytes, int maxLines)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        int cap = (int)Math.Min(maxBytes, fs.Length);
        var buf = new byte[cap];
        int read = 0;
        while (read < cap)
        {
            int n = fs.Read(buf, read, cap - read);
            if (n <= 0) break;
            read += n;
        }
        string s = System.Text.Encoding.UTF8.GetString(buf, 0, read);
        if (s.Contains('\0')) return "(binary file — no text preview)";
        return TruncateHead(s, maxLines);
    }

    private static string TruncateHead(string s, int maxLines)
    {
        if (s.Length == 0) return "(empty)";
        var lines = s.Replace("\r\n", "\n").Split('\n');
        if (lines.Length <= maxLines) return s;
        return string.Join('\n', lines[..maxLines]) + $"\n… ({lines.Length - maxLines:N0} more lines)";
    }

    private static bool IsImageExtension(string ext) => ext.ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".ico";

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

            // v2.4.0-alpha.2 — Raycast material: frosted-glass acrylic blur-behind for
            // the launcher (falls back to the solid palette when unsupported); returns
            // whether the glass is actually live so the surface paints translucent.
            bool glass = false;
            if (_sourceReady)
                glass = GlassBackdrop.Apply(this, dark, acrylic: _settings.Acrylic);

            var p = Appearance.PaletteFor(dark, _settings.AccentColor);

            Resources["TitleBrush"] = new SolidColorBrush(p.Title);
            Resources["SubtitleBrush"] = new SolidColorBrush(p.Subtitle);
            Resources["HoverBrush"] = new SolidColorBrush(p.Hover);
            Resources["SelectedBrush"] = new SolidColorBrush(p.Selected);
            Resources["AccentBrush"] = new SolidColorBrush(p.Accent);
            Resources["ChipBrush"] = new SolidColorBrush(dark
                ? Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x0D, 0x00, 0x00, 0x00));
            Resources["ChipTextBrush"] = new SolidColorBrush(p.Subtitle);
            Resources["PlaceholderBrush"] = new SolidColorBrush(Appearance.PlaceholderFor(dark));
            Resources["IconBrush"] = new SolidColorBrush(p.Subtitle);
            Resources["GlyphBoxBrush"] = new SolidColorBrush(p.GlyphBox);
            Resources["BorderLineBrush"] = new SolidColorBrush(p.Border);

            // v2.4.0-alpha.3 — bordered raised-card selection: the 1 px outline around
            // the active row (Palette.SelStroke), also used by the primary footer chip.
            Resources["SelStrokeBrush"] = new SolidColorBrush(p.SelStroke);
            // v2.4.0-alpha.3 — top-lit strokes: the rim ring and the preview pane share
            // the reference material's light-catch border — a vertical gradient that
            // starts as a bright white edge at the top and settles into the hairline.
            Resources["PreviewStrokeBrush"] = TopLitStrokeBrush(p.Border, dark, 0x26);

            // v2.2.0-alpha.3 FIX — FieldBrush was never defined in this window, so every
            // DynamicResource referencing it resolved to NULL. The quick-action menu card
            // (a WPF popup has no backdrop of its own) rendered completely see-through,
            // and the search field fill silently fell back to the panel colour. Wire the
            // palette's OPAQUE Field token — same solid value SettingsWindow already uses.
            Resources["FieldBrush"] = new SolidColorBrush(p.Field);

            // Win11 rounded corners come from DWM on Windows 11; Windows 10 keeps square
            // corners (an unpainted radius would otherwise expose raw window corners).
            // v2.0.1 — the user can also force square corners from Settings → Appearance.
            bool rounded = !string.Equals(_settings.CornerStyle, "square", StringComparison.OrdinalIgnoreCase);
            float r = GlassBackdrop.IsWin11 && rounded ? 8f : 0f;
            double t = Math.Clamp(_settings.RimThickness, 2.0, 6.0);

            // v2.4.0-alpha.2 — when the acrylic glass is live, the card itself becomes
            // translucent so the frosted desktop shows through (Raycast rgba(28,28,30,.9));
            // content-tier surfaces (fields, chips, tiles) stay OPAQUE for readability.
            byte panelAlpha = glass ? (byte)0xB4 : (byte)0xFF;
            Root.Background = new SolidColorBrush(Color.FromArgb(panelAlpha, p.Panel.R, p.Panel.G, p.Panel.B));
            Root.CornerRadius = new CornerRadius(r);
            _themeBorderBrush = TopLitStrokeBrush(p.Border, dark, 0x30);

            // v2.4.0-alpha.3 — the ambient sheen under the top edge is a dark-mode
            // material cue (light pools from the top-lit rim); light stays clean.
            AmbientLight.Opacity = dark ? 1.0 : 0.0;

            // v2.0.1 — rim comet geometry follows the theme radius: hairline ring for
            // definition, clipped orbit host, and the cover patch inset by the rim
            // thickness (painted with the SAME panel fill → the comet shows only
            // in the outer band). On glass the cover goes slightly more translucent
            // than the panel so the interior blobs read as light diffusing through
            // the frost instead of a hard edge-lit band.
            RimLine.CornerRadius = new CornerRadius(r);
            RimLine.BorderBrush = _themeBorderBrush;
            GlowClip.CornerRadius = new CornerRadius(r);
            GlowCover.CornerRadius = new CornerRadius(Math.Max(0, r - t));
            GlowCover.Margin = new Thickness(t);
            GlowCover.Background = new SolidColorBrush(
                Color.FromArgb(glass ? (byte)0xC0 : (byte)0xFF, p.Panel.R, p.Panel.G, p.Panel.B));
            Resources["ResultRowPad"] = string.Equals(_settings.RowDensity, "compact", StringComparison.OrdinalIgnoreCase)
                ? new Thickness(14, 4, 14, 4)
                : new Thickness(14, 7, 14, 7);
            if (ActualWidth >= 40 && ActualHeight >= 40)
                UpdateGlowGeometry(new Size(ActualWidth, ActualHeight));
            Input.Foreground = new SolidColorBrush(p.Title);
            Input.CaretBrush = new SolidColorBrush(p.Accent);
            Input.SelectionBrush = new SolidColorBrush(Appearance.Tint(p.Accent, 0x55));
            Input.SelectionTextBrush = new SolidColorBrush(p.Title);
            Separator.Background = new SolidColorBrush(p.Separator);
            FooterRule.Background = EdgeFadeBrush(p.Separator);

            // Apple craft — the light-catch: a 1 px bright top edge, the way light grazes
            // the top of a material. Dark mode gets a faint sheen; light mode stays clean.
            TopLight.Background = dark
                ? new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF))
                : Brushes.Transparent;
            PrefixBadge.Foreground = new SolidColorBrush(p.Accent);
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.ApplyTheme", ex);
        }
    }

    /// <summary>
    /// v2.4 — Raycast search row: the input is flush with the card header, so there is
    /// no focus chrome to toggle any more (the Win11 accent focus bar is gone). The
    /// handler stays wired because the focus notifications are still useful state,
    /// but it no longer paints anything.
    /// </summary>
    private void OnInputFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Intentionally empty — the Raycast header has no focus ring to draw.
    }

    /// <summary>
    /// v2.4.0-alpha.3 — the top-lit stroke: a vertical gradient border that reads as
    /// a bright 1 px light edge along the top of a surface and settles into the
    /// quiet hairline below it (the reference material's glass-card outline).
    /// Used for the resting rim ring and the Tab preview pane.
    /// </summary>
    private static Brush TopLitStrokeBrush(Color hairline, bool dark, byte topAlpha)
    {
        var b = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        if (dark)
        {
            b.GradientStops.Add(new GradientStop(Color.FromArgb(topAlpha, 0xFF, 0xFF, 0xFF), 0.0));
            b.GradientStops.Add(new GradientStop(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF), 0.10));
            b.GradientStops.Add(new GradientStop(hairline, 0.42));
        }
        else
        {
            b.GradientStops.Add(new GradientStop(Colors.White, 0.0));
            b.GradientStops.Add(new GradientStop(hairline, 0.40));
        }
        b.GradientStops.Add(new GradientStop(hairline, 1.0));
        b.Freeze();
        return b;
    }

    private static Color FromRgb(byte r, byte g, byte b) => Color.FromRgb(r, g, b);

    /// <summary>
    /// Apple §12 — scroll edge effects, not hard dividers: a hairline that fades out at
    /// both ends, so rules dissolve into the surface instead of cutting it.
    /// </summary>
    private static Brush EdgeFadeBrush(Color c)
    {
        var b = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0.5),
            EndPoint = new Point(1, 0.5),
        };
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0.0));
        b.GradientStops.Add(new GradientStop(c, 0.18));
        b.GradientStops.Add(new GradientStop(c, 0.82));
        b.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1.0));
        b.Freeze();
        return b;
    }

    private double CurrentGlowOpacity()
    {
        // v2.0.1 — user-tunable brightness (Settings → Appearance → Glow brightness)
        double active = Math.Clamp(_settings.GlowOpacity, 0.4, 1.0);
        return _glowDimmed ? active * GlowDimFactor : active;
    }

    /// <summary>
    /// v2.0.2 glow engine — the z.ai chat-box comet, rebuilt to CANNOT stop looping.
    /// The old alpha.2/3 engine used a controllable Storyboard of
    /// DoubleAnimationUsingPath timelines (negative BeginTime for the tail, in-place
    /// path mutation on resize) — a fragile combination that can stop after one lap.
    /// Now: the perimeter is sampled ONCE into a point array, and every composition
    /// frame the head/tail positions are set from elapsed-time-modulo-lap. The loop is
    /// seamless by construction, resize-proof (re-sample in place), and costs zero
    /// idle CPU (the clock detaches when the window hides or deactivates).
    /// "Solid" style = static accent ring, no motion.
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

                    if (ActualWidth >= 40 && ActualHeight >= 40)
                        EnsureGlowSamples(new Size(ActualWidth, ActualHeight));
                    AttachGlowClock();
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

    /// <summary>Number of perimeter samples — dense enough to look continuous, trivial to recompute.</summary>
    private const int GlowSampleCount = 720;

    /// <summary>
    /// Samples the rounded-rect perimeter (blob centres travel the band's middle line)
    /// into a frozen point array. Rebuilt in place on resize/style change — the comet
    /// continues from its time-based position with no restart and no snap.
    /// </summary>
    private void EnsureGlowSamples(Size size)
    {
        try
        {
            bool rounded = !string.Equals(_settings.CornerStyle, "square", StringComparison.OrdinalIgnoreCase);
            double r = GlassBackdrop.IsWin11 && rounded ? 8f : 0f;
            double t = Math.Clamp(_settings.RimThickness, 2.0, 6.0);

            var geo = new PathGeometry();
            geo.Figures.Add(BuildPerimeterFigure(size.Width, size.Height, t / 2, Math.Max(2, r - t / 2)));
            geo.Freeze();

            var pts = new Point[GlowSampleCount];
            for (int i = 0; i < GlowSampleCount; i++)
                geo.GetPointAtFractionLength((double)i / GlowSampleCount, out pts[i], out _);
            _glowSamples = pts;
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.GlowSamples", ex);
        }
    }

    private void AttachGlowClock()
    {
        if (_glowClockAttached) return;
        CompositionTarget.Rendering += OnGlowFrame;
        _glowClockAttached = true;
    }

    private void DetachGlowClock()
    {
        if (!_glowClockAttached) return;
        CompositionTarget.Rendering -= OnGlowFrame;
        _glowClockAttached = false;
    }

    /// <summary>
    /// Per-frame comet update. Setting the transform positions invalidates the visual,
    /// which schedules the next frame — the loop self-drives while visible and costs
    /// nothing when paused (handler early-outs, clock detached on hide/deactivate).
    /// </summary>
    private void OnGlowFrame(object? sender, EventArgs e)
    {
        try
        {
            var pts = _glowSamples;
            if (pts.Length == 0 || !IsVisible) return;

            double sec = _settings.BorderSpeedSec is <= 0 or double.NaN ? 9.0 : Math.Clamp(_settings.BorderSpeedSec, 4.0, 30.0);
            double frac = (_glowWatch.Elapsed.TotalSeconds % sec) / sec;                 // 0..1, seamless
            int n = pts.Length;
            int head = (int)(frac * n) % n;
            int tail = ((int)(frac * n) - (int)(0.07 * n) + n) % n;                      // trails the head

            GlowHeadPos.X = pts[head].X; GlowHeadPos.Y = pts[head].Y;
            GlowTailPos.X = pts[tail].X; GlowTailPos.Y = pts[tail].Y;
        }
        catch { }
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
    /// Keeps the rounded-rect clip and the perimeter samples in sync with the window
    /// size. The comet position derives from time, so a resize never restarts or snaps
    /// the animation — the next frame simply uses the new samples.
    /// </summary>
    private void UpdateGlowGeometry(Size size)
    {
        bool rounded = !string.Equals(_settings.CornerStyle, "square", StringComparison.OrdinalIgnoreCase);
        double r = GlassBackdrop.IsWin11 && rounded ? 8f : 0f;
        GlowClip.Clip = new RectangleGeometry(new Rect(0, 0, size.Width, size.Height), r, r);
        // The top light-catch must obey the same rounded mask — otherwise the straight
        // line paints across the corner cutouts of the window.
        TopLightHost.Clip = new RectangleGeometry(new Rect(0, 0, size.Width, size.Height), r, r);
        EnsureGlowSamples(size);
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
        DetachGlowClock();
    }

    private void PauseGlow()
    {
        try { DetachGlowClock(); } catch { }
    }

    private void ResumeGlow()
    {
        try
        {
            if (_settings.BorderEffect && Appearance.IsAnimatedStyle(_settings.BorderStyle))
                AttachGlowClock();
        }
        catch { }
    }

    protected override void OnClosed(EventArgs e)
    {
        DetachGlowClock();
        base.OnClosed(e);
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

    // ---------------------------------------------------------------- v2.5 — per-shortcut hotkeys (DEV_PLAN Task 4.3)

    /// <summary>
    /// Re-registers every shortcut's global hotkey. Called on startup, after the
    /// shortcut editor saves, and whenever Settings touches shortcuts — the map
    /// (hotkey id → shortcut id) is rebuilt from scratch each time, so the WndProc
    /// side can never hold a stale id.
    /// </summary>
    public void ReapplyShortcutHotkeys()
    {
        try
        {
            if (_hotkey is null) return;
            foreach (int id in _shortcutHotkeyMap.Keys.ToList())
                _hotkey.UnregisterId(id);
            _shortcutHotkeyMap.Clear();

            if (_shortcuts is null) return;
            int next = 1;
            foreach (var def in _shortcuts.Snapshot())
            {
                if (string.IsNullOrWhiteSpace(def.Hotkey)) continue;
                if (_shortcutHotkeyMap.Count >= HotkeyService.MaxShortcutHotkeys)
                {
                    DiagnosticLogger.Log("Hotkey", $"'{def.Name}' skipped — {HotkeyService.MaxShortcutHotkeys} shortcut-hotkey cap reached");
                    continue;
                }
                int id = HotkeyService.ShortcutHotkeyBase + next++;
                if (_hotkey.TryRegisterId(id, def.Hotkey))
                    _shortcutHotkeyMap[id] = def.Id;
                else
                    DiagnosticLogger.Log("Hotkey", $"'{def.Name}' hotkey '{def.Hotkey}' unavailable (taken by another app?)");
            }
        }
        catch (Exception ex)
        {
            DiagnosticLogger.LogException("Window.ReapplyShortcutHotkeys", ex);
        }
    }

    /// <summary>
    /// Editor feedback after a save: is THIS shortcut's hotkey live right now?
    /// "" when the shortcut defines no hotkey.
    /// </summary>
    public string DescribeShortcutHotkeyState(string shortcutId)
    {
        try
        {
            string combo = _shortcuts?.Find(shortcutId)?.Hotkey ?? "";
            if (string.IsNullOrWhiteSpace(combo)) return "";
            bool active = _shortcutHotkeyMap.ContainsValue(shortcutId);
            return active ? $"Global hotkey {combo} is live — press it anywhere."
                          : $"Global hotkey {combo} could NOT register — another app may own it. Try a different combination.";
        }
        catch { return ""; }
    }

    private void OnGearClick(object sender, MouseButtonEventArgs e)
    {
        try { SettingsRequested?.Invoke(); }
        catch (Exception ex) { DiagnosticLogger.LogException("Window.Gear", ex); }
    }
}
